using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// Edits of the values of a native show's cues and palettes, shared by its editor, the cue table and the files: a value
/// by key, a DMX value by address, and the tracked value of a channel at a cue. The caller calls
/// <see cref="DesignShow.Changed"/> afterwards.
/// </summary>
public static class DesignEdits
{
    /// <summary>Sets (or with null, removes) the value of a head's channel in a set of looks.</summary>
    public static void Set(Dictionary<string, Dictionary<string, LookValue>> looks, int head, string key, LookValue value)
    {
        string h = DesignShow.Key(head);
        if (!looks.TryGetValue(h, out var values))
        {
            if (value == null)
                return;
            looks[h] = values = new Dictionary<string, LookValue>();
        }
        if (value == null)
            values.Remove(key);
        else
            values[key] = value;
        if (values.Count == 0)
            looks.Remove(h);
    }

    /// <summary>
    /// Sets a DMX value at an address in a set of looks: as the value of its head's channel (a channel of one byte), as
    /// that byte alone ("#n", a byte of a 16-bit channel), or as a DMX value of no head. False when nothing changed.
    /// </summary>
    public static bool SetDmx(DesignShow show, Dictionary<string, Dictionary<string, LookValue>> looks, Dictionary<string, int> loose,
        int address, int value)
    {
        var head = show.HeadCovering(address);
        if (head == null)
        {
            if (loose == null)
                return false;
            loose[address.ToString(System.Globalization.CultureInfo.InvariantCulture)] = value;
            return true;
        }
        var layout = show.LayoutOf(head);
        int offset = address - head.Address + 1;
        var channel = layout.AtOffset(offset);
        if (channel != null && channel.Offsets.Length == 1)
            Set(looks, head.Number, channel.Key, channel.FromDmx(value));
        else
            Set(looks, head.Number, "#" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture), LookValue.Of(value));
        return true;
    }

    /// <summary>
    /// The value a head's channel has after cue `index` of the cue list (tracking: the last cue up to it that sets it),
    /// and the index of that cue; (null, -1) when no cue sets it.
    /// </summary>
    public static (LookValue Value, int Cue) Tracked(DesignShow show, int index, int head, string key)
    {
        for (int i = System.Math.Min(index, show.CueOrder.Count - 1); i >= 0; i--)
            if (show.CueOrder[i].Of(head)?.GetValueOrDefault(key) is { } value)
                return (value, i);
        return (null, -1);
    }

    /// <summary>The value a palette reference stands for (the palette's value for that head and key), or the value itself.</summary>
    public static LookValue Resolved(DesignShow show, int head, string key, LookValue value) =>
        value is { IsPalette: true } ? show.Palette(value.Palette)?.Of(head)?.GetValueOrDefault(key) : value;

    /// <summary>A free cue number after cue `index` (before the next one), with as few decimals as possible; null when none.</summary>
    public static float? NumberAfter(DesignShow show, int index)
    {
        float? current = index >= 0 && index < show.CueOrder.Count ? show.CueOrder[index].Number : null;
        float? next = index + 1 < show.CueOrder.Count ? show.CueOrder[index + 1].Number : null;
        if (index < 0 && show.CueOrder.Count > 0)
            next = show.CueOrder[0].Number;
        return CueNumbers.Between(current, next);
    }

    /// <summary>The intensity values (in %) of every head with an intensity channel at 0: a blackout.</summary>
    public static Dictionary<string, Dictionary<string, LookValue>> Blackout(DesignShow show) =>
        show.Rig.Heads.Where(h => show.LayoutOf(h)?.Intensity != null)
            .ToDictionary(h => DesignShow.Key(h.Number), _ => new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(0) });
}
