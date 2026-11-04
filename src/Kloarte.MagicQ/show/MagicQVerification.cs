using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// What a cue stack should output in MagicQ, step by step, and the check of one step: unlike
/// <see cref="MagicQCheck.BestMatch"/> over a whole stack, a wrong cue that happens to look like a neighbour does not
/// pass. No Godot dependencies.
/// </summary>
public static class MagicQVerification
{
    /// <summary>First and last time field of a cue record (In, Out, Pos, Col, Beam).</summary>
    private const int FirstTimeField = 6, LastTimeField = 10;

    /// <summary>Seconds per unit of a cue's time fields.</summary>
    private const float SecondsPerUnit = 0.03f;

    /// <summary>The DMX expected after each step (dimmer curves applied): [i] = after step i.</summary>
    public static List<Dictionary<int, int>> ExpectedLevels(IShowDocument show, ICueStack stack) =>
        Enumerable.Range(0, stack.Steps.Count)
            .Select(i => show.WithCurves(CueTracking.LevelsAfter(show, stack, i)))
            .ToList();

    /// <summary>The longest fade of a cue in seconds (In, Out, Pos, Col, Beam).</summary>
    public static float LongestFade(MagicQShow.Cue cue) =>
        Enumerable.Range(FirstTimeField, LastTimeField - FirstTimeField + 1).Max(cue.Record.GetHex) * SecondsPerUnit;

    /// <summary>Compares the received DMX with the look of exactly one step.</summary>
    public static (int Compared, List<(int Address, int Expected, int Received)> Diffs) Compare(
        IReadOnlyDictionary<int, int> received, Dictionary<int, int> expected, IEnumerable<int> intensityAddresses)
    {
        var (_, compared, diffs) = MagicQCheck.BestMatch(received, new[] { expected }, intensityAddresses);
        return (compared, diffs);
    }
}
