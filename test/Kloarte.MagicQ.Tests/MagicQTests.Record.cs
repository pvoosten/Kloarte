using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: recording the programmer into a cue (record merge with tracking and palette references).</summary>
public partial class MagicQTests
{
    /// <summary>
    /// A cue step of the "show" stack of RT2025_031 with two intensity channels that neither it nor the next step
    /// records (new levels track into the next step), and one channel it records with a level other than the cue
    /// before (recording that level again makes it track).
    /// </summary>
    private static (MagicQShow Show, MagicQShow.CueStack Stack, int Step, int Fresh, int Fresh2, int Recorded) RecordSetup()
    {
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var stack = show.CueStacks.First(s => s.Name == "show");
        var steps = stack.Steps;
        var cues = show.Cues.ToDictionary(c => c.Id);
        bool Records(int step, int address) => cues[steps[step].CueId].Channels.Any(c => c.Address == address);
        var intensities = MagicQCheck.IntensityAddresses(show).Order().ToList();
        foreach (int k in Enumerable.Range(1, steps.Count - 2))
        {
            var fresh = intensities.Where(a => !Records(k, a) && !Records(k + 1, a)).Take(2).ToList();
            var before = show.LevelsAfter(stack, k - 1);
            int recorded = cues[steps[k].CueId].Channels
                .Where(c => c.Value >> 8 == 0 && intensities.Contains(c.Address) && c.Value != before.GetValueOrDefault(c.Address))
                .Select(c => c.Address).FirstOrDefault();
            if (fresh.Count == 2 && recorded > 0)
                return (show, stack, k, fresh[0], fresh[1], recorded);
        }
        throw new Xunit.Sdk.XunitException("no suitable cue in RT2025_031");
    }

    /// <summary>
    /// Record into cue: a new level is inserted (pairs in address order, count updated) and tracks into the next cue;
    /// a level equal to the cue before is removed from the cue (it tracks); a level an intensity palette holds is stored
    /// as that palette; earlier and other cues don't change.
    /// </summary>
    [Fact]
    public void RecordIntoCue()
    {
        var (show, stack, k, fresh, fresh2, recorded) = RecordSetup();
        var steps = stack.Steps;
        var cue = show.Cues.First(c => c.Id == steps[k].CueId);
        var others = show.Cues.Where(c => c.Id != cue.Id).ToDictionary(c => c.Id, c => c.Channels.ToList());
        var previous = show.LevelsAfter(stack, k - 1);
        int count = cue.Count;
        var palette = show.AddPalette(MagicQShow.PaletteType.Intensity, "Test 60", new[] { (fresh2, 153) });

        var result = show.RecordIntoCue(stack, k, new[] { (fresh, 200), (fresh2, 153), (recorded, previous.GetValueOrDefault(recorded)) });

        Check(result.Recorded.SequenceEqual(new[] { fresh, fresh2 }.Order()) && result.Tracked.SequenceEqual(new[] { recorded }),
              $"recorded {string.Join(",", result.Recorded)}, tracked {string.Join(",", result.Tracked)}");
        Check(cue.Channels.Contains((fresh, 200)), "a new level is stored as a hard value");
        Check(cue.Channels.Contains((fresh2, palette.Id << 8 | 153)) && result.Palettes[fresh2] == palette,
              "a level the intensity palette holds is stored as a reference to it");
        Check(cue.Channels.All(c => c.Address != recorded), "a level equal to the cue before is removed from the cue (it tracks)");
        Check(cue.Count == count + 1 && cue.Channels.Select(c => c.Address).SequenceEqual(cue.Channels.Select(c => c.Address).Order()),
              $"pair count {cue.Count} (was {count}: +2 -1), pairs in address order");
        var after = show.LevelsAfter(stack, k);
        Check(after.GetValueOrDefault(fresh) == 200 && after.GetValueOrDefault(fresh2) == 153
              && after.GetValueOrDefault(recorded) == previous.GetValueOrDefault(recorded), "the cue's levels");
        Check(show.LevelsAfter(stack, k + 1).GetValueOrDefault(fresh) == 200 && result.LaterCues >= 1,
              $"the next cue tracks the new level ({result.LaterCues} later cues)");
        Check(show.LevelsAfter(stack, k - 1).SequenceEqual(previous), "the cue before is unchanged");
        Check(show.Cues.Where(c => c.Id != cue.Id).All(c => c.Channels.SequenceEqual(others[c.Id])), "no other cue changes");

        // Recording the same again changes nothing.
        var pairs = cue.Channels.ToList();
        show.RecordIntoCue(stack, k, new[] { (fresh, 200), (fresh2, 153) });
        Check(cue.Channels.SequenceEqual(pairs), "recording the same levels again changes nothing");
    }

    /// <summary>
    /// The recorded show saved and read back: the levels are there, and only the edited cue's lines differ from the
    /// original file (MagicQ's other records stay byte-identical).
    /// </summary>
    [Fact]
    public void RecordIntoCueRoundTrip()
    {
        var (show, stack, k, fresh, _, recorded) = RecordSetup();
        int previous = show.LevelsAfter(stack, k - 1).GetValueOrDefault(recorded);
        show.RecordIntoCue(stack, k, new[] { (fresh, 200), (recorded, previous) });
        string path = SaveCopy(show, "record_into_cue.shw");

        var reread = MagicQShow.Load(path);
        var levels = reread.LevelsAfter(reread.CueStacks.First(s => s.Name == "show"), k);
        Check(levels.GetValueOrDefault(fresh) == 200 && levels.GetValueOrDefault(recorded) == previous, "saved and read back");

        var original = File.ReadAllLines(Show("RT2025_031.shw"));
        var saved = File.ReadAllLines(path);
        var gone = original.Except(saved).ToList();
        var added = saved.Except(original).ToList();
        Check(saved.Length == original.Length && gone.Count <= 2 && added.Count <= 2,
              $"only the cue's lines differ: {gone.Count} gone, {added.Count} new, {saved.Length} / {original.Length} lines");
    }
}
