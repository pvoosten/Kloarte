using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// Lossless model of a MagicQ .shw file: every line, record, field and line ending is kept, so a file that is read
// and written back unchanged is byte-identical, also for record types we don't understand yet.
//
// Format: ASCII, CRLF. Lines starting with '\' are comments. A record is one or more lines of comma-separated
// fields; the first field is the record type; it ends with a ';' token. Most lines end with ',' (which is a
// separator, not an empty field). Quoted fields keep their quotes in Token, see GetString/SetString.
public sealed class ShwDocument
{
    // Either a comment / blank line between records (Record == null) or a record.
    public sealed class Item
    {
        public string RawLine;          // without line ending
        public string LineEnding = "";
        public ShwRecord Record;
    }

    public List<Item> Items { get; } = new();

    public IEnumerable<ShwRecord> Records
    {
        get
        {
            foreach (var item in Items)
                if (item.Record != null)
                    yield return item.Record;
        }
    }

    public static ShwDocument Load(string path) => Parse(File.ReadAllText(path, Encoding.Latin1));

    public void Save(string path) => File.WriteAllText(path, ToText(), Encoding.Latin1);

    public static ShwDocument Parse(string text)
    {
        var doc = new ShwDocument();
        ShwRecord current = null;
        int pos = 0;
        while (pos < text.Length)
        {
            int nl = text.IndexOf('\n', pos);
            int end = nl < 0 ? text.Length : nl;
            int contentEnd = end > pos && text[end - 1] == '\r' ? end - 1 : end;
            string line = text[pos..contentEnd];
            string ending = text[contentEnd..(nl < 0 ? end : nl + 1)];
            pos = nl < 0 ? text.Length : nl + 1;

            if (current == null && (line.Length == 0 || line[0] == '\\'))
            {
                doc.Items.Add(new Item { RawLine = line, LineEnding = ending });
                continue;
            }
            if (current == null)
            {
                current = new ShwRecord();
                doc.Items.Add(new Item { Record = current });
            }
            var recLine = new ShwRecord.Line(SplitTokens(line), ending);
            current.Lines.Add(recLine);
            if (recLine.Tokens.Contains(";"))
            {
                current.Reindex();
                current = null;
            }
        }
        current?.Reindex();
        return doc;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var item in Items)
        {
            if (item.Record == null)
                sb.Append(item.RawLine).Append(item.LineEnding);
            else
                foreach (var line in item.Record.Lines)
                    sb.Append(string.Join(",", line.Tokens)).Append(line.LineEnding);
        }
        return sb.ToString();
    }

    // Appends a new record (one line, MagicQ-style trailing ',' and ';'), before the trailing blank line if any.
    public ShwRecord AddRecord(IEnumerable<string> fields)
    {
        var tokens = new List<string>(fields) { ";" };
        var record = new ShwRecord();
        record.Lines.Add(new ShwRecord.Line(tokens, "\r\n"));
        record.Reindex();
        Items.Add(new Item { Record = record });
        Items.Add(new Item { RawLine = "", LineEnding = "\r\n" });
        return record;
    }

    public void RemoveRecord(ShwRecord record) => Items.RemoveAll(i => i.Record == record);

    private static List<string> SplitTokens(string line)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            if (c == ',' && !inQuotes)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
            else
                sb.Append(c);
        }
        tokens.Add(sb.ToString());
        return tokens;
    }
}

// One record of a .shw file. Fields are numbered across lines (0 = record type); the trailing empty token of a
// line ending in ',' and the ';' terminator are not fields.
public sealed class ShwRecord
{
    public sealed class Line
    {
        public List<string> Tokens;
        public string LineEnding;

        public Line(List<string> tokens, string lineEnding)
        {
            Tokens = tokens;
            LineEnding = lineEnding;
        }
    }

    public List<Line> Lines { get; } = new();
    private readonly List<(int Line, int Token)> _fields = new();

    public string Type => _fields.Count > 0 ? this[0] : "";
    public int FieldCount => _fields.Count;

    // Raw token, including quotes for string fields.
    public string this[int field]
    {
        get
        {
            var (l, t) = _fields[field];
            return Lines[l].Tokens[t];
        }
        set
        {
            var (l, t) = _fields[field];
            Lines[l].Tokens[t] = value;
        }
    }

    public string GetString(int field)
    {
        string s = this[field];
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
    }

    public void SetString(int field, string value)
    {
        if (value.Contains('"'))
            throw new ArgumentException("MagicQ strings cannot contain '\"'", nameof(value));
        this[field] = $"\"{value}\"";
    }

    // Hex fields keep their original width (MagicQ pads some to 4 or 8 digits).
    public int GetHex(int field) => int.Parse(this[field], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public void SetHex(int field, int value)
    {
        int width = this[field].Length;
        string s = value.ToString("x", CultureInfo.InvariantCulture);
        this[field] = width > s.Length && this[field].StartsWith('0') ? s.PadLeft(width, '0') : s;
    }

    public float GetFloat(int field) => float.Parse(this[field], NumberStyles.Float, CultureInfo.InvariantCulture);

    public void SetFloat(int field, float value) => this[field] = value.ToString("F6", CultureInfo.InvariantCulture);

    internal void Reindex()
    {
        _fields.Clear();
        for (int l = 0; l < Lines.Count; l++)
        {
            var tokens = Lines[l].Tokens;
            int count = tokens.Count;
            if (count > 0 && tokens[count - 1] == "")
                count--;                                  // trailing ',' on the line
            for (int t = 0; t < count; t++)
                if (tokens[t] != ";")
                    _fields.Add((l, t));
        }
    }
}
