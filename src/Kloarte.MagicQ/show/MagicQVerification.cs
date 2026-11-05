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
        CueChanges.Outputs(show, stack);

    /// <summary>
    /// The steps worth playing (indices, in order): those whose output differs from the step before
    /// (<see cref="CueChanges"/>) in the expected show or in the played one (a played cue that changes something the
    /// expected one doesn't must be played to find the difference). All steps when the two stacks differ in length.
    /// </summary>
    public static List<int> StepsToPlay(IShowDocument expected, ICueStack expectedStack, IShowDocument played, ICueStack playedStack)
    {
        if (expectedStack.Steps.Count != playedStack.Steps.Count)
            return Enumerable.Range(0, playedStack.Steps.Count).ToList();
        return CueChanges.ChangingSteps(expected, expectedStack)
            .Union(CueChanges.ChangingSteps(played, playedStack))
            .Order().ToList();
    }

    /// <summary>
    /// Removes the steps of a stack that are not in `keep` (indices in cue-number order) with
    /// <see cref="MagicQShow.CueStack.RemoveStep"/>; the cue records stay. A step whose cue another step of the stack
    /// plays too stays (removal goes by cue id). Returns the indices of the steps that are left, in order.
    /// </summary>
    public static List<int> KeepOnly(MagicQShow.CueStack stack, IReadOnlyCollection<int> keep)
    {
        var steps = stack.Steps;
        var shared = steps.GroupBy(s => s.CueId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var left = new List<int>();
        for (int i = 0; i < steps.Count; i++)
        {
            if (keep.Contains(i) || shared.Contains(steps[i].CueId))
                left.Add(i);
            else
                stack.RemoveStep(steps[i].CueId);
        }
        return left;
    }

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
