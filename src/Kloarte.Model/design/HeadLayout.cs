using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The channels of a head as cues and palettes set them (<see cref="HeadChannel"/>), by key. A head whose fixture type
/// has a GDTF mode with its channel count gets the GDTF channels (key = the attribute, "Pan", "ColorSub_C"; 16-bit
/// channels are one key); any other head gets one key per channel (its name). The first intensity channel is
/// "intensity" (in %). Every channel can also be set alone as "#n" (channel n of the head, raw DMX), which reads and
/// writes one byte.
/// </summary>
public sealed class HeadLayout
{
    /// <summary>The channels by key, in DMX order.</summary>
    private readonly Dictionary<string, HeadChannel> _channels = new();

    /// <summary>The GDTF fixture type (moving heads), or null.</summary>
    public GdtfFixture Fixture { get; }

    /// <summary>The GDTF mode, or null.</summary>
    public GdtfMode Mode { get; }

    /// <summary>The number of channels of the head.</summary>
    public int Count { get; }

    /// <summary>The channels in DMX order (without the "#n" ones).</summary>
    public IReadOnlyList<HeadChannel> Channels { get; }

    /// <summary>The layout of a head of this fixture type and channel count; the GDTF fixture and mode, if any.</summary>
    public HeadLayout(DesignPersonality type, int count, GdtfFixture fixture, GdtfMode mode)
    {
        Count = count;
        type ??= new DesignPersonality();
        int intensity = type.IntensityOffset;
        var list = new List<HeadChannel>();
        var covered = new HashSet<int>();
        if (fixture != null && mode != null && mode.ChannelCount == count)
        {
            Fixture = fixture;
            Mode = mode;
            foreach (var ch in mode.Channels.Where(c => c.Offsets.All(o => o >= 1 && o <= count)))
            {
                bool isIntensity = ch.Offsets[0] - 1 == intensity;
                string key = isIntensity ? "intensity" : Unique(ch.Attribute.Length > 0 ? ch.Attribute : $"Channel {ch.Offsets[0]}");
                list.Add(new HeadChannel
                {
                    Key = key,
                    Kind = isIntensity ? HeadChannelKind.Intensity : HeadChannelKind.Gdtf,
                    Offsets = ch.Offsets,
                    Gdtf = ch,
                    Class = type.ClassAt(ch.Offsets[0] - 1),
                    Name = NameAt(type, ch.Offsets[0] - 1),
                });
                covered.UnionWith(ch.Offsets);
                _channels[key] = list[^1];
            }
        }
        for (int offset = 1; offset <= count; offset++)
        {
            if (covered.Contains(offset))
                continue;
            bool isIntensity = offset - 1 == intensity;
            string key = isIntensity ? "intensity" : Unique(NameAt(type, offset - 1));
            var channel = new HeadChannel
            {
                Key = key,
                Kind = isIntensity ? HeadChannelKind.Intensity : HeadChannelKind.Raw,
                Offsets = new[] { offset },
                Class = type.ClassAt(offset - 1),
                Name = NameAt(type, offset - 1),
            };
            list.Add(channel);
            _channels[key] = channel;
        }
        Channels = list.OrderBy(c => c.Offsets[0]).ToList();
    }

    /// <summary>The fixture type's name of a channel ("Channel 3" when it has none).</summary>
    private static string NameAt(DesignPersonality type, int offset) =>
        offset < type.Channels.Count && !string.IsNullOrWhiteSpace(type.Channels[offset]) ? type.Channels[offset].Trim()
            : $"Channel {offset + 1}";

    /// <summary>A key not used yet: the name, else "name 2", "name 3"...; never "intensity" or "#..." for another channel.</summary>
    private string Unique(string name)
    {
        if (name == "intensity" || name.StartsWith('#'))
            name = "_" + name;
        if (!_channels.ContainsKey(name))
            return name;
        for (int n = 2; ; n++)
            if (!_channels.ContainsKey($"{name} {n}"))
                return $"{name} {n}";
    }

    /// <summary>The channel of a key ("#n" = channel n alone, raw), or null when the head has no such channel.</summary>
    public HeadChannel this[string key]
    {
        get
        {
            if (key == null)
                return null;
            if (_channels.TryGetValue(key, out var channel))
                return channel;
            if (key.StartsWith('#') && int.TryParse(key[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset)
                && offset >= 1 && offset <= Count)
                return new HeadChannel { Key = key, Kind = HeadChannelKind.Raw, Offsets = new[] { offset }, Class = ClassOfOffset(offset) };
            return null;
        }
    }

    /// <summary>The class of the channel at an offset (1-based).</summary>
    private char ClassOfOffset(int offset) => Channels.FirstOrDefault(c => c.Offsets.Contains(offset))?.Class ?? 'I';

    /// <summary>The channel that holds a DMX offset of the head (1-based), or null.</summary>
    public HeadChannel AtOffset(int offset) => Channels.FirstOrDefault(c => c.Offsets.Contains(offset));

    /// <summary>The intensity channel, or null.</summary>
    public HeadChannel Intensity => _channels.GetValueOrDefault("intensity");
}
