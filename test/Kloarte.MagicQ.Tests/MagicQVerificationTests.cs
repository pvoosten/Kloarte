using System.Collections.Generic;
using System.Linq;
using Kloarte.MagicQ;
using Xunit;

namespace Kloarte.Tests;

/// <summary>The parts of MagicQ verification that need no running MagicQ.</summary>
public class MagicQVerificationTests
{
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
}
