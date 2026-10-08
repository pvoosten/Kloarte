using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>A DMX mode of a GDTF fixture type.</summary>
public sealed class GdtfMode
{
    /// <summary>Mode name, e.g. "Mode 1 - CMY".</summary>
    public string Name { get; init; } = "";

    /// <summary>The channels.</summary>
    public List<GdtfChannel> Channels { get; } = new();

    /// <summary>Number of DMX channels (footprint).</summary>
    public int ChannelCount => Channels.Count == 0 ? 0 : Channels.Max(c => c.Offsets.Max());

    /// <summary>The channel whose main attribute is `attribute` (or that has a function of it), or null.</summary>
    public GdtfChannel Channel(string attribute) =>
        Channels.FirstOrDefault(c => c.Attribute == attribute) ?? Channels.FirstOrDefault(c => c.Functions.Any(f => f.Attribute == attribute));

    /// <summary>True when the mode has a channel for the attribute.</summary>
    public bool Has(string attribute) => Channel(attribute) != null;
}
