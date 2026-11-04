using System;
using System.Collections.Generic;
using System.Linq;

namespace Kloarte.MagicQ;

/// <summary>
/// Measures a fade from timed samples of the output: from the first to the last sample that differs from the one
/// before it. No Godot dependencies.
/// </summary>
public sealed class FadeMeter
{
    /// <summary>The last sample.</summary>
    private IReadOnlyDictionary<int, int> _last;

    /// <summary>Time of the first change, in seconds, or null.</summary>
    public double? FirstChange { get; private set; }

    /// <summary>Time of the last change, in seconds, or null.</summary>
    public double? LastChange { get; private set; }

    /// <summary>Seconds from the first to the last change; 0 without a change.</summary>
    public double Duration => FirstChange is { } first && LastChange is { } last ? last - first : 0;

    /// <summary>Adds a sample taken at time `seconds`; true when it differs from the one before.</summary>
    public bool Add(double seconds, IReadOnlyDictionary<int, int> sample)
    {
        bool changed = _last != null && Differs(_last, sample);
        _last = sample;
        if (changed)
        {
            FirstChange ??= seconds;
            LastChange = seconds;
        }
        return changed;
    }

    /// <summary>True when the measured duration is the expected one, within 10 % or `margin` seconds.</summary>
    public bool Matches(double expected, double margin = 0.25) =>
        Math.Abs(Duration - expected) <= Math.Max(expected * 0.1, margin);

    /// <summary>True when a channel has another value in the two samples.</summary>
    private static bool Differs(IReadOnlyDictionary<int, int> a, IReadOnlyDictionary<int, int> b) =>
        a.Count != b.Count || a.Any(kv => b.GetValueOrDefault(kv.Key) != kv.Value);
}
