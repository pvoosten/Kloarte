using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// MagicQ's colour / gel table (show/heads/allcols.csv in the MagicQ folder): web colours, Lee (id 1000 + number),
// Rosco (2000 + number), Apollo, GAM... Columns: name, description, id, colour (hex, **BGR**), CIE x, CIE y,
// brightness (0..1). The patch (`L` record field 11) refers to a gel by this id. No Godot dependencies.
public sealed class MagicQColours
{
    public sealed record Entry(int Id, string Name, float R, float G, float B, float Brightness)
    {
        // Gels that don't colour the light (no colour, colour wheel, colour mixing).
        public bool IsNeutral => Id is 0 or 1 or 2;
    }

    private readonly Dictionary<int, Entry> _byId = new();

    public int Count => _byId.Count;

    public Entry this[int id] => _byId.GetValueOrDefault(id);

    public static MagicQColours Load(string csvPath)
    {
        var table = new MagicQColours();
        foreach (string line in File.ReadLines(csvPath, Encoding.Latin1))
        {
            var f = SplitCsv(line);
            if (f.Count < 4 || !int.TryParse(f[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                continue;
            if (!int.TryParse(f[3].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int bgr))
                continue;
            float brightness = f.Count > 6 && float.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float b)
                ? b : 1f;
            table._byId[id] = new Entry(id, f[0], (bgr & 0xff) / 255f, ((bgr >> 8) & 0xff) / 255f,
                                        ((bgr >> 16) & 0xff) / 255f, brightness);
        }
        return table;
    }

    // Looks for heads/allcols.csv in the MagicQ show folder the .shw lives in (walking up), else null.
    public static string FindFor(string shwPath)
    {
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(shwPath)); dir != null; dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, "heads", "allcols.csv");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // Simple CSV split with "..." quoting ("" = escaped quote).
    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                    inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
                sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
