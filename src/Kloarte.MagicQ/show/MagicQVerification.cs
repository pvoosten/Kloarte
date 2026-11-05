using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// What a cue stack should output in MagicQ, step by step, and the check of one step: unlike
/// <see cref="MagicQCheck.BestMatch"/> over a whole stack, a wrong cue that happens to look like a neighbour does not
/// pass. Models what MagicQ PC 1.9.7.3 does with RT2025_044 beyond the cue values: steps that follow the step before
/// without a Go, mark cues, In delays and FX. No Godot dependencies.
/// </summary>
public static class MagicQVerification
{
    /// <summary>First and last time field of a cue record (In, Out, Pos, Col, Beam).</summary>
    private const int FirstTimeField = 6, LastTimeField = 10;

    /// <summary>Seconds per unit of a cue's time fields.</summary>
    private const float SecondsPerUnit = 0.03f;

    /// <summary>Indices into a cue's <see cref="MagicQShow.Cue.Times"/> and <see cref="MagicQShow.Cue.Delays"/>.</summary>
    private const int In = 0, Out = 1, Pos = 2, Beam = 4;

    /// <summary>Patch attribute flags (L field 3): intensity, coarse and fine channel of a 16-bit attribute.</summary>
    private const int IntensityFlag = 0x1, FineFlag = 0x8;

    /// <summary>
    /// The DMX expected after each step (dimmer curves applied): [i] = after step i. Two things MagicQ PC 1.9.7.3 does
    /// beyond tracking the cue values (RT2025_044, 5 Nov 2026):
    /// - a cue that sets the coarse channel of a 16-bit attribute without its fine (a palette reference: palettes hold
    ///   the coarse byte) outputs the fine at 0 (29.13 sets the T1's CMY coarse from "Yellow"; the fines recorded at
    ///   255 / 92 / 66 in 1.1 went to 0);
    /// - a mark cue (<see cref="MagicQShow.CueStack.Step.Mark"/>) presets the heads that are dark after it to the next
    ///   step's values of their other (not intensity) channels.
    /// </summary>
    public static List<Dictionary<int, int>> ExpectedLevels(IShowDocument show, ICueStack stack)
    {
        var levels = CueChanges.Outputs(show, stack);
        if (show is not MagicQShow magicq)
            return levels;
        var fineOf = new Dictionary<int, int>();
        foreach (var channel in magicq.Patch.Where(c => (c.Attribute & FineFlag) != 0))
            fineOf.TryAdd(channel.Address - 1, channel.Address);
        var cues = magicq.Cues.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var zeroFines = new HashSet<int>();
        for (int i = 0; i < levels.Count; i++)
        {
            if (cues.TryGetValue(stack.Steps[i].CueId, out var cue))
            {
                var set = cue.Channels.Select(c => c.Address).ToHashSet();
                zeroFines.ExceptWith(set);
                foreach (int coarse in set)
                    if (fineOf.TryGetValue(coarse, out int fine) && !set.Contains(fine))
                        zeroFines.Add(fine);
            }
            foreach (int fine in zeroFines)
                levels[i][fine] = 0;
        }
        var heads = Heads(magicq);
        for (int i = 0; i + 1 < levels.Count; i++)
        {
            if (stack.Steps[i] is not MagicQShow.CueStack.Step { Mark: true })
                continue;
            var marked = new Dictionary<int, int>(levels[i]);
            foreach (var (intensity, others) in heads)
                if (levels[i].GetValueOrDefault(intensity) == 0)
                    foreach (int address in others)
                        if (levels[i + 1].TryGetValue(address, out int next))
                            marked[address] = next;
            levels[i] = marked;
        }
        return levels;
    }

    /// <summary>Each patched head with a dimmer: its intensity address and its other (not intensity) addresses.</summary>
    private static List<(int Intensity, List<int> Others)> Heads(MagicQShow show)
    {
        var offsets = show.Personalities.ToDictionary(p => p.Id, p => p.IntensityChannel);
        var result = new List<(int, List<int>)>();
        foreach (var head in show.Patch.GroupBy(c => (c.HeadNumber, c.Personality)))
        {
            int offset = offsets.GetValueOrDefault(head.Key.Personality, -1);
            var intensity = head.FirstOrDefault(c => c.ChannelInHead == offset);
            var others = head.Where(c => c != intensity && (c.Attribute & IntensityFlag) == 0).Select(c => c.Address).ToList();
            if (intensity != null && others.Count > 0)
                result.Add((intensity.Address, others));
        }
        return result;
    }

