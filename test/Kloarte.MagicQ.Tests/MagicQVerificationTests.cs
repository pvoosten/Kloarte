using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;
using Xunit.Abstractions;

namespace Kloarte.Tests;

/// <summary>The parts of MagicQ verification that need no running MagicQ.</summary>
public class MagicQVerificationTests : TestBase
{
    /// <summary>Test output.</summary>
    private readonly ITestOutputHelper _log;

    /// <summary>Creates the test class (output folder verification).</summary>
    public MagicQVerificationTests(ITestOutputHelper log) : base("verification") => _log = log;

    /// <summary>A look of one channel.</summary>
    private static Dictionary<int, int> Look(int address, int value) => new() { [address] = value };

    /// <summary>A neighbour's look does not pass: only the expected look of the step counts.</summary>
    [Fact]
    public void CompareChecksExactlyOneStep()
    {
        var (compared, diffs) = MagicQVerification.Compare(Look(1, 200), Look(1, 100), new[] { 1, 2 });
        Assert.Equal(2, compared);
        Assert.Equal((1, 100, 200), Assert.Single(diffs));
    }

    /// <summary>The tolerance of <see cref="MagicQCheck"/> applies; an unset intensity is expected at 0.</summary>
    [Fact]
    public void CompareAllowsRoundingAndChecksUnsetIntensities()
    {
        Assert.Empty(MagicQVerification.Compare(Look(1, 101), Look(1, 100), new[] { 1 }).Diffs);
        var received = new Dictionary<int, int> { [1] = 100, [2] = 50 };
        Assert.Equal((2, 0, 50), Assert.Single(MagicQVerification.Compare(received, Look(1, 100), new[] { 1, 2 }).Diffs));
    }

    /// <summary>The fade runs from the first to the last change; repeated packets don't count.</summary>
    [Fact]
    public void FadeMeterMeasuresFirstToLastChange()
    {
        var meter = new FadeMeter();
        Assert.False(meter.Add(0, Look(1, 0)));
        Assert.False(meter.Add(1, Look(1, 0)));
        Assert.True(meter.Add(2, Look(1, 10)));
        Assert.True(meter.Add(5, Look(1, 20)));
        Assert.False(meter.Add(9, Look(1, 20)));
        Assert.Equal(2, meter.FirstChange);
        Assert.Equal(5, meter.LastChange);
        Assert.Equal(3, meter.Duration);
        Assert.True(meter.Matches(3.2));
        Assert.False(meter.Matches(5));
    }

    /// <summary>Without a change the duration is 0: a snap to the same look matches 0 s.</summary>
    [Fact]
    public void FadeMeterWithoutChange()
    {
        var meter = new FadeMeter();
        meter.Add(0, Look(1, 5));
        meter.Add(1, Look(1, 5));
        Assert.Null(meter.FirstChange);
        Assert.Equal(0, meter.Duration);
        Assert.True(meter.Matches(0));
    }

    /// <summary>
    /// A Risquons-Tout stack without the cues that add nothing: the stack that is left gives, cue by cue, the output of
    /// the kept cues of the whole stack, also after saving and reading back (MagicQ plays the same looks in fewer cues).
    /// </summary>
    [Fact]
    public void StackWithoutRedundantCuesPlaysTheSameLooks()
    {
        string shw = Show("Risquons-Tout_RT2025_044.shw");
        var show = MagicQShow.Load(shw);
        var stack = show.CueStacks.First(s => s.Name == "show");
        var full = MagicQVerification.ExpectedLevels(show, stack);
        var play = MagicQVerification.StepsToPlay(show, stack, show, stack);
        // Changing steps, plus the whole chains of steps that follow on their own and the step after a mark cue.
        Assert.Superset(CueChanges.ChangingSteps(show, stack).ToHashSet(), play.ToHashSet());
        _log.WriteLine($"{play.Count} of {stack.Steps.Count} cues change the output");
        Assert.True(play.Count < stack.Steps.Count, "the stack has cues that add nothing");

        var left = MagicQVerification.KeepOnly(stack, play);
        Assert.Equal(play, left);
        string reduced = Path.Combine(_out, "reduced.shw");
        show.Save(reduced);
        foreach (var check in new[] { show, MagicQShow.Load(reduced) })
        {
            var checkStack = check.CueStacks.First(s => s.Name == "show");
            Assert.Equal(left.Count, checkStack.Steps.Count);
            var levels = MagicQVerification.ExpectedLevels(check, checkStack);
            for (int j = 0; j < left.Count; j++)
                Assert.True(CueChanges.SameOutput(full[left[j]], levels[j]), $"cue {checkStack.Steps[j].CueNumberText}");
        }
    }

