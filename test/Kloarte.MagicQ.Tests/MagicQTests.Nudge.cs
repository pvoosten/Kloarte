using System.Collections.Generic;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: tests from ModelTests.Nudge.cs (converted from the Godot model tests).</summary>
public partial class MagicQTests
{
    /// <summary>Changing a head's intensity in a cue (tracked level + step, clamped, tracking).</summary>
    [Fact]
    public void NudgeTest()
    {
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var stack = show.CueStacks.First(s => s.Name == "show");
        var steps = stack.Steps;
        var cues = show.Cues.ToDictionary(c => c.Id);
        bool Records(int step, int address) => cues[steps[step].CueId].Channels.Any(c => c.Address == address);

        // A step whose cue doesn't record the channel, followed by one that doesn't either: the edit is inserted
        // into the cue and tracks into the next step.
        var intensities = MagicQCheck.IntensityAddresses(show).Order().ToList();
        var (k, a) = (from step in Enumerable.Range(1, steps.Count - 2)
                      from address in intensities
                      where !Records(step, address) && !Records(step + 1, address)
                      select (step, address)).First();
        var before = show.Cues.Where(c => c.Id != steps[k].CueId).ToDictionary(c => c.Id, c => c.Channels.ToList());
        int tracked = show.LevelsAfter(stack, k).GetValueOrDefault(a);
        int previous = show.LevelsAfter(stack, k - 1).GetValueOrDefault(a);
        int count = cues[steps[k].CueId].Count;

        int level = show.NudgeLevel(stack, k, a, 5);
        Check(level == System.Math.Min(255, tracked + 5), $"cue {steps[k].CueNumberText} channel {a}: {tracked} + 5 = {level}");
        Check(cues[steps[k].CueId].Channels.Contains((a, level)) && cues[steps[k].CueId].Count == count + 1,
              "recorded in that cue as a hard value");
        Check(show.LevelsAfter(stack, k - 1).GetValueOrDefault(a) == previous, "the previous cue is unchanged");
        Check(show.LevelsAfter(stack, k + 1).GetValueOrDefault(a) == level, "the next cue tracks the new level");
        Check(show.Cues.Where(c => c.Id != steps[k].CueId).All(c => c.Channels.SequenceEqual(before[c.Id])),
              "no other cue changes");
        Check(show.NudgeLevel(stack, k, a, 1000) == 255 && show.NudgeLevel(stack, k, a, -1000) == 0
              && cues[steps[k].CueId].Count == count + 1, "clamped to 0..255, the pair is reused");

        // Round trip: the edited cue reads back the same.
        var reread = MagicQShow.Load(SaveCopy(show, "nudge.shw"));
        Check(reread.LevelsAfter(reread.CueStacks.First(s => s.Name == "show"), k).GetValueOrDefault(a) == 0,
              "saved and read back");
    }
}
