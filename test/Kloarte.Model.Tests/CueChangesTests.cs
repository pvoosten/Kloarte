using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary><see cref="CueChanges"/>: which steps of a cue stack change the output.</summary>
public sealed class CueChangesTests
{
    /// <summary>An output of address / value pairs.</summary>
    private static Dictionary<int, int> Output(params (int Address, int Value)[] values) =>
        values.ToDictionary(v => v.Address, v => v.Value);

    /// <summary>Same addresses with the same values are the same output; an extra address differs, even at 0.</summary>
    [Fact]
    public void SameOutputNeedsTheSameAddressesAndValues()
    {
        Assert.True(CueChanges.SameOutput(Output((1, 10), (2, 0)), Output((2, 0), (1, 10))));
        Assert.True(CueChanges.SameOutput(Output(), Output()));
        Assert.False(CueChanges.SameOutput(Output((1, 10)), Output((1, 11))));
        Assert.False(CueChanges.SameOutput(Output((1, 10)), Output((1, 10), (2, 0))));
        Assert.False(CueChanges.SameOutput(Output((1, 10), (2, 0)), Output((1, 10), (3, 0))));
    }

    /// <summary>The first output always changes; a repeat of the one before does not.</summary>
    [Fact]
    public void ChangingStepsOfOutputs()
    {
        var outputs = new[] { Output((1, 5)), Output((1, 5)), Output((1, 6)), Output((1, 6)), Output((1, 5)) };
        Assert.Equal(new[] { 0, 2, 4 }, CueChanges.ChangingSteps(outputs));
        Assert.Empty(CueChanges.ChangingSteps(new List<Dictionary<int, int>>()));
    }

    /// <summary>
    /// A cue stack: a cue that repeats the tracked look and an empty cue add nothing; a new channel and a level change
    /// do.
    /// </summary>
    [Fact]
    public void ChangingStepsOfACueStack()
    {
        var show = new DesignShow();
        show.Rig.Types.Add(DesignTypes.Generic());
        show.Rig.Heads.Add(new DesignHead { Number = 1, Name = "Front L", Type = DesignTypes.GenericName, Address = 1 });
        show.Rig.Heads.Add(new DesignHead { Number = 2, Name = "Front R", Type = DesignTypes.GenericName, Address = 2 });
        show.CueOrder.Add(Cue(1, (1, 50)));
        show.CueOrder.Add(Cue(2, (1, 50)));             // the tracked level again
        show.CueOrder.Add(Cue(3));                      // nothing
        show.CueOrder.Add(Cue(4, (2, 100)));            // a new channel
        show.CueOrder.Add(Cue(5, (1, 50), (2, 100)));   // all as before
        show.CueOrder.Add(Cue(6, (1, 0)));              // a level change
        show.Changed();
        IShowDocument document = show;
        var stack = document.CueStacks.Single();
        Assert.Equal(new[] { 0, 3, 5 }, CueChanges.ChangingSteps(show, stack));

        // The outputs are those of LevelsAfter, with the dimmer curves.
        var outputs = CueChanges.Outputs(show, stack);
        Assert.Equal(6, outputs.Count);
        for (int i = 0; i < outputs.Count; i++)
            Assert.Equal(show.WithCurves(CueTracking.LevelsAfter(show, stack, i)).OrderBy(kv => kv.Key), outputs[i].OrderBy(kv => kv.Key));

        // A cue that changes the stage besides the lights (smoke, an actor, a set piece) changes the output too.
        var blocking = new Blocking();
        blocking.Smoke[3] = new CueSmoke();
        Assert.Equal(new[] { 0, 2, 3, 5 }, CueChanges.ChangingSteps(show, stack, blocking));
        Assert.True(blocking.ChangesAt(3));
        Assert.False(blocking.ChangesAt(2));
    }

    /// <summary>A cue that sets intensities (0..100 %) of heads.</summary>
    private static DesignCue Cue(int number, params (int Head, double Level)[] levels)
    {
        var cue = new DesignCue { Cue = number.ToString(System.Globalization.CultureInfo.InvariantCulture), Id = number };
        foreach (var (head, level) in levels)
            cue.Heads[head.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(level) };
        return cue;
    }
}
