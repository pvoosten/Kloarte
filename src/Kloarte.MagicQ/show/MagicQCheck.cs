using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// Compares what MagicQ outputs (live Art-Net) with the app's model of a cue stack: which step's look matches the
/// received DMX best, and which channels differ. No Godot dependencies.
/// </summary>
public static class MagicQCheck
{
    /// <summary>Allowed difference per channel (fades end on whole values; 8/16-bit rounding).</summary>
    public const int Tolerance = 1;

    /// <summary>
    /// The step (-1 = before the first cue) whose expected DMX matches <paramref name="received"/> best.
    /// <paramref name="expected"/>[i + 1] = DMX after step i (address -&gt; value). Compared are the addresses in that
    /// state plus every intensity address (an intensity no cue has set yet is expected at 0).
    /// </summary>
    public static (int Step, int Compared, List<(int Address, int Expected, int Received)> Diffs) BestMatch(
        IReadOnlyDictionary<int, int> received, IReadOnlyList<Dictionary<int, int>> expected, IEnumerable<int> intensityAddresses)
    {
        var intensities = intensityAddresses.ToHashSet();
        (int Step, int Compared, List<(int, int, int)> Diffs) best = (-1, 0, null);
        for (int i = 0; i < expected.Count; i++)
        {
            var state = expected[i];
            var diffs = new List<(int, int, int)>();
            var addresses = state.Keys.Union(intensities).ToList();
            foreach (int a in addresses)
            {
                int want = state.GetValueOrDefault(a);
                int got = received.GetValueOrDefault(a);
                if (Math.Abs(want - got) > Tolerance)
                    diffs.Add((a, want, got));
            }
            if (best.Diffs == null || diffs.Count < best.Diffs.Count)
                best = (i - 1, addresses.Count, diffs);
        }
        return (best.Step, best.Compared, best.Diffs ?? new List<(int, int, int)>());
    }

    /// <summary>The intensity address of every patched head (head start address + the personality's dimmer offset).</summary>
    public static HashSet<int> IntensityAddresses(MagicQShow show)
    {
        var offsets = show.Personalities.ToDictionary(p => p.Id, p => p.IntensityChannel);
        var result = new HashSet<int>();
        foreach (var ch in show.Patch)
        {
            int offset = offsets.GetValueOrDefault(ch.Personality, -1);
            if (offset >= 0 && ch.ChannelInHead == offset)
                result.Add(ch.Address);
        }
        return result;
    }

    /// <summary>A short text of the differences: "1-150 want 255 got 0", the first <paramref name="max"/>.</summary>
    public static string FormatDiffs(IEnumerable<(int Address, int Expected, int Received)> diffs, int max = 8)
    {
        var list = diffs.ToList();
        string text = string.Join(", ", list.Take(max).Select(d =>
            $"{DmxAddress.Format(d.Address)} want {d.Expected} got {d.Received}"));
        return list.Count > max ? $"{text}, ... ({list.Count} in all)" : text;
    }
}
