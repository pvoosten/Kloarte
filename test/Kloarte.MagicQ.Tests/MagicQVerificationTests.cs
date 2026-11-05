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
        Assert.Equal(CueChanges.ChangingSteps(show, stack), play);
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
        var redundant = Enumerable.Range(0, expectedStack.Steps.Count).Except(CueChanges.ChangingSteps(expected, expectedStack)).First();
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
}
