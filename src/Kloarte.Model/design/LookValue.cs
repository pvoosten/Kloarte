using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kloarte.Model;

/// <summary>
/// One value a cue or palette sets for one channel of a head, as the project files hold it: readable, and exact when
/// needed. The readable part is interpreted with the head's channel (<see cref="HeadChannel"/>): a number (intensity in
/// %, a GDTF attribute's physical value such as Pan in degrees, a raw channel's DMX value), the name of a channel set or
/// wheel slot ("Stars", "Open"), or a function with its physical value ("Shutter1Strobe 5"). An exact DMX value in the
/// channel's resolution wins over the readable part ("Stars (dmx 37)"; "dmx 37" alone). A palette reference ("@Front")
/// takes the palette's value for that channel of the head. Immutable.
/// </summary>
[FileScalar("A number (intensity %, degrees, ...), a slot or set name (\"Stars\"), a function and its value " +
            "(\"Shutter1Strobe 5\"), an exact DMX value (\"dmx 37\", or after a readable value: \"Stars (dmx 37)\"), " +
            "or a palette (\"@Front\")")]
public sealed class LookValue : IEquatable<LookValue>
{
    /// <summary>Text of an exact DMX value after a readable one: " (dmx 37)".</summary>
    private static readonly Regex ExactSuffix = new(@"^(.*?)\s*\(dmx\s+(\d+)\)\s*$", RegexOptions.IgnoreCase);

    /// <summary>Text of an exact DMX value alone: "dmx 37".</summary>
    private static readonly Regex DmxOnly = new(@"^dmx\s+(\d+)$", RegexOptions.IgnoreCase);

    /// <summary>The readable part ("" when there is none: an exact DMX value only, or a palette reference).</summary>
    public string Text { get; }

    /// <summary>The exact DMX value in the channel's resolution, or null (then the readable part counts).</summary>
    public int? Dmx { get; }

    /// <summary>The palette this value refers to, or null.</summary>
    public string Palette { get; }

    /// <summary>A value of these parts.</summary>
    private LookValue(string text, int? dmx, string palette)
    {
        Text = text ?? "";
        Dmx = dmx;
        Palette = palette;
    }

    /// <summary>A number (intensity %, physical value, a raw channel's DMX value), written as short as it is.</summary>
    public static LookValue Of(double number) => new(Number(number), null, null);

    /// <summary>A readable text (a set name, "function value", or a number as text), optionally with the exact DMX value.</summary>
    public static LookValue Of(string text, int? dmx = null) => new(text?.Trim() ?? "", dmx, null);

    /// <summary>An exact DMX value only.</summary>
    public static LookValue OfDmx(int dmx) => new("", dmx, null);

    /// <summary>A reference to a palette.</summary>
    public static LookValue OfPalette(string palette) => new("", null, palette?.Trim());

    /// <summary>True when the value refers to a palette.</summary>
    public bool IsPalette => Palette != null;

    /// <summary>The readable part as a number, or null when it isn't one.</summary>
    public double? AsNumber =>
        double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    /// <summary>
    /// Reads a value as written in a file. Throws <see cref="FormatException"/> for an empty text or a palette without a
    /// name.
    /// </summary>
    public static LookValue Parse(string text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0)
            throw new FormatException("an empty value (give a number, a name, \"dmx 37\" or \"@palette\")");
        if (text.StartsWith('@'))
        {
            string name = text[1..].Trim();
            if (name.Length == 0)
                throw new FormatException("'@' without a palette name");
            return OfPalette(name);
        }
        if (DmxOnly.Match(text) is { Success: true } only)
            return OfDmx(int.Parse(only.Groups[1].Value, CultureInfo.InvariantCulture));
        if (ExactSuffix.Match(text) is { Success: true } exact)
            return Of(exact.Groups[1].Value, int.Parse(exact.Groups[2].Value, CultureInfo.InvariantCulture));
        return Of(text);
    }

    /// <summary>The value as written in a file: "12.5", "Stars", "Stars (dmx 37)", "dmx 37", "@Front".</summary>
    public override string ToString()
    {
        if (Palette != null)
            return "@" + Palette;
        if (Dmx is not { } dmx)
            return Text;
        string exact = "dmx " + dmx.ToString(CultureInfo.InvariantCulture);
        return Text.Length == 0 ? exact : $"{Text} ({exact})";
    }

    /// <summary>A number as short as it is ("12.5", "40", "-0.25").</summary>
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool Equals(LookValue other) =>
        other != null && Text == other.Text && Dmx == other.Dmx && Palette == other.Palette;

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as LookValue);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Text, Dmx, Palette);
}
