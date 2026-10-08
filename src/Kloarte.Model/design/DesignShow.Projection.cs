using System;
using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

public sealed partial class DesignShow
{
    /// <summary>The DMX channels of each cue (this version).</summary>
    private readonly Dictionary<DesignCue, IReadOnlyList<DesignChannel>> _projections = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The DMX channels a cue sets, in address order: its values through each head's layout (palette references take
    /// the palette's value and say so), then the channels of no head. Values that mean nothing for their head are left
    /// out (<see cref="Problems"/> lists them). Cached until the next <see cref="Changed"/>.
    /// </summary>
    public IReadOnlyList<DesignChannel> ChannelsOf(DesignCue cue)
    {
        if (cue == null)
            return Array.Empty<DesignChannel>();
        if (!_projections.TryGetValue(cue, out var channels))
            _projections[cue] = channels = Project(cue.Heads, cue.Dmx, null, true);
        return channels;
    }

    /// <summary>The DMX channels a palette holds, in address order.</summary>
    public IReadOnlyList<DesignChannel> ChannelsOf(DesignPalette palette) =>
        palette == null ? Array.Empty<DesignChannel>() : Project(palette.Heads, null, null, false);

    /// <summary>The DMX values (address -&gt; value) of values per head (e.g. what a view is about to set).</summary>
    public Dictionary<int, int> DmxOf(Dictionary<string, Dictionary<string, LookValue>> heads) =>
        Project(heads, null, null, true).ToDictionary(c => c.Address, c => c.Value);

    /// <summary>
    /// What doesn't mean anything in the cues and palettes: a head the rig doesn't have, a key the head has no channel
    /// for, a value its channel can't take, a palette that isn't there or has no value for that channel. One line each
    /// ("cue 3, head 201: no channel 'Foo'").
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        foreach (var cue in CueOrder)
            Project(cue.Heads, cue.Dmx, m => problems.Add($"cue {cue.Cue}, {m}"), true);
        foreach (var palette in Palettes)
            Project(palette.Heads, null, m => problems.Add($"palette {palette.Name}, {m}"), false);
        return problems;
    }

    /// <summary>
    /// The DMX channels of values per head (and DMX values of addresses), in address order. Raw single channels ("#n")
    /// are written after the head's other values (they win). `palettes` false: palette references are a problem.
    /// </summary>
    private List<DesignChannel> Project(Dictionary<string, Dictionary<string, LookValue>> heads, Dictionary<string, int> dmx,
        Action<string> problem, bool palettes)
    {
        var bytes = new Dictionary<int, (int Value, string Palette)>();
        var buffer = new Dictionary<int, int>();
        foreach (var (key, values) in heads ?? new())
        {
            if (values == null || values.Count == 0)
                continue;
            var head = HeadOfKey(key) is { } number ? Head(number) : null;
            if (head == null)
            {
                problem?.Invoke($"head {key}: the rig has no such head");
                continue;
            }
            var layout = LayoutOf(head);
            foreach (var (channelKey, value) in values.OrderBy(v => v.Key.StartsWith('#') ? 1 : 0))
            {
                var channel = layout[channelKey];
                if (channel == null)
                {
                    problem?.Invoke($"head {key}: no channel '{channelKey}'");
                    continue;
                }
                var resolved = value;
                string from = null;
                if (value is { IsPalette: true })
                {
                    if (!palettes)
                    {
                        problem?.Invoke($"head {key}, {channelKey}: a palette can't refer to a palette");
                        continue;
                    }
                    var palette = Palette(value.Palette);
                    resolved = palette?.Of(head.Number)?.GetValueOrDefault(channelKey);
                    if (resolved == null || resolved.IsPalette)
                    {
                        problem?.Invoke(palette == null ? $"head {key}, {channelKey}: no palette '{value.Palette}'"
                            : $"head {key}, {channelKey}: palette '{value.Palette}' has no value for it");
                        continue;
                    }
                    from = palette.Name;
                }
                if (channel.ToDmx(resolved) is not { } level)
                {
                    problem?.Invoke($"head {key}, {channelKey}: '{resolved}' means nothing for this channel");
                    continue;
                }
                buffer.Clear();
                channel.Write(buffer, head.Address, level);
                foreach (var (address, b) in buffer)
                    bytes[address] = (b, from);
            }
        }
        foreach (var (key, value) in dmx ?? new())
            if (HeadOfKey(key) is { } address)
                bytes[address] = (Math.Clamp(value, 0, 255), null);
            else
                problem?.Invoke($"dmx {key}: not a DMX address");
        return bytes.OrderBy(b => b.Key).Select(b => new DesignChannel(b.Key, b.Value.Value, b.Value.Palette)).ToList();
    }
}
