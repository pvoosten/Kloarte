using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// A GDTF channel function: a DMX range of a channel (from DmxFrom up to the next function) with its attribute and
/// physical range (degrees, 0..1, Kelvin...), optionally a wheel and named channel sets (e.g. wheel slots).
/// DMX values are in the channel's own resolution (8 bit: 0..255, 16 bit: 0..65535).
/// </summary>
public sealed class GdtfFunction
{
    /// <summary>Attribute, e.g. "Pan", "Gobo1", "Shutter1Strobe".</summary>
    public string Attribute { get; init; } = "";

    /// <summary>Function name, e.g. "Open/Closed".</summary>
    public string Name { get; init; } = "";

    /// <summary>First DMX value of the function.</summary>
    public int DmxFrom { get; init; }

    /// <summary>Last DMX value (the next function's DmxFrom - 1, or the channel maximum).</summary>
    public int DmxTo { get; set; }

    /// <summary>Physical value at DmxFrom.</summary>
    public float PhysicalFrom { get; init; }

    /// <summary>Physical value at DmxTo.</summary>
    public float PhysicalTo { get; init; }

    /// <summary>Wheel this function selects slots of ("" = none).</summary>
    public string Wheel { get; init; } = "";

    /// <summary>Channel sets: DMX start, name, wheel slot index (1-based, 0 = none).</summary>
    public List<(int DmxFrom, string Name, int Slot)> Sets { get; } = new();

    /// <summary>Physical value of a DMX value in this function (linear).</summary>
    public float Physical(int dmx) =>
        DmxTo <= DmxFrom ? PhysicalFrom : PhysicalFrom + (PhysicalTo - PhysicalFrom) * (dmx - DmxFrom) / (float)(DmxTo - DmxFrom);

    /// <summary>DMX value of a physical value (clamped to the function's range).</summary>
    public int Dmx(float physical)
    {
        if (System.Math.Abs(PhysicalTo - PhysicalFrom) < 1e-9f)
            return DmxFrom;
        float k = System.Math.Clamp((physical - PhysicalFrom) / (PhysicalTo - PhysicalFrom), 0f, 1f);
        return DmxFrom + (int)System.Math.Round(k * (DmxTo - DmxFrom));
    }

    /// <summary>The channel set a DMX value falls in, or null.</summary>
    public (int DmxFrom, string Name, int Slot)? SetAt(int dmx)
    {
        (int, string, int)? found = null;
        foreach (var s in Sets)
            if (s.DmxFrom <= dmx)
                found = s;
        return found;
    }
}
