using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// A fixture type of the native show (<c>rig.yaml</c>): its DMX channels by name, what each one is (intensity,
/// position, colour, beam) and the GDTF file and mode that say what the channels do (moving heads). Imported from the
/// console's personality (MagicQ: P record) or made from the catalogue.
/// </summary>
public sealed class DesignPersonality
{
    /// <summary>Name (the console's personality name, e.g. "Robe_RobinT1Profile_Mode1"; "generic_dimmer").</summary>
    [FileField("Name of the fixture type (MagicQ's personality name, e.g. generic_dimmer, Robe_RobinT1Profile_Mode1)")]
    public string Name { get; set; } = "";

    /// <summary>The console's mode name, if any.</summary>
    [FileField("The console's mode name")]
    public string Mode { get; set; }

    /// <summary>The DMX channels' names, in DMX order.</summary>
    [FileField("The DMX channels' names, in DMX order")]
    public List<string> Channels { get; set; } = new();

    /// <summary>One letter per channel: I intensity, P position, C colour, B beam.</summary>
    [FileField("What each channel is, one letter per channel in DMX order: I intensity, P position, C colour, B beam")]
    public string Classes { get; set; } = "";

    /// <summary>GDTF file name prefix in data/gdtf (moving heads), or null.</summary>
    [FileField("GDTF file of the fixture type (file name prefix in data/gdtf), for its attributes in physical values")]
    public string Gdtf { get; set; }

    /// <summary>The GDTF mode, or null.</summary>
    [FileField("The GDTF mode matching the channels, e.g. \"Mode 1 - CMY\"")]
    public string GdtfMode { get; set; }

    /// <summary>What channel `offset` (0-based) is: I, P, C or B (I for a channel the type doesn't describe, as MagicQ).</summary>
    public char ClassAt(int offset) => offset >= 0 && offset < Classes.Length ? Classes[offset] : 'I';

    /// <summary>The first intensity channel (0-based), or -1.</summary>
    public int IntensityOffset => Classes.IndexOf('I');

    /// <summary>
    /// A channel's class from its name, as MagicQ shows it: pan / tilt = P; cyan, magenta, yellow, CTO, colour, the
    /// emitters = C; any other = B (`intensity` true = I).
    /// </summary>
    public static char ClassOf(string name, bool intensity)
    {
        if (intensity)
            return 'I';
        string n = (name ?? "").ToLowerInvariant();
        if (n.Contains("pan") || n.Contains("tilt"))
            return 'P';
        if (n.Contains("cyan") || n.Contains("magenta") || n.Contains("yellow") || n.Contains("cto") || n.Contains("col")
            || n.Contains("red") || n.Contains("green") || n.Contains("blue") || n.Contains("white") || n.Contains("amber")
            || n.Contains("lime") || n.Contains("cct") || n.Contains("tint"))
            return 'C';
        return 'B';
    }
}
