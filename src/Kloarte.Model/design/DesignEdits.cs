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

    /// <summary>
    /// The colour mixing values (RGB(W) emitters) that keep a head's colour when colour wheel `key` gets `value`: when the
    /// wheel leaves a slot that filters (a preset of a virtual colour wheel) for a value without one ("Raw DMX"), the
    /// emitters mix the colour the slot gave (as far as they reach: the same hue, at most as bright). Empty when there is
    /// nothing to keep. `current`: the head's (resolved) value of a key now; null = not set (the GDTF default).
    /// </summary>
    public static Dictionary<string, LookValue> KeepColour(HeadLayout layout, string key, LookValue value, System.Func<string, LookValue> current)
    {
        var kept = new Dictionary<string, LookValue>();
        var channel = layout?[key];
        if (layout?.Fixture == null || layout.Mode == null || channel?.Gdtf == null || !key.StartsWith("Color")
            || channel.ToDmx(value) is not { } next)
            return kept;
        var function = channel.Gdtf.FunctionAt(next);
        if (function == null || string.IsNullOrEmpty(function.Wheel) || function.SetAt(next)?.Slot > 1)
            return kept;
        var emitters = new[] { "ColorAdd_R", "ColorAdd_G", "ColorAdd_B", "ColorAdd_W", "ColorAdd_RY", "ColorAdd_GY" }
            .Where(k => layout[k] != null).ToList();
        if (emitters.Count == 0)
            return kept;
        var dmx = new Dictionary<int, int>();
        foreach (var c in layout.Channels)
            if (current(c.Key) is { } v && c.ToDmx(v) is { } d)
                c.Write(dmx, 1, d);
        // Colour Mix control not set by the cue: the slot is the colour that was picked, also where its home value hides it.
        if (layout.Mode.Channel("ColorMixMode") is { } mix && !dmx.ContainsKey(mix.Offsets[0]))
            mix.Write(dmx, 1, 0);
        var before = GdtfDecoder.Decode(layout.Fixture, layout.Mode, dmx, 1);
        channel.Write(dmx, 1, next);
        var after = GdtfDecoder.Decode(layout.Fixture, layout.Mode, dmx, 1);
        if (System.Math.Abs(before.R - after.R) + System.Math.Abs(before.G - after.G) + System.Math.Abs(before.B - after.B) < 1e-3f)
            return kept;
        // CTO and the other wheels stay: the mix changes by what the slot did.
        var (r, g, b) = GdtfDecoder.Mix(layout.Mode, dmx, 1);
        static float Kept(float mix, float was, float now) => now > 1e-4f ? mix * was / now : 0;
        if (!GdtfDecoder.SetMix(layout.Mode, dmx, 1, Kept(r, before.R, after.R), Kept(g, before.G, after.G), Kept(b, before.B, after.B)))
            return kept;
        foreach (string k in emitters)
            if (layout[k].Read(dmx, 1) is { } d)
                kept[k] = layout[k].FromDmx(d);
        return kept;
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

    /// <summary>
    /// The cues in which a head's intensity is above 0 (its own value, a palette's value resolved), in cue order; empty
    /// when it is 0 everywhere (a cue that doesn't set it leaves it as the cues before: 0 when no cue sets it). Null when
    /// the head has no intensity channel.
    /// </summary>
    public static List<DesignCue> CuesLighting(DesignShow show, int head)
    {
        if (show.LayoutOf(head)?.Intensity is not { } channel)
            return null;
        return show.CueOrder.Where(c => c.Of(head)?.GetValueOrDefault(channel.Key) is { } value
                                        && Resolved(show, head, channel.Key, value) is { } level
                                        && channel.ToDmx(level) is > 0)
            .ToList();
    }

    /// <summary>
    /// Forgets a head that no lamp hangs on any more: its values in every cue and palette, its place in the groups and
    /// its patch. False (and nothing changed) when a lamp still uses it. The caller calls <see cref="DesignShow.Changed"/>.
    /// </summary>
    public static bool ForgetHead(DesignShow show, int head)
    {
        if (show.Rig.Items.Any(i => i.IsLamp && i.Head == head))
            return false;
        string key = DesignShow.Key(head);
        foreach (var cue in show.CueOrder)
            cue.Heads.Remove(key);
        foreach (var palette in show.Palettes)
            palette.Heads.Remove(key);
        foreach (var group in show.Rig.Groups.Where(g => g.Heads?.Contains(head) == true))
            group.Heads = group.Heads.Where(h => h != head).ToArray();
        show.Rig.Heads.RemoveAll(h => h.Number == head);
        return true;
    }

    /// <summary>The intensity values (in %) of every head with an intensity channel at 0: a blackout.</summary>
    public static Dictionary<string, Dictionary<string, LookValue>> Blackout(DesignShow show) =>
        show.Rig.Heads.Where(h => show.LayoutOf(h)?.Intensity != null)
            .ToDictionary(h => DesignShow.Key(h.Number), _ => new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(0) });
}
