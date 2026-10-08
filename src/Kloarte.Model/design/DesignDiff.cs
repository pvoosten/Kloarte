using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// What differs between two native shows, as a technician reads it (decided 14 Oct 2026: a show edited in MagicQ is
/// re-imported with a list of its changes, applied as one undo step): cues added, removed, renumbered or changed (text,
/// times, values per head), palettes, the patch, groups and the rig. One line each.
/// </summary>
public static class DesignDiff
{
    /// <summary>The changes from `before` to `after` (empty = the same show).</summary>
    public static IReadOnlyList<string> Describe(DesignShow before, DesignShow after)
    {
        var lines = new List<string>();
        Cues(before, after, lines);
        Palettes(before, after, lines);
        Patch(before, after, lines);
        Rig(before, after, lines);
        return lines;
    }

    /// <summary>The cues, matched by id.</summary>
    private static void Cues(DesignShow before, DesignShow after, List<string> lines)
    {
        var old = before.CueOrder.Where(c => c.Id != null).ToDictionary(c => c.Id.Value);
        var now = after.CueOrder.Where(c => c.Id != null).ToDictionary(c => c.Id.Value);
        foreach (var cue in after.CueOrder.Where(c => c.Id == null || !old.ContainsKey(c.Id.Value)))
            lines.Add($"cue {cue.Cue} {cue.Text} added");
        foreach (var cue in before.CueOrder.Where(c => c.Id != null && !now.ContainsKey(c.Id.Value)))
            lines.Add($"cue {cue.Cue} {cue.Text} removed");
        foreach (var cue in after.CueOrder.Where(c => c.Id != null && old.ContainsKey(c.Id.Value)))
        {
            var was = old[cue.Id.Value];
            var what = new List<string>();
            if (!CueNumbers.Same(was.Number, cue.Number))
                what.Add($"renumbered from {was.Cue}");
            if ((was.Text ?? "") != (cue.Text ?? ""))
                what.Add($"text \"{cue.Text}\"");
            if (Seconds(was.FadeIn) != Seconds(cue.FadeIn) || Seconds(was.FadeOut) != Seconds(cue.FadeOut))
                what.Add($"times {DesignShow.Seconds(cue.FadeIn)} / {DesignShow.Seconds(cue.FadeOut)} s");
            var heads = Changed(was.Heads, cue.Heads);
            if (heads.Count > 0)
                what.Add($"values of head{(heads.Count == 1 ? "" : "s")} {List(heads)}");
            if (!Same(was.Dmx, cue.Dmx))
                what.Add("DMX values of no head");
            if (what.Count > 0)
                lines.Add($"cue {cue.Cue}: {string.Join(", ", what)}");
        }
    }

    /// <summary>The palettes, matched by name.</summary>
    private static void Palettes(DesignShow before, DesignShow after, List<string> lines)
    {
        foreach (var palette in after.Palettes.Where(p => before.Palette(p.Name) == null))
            lines.Add($"palette {palette.Name} added");
        foreach (var palette in before.Palettes.Where(p => after.Palette(p.Name) == null))
            lines.Add($"palette {palette.Name} removed");
        foreach (var palette in after.Palettes)
            if (before.Palette(palette.Name) is { } was && Changed(was.Heads, palette.Heads) is { Count: > 0 } heads)
                lines.Add($"palette {palette.Name}: values of head{(heads.Count == 1 ? "" : "s")} {List(heads)}");
    }

    /// <summary>The heads (patch), types and groups.</summary>
    private static void Patch(DesignShow before, DesignShow after, List<string> lines)
    {
        foreach (var head in after.Rig.Heads.Where(h => before.Head(h.Number) == null))
            lines.Add($"head {head.Number} {head.Name} ({head.Type}) patched at {DmxAddress.Format(head.Address)}");
        foreach (var head in before.Rig.Heads.Where(h => after.Head(h.Number) == null))
            lines.Add($"head {head.Number} {head.Name} removed from the patch");
        foreach (var head in after.Rig.Heads)
        {
            if (before.Head(head.Number) is not { } was)
                continue;
            var what = new List<string>();
            if (was.Address != head.Address)
                what.Add($"moved to {DmxAddress.Format(head.Address)}");
            if (was.Type != head.Type)
                what.Add($"type {head.Type}");
            if (was.Name != head.Name)
                what.Add($"named \"{head.Name}\"");
            if (was.Gel != head.Gel)
                what.Add(head.Gel is { } gel ? $"gel {GelCodes.Code(gel)}" : "no gel");
            if (was.Beam != head.Beam)
                what.Add($"beam angle {head.Beam?.ToString() ?? "default"}");
            if (what.Count > 0)
                lines.Add($"head {head.Number}: {string.Join(", ", what)}");
        }
        var groups = before.Rig.Groups.ToDictionary(g => g.Number);
        foreach (var group in after.Rig.Groups)
            if (!groups.TryGetValue(group.Number, out var was))
                lines.Add($"group {group.Number} {group.Name} added");
            else if (was.Name != group.Name || !was.Heads.SequenceEqual(group.Heads))
                lines.Add($"group {group.Number} {group.Name} changed");
        foreach (var group in before.Rig.Groups.Where(g => after.Rig.Groups.All(n => n.Number != g.Number)))
            lines.Add($"group {group.Number} {group.Name} removed");
    }

