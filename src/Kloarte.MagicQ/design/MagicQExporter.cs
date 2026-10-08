using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// One export of a native show into a MagicQ show (<see cref="MagicQExport"/>): compares each part of the design with the
/// show and changes only what differs, collecting what it wrote, the warnings and what to check in MagicQ PC.
/// </summary>
internal sealed partial class MagicQExporter
{
    /// <summary>The native show.</summary>
    private readonly DesignShow _design;

    /// <summary>The MagicQ show being written (a copy of the source, in memory).</summary>
    private readonly MagicQShow _show;

    /// <summary>The console templates (personalities, a record of each kind).</summary>
    private readonly MagicQShow _templates;

    /// <summary>The new-show template (the house venue's MagicVis objects, for new objects).</summary>
    private readonly MagicQShow _venue;

    /// <summary>What was written.</summary>
    public List<string> Written { get; } = new();

    /// <summary>What could not be exported as it is.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>What to check in MagicQ PC (kinds of records not yet verified there).</summary>
    public List<string> CheckInConsole { get; } = new();

    /// <summary>An export of a design into a show, with the templates.</summary>
    public MagicQExporter(DesignShow design, MagicQShow show, MagicQShow templates, MagicQShow venue)
    {
        _design = design;
        _show = show;
        _templates = templates;
        _venue = venue;
    }

    /// <summary>Writes every part into the show.</summary>
    public void Run()
    {
        _design.Changed();
        ExportPatch();
        ExportGroups();
        ExportItems();
        ExportCurves();
        ExportPalettes();
        ExportCues();
    }

    /// <summary>
    /// Imports the written show and compares it with the design: the DMX after every cue, the heads and the rig items.
    /// A difference is a warning (the export is not what Kloarte shows).
    /// </summary>
    public void Verify(string path, FixtureCatalog catalog)
    {
        var back = MagicQImport.Import(path, catalog).Show;
        IShowDocument expected = _design, actual = back;
        var expectedStack = expected.CueStacks.Single();
        var actualStack = actual.CueStacks.FirstOrDefault(s => s.Number == expectedStack.Number);
        if (actualStack == null || actualStack.Steps.Count != expectedStack.Steps.Count)
        {
            Warnings.Add($"check after writing: the cue stack has {actualStack?.Steps.Count ?? 0} cues, Kloarte {expectedStack.Steps.Count}");
            return;
        }
        for (int i = 0; i < expectedStack.Steps.Count; i++)
        {
            var want = CueTracking.LevelsAfter(expected, expectedStack, i);
            var got = CueTracking.LevelsAfter(actual, actualStack, i);
            int wrong = want.Keys.Union(got.Keys).Count(a => want.GetValueOrDefault(a, -1) != got.GetValueOrDefault(a, -1));
            if (wrong > 0 || !CueNumbers.Same(expectedStack.Steps[i].CueNumber, actualStack.Steps[i].CueNumber))
            {
                Warnings.Add($"check after writing: cue {expectedStack.Steps[i].CueNumberText} gives {wrong} other DMX values in MagicQ than in Kloarte");
                break;
            }
        }
        foreach (var head in _design.Rig.Heads)
            if (back.Head(head.Number) is not { } h || h.Address != head.Address || h.Channels != head.Channels)
                Warnings.Add($"check after writing: head {head.Number} is not patched as in Kloarte");
    }

    /// <summary>Notes a change.</summary>
    private void Wrote(string what) => Written.Add(what);

    /// <summary>Notes something to check in MagicQ PC (once).</summary>
    private void Check(string what)
    {
        if (!CheckInConsole.Contains(what))
            CheckInConsole.Add(what);
    }

    /// <summary>The MagicQ attribute class (I, P, C, B) of a DMX address of the design's patch.</summary>
    private char ClassAt(int address)
    {
        var head = _design.HeadCovering(address);
        if (head == null)
            return 'I';
        return _design.LayoutOf(head).AtOffset(address - head.Address + 1)?.Class ?? 'I';
    }

    /// <summary>The MagicQ palette type of a class.</summary>
    private static MagicQShow.PaletteType TypeOf(char cls) => cls switch
    {
        'P' => MagicQShow.PaletteType.Position,
        'C' => MagicQShow.PaletteType.Colour,
        'B' => MagicQShow.PaletteType.Beam,
        _ => MagicQShow.PaletteType.Intensity,
    };

    /// <summary>A text as MagicQ keeps it: no double quotes, at most `max` characters.</summary>
    private static string Clean(string text, int max = 15)
    {
        text = (text ?? "").Replace("\"", "'").Trim();
        return text.Length > max ? text[..max] : text;
    }

    /// <summary>"head 12", "heads 12, 14, ..." (status lines).</summary>
    private static string List(IEnumerable<string> items, int max = 8)
    {
        var all = items.ToList();
        return string.Join(", ", all.Take(max)) + (all.Count > max ? $", ... ({all.Count})" : "");
    }

    /// <summary>The first record of a kind in the show, else in the templates (to copy the layout of).</summary>
    private ShwRecord TemplateRecord(Func<ShwRecord, bool> kind) =>
        _show.Shw.Records.FirstOrDefault(kind) ?? _templates.Shw.Records.FirstOrDefault(kind)
        ?? throw new InvalidOperationException("no record to copy the layout from");

    /// <summary>The smallest record of a kind (fewest fields), of the show else the templates.</summary>
    private ShwRecord SimplestRecord(Func<ShwRecord, bool> kind) =>
        _show.Shw.Records.Where(kind).OrderBy(r => r.FieldCount).FirstOrDefault()
        ?? _templates.Shw.Records.Where(kind).OrderBy(r => r.FieldCount).FirstOrDefault()
        ?? throw new InvalidOperationException("no record to copy the layout from");
}
