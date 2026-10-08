using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// Cue numbers and ids of a show for the blocking files: the number of a cue id, the id of a typed number and the cue
/// order. Stacks are taken in the order the playback prefers (the stack called "show", then the one with most cues), so a
/// cue in several stacks gets the number it has in that one.
/// </summary>
public sealed class CueIndex
{
    /// <summary>cue id -> number text, and the order of the cue ids.</summary>
    private readonly Dictionary<int, string> _numbers = new();
    private readonly Dictionary<int, int> _order = new();

    /// <summary>(number, id) in preferred order, to find a typed number.</summary>
    private readonly List<(float Number, int Id)> _steps = new();

    /// <summary>The cues of a show (null = none: numbers stay empty).</summary>
    public static CueIndex Of(IShowDocument show)
    {
        var index = new CueIndex();
        var stacks = (show?.CueStacks ?? Array.Empty<ICueStack>())
            .OrderByDescending(s => s.Name.Equals("show", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => s.Steps.Count);
        foreach (var stack in stacks)
            foreach (var step in stack.Steps)
            {
                if (index._numbers.TryAdd(step.CueId, step.CueNumberText))
                    index._order[step.CueId] = index._order.Count;
                index._steps.Add((step.CueNumber, step.CueId));
            }
        return index;
    }

    /// <summary>The cue number of a cue id ("" when no stack has it).</summary>
    public string NumberOf(int cueId) => _numbers.GetValueOrDefault(cueId, "");

    /// <summary>The cue id of a typed cue number ("12.5", "12.50"), or null.</summary>
    public int? IdOf(string number)
    {
        if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float n))
            return null;
        foreach (var (stepNumber, id) in _steps)
            if (Math.Abs(stepNumber - n) < 0.0005f)
                return id;
        return null;
    }

    /// <summary>Sort key of a cue id: its place in the preferred stacks, cues of no stack last (by id).</summary>
    public long Order(int cueId) => _order.TryGetValue(cueId, out int o) ? o : int.MaxValue + (long)cueId;
}
