using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// Which steps of a cue stack change the output: a step whose DMX output (all universes, after its fade; dimmer curves
/// applied) is the same as the step before it adds nothing to see, so a test or a verification that plays a stack can
/// leave it out. The first step always changes the output (from nothing). No Godot dependencies.
/// </summary>
public static class CueChanges
{
    /// <summary>
    /// True when two outputs (address -&gt; DMX value) are the same: the same addresses with the same values. An
    /// address that only one of them sets differs, whatever its value: its default output is not known here.
    /// </summary>
    public static bool SameOutput(IReadOnlyDictionary<int, int> previous, IReadOnlyDictionary<int, int> current)
    {
        if (previous.Count != current.Count)
            return false;
        foreach (var (address, value) in current)
            if (!previous.TryGetValue(address, out int before) || before != value)
                return false;
        return true;
    }

    /// <summary>The indices of the outputs that differ from the one before them (index 0 always), in order.</summary>
    public static List<int> ChangingSteps(IReadOnlyList<IReadOnlyDictionary<int, int>> outputs) =>
        Enumerable.Range(0, outputs.Count).Where(i => i == 0 || !SameOutput(outputs[i - 1], outputs[i])).ToList();

    /// <summary>
    /// The DMX output after every step of a cue stack (tracking; palette references resolved; dimmer curves applied, as
    /// the console outputs it): [i] = after step i.
    /// </summary>
    public static List<Dictionary<int, int>> Outputs(IShowDocument show, ICueStack stack) =>
        CueTracking.LevelsPerStep(show, stack).Select(show.WithCurves).ToList();

    /// <summary>
    /// The indices of the steps of a cue stack whose output differs from the step before (step 0 always), in order.
    /// With `blocking`, a step whose cue changes the stage besides the lights (<see cref="Blocking.ChangesAt"/>) counts
    /// as a change too.
    /// </summary>
    public static List<int> ChangingSteps(IShowDocument show, ICueStack stack, Blocking blocking = null)
    {
        var outputs = Outputs(show, stack);
        return Enumerable.Range(0, outputs.Count)
            .Where(i => i == 0 || !SameOutput(outputs[i - 1], outputs[i])
                        || blocking != null && blocking.ChangesAt(stack.Steps[i].CueId))
            .ToList();
    }
}
