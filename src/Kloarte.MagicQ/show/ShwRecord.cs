using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kloarte.MagicQ;

/// <summary>
/// One record of a .shw file. Fields are numbered across lines (0 = record type); the trailing empty token of a
/// line ending in ',' and the ';' terminator are not fields.
/// </summary>
public sealed partial class ShwRecord
{

    /// <summary>The record's lines.</summary>
    public List<Line> Lines { get; } = new();
    private readonly List<(int Line, int Token)> _fields = new();

    /// <summary>Record type (field 0), e.g. "L".</summary>
    public string Type => _fields.Count > 0 ? this[0] : "";
    /// <summary>Number of fields.</summary>
    public int FieldCount => _fields.Count;

    /// <summary>Raw token, including quotes for string fields.</summary>
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

    /// <summary>A string field without its quotes.</summary>
    public string GetString(int field)
    {
        string s = this[field];
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;
    }

    /// <summary>Sets a string field (quoted).</summary>
    public void SetString(int field, string value)
    {
        if (value.Contains('"'))
            throw new ArgumentException("MagicQ strings cannot contain '\"'", nameof(value));
        this[field] = $"\"{value}\"";
    }

    /// <summary>Hex fields keep their original width (MagicQ pads some to 4 or 8 digits).</summary>
    public int GetHex(int field) => int.Parse(this[field], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>Sets a hex field, keeping its width.</summary>
    public void SetHex(int field, int value)
    {
        int width = this[field].Length;
        string s = value.ToString("x", CultureInfo.InvariantCulture);
        this[field] = width > s.Length && this[field].StartsWith('0') ? s.PadLeft(width, '0') : s;
    }

    /// <summary>Inserts fields right after `field`, on the same line.</summary>
    public void InsertFieldsAfter(int field, IEnumerable<string> values)
    {
        var (l, t) = _fields[field];
        Lines[l].Tokens.InsertRange(t + 1, values);
        Reindex();
    }

    /// <summary>
    /// Inserts a new line with `values` right after the line that holds `field` (the line ends with ',' and gets the
    /// line ending of that line), so MagicQ's one-item-per-line layout is kept.
    /// </summary>
    public void InsertLineAfter(int field, IEnumerable<string> values)
    {
        int l = _fields[field].Line;
        Lines.Insert(l + 1, new Line(values.Append("").ToList(), Lines[l].LineEnding));
        Reindex();
    }

    /// <summary>Removes `count` fields starting at `field` (they may span lines).</summary>
    public void RemoveFields(int field, int count)
    {
        var doomed = new List<(int Line, int Token)>();
        for (int i = field; i < field + count && i < _fields.Count; i++)
            doomed.Add(_fields[i]);
        foreach (var (l, t) in doomed.OrderByDescending(x => x.Line).ThenByDescending(x => x.Token))
            Lines[l].Tokens.RemoveAt(t);
        Reindex();
    }

    /// <summary>
    /// Removes `count` fields starting at `field`; when they are all the fields of their line, the line goes too (a step
    /// of a cue stack, a per-step field), so the record keeps MagicQ's one-item-per-line layout.
    /// </summary>
    public void RemoveFieldsOrLine(int field, int count)
    {
        int line = _fields[field].Line;
        int onLine = _fields.Count(f => f.Line == line);
        bool whole = onLine == count && Enumerable.Range(field, count).All(i => i < _fields.Count && _fields[i].Line == line)
                     && !Lines[line].Tokens.Contains(";");
        if (!whole)
        {
            RemoveFields(field, count);
            return;
        }
        Lines.RemoveAt(line);
        Reindex();
    }

    /// <summary>Deep copy of the lines (tokens and line endings), e.g. to make a new record from a template.</summary>
    public List<(List<string> Tokens, string LineEnding)> CopyLines() =>
        Lines.Select(l => (new List<string>(l.Tokens), l.LineEnding)).ToList();

    /// <summary>A decimal field.</summary>
    public float GetFloat(int field) => float.Parse(this[field], NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Sets a decimal field ("F6").</summary>
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
