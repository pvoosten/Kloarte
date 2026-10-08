using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

/// <summary>
/// Lossless model of a MagicQ .shw file: every line, record, field and line ending is kept, so a file that is read
/// and written back unchanged is byte-identical, also for record types we don't understand yet.
///
/// Format: ASCII, CRLF. Lines starting with '\' are comments. A record is one or more lines of comma-separated
/// fields; the first field is the record type; it ends with a ';' token. Most lines end with ',' (which is a
/// separator, not an empty field). Quoted fields keep their quotes in Token, see GetString/SetString.
/// </summary>
public sealed partial class ShwDocument
{

    /// <summary>Lines and records in file order.</summary>
    private List<Item> Items { get; } = new();

    /// <summary>All records in file order.</summary>
    public IEnumerable<ShwRecord> Records
    {
        get
        {
            foreach (var item in Items)
                if (item.Record != null)
                    yield return item.Record;
        }
    }

    /// <summary>Reads a .shw (Latin-1).</summary>
    public static ShwDocument Load(string path) => Parse(File.ReadAllText(path, Encoding.Latin1));

    /// <summary>Writes the .shw (Latin-1).</summary>
    public void Save(string path) => File.WriteAllText(path, ToText(), Encoding.Latin1);

    /// <summary>
    /// True when the file holds exactly this document (round trips are byte-identical, so false = changed since it
    /// was read, or a different file).
    /// </summary>
    public bool Matches(string path) => File.Exists(path) && File.ReadAllText(path, Encoding.Latin1) == ToText();

    /// <summary>Parses .shw text.</summary>
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
            // MagicQ's status.dat ends its V record with `"MagicQ 1";` (no separate ';' token).
            string last = recLine.Tokens.Count > 0 ? recLine.Tokens[^1] : "";
            if (recLine.Tokens.Contains(";") || last.EndsWith("\";"))
            {
                current.Reindex();
                current = null;
            }
        }
        current?.Reindex();
        return doc;
    }

    /// <summary>The document as text (byte-identical when unchanged).</summary>
    internal string ToText()
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

    /// <summary>
    /// Appends a new record (one line, MagicQ-style trailing ',' and ';'), before the trailing blank line if any.
    /// </summary>
    private ShwRecord AddRecord(IEnumerable<string> fields)
    {
        var tokens = new List<string>(fields) { ";" };
        var record = new ShwRecord();
        record.Lines.Add(new ShwRecord.Line(tokens, "\r\n"));
        record.Reindex();
        Items.Add(new Item { Record = record });
        Items.Add(new Item { RawLine = "", LineEnding = "\r\n" });
        return record;
    }

    /// <summary>Removes a record.</summary>
    public void RemoveRecord(ShwRecord record) => Items.RemoveAll(i => i.Record == record);

    /// <summary>
    /// Inserts a record (given as lines of tokens; the last line must contain the ';' token) right after another
    /// record and the blank line that follows it, like MagicQ lays out records of one type.
    /// </summary>
    public ShwRecord InsertRecordAfter(ShwRecord after, IEnumerable<(List<string> Tokens, string LineEnding)> lines)
    {
        var record = new ShwRecord();
        foreach (var (tokens, ending) in lines)
            record.Lines.Add(new ShwRecord.Line(new List<string>(tokens), ending));
        record.Reindex();
        int index = Items.FindIndex(i => i.Record == after);
        if (index < 0)
            throw new ArgumentException("record not in this document", nameof(after));
        index++;
        bool blank = index < Items.Count && Items[index].Record == null && Items[index].RawLine.Length == 0;
        if (blank)
            index++;
        Items.Insert(index, new Item { Record = record });
        if (blank)
            Items.Insert(index + 1, new Item { RawLine = "", LineEnding = "\r\n" });
        return record;
    }

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
