using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// MagicQ's colour / gel table (show/heads/allcols.csv in the MagicQ folder): web colours, Lee (id 1000 + number),
/// Rosco (2000 + number), Apollo, GAM... Columns: name, description, id, colour (hex, **BGR**), CIE x, CIE y,
/// brightness (0..1). The patch (`L` record field 11) refers to a gel by this id. No Godot dependencies.
/// </summary>
public sealed partial class MagicQColours : IGelTable
{

    private readonly Dictionary<int, Entry> _byId = new();

    /// <summary>Number of colours in the table.</summary>
    public int Count => _byId.Count;

    /// <inheritdoc />
    public IEnumerable<int> Ids => _byId.Keys;

    /// <summary>The colour with this MagicQ id, or null.</summary>
    public Entry this[int id] => _byId.GetValueOrDefault(id);

    /// <inheritdoc />
    IGel IGelTable.this[int id] => this[id];

    /// <summary>Reads allcols.csv.</summary>
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

    /// <summary>
    /// Looks for heads/allcols.csv in the MagicQ show folder the .shw lives in (walking up), else null.
    /// </summary>
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

    /// <summary>Simple CSV split with "..." quoting ("" = escaped quote).</summary>
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
