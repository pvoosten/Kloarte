using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: tests from ModelTests.Cues.cs (converted from the Godot model tests).</summary>
public partial class MagicQTests
{
    /// <summary>Inserting blackout and no-change cues into stacks.</summary>
    [Fact]
    public void CueInsertTest()
    {
        Check(MagicQShow.CueStack.NumberBetween(1f, 2f) == 1.1f && MagicQShow.CueStack.NumberBetween(1f, 4f) == 2f
              && MagicQShow.CueStack.NumberBetween(1.1f, 1.2f) == 1.11f && MagicQShow.CueStack.NumberBetween(100f, null) == 101f
              && MagicQShow.CueStack.NumberBetween(null, 0.01f) == 0.001f && MagicQShow.CueStack.NumberBetween(29.11f, 29.12f) == 29.111f,
              "new cue numbers: 1|2 -> 1.1, 1|4 -> 2, 1.1|1.2 -> 1.11, last 100 -> 101, before 0.01 -> 0.001");

        string src = Show("RT2025_031.shw");
        string dst = Path.Combine(_out, "cues_inserted.shw");
        var show = MagicQShow.Load(src);
        var stack = show.CueStacks.First(s => s.Name == "show");   // 39 steps, with step notes
        var stack1 = show.CueStacks.First(s => s.Number == 1);      // 2 steps, no notes
        int before = stack.Steps.Count;
        int start = stack.Steps.ToList().FindIndex(s => s.CueNumberText == "1");
        float bo = MagicQShow.CueStack.NumberBetween(1f, stack.Steps[start + 1].CueNumber).Value;
        show.InsertCue(stack, bo, "BO", blackout: true);
        show.InsertCue(stack, 101f, "no change", blackout: false);
        show.InsertCue(stack, 0.001f, "first", blackout: false);
        show.InsertCue(stack1, 1.5f, "", blackout: false);
        bool duplicate = false;
        try
        {
            show.InsertCue(stack, 1f, "dup", blackout: false);
        }
        catch (ArgumentException)
        {
            duplicate = true;
        }
        Check(duplicate, "an existing cue number is refused");
        show.Save(dst);

        var re = MagicQShow.Load(dst);
        var s2 = re.CueStacks.First(s => s.Name == "show");
        var steps = s2.Steps;
        Check(steps.Count == before + 3 && steps[0].Text == "first" && steps[^1].Text == "no change"
              && steps.Select(s => s.CueNumberText).Contains(bo.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
              $"stack 'show' has {steps.Count} steps (was {before}), new ones in cue order");
        Check(StackConsistent(s2.Record) && StackConsistent(re.CueStacks.First(s => s.Number == 1).Record),
              "steps in cue order, circular links, two note strings and one trailing field per step");

        var boCue = re.Cues.First(c => c.Id == steps.First(s => s.Text == "BO").CueId);
        var dimmers = re.Patch.Where(p => MagicQShow.AttributeClass(re, p) == 'I').Select(p => p.Address).Distinct().ToList();
        Check(boCue.Count == dimmers.Count && boCue.Channels.All(c => c.Value == 0), $"BO cue: {boCue.Count} intensity channels at 0");
        var empty = re.Cues.First(c => c.Id == steps[^1].CueId);
        Check(empty.Count == 0 && empty.Name == "no change", "cue without changes has no channels");

        // Playback: the BO cue darkens every intensity, the empty cue tracks the previous look.
        var timeline = new PlaybackTimeline(re, s2, new Blocking(), 1f);
        int boIndex = steps.ToList().FindIndex(s => s.Text == "BO");
        var afterBo = timeline.DmxAt(timeline.Segments[boIndex].End - 0.01);
        Check(dimmers.All(a => afterBo.GetValueOrDefault(a) == 0), "after BO every intensity is 0");
        var lastLook = timeline.DmxAt(timeline.Segments[^2].End - 0.01);
        var afterEmpty = timeline.DmxAt(timeline.Segments[^1].End - 0.01);
        Check(lastLook.Count == afterEmpty.Count && lastLook.All(kv => afterEmpty.GetValueOrDefault(kv.Key) == kv.Value),
              "the cue without changes keeps the previous look");

        // Only the two stacks changed and four cues were added; everything else is byte-identical.
        var oldRecords = MagicQShow.Load(src).Shw.Records.Select(Text).ToHashSet();
        var changed = re.Shw.Records.Where(r => !oldRecords.Contains(Text(r))).Select(r => r.Type).ToList();
        var changedIds = re.Shw.Records.Where(r => !oldRecords.Contains(Text(r))).Select(r => r.Type + r[1]).ToList();
        Check(changed.Count(t => t == "C") == 2 && changed.Count(t => t == "S") == 4 && changed.Count == 6,
              $"changed records: {string.Join(", ", changedIds)}");
    }

    /// <summary>The C record invariants found in all MagicQ shows (see MagicQShow.CueStack).</summary>
    private static bool StackConsistent(ShwRecord r)
    {
        int n = r.GetHex(4);
        var numbers = new List<float>();
        for (int i = 0; i < n; i++)
        {
            int f = 23 + 7 * i;
            if (r.GetHex(f) != (i + 1) % n || r.GetHex(f + 1) != (i + n - 1) % n)
                return false;
            numbers.Add(r.GetFloat(f + 5));
        }
        int flag = 23 + 7 * n;
        int notes = r.GetHex(flag) != 0 ? 2 * n : 0;
        for (int i = 0; i < notes; i++)
            if (!r[flag + 1 + i].StartsWith('"'))
                return false;
        int five = flag + 1 + notes;
        for (int i = 0; i < n; i++)
            if (r[five + 5 + i].Length != 8 || r[five + 5 + i].StartsWith('"'))
                return false;
        return numbers.SequenceEqual(numbers.OrderBy(x => x)) && !r[five].StartsWith('"') && r[five + 1].Length == 4;
    }
}