    /// <summary>A played cue that changes something the expected one doesn't is played; stacks of other lengths play all.</summary>
    [Fact]
    public void StepsToPlayKeepsDifferencesOfThePlayedShow()
    {
        string shw = Show("Risquons-Tout_RT2025_044.shw");
        var expected = MagicQShow.Load(shw);
        var expectedStack = expected.CueStacks.First(s => s.Name == "show");
        var redundant = Enumerable.Range(0, expectedStack.Steps.Count)
            .Except(MagicQVerification.StepsToPlay(expected, expectedStack, expected, expectedStack)).First();
        var played = MagicQShow.Load(shw);
        var playedStack = played.CueStacks.First(s => s.Name == "show");
        var cue = played.Cues.First(c => c.Id == playedStack.Steps[redundant].CueId);
        var levels = MagicQVerification.ExpectedLevels(played, playedStack)[redundant];
        int address = cue.Channels.Count > 0 ? cue.Channels[0].Address : levels.Keys.First();
        cue.SetChannel(address, (levels.GetValueOrDefault(address) + 1) % 256);
        Assert.Contains(redundant, MagicQVerification.StepsToPlay(expected, expectedStack, played, playedStack));
        Assert.DoesNotContain(redundant, MagicQVerification.StepsToPlay(expected, expectedStack, expected, expectedStack));

        playedStack.RemoveStep(playedStack.Steps[redundant].CueId);
        Assert.Equal(Enumerable.Range(0, playedStack.Steps.Count),
            MagicQVerification.StepsToPlay(expected, expectedStack, played, playedStack));
    }

    /// <summary>The "show" stack of RT2025_044 and the index of a step by its cue number.</summary>
    private (MagicQShow Show, MagicQShow.CueStack Stack, System.Func<string, int> Step) Rt044()
    {
        var show = MagicQShow.Load(Show("Risquons-Tout_RT2025_044.shw"));
        var stack = show.CueStacks.First(s => s.Name == "show");
        var steps = stack.Steps;
        return (show, stack, cue => Enumerable.Range(0, steps.Count).Single(i => steps[i].CueNumberText == cue));
    }

    /// <summary>
    /// MagicQ PC played RT2025_044's 29.1 ("otomatic", flags 00004000) and the mark cue 29.11 (0000c000) on the Go of
    /// 27.1, and 29.13 on the Go of 29.12 (5 Nov 2026: one Go per step put MagicQVerify two cues ahead from 27.1 on, 16
    /// of 41 cues passed on the unchanged show): the steps are grouped by Go.
    /// </summary>
    [Fact]
    public void StepsThatFollowOnTheirOwnArePlayedByTheGoBefore()
    {
        var (_, stack, step) = Rt044();
        Assert.False(stack.Steps[step("29.1")].WaitsForGo);
        Assert.True(stack.Steps[step("29.11")].Mark);
        Assert.False(stack.Steps[step("29.11")].WaitsForGo);
        Assert.True(stack.Steps[step("29.12")].WaitsForGo);
        Assert.False(stack.Steps[step("29.13")].WaitsForGo);
        Assert.Equal(2, stack.Steps.Count(s => !s.WaitsForGo && !s.Mark));

        var chains = MagicQVerification.Chains(stack, Enumerable.Range(0, stack.Steps.Count).ToList());
        Assert.Equal(stack.Steps.Count - 3, chains.Count);
        Assert.Contains(chains, c => c.SequenceEqual(new[] { step("27.1"), step("29.1"), step("29.11") }));
        Assert.Contains(chains, c => c.SequenceEqual(new[] { step("29.12"), step("29.13") }));

        // Leaving out cues with the same output keeps a chain whole and the cue after a mark cue.
        var play = MagicQVerification.StepsToPlay(Rt044().Show, stack, Rt044().Show, Rt044().Stack);
        foreach (string cue in new[] { "27.1", "29.1", "29.11", "29.12", "29.13" })
            Assert.Contains(step(cue), play);
    }

    /// <summary>
    /// RT2025_044 cue 6 has In 3 s and an In delay of 3 s (field 11): MagicQ PC's output changed for 5.7 s, which
    /// MagicQVerify called a wrong fade (it expected 2.97 s); 12 cues of the stack have such a delay. The expected fade
    /// now runs from the earliest start to the latest end of what changes.
    /// </summary>
    [Fact]
    public void ExpectedFadeCountsTheInDelay()
    {
        var (show, stack, step) = Rt044();
        var levels = MagicQVerification.ExpectedLevels(show, stack);
        var intensities = MagicQCheck.IntensityAddresses(show);
        var none = new HashSet<int>();
        MagicQShow.Cue Cue(string number) => show.Cues.First(c => c.Id == stack.Steps[step(number)].CueId);
        float Fade(string number) => MagicQVerification.ExpectedFade(Cue(number), levels[step(number) - 1], levels[step(number)], intensities, none);

        Assert.Equal(new[] { 99, 99, 99, 99, 99 }, Cue("6").Times);
        Assert.Equal(new[] { 99, 0, 0, 0, 0 }, Cue("6").Delays);
        Assert.Equal(2.97f, MagicQVerification.LongestFade(Cue("6")), 2);
        Assert.Equal(5.94f, Fade("6"), 2);            // measured 5.73 s
        Assert.Equal((66 + 99) * 0.03f, Fade("7"), 2);  // In delay 2 s; measured 4.75 s
        Assert.Equal(2.97f, Fade("5"), 2);            // no delay; measured 2.76 s
        // 13 cues have an In delay; cue 2 (In delay 2 s) has no rising intensity: measured 2.73 s.
        Assert.Equal(13, stack.Steps.Count(s => show.Cues.First(c => c.Id == s.CueId).Delays[0] > 0));
        Assert.Equal(2.97f, Fade("2"), 2);
        for (int i = 1; i < stack.Steps.Count; i++)
            _log.WriteLine($"{stack.Steps[i].CueNumberText}: {Fade(stack.Steps[i].CueNumberText):0.00} s");
        // Only a rising intensity waits for the In delay.
        int dimmer = intensities.First();
        var cue = Cue("6");
        Assert.Equal(2.97f, MagicQVerification.ExpectedFade(cue, Look(dimmer, 100), Look(dimmer, 0), intensities, none), 2);
        Assert.Equal(2.97f, MagicQVerification.ExpectedFade(cue, Look(dimmer, 0), Look(dimmer, 100), intensities, none), 2);
    }

