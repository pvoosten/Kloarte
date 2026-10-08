namespace Kloarte.Model;

/// <summary>
/// A head of the native show's patch (<c>rig.yaml</c>): a number, a fixture type and the DMX channels from its address.
/// Several lamps can share a head (they follow the same channels); the gel is per head, as on the console.
/// </summary>
[FileFlow]
public sealed class DesignHead
{
    /// <summary>Head number (what cues and palettes refer to).</summary>
    [FileField("Head number (cues and palettes refer to heads by it)", Min = 1)]
    public int Number { get; set; }

    /// <summary>Head name.</summary>
    [FileField("Head name")]
    public string Name { get; set; } = "";

    /// <summary>Fixture type, by name (<see cref="DesignPersonality.Name"/>).</summary>
    [FileField("Fixture type (a personality of the rig, by name)")]
    public string Type { get; set; } = "";

    /// <summary>DMX address of the first channel (absolute, 1-based).</summary>
    [FileField("DMX address of the first channel (absolute, 1-based: 513 = universe 2, channel 1)", Min = 1)]
    public int Address { get; set; }

    /// <summary>Number of DMX channels.</summary>
    [FileField("Number of DMX channels from the address", Min = 1)]
    public int Channels { get; set; } = 1;

    /// <summary>Gel id (MagicQ colour table: 1000 + n = Lee n, 2000 + n = Rosco n), or null for none.</summary>
    [FileField("Gel (colour table id: 1000 + n = Lee n, 2000 + n = Rosco n); missing = open white", Min = 0)]
    public int? Gel { get; set; }

    /// <summary>Beam angle set in the patch (whole degrees), or null for the fixture type's.</summary>
    [FileField("Beam angle set in the patch (whole degrees); missing = the fixture type's", Unit = "°", Min = 0)]
    public int? Beam { get; set; }

    /// <summary>Beam shape id set in the patch, or null.</summary>
    [FileField("Beam shape id set in the patch (MagicVis); missing = none", Min = 0)]
    public int? Shape { get; set; }

    /// <summary>Dimmer curve (a curve id of the rig), or null for linear.</summary>
    [FileField("Dimmer curve (a curve of the rig, by id); missing = linear", Min = 1)]
    public int? Curve { get; set; }

    /// <summary>True when the head has the DMX address (one of its channels).</summary>
    public bool Covers(int address) => address >= Address && address < Address + Channels;

    /// <summary>A copy.</summary>
    public DesignHead Copy() => (DesignHead)MemberwiseClone();
}
