using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>A DMX channel of a GDTF mode: 1 (8 bit) or 2 (16 bit) offsets, its main attribute and its functions.</summary>
public sealed class GdtfChannel
{
    /// <summary>Main attribute (of the first logical channel), e.g. "Pan", "ColorSub_C", "Dimmer".</summary>
    public string Attribute { get; init; } = "";

    /// <summary>DMX offsets in the fixture, 1-based, coarse first (e.g. 1, 2 for 16-bit pan).</summary>
    public int[] Offsets { get; init; } = { 1 };

    /// <summary>Default DMX value (channel resolution).</summary>
    public int Default { get; init; }

    /// <summary>Channel functions in DMX order.</summary>
    public List<GdtfFunction> Functions { get; } = new();

    /// <summary>Highest DMX value (255 or 65535).</summary>
    public int Max => Offsets.Length >= 2 ? 65535 : 255;

    /// <summary>The function a DMX value falls in.</summary>
    public GdtfFunction FunctionAt(int dmx) => Functions.LastOrDefault(f => f.DmxFrom <= dmx) ?? Functions.FirstOrDefault();

    /// <summary>The (first) function of an attribute.</summary>
    public GdtfFunction Function(string attribute) => Functions.FirstOrDefault(f => f.Attribute == attribute);

    /// <summary>Value of this channel from DMX bytes (address -&gt; byte); missing bytes = the default.</summary>
    public int Read(IReadOnlyDictionary<int, int> dmx, int start)
    {
        if (Offsets.Length >= 2)
        {
            int coarse = dmx.TryGetValue(start + Offsets[0] - 1, out int c) ? c : Default >> 8;
            int fine = dmx.TryGetValue(start + Offsets[1] - 1, out int f) ? f : Default & 0xff;
            return coarse << 8 | fine;
        }
        return dmx.TryGetValue(start + Offsets[0] - 1, out int v) ? v : Default;
    }

    /// <summary>Writes a value as DMX bytes (address -&gt; byte).</summary>
    public void Write(IDictionary<int, int> dmx, int start, int value)
    {
        value = System.Math.Clamp(value, 0, Max);
        if (Offsets.Length >= 2)
        {
            dmx[start + Offsets[0] - 1] = value >> 8;
            dmx[start + Offsets[1] - 1] = value & 0xff;
        }
        else
            dmx[start + Offsets[0] - 1] = value;
    }
}
