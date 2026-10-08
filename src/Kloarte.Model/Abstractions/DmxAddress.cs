using System.Globalization;

namespace Kloarte.Model;

/// <summary>DMX addresses as text: "universe-channel" (1-based absolute address in the model).</summary>
public static class DmxAddress
{
    /// <summary>Highest absolute address (64 universes).</summary>
    public const int Max = 64 * 512;

    /// <summary>"universe-channel", e.g. 1-150, 2-001; "-" for 0 (not patched).</summary>
    public static string Format(int address) =>
        address <= 0 ? "-" : $"{(address - 1) / 512 + 1}-{(address - 1) % 512 + 1:000}";

    /// <summary>Parses "2-001", "2.001", "2/1" (universe-channel) or an absolute address ("513"); null if invalid.</summary>
    public static int? Parse(string text)
    {
        text = (text ?? "").Trim();
        var parts = text.Split('-', '.', '/', ':');
        if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int u)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) && u >= 1 && c >= 1 && c <= 512)
            return (u - 1) * 512 + c;
        if (parts.Length == 1 && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int abs) && abs >= 1 && abs <= Max)
            return abs;
        return null;
    }
}