    /// <summary>
    /// The steps worth playing (indices, in order): those whose output differs from the step before
    /// (<see cref="CueChanges"/>) in the expected show or in the played one (a played cue that changes something the
    /// expected one doesn't must be played to find the difference). A step that follows on its own keeps its whole
    /// chain (<see cref="Chains"/>), and a mark cue the step after it, so MagicQ plays them as in the whole stack. All
    /// steps when the two stacks differ in length.
    /// </summary>
    public static List<int> StepsToPlay(IShowDocument expected, ICueStack expectedStack, IShowDocument played, ICueStack playedStack)
    {
        if (expectedStack.Steps.Count != playedStack.Steps.Count)
            return Enumerable.Range(0, playedStack.Steps.Count).ToList();
        var play = CueChanges.ChangingSteps(expected, expectedStack)
            .Union(CueChanges.ChangingSteps(played, playedStack)).ToHashSet();
        foreach (var chain in Chains(expectedStack, Enumerable.Range(0, expectedStack.Steps.Count).ToList()))
            if (chain.Count > 1 && chain.Any(play.Contains))
                play.UnionWith(chain);
        for (int i = 0; i + 1 < expectedStack.Steps.Count; i++)
            if (play.Contains(i) && expectedStack.Steps[i] is MagicQShow.CueStack.Step { Mark: true })
                play.Add(i + 1);
        return play.Order().ToList();
    }

    /// <summary>
    /// The steps of `steps` (indices in `stack`, in order) grouped by Go: a step that waits for a Go starts a chain, the
    /// steps that follow on their own (<see cref="MagicQShow.CueStack.Step.WaitsForGo"/> false) join it. The first
    /// step always starts one.
    /// </summary>
    public static List<List<int>> Chains(ICueStack stack, IReadOnlyList<int> steps)
    {
        var chains = new List<List<int>>();
        foreach (int i in steps)
        {
            bool follows = stack.Steps[i] is MagicQShow.CueStack.Step { WaitsForGo: false };
            if (chains.Count == 0 || !follows)
                chains.Add(new List<int>());
            chains[^1].Add(i);
        }
        return chains;
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

    /// <summary>The longest fade of a cue in seconds (In, Out, Pos, Col, Beam), delays left out.</summary>
    public static float LongestFade(MagicQShow.Cue cue) =>
        Enumerable.Range(FirstTimeField, LastTimeField - FirstTimeField + 1).Max(cue.Record.GetHex) * SecondsPerUnit;

    /// <summary>
    /// How long the output changes when a cue takes it from `before` to `after`, in seconds: from the earliest start
    /// to the latest end of the parts that change (rising intensities: In time and delay; falling: Out; other channels:
    /// Pos / Col / Beam, which channel is which is not known here, so their shortest delay and longest end). Addresses
    /// in `ignore` (FX) are left out.
    /// </summary>
    public static float ExpectedFade(MagicQShow.Cue cue, IReadOnlyDictionary<int, int> before, IReadOnlyDictionary<int, int> after,
        ISet<int> intensities, ISet<int> ignore)
    {
        bool rise = false, fall = false, other = false;
        foreach (int address in before.Keys.Union(after.Keys))
        {
            if (ignore.Contains(address))
                continue;
            int from = before.GetValueOrDefault(address), to = after.GetValueOrDefault(address);
            if (Math.Abs(from - to) <= MagicQCheck.Tolerance)
                continue;
            if (!intensities.Contains(address))
                other = true;
            else if (to > from)
                rise = true;
            else
                fall = true;
        }
        var times = cue.Times;
        var delays = cue.Delays;
        var parts = new List<int>();
        if (rise) parts.Add(In);
        if (fall) parts.Add(Out);
        if (other) parts.AddRange(Enumerable.Range(Pos, Beam - Pos + 1));
        if (parts.Count == 0)
            return 0;
        int start = parts.Min(p => delays[p]), end = parts.Max(p => delays[p] + times[p]);
        return (end - start) * SecondsPerUnit;
    }

    /// <summary>
    /// The addresses an FX runs on after each step (FX are not simulated; their output never settles): [i] = the FX
    /// addresses of the cues of steps 0..i. An FX is taken to run on until the end of the stack.
    /// </summary>
    public static List<HashSet<int>> FxAddresses(MagicQShow show, ICueStack stack)
    {
        var cues = show.Cues.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var running = new HashSet<int>();
        var result = new List<HashSet<int>>();
        foreach (var step in stack.Steps)
        {
            if (cues.TryGetValue(step.CueId, out var cue))
                running.UnionWith(cue.FxAddresses);
            result.Add(new HashSet<int>(running));
        }
        return result;
    }

    /// <summary>Compares the received DMX with the look of exactly one step.</summary>
    public static (int Compared, List<(int Address, int Expected, int Received)> Diffs) Compare(
        IReadOnlyDictionary<int, int> received, Dictionary<int, int> expected, IEnumerable<int> intensityAddresses)
    {
        var (_, compared, diffs) = MagicQCheck.BestMatch(received, new[] { expected }, intensityAddresses);
        return (compared, diffs);
    }

    /// <summary>A look without the addresses in `ignore`.</summary>
    public static Dictionary<int, int> Without(IReadOnlyDictionary<int, int> look, ISet<int> ignore) =>
        look.Where(kv => !ignore.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
}
