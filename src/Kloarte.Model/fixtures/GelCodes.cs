using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// Gels as a technician names them: "L201" (Lee), "R80" (Rosco), "O/W" (open white, no gel), from the console's colour
/// table ids (MagicQ: 1000 + n = Lee n, 2000 + n = Rosco n, 0 = none). <see cref="Parse"/> takes what is typed in a gel
/// field ("L201", "l 201", "201" = Lee, "R80", "O/W"); <see cref="Search"/> lists the gels that match as you type.
/// </summary>
public static class GelCodes
{
    /// <summary>The code of open white (no gel).</summary>
    public const string OpenWhite = "O/W";

    /// <summary>"L201", "R080" for a colour table id; "" for none / other entries.</summary>
    public static string Code(int gelId) =>
        gelId >= 2000 && gelId < 3000 ? $"R{gelId - 2000:000}"
        : gelId >= 1000 && gelId < 2000 ? $"L{gelId - 1000:000}"
        : "";

    /// <summary>
    /// The colour table id of a typed gel: 0 for open white ("O/W", "open", "0", ""), Lee / Rosco by code (a number
    /// alone = Lee); null when it is not a gel of the table.
    /// </summary>
    public static int? Parse(string text, IGelTable table)
    {
        string t = (text ?? "").Trim().Replace(" ", "").ToUpperInvariant();
        if (t is "" or "O/W" or "OW" or "OPEN" or "0" or "NONE")
            return 0;
        int offset = t[0] switch { 'L' => 1000, 'R' => 2000, _ => 1000 };
        string number = char.IsLetter(t[0]) ? t[1..] : t;
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n >= 1000)
            return null;
        int id = offset + n;
        return table[id] != null ? id : null;
    }

    /// <summary>
    /// Gels that match typed text: the exact one first, then codes that start with it ("L20" -> L200..L209 ...), then
    /// names that contain it ("blue"); Lee before Rosco, by number. At most `max`.
    /// </summary>
    public static List<int> Search(IGelTable table, string text, int max = 12)
    {
        string t = (text ?? "").Trim();
        var result = new List<int>();
        if (Parse(t, table) is { } exact and > 0)
            result.Add(exact);
        if (t.Length == 0)
            return result;
        string code = t.Replace(" ", "").ToUpperInvariant();
        if (char.IsDigit(code[0]))
            code = "L" + code;
        var ids = table.Ids.Where(id => Code(id) != "").OrderBy(id => id).ToList();
        result.AddRange(ids.Where(id => CodePrefix(id, code)));
        result.AddRange(ids.Where(id => table[id].Name.Contains(t, StringComparison.OrdinalIgnoreCase)));
        return result.Distinct().Take(max).ToList();
    }

    /// <summary>True when the gel's code without leading zeros ("L201", "R80") starts with `code`.</summary>
    private static bool CodePrefix(int id, string code)
    {
        string full = Code(id);
        string shortCode = full[0] + int.Parse(full[1..], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        return full.StartsWith(code, StringComparison.Ordinal) || shortCode.StartsWith(code, StringComparison.Ordinal);
    }
}
