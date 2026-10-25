using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// RemoteTools: the values a tool sets into a cue or a palette, read from its arguments (<c>values</c>: per head, per key;
/// or <c>heads</c> + <c>set</c>: the same values for every head named) and checked against each head's channels, so a
/// wrong key or value is an error before anything changes.
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>The description of the values arguments, shared by the tools.</summary>
    private const string ValuesHelp =
        "Per head number, the channel values: {\"101\": {\"intensity\": 80, \"Pan\": -12.5, \"Color1\": \"Red\"}}. A " +
        "value is a number (intensity %, the attribute's physical unit), a slot / set name, \"dmx 37\" (exact DMX), " +
        "\"@Palette\" (cues only) or null (removes the value: it tracks again). Keys: see get_head.";

    /// <summary>One value to set: head, key (as the head's layout names it), value (null = remove).</summary>
    private readonly record struct HeadValue(int Head, string Key, LookValue Value);

    /// <summary>
    /// The values the arguments give (<c>values</c>, and <c>heads</c> + <c>set</c>), checked: the head exists, the key is one
    /// of its channels (matched without case), the value reads on that channel, a palette exists (and only when
    /// `palettes` allows them). Throws on the first problem.
    /// </summary>
    private static List<HeadValue> ValuesOf(DesignShow show, ToolArgs args, bool palettes)
    {
        var result = new List<HeadValue>();
        if (args.Object("values") is { } values)
            foreach (var head in values.EnumerateObject())
            {
                if (!int.TryParse(head.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new ArgumentException($"values: '{head.Name}' is not a head number (use heads + set for a group)");
                if (head.Value.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException($"values: head {head.Name} needs an object of channel values");
                foreach (var channel in head.Value.EnumerateObject())
                    result.Add(Checked(show, number, channel.Name, channel.Value, palettes));
            }
        var heads = HeadsOf(show, args);
        if (args.Object("set") is { } set)
        {
            if (heads.Count == 0)
                throw new ArgumentException("'set' needs 'heads' (head numbers or group names)");
            foreach (var channel in set.EnumerateObject())
            {
                var found = heads.Select(h => Checked(show, h, channel.Name, channel.Value, palettes, lenient: true))
                    .Where(v => v.Key != null).ToList();
                if (found.Count == 0)
                    Checked(show, heads[0], channel.Name, channel.Value, palettes);   // throws: no head named has the channel
                result.AddRange(found);
            }
        }
        else if (heads.Count > 0)
            throw new ArgumentException("'heads' needs 'set' (the channel values for those heads)");
        if (result.Count == 0)
            throw new ArgumentException("No values given (use 'values', or 'heads' with 'set')");
        return result;
    }

    /// <summary>
    /// One value, checked against the head's channel. `lenient` (the same values for many heads): a head without that
    /// channel is skipped (Key null) when another named head has it.
    /// </summary>
    private static HeadValue Checked(DesignShow show, int head, string key, JsonElement value, bool palettes, bool lenient = false)
    {
        var layout = show.LayoutOf(head) ?? throw new ArgumentException($"No head {head}");
        var channel = layout[key] ?? layout.Channels.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (channel == null)
        {
            if (lenient)
                return default;
            throw new ArgumentException(
                $"Head {head} has no channel '{key}' (keys: {string.Join(", ", layout.Channels.Select(c => c.Key))}, or #n for channel n)");
        }
        if (value.ValueKind == JsonValueKind.Null)
            return new HeadValue(head, channel.Key, null);
        LookValue look;
        try
        {
            look = value.ValueKind switch
            {
                JsonValueKind.Number => LookValue.Of(value.GetDouble()),
                JsonValueKind.String => LookValue.Parse(value.GetString()),
                _ => throw new FormatException("give a number, a text or null"),
            };
        }
        catch (FormatException e)
        {
            throw new ArgumentException($"Head {head} {channel.Key}: {e.Message}");
        }
        if (look.IsPalette)
        {
            if (!palettes)
                throw new ArgumentException($"Head {head} {channel.Key}: a palette can't refer to a palette");
            DesignPalette palette;
            try
            {
                palette = PaletteOf(show, look.Palette);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException($"Head {head} {channel.Key}: {e.Message}");
            }
            // the palette's own name, so "@centre" refers to palette "Centre"
            return new HeadValue(head, channel.Key, LookValue.OfPalette(palette.Name));
        }
        if (channel.ToDmx(look) == null)
            throw new ArgumentException($"Head {head} {channel.Key}: '{look}' is not a value of this channel ({Describe(channel)})");
        return new HeadValue(head, channel.Key, look);
    }

    /// <summary>What values a channel takes, for messages and get_head.</summary>
    private static string Describe(HeadChannel channel)
    {
        var parts = new List<string>();
        switch (channel.Kind)
        {
            case HeadChannelKind.Intensity:
                parts.Add("0..100 %");
                break;
            case HeadChannelKind.Raw:
                parts.Add($"DMX 0..{channel.Max}");
                break;
        }
        if (channel.Range is { } range)
            parts.Add($"{LookValue.Number(range.From)}..{LookValue.Number(range.To)}");
        if (channel.SetNames.Count > 0)
            parts.Add("sets: " + string.Join(", ", channel.SetNames.Take(24)) + (channel.SetNames.Count > 24 ? ", ..." : ""));
        parts.Add("or \"dmx n\"");
        return string.Join("; ", parts);
    }

    /// <summary>Puts checked values into a set of looks.</summary>
    private static void Apply(Dictionary<string, Dictionary<string, LookValue>> looks, IEnumerable<HeadValue> values)
    {
        foreach (var value in values)
            DesignEdits.Set(looks, value.Head, value.Key, value.Value);
    }

    /// <summary>A short text of what values were set ("3 values of heads 101, 102").</summary>
    private static string Summary(IReadOnlyCollection<HeadValue> values)
    {
        var heads = values.Select(v => v.Head).Distinct().ToList();
        string list = string.Join(", ", heads.Take(8)) + (heads.Count > 8 ? ", ..." : "");
        return $"{values.Count} value{(values.Count == 1 ? "" : "s")} of head{(heads.Count == 1 ? "" : "s")} {list}";
    }
}