    /// <summary>
    /// RT2025_044 29.1 runs an FX on the dimmers 1-051..1-061 (cue field 4 = 1, a block after the pairs); the FX keeps
    /// running in the later cues, so those channels are not compared (FX are not simulated).
    /// </summary>
    [Fact]
    public void FxChannelsAreKnown()
    {
        var (show, stack, step) = Rt044();
        var cue = show.Cues.First(c => c.Id == stack.Steps[step("29.1")].CueId);
        Assert.Equal(Enumerable.Range(0x33, 11), cue.FxAddresses.Order());
        Assert.Empty(show.Cues.First(c => c.Id == stack.Steps[step("27.1")].CueId).FxAddresses);
        var fx = MagicQVerification.FxAddresses(show, stack);
        Assert.Empty(fx[step("27.1")]);
        Assert.Superset(cue.FxAddresses.ToHashSet(), fx[step("30.1")]);
        Assert.Equal(new[] { 1, 2 }, MagicQVerification.Without(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, new HashSet<int> { 3 }).Keys.Order());
    }

    /// <summary>
    /// A mark cue (RT2025_044 29.11, no values of its own) presets the heads that are dark after it to the next cue's
    /// other channels; lit heads keep theirs.
    /// </summary>
    [Fact]
    public void MarkCuePresetsDarkHeadsToTheNextCue()
    {
        var (show, stack, step) = Rt044();
        int mark = step("29.11");
        var plain = CueChanges.Outputs(show, stack);
        var levels = MagicQVerification.ExpectedLevels(show, stack);
        Assert.True(CueChanges.SameOutput(plain[mark - 1], plain[mark]));
        var changed = levels[mark].Where(kv => plain[mark].GetValueOrDefault(kv.Key) != kv.Value).Select(kv => kv.Key).ToList();
        _log.WriteLine($"mark cue presets {changed.Count} channels: {string.Join(", ", changed.Select(DmxAddress.Format))}");
        Assert.NotEmpty(changed);
        var intensities = MagicQCheck.IntensityAddresses(show);
        Assert.DoesNotContain(changed, intensities.Contains);
        Assert.All(changed, a => Assert.Equal(levels[mark + 1][a], levels[mark][a]));
        Assert.DoesNotContain(changed, a => show.Patch.First(p => p.Address == a).Attribute % 2 == 1);
        // 2-049 (0x231) is the T1's intensity fine: the mark cue leaves it (MagicQ PC kept it at 0, 5 Nov 2026).
        Assert.DoesNotContain(0x231, changed);
        for (int i = 0; i < mark; i++)
            Assert.True(CueChanges.SameOutput(plain[i], levels[i]), $"step {i}");
    }

    /// <summary>
    /// RT2025_044 29.13 sets the T1's CMY coarse channels (2-111 / 2-113 / 2-115) from the palette "Yellow" without their
    /// fines; MagicQ PC output the fines (2-112 / 2-114 / 2-116, recorded at 255 / 92 / 66 in 1.1) at 0 from then on,
    /// and MagicQVerify failed 8 cues on them. A cue that sets the fine itself sets it again.
    /// </summary>
    [Fact]
    public void CoarseWithoutFineOutputsTheFineAtZero()
    {
        var (show, stack, step) = Rt044();
        var plain = CueChanges.Outputs(show, stack);
        var levels = MagicQVerification.ExpectedLevels(show, stack);
        int[] fines = { 0x270, 0x272, 0x274 };
        Assert.Equal(new[] { 255, 92, 66 }, fines.Select(a => plain[step("29.13")][a]));
        Assert.Equal(new[] { 255, 92, 66 }, fines.Select(a => levels[step("29.12")][a]));
        foreach (string cue in new[] { "29.13", "29.3", "30.1", "30.4", "99" })
            Assert.All(fines, a => Assert.Equal(0, levels[step(cue)][a]));
        Assert.Equal(new[] { 0x26f, 0x271, 0x273 }, fines.Select(a => show.Patch.First(p => p.Address == a - 1).Address));
        Assert.All(fines, a => Assert.Equal(8, show.Patch.First(p => p.Address == a).Attribute & 8));
    }
}