    /// <summary>The rig items, matched by id.</summary>
    private static void Rig(DesignShow before, DesignShow after, List<string> lines)
    {
        var old = before.Rig.Items.ToDictionary(i => i.Id);
        var now = after.Rig.Items.ToDictionary(i => i.Id);
        int added = now.Keys.Count(id => !old.ContainsKey(id)), removed = old.Keys.Count(id => !now.ContainsKey(id));
        int moved = now.Values.Count(i => old.TryGetValue(i.Id, out var was) && !SameItem(was, i));
        if (added > 0)
            lines.Add($"{added} lamp{(added == 1 ? "" : "s")} / object{(added == 1 ? "" : "s")} added to the rig");
        if (removed > 0)
            lines.Add($"{removed} lamp{(removed == 1 ? "" : "s")} / object{(removed == 1 ? "" : "s")} removed from the rig");
        if (moved > 0)
            lines.Add($"{moved} lamp{(moved == 1 ? "" : "s")} / object{(moved == 1 ? "" : "s")} moved or changed in the rig");
    }

    /// <summary>True when two rig items are the same (positions to the cm, as MagicVis keeps them).</summary>
    private static bool SameItem(DesignItem a, DesignItem b)
    {
        static bool Close(double[] x, double[] y) =>
            Enumerable.Range(0, 3).All(i => System.Math.Abs((x != null && i < x.Length ? x[i] : 0) - (y != null && i < y.Length ? y[i] : 0)) < 0.005);
        return a.Kind == b.Kind && a.Parent == b.Parent && a.Address == b.Address && a.Head == b.Head && a.Type == b.Type
               && a.Name == b.Name && Close(a.At, b.At) && Close(a.Rot, b.Rot) && Close(a.Size, b.Size);
    }

    /// <summary>The heads whose values differ.</summary>
    private static List<string> Changed(Dictionary<string, Dictionary<string, LookValue>> a, Dictionary<string, Dictionary<string, LookValue>> b)
    {
        a ??= new();
        b ??= new();
        return a.Keys.Union(b.Keys)
            .Where(h => !Looks.Same(new() { [h] = a.GetValueOrDefault(h) ?? new() }, new() { [h] = b.GetValueOrDefault(h) ?? new() }))
            .OrderBy(h => DesignShow.HeadOfKey(h) ?? 0).ToList();
    }

    /// <summary>True when two DMX maps are the same.</summary>
    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) =>
        (a?.Count ?? 0) == (b?.Count ?? 0) && (a == null || a.All(kv => b != null && b.TryGetValue(kv.Key, out int v) && v == kv.Value));

    /// <summary>A time rounded as the console keeps it (0.01 s).</summary>
    private static double Seconds(double? value) => System.Math.Round(value ?? 0, 2);

    /// <summary>"12, 14, 15, ... (20)".</summary>
    private static string List(IReadOnlyList<string> items) =>
        string.Join(", ", items.Take(8)) + (items.Count > 8 ? $", ... ({items.Count})" : "");

    /// <summary>
    /// Takes a re-imported show into `show` (the same object, so the session's history and editor follow): its rig,
    /// palettes and cue list, keeping what only Kloarte has (the cues' delays, by id).
    /// </summary>
    public static void TakeInto(DesignShow show, DesignShow imported)
    {
        var delays = show.CueOrder.Where(c => c.Id != null && c.Delay != null).ToDictionary(c => c.Id.Value, c => c.Delay);
        foreach (var cue in imported.CueOrder.Where(c => c.Id != null && delays.ContainsKey(c.Id.Value)))
            cue.Delay = delays[cue.Id.Value];
        show.ReplaceWith(imported);
    }
}
