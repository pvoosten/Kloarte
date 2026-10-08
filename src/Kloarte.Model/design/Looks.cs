using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The values a cue or a palette sets, per head number (a text key, as in the files) and channel key: copies and
/// comparisons.
/// </summary>
public static class Looks
{
    /// <summary>A deep copy (the values themselves are immutable).</summary>
    public static Dictionary<string, Dictionary<string, LookValue>> Copy(Dictionary<string, Dictionary<string, LookValue>> looks) =>
        looks?.ToDictionary(h => h.Key, h => new Dictionary<string, LookValue>(h.Value)) ?? new();

    /// <summary>True when both set the same values for the same heads.</summary>
    public static bool Same(Dictionary<string, Dictionary<string, LookValue>> a, Dictionary<string, Dictionary<string, LookValue>> b)
    {
        a ??= new();
        b ??= new();
        if (a.Count(h => h.Value.Count > 0) != b.Count(h => h.Value.Count > 0))
            return false;
        foreach (var (head, values) in a.Where(h => h.Value.Count > 0))
        {
            if (!b.TryGetValue(head, out var other) || other.Count != values.Count)
                return false;
            foreach (var (key, value) in values)
                if (!other.TryGetValue(key, out var v) || !Equals(v, value))
                    return false;
        }
        return true;
    }
}
