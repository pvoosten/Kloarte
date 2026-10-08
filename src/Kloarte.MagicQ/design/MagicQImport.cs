using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// A MagicQ show as a native show (<see cref="DesignShow"/>; decided 14 Oct 2026): personalities become fixture types
/// (with the catalogue's GDTF for the moving heads), the patch becomes heads, MagicVis items the rig, palettes and the
/// main cue stack (the one with the most steps) the palettes and the cue list, every value in physical units where the
/// channel has them. Exact: after every cue of the main stack the native show's DMX is the show's (palette references
/// kept where the palette holds the value). What can't come along as it was is in the notes.
/// </summary>
public static class MagicQImport
{
    /// <summary>MagicQ's palette window names by type, for palettes that need a type in their name.</summary>
    private static readonly Dictionary<int, string> TypeNames = new()
    {
        [(int)MagicQShow.PaletteType.Intensity] = "intensity", [(int)MagicQShow.PaletteType.Position] = "position",
        [(int)MagicQShow.PaletteType.Colour] = "colour", [(int)MagicQShow.PaletteType.Beam] = "beam",
    };

    /// <summary>Imports a show file (read only).</summary>
    public static ShowImport Import(string path, FixtureCatalog catalog) => Import(MagicQShow.Load(path), path, catalog);

    /// <summary>Imports a loaded show; `path` is remembered as the source the exports are written into a copy of.</summary>
    public static ShowImport Import(MagicQShow source, string path, FixtureCatalog catalog)
    {
        var notes = new List<string>();
        var design = new DesignShow();
        design.Rig.Source = path;
        var typeNames = ImportTypes(source, design, catalog, notes);
        var heads = ImportHeads(source, design, typeNames, notes);
        foreach (var (id, name, table) in source.UserCurves)
            design.Rig.Curves.Add(new DesignCurve { Id = id, Name = name, Table = table.ToArray() });
        foreach (var group in source.Groups)
            design.Rig.Groups.Add(new DesignGroup { Number = group.Number, Name = group.Name, Heads = group.Heads.ToArray() });
        ImportItems(source, design, typeNames);
        design.Changed();
        var palettes = ImportPalettes(source, design, heads, notes);
        design.Changed();
        ImportCues(source, design, heads, palettes, notes);
        design.Changed();
        foreach (string problem in design.Problems())
            notes.Add("not imported as it was: " + problem);
        return new ShowImport(design, notes);
    }

    /// <summary>The personalities as fixture types (P id -&gt; type name).</summary>
    private static Dictionary<int, string> ImportTypes(MagicQShow source, DesignShow design, FixtureCatalog catalog, List<string> notes)
    {
        var names = new Dictionary<int, string>();
        foreach (var p in source.Personalities)
        {
            var channels = p.Channels;
            var type = new DesignPersonality
            {
                Name = p.Name,
                Mode = string.IsNullOrEmpty(p.Mode) ? null : p.Mode,
                Channels = channels.Select(c => c.Name).ToList(),
                Classes = new string(channels.Select(c => DesignPersonality.ClassOf(c.Name, c.Attribute == 0)).ToArray()),
            };
            if (GdtfOf(p.Name, channels.Count, type.Channels, catalog) is { } gdtf)
                (type.Gdtf, type.GdtfMode) = gdtf;
            var same = design.Type(type.Name);
            if (same != null && !(same.Channels.SequenceEqual(type.Channels) && same.Classes == type.Classes))
            {
                string unique = type.Name;
                for (int n = 2; design.Type(unique) != null; n++)
                    unique = $"{type.Name} #{n}";
                notes.Add($"personality {p.Id} \"{p.Name}\" differs from another of that name: fixture type \"{unique}\"");
                type.Name = unique;
                same = null;
            }
            if (same == null)
                design.Rig.Types.Add(type);
            names[p.Id] = type.Name;
        }
        return names;
    }

    /// <summary>
    /// The GDTF file and mode of a personality: a catalogue moving head whose MagicQ personality has the same name up to
    /// its mode ("Robe_RobinT1Profile"), and its mode with the personality's channel count and colour channels.
    /// </summary>
    private static (string Prefix, string Mode)? GdtfOf(string personality, int count, IEnumerable<string> channelNames, FixtureCatalog catalog)
    {
        static string Base(string name)
        {
            int mode = name.LastIndexOf("_Mode", StringComparison.OrdinalIgnoreCase);
            return (mode > 0 ? name[..mode] : name).Trim();
        }
        foreach (var model in catalog?.Models ?? new List<FixtureModel>())
        {
            if (string.IsNullOrEmpty(model.Gdtf) || string.IsNullOrEmpty(model.MagicQ)
                || !string.Equals(Base(model.MagicQ), Base(personality), StringComparison.OrdinalIgnoreCase))
                continue;
            var mode = GdtfLibrary.Get(model.Gdtf)?.ModeFor(count, channelNames);
            if (mode != null)
                return (model.Gdtf, mode.Name);
        }
        return null;
    }

    /// <summary>The patch as heads (DMX address -&gt; head, for the cues and palettes).</summary>
    private static Dictionary<int, DesignHead> ImportHeads(MagicQShow source, DesignShow design, Dictionary<int, string> typeNames,
        List<string> notes)
    {
        var byAddress = new Dictionary<int, DesignHead>();
        foreach (var group in source.Patch.GroupBy(p => (p.HeadNumber, p.HeadName, p.Personality)).OrderBy(g => g.Min(p => p.Address)))
        {
            var channels = group.OrderBy(p => p.Address).ToList();
            int first = channels[0].Address, count = channels[^1].Address - first + 1;
            if (channels.Select(c => c.Address).Distinct().Count() != count)
                notes.Add($"head {group.Key.HeadNumber}: its {channels.Count} channels are not one block from {first} (taken as {count})");
            if (design.Head(group.Key.HeadNumber) != null)
            {
                notes.Add($"head number {group.Key.HeadNumber} is used by two heads: the one at {first} is left out (its values stay as DMX)");
                continue;
            }
            var type = design.Type(typeNames.GetValueOrDefault(group.Key.Personality, ""));
            int intensity = type?.IntensityOffset ?? 0;
            var curveChannel = channels.FirstOrDefault(c => c.Address == first + Math.Max(intensity, 0)) ?? channels[0];
            var head = new DesignHead
            {
                Number = group.Key.HeadNumber,
                Name = group.Key.HeadName,
                Type = type?.Name ?? "",
                Address = first,
                Channels = count,
                Gel = channels[0].GelOrNone is > 0 and var gel ? gel : null,
                Beam = channels[0].BeamAngle is > 0 and var beam ? beam : null,
                Shape = channels[0].Shape is > 0 and var shape ? shape : null,
                Curve = curveChannel.Curve >= 4 ? curveChannel.Curve - 3 : null,
            };
            design.Rig.Heads.Add(head);
            for (int a = first; a < first + count; a++)
                byAddress.TryAdd(a, head);
        }
        return byAddress;
    }

    /// <summary>The MagicVis items as the rig.</summary>
    private static void ImportItems(MagicQShow source, DesignShow design, Dictionary<int, string> typeNames)
    {
        foreach (var vis in source.Vis)
        {
            IRigItem rig = vis;
            bool lamp = vis.Kind == MagicQShow.VisKind.Fixture;
            var item = new DesignItem
            {
                Id = vis.Id,
                Kind = DesignItem.KindName(rig.ObjectKind, vis.Kind == MagicQShow.VisKind.Fixture),
                Parent = vis.ParentId > 0 ? vis.ParentId : null,
                At = new[] { DesignItem.Round(vis.X), DesignItem.Round(vis.Y), DesignItem.Round(vis.Z) },
                Rot = vis.RotX == 0 && vis.RotY == 0 && vis.RotZ == 0 ? null
                    : new[] { DesignItem.Round(vis.RotX), DesignItem.Round(vis.RotY), DesignItem.Round(vis.RotZ) },
            };
            if (lamp)
            {
                item.Address = vis.Address > 0 ? vis.Address : null;
                item.Head = vis.HeadNumber > 0 ? vis.HeadNumber : null;
                item.Type = typeNames.GetValueOrDefault(vis.Personality);
            }
            else
            {
                item.Size = new[] { DesignItem.Round(vis.SizeA), DesignItem.Round(vis.SizeB), DesignItem.Round(vis.SizeC) };
                item.Name = string.IsNullOrEmpty(vis.Name) ? null : vis.Name;
                item.Code = string.IsNullOrEmpty(vis.ObjectCode) ? null : vis.ObjectCode;
                item.Model = string.IsNullOrEmpty(vis.Model) ? null : vis.Model;
            }
            design.Rig.Items.Add(item);
        }
    }

    /// <summary>The palettes (MagicQ palette id -&gt; native palette).</summary>
    private static Dictionary<int, DesignPalette> ImportPalettes(MagicQShow source, DesignShow design, Dictionary<int, DesignHead> heads,
        List<string> notes)
    {
        var made = new List<(MagicQShow.Palette Record, DesignPalette Palette)>();
        foreach (var record in source.Palettes)
        {
            var values = new Dictionary<int, int>();
            foreach (var (address, value, link) in record.Values)
                values[address] = link != 0 && source.FindPalette(link) is { } linked && linked.TryGetValue(address, out int v, out _) ? v : value;
            var palette = new DesignPalette
            {
                Name = PaletteName(record),
                Magicq = new[] { record.Id },
                Heads = Looks(design, values, heads, out int loose),
            };
            if (loose > 0)
                notes.Add($"palette {record.Id:x4} \"{palette.Name}\": {loose} values of no head left out");
            made.Add((record, palette));
        }
        // One combined palette for palettes of one name and different types holding the same heads; else unique names.
        var result = new Dictionary<int, DesignPalette>();
        foreach (var group in made.GroupBy(m => m.Palette.Name))
        {
            var list = group.ToList();
            bool merge = list.Count > 1 && list.Select(m => m.Record.Type).Distinct().Count() == list.Count
                         && list.All(m => m.Palette.Heads.Keys.ToHashSet().SetEquals(list[0].Palette.Heads.Keys))
                         && !list.SelectMany(m => m.Palette.Heads.SelectMany(h => h.Value.Keys.Select(k => (h.Key, k))))
                             .GroupBy(x => x).Any(x => x.Count() > 1);
            if (merge)
            {
                var combined = new DesignPalette { Name = group.Key, Magicq = list.Select(m => m.Record.Id).ToArray() };
                foreach (var (_, palette) in list)
                    foreach (var (head, values) in palette.Heads)
                    {
                        if (!combined.Heads.TryGetValue(head, out var into))
                            combined.Heads[head] = into = new Dictionary<string, LookValue>();
                        foreach (var (key, value) in values)
                            into[key] = value;
                    }
                design.Palettes.Add(combined);
                foreach (var (record, _) in list)
                    result[record.Id] = combined;
                continue;
            }
            foreach (var (record, palette) in list)
            {
                if (list.Count > 1)
                {
                    string name = $"{palette.Name} ({TypeNames.GetValueOrDefault((int)record.Type, "palette")})";
                    string unique = name;
                    for (int n = 2; design.Palette(unique) != null || made.Any(m => m.Palette.Name == unique && m.Palette != palette); n++)
                        unique = $"{name} {n}";
                    palette.Name = unique;
                }
                design.Palettes.Add(palette);
                result[record.Id] = palette;
            }
        }
        return result;
    }

    /// <summary>The name a MagicQ palette gets in Kloarte: its own (trimmed), else "Palette a00a".</summary>
    internal static string PaletteName(MagicQShow.Palette record) =>
        string.IsNullOrWhiteSpace(record.Name) ? $"Palette {record.Id:x4}" : record.Name.Trim();

    /// <summary>
    /// DMX values (address -&gt; value) as values per head: each channel of a head's layout whose bytes are all there,
    /// read in its units; a byte of a channel without its other byte as "#n". `loose` = values of no head.
    /// </summary>
    private static Dictionary<string, Dictionary<string, LookValue>> Looks(DesignShow design, Dictionary<int, int> values,
        Dictionary<int, DesignHead> heads, out int loose)
    {
        var looks = new Dictionary<string, Dictionary<string, LookValue>>();
        loose = values.Keys.Count(a => !heads.ContainsKey(a));
        foreach (var head in values.Keys.Where(heads.ContainsKey).Select(a => heads[a]).Distinct().OrderBy(h => h.Address))
        {
            var layout = design.LayoutOf(head);
            var look = new Dictionary<string, LookValue>();
            foreach (var channel in layout.Channels)
                if (channel.Read(values, head.Address) is { } dmx)
                    look[channel.Key] = channel.FromDmx(dmx);
                else
                    foreach (int offset in channel.Offsets.Where(o => values.ContainsKey(head.Address + o - 1)))
                        look[Raw(offset)] = LookValue.Of(values[head.Address + offset - 1]);
            if (look.Count > 0)
                looks[DesignShow.Key(head.Number)] = look;
        }
        return looks;
    }

    /// <summary>The key of one channel of a head alone ("#12").</summary>
    private static string Raw(int offset) => "#" + offset.ToString(CultureInfo.InvariantCulture);

    /// <summary>The main cue stack (the one with the most steps) as the cue list.</summary>
    private static void ImportCues(MagicQShow source, DesignShow design, Dictionary<int, DesignHead> heads,
        Dictionary<int, DesignPalette> palettes, List<string> notes)
    {
        var stack = source.CueStacks.OrderByDescending(s => s.Steps.Count).ThenBy(s => s.Number).FirstOrDefault();
        if (stack == null)
            return;
        var others = source.CueStacks.Where(s => s != stack).Select(s => s.Number.ToString(CultureInfo.InvariantCulture)).ToList();
        if (others.Count > 0)
            notes.Add($"only cue stack {stack.Number} is the cue list; stack{(others.Count == 1 ? "" : "s")} {string.Join(", ", others)} " +
                      "stay in the MagicQ show (exports keep them)");
        design.CueList.Name = string.IsNullOrEmpty(stack.Name) ? null : stack.Name;
        design.CueList.Stack = stack.Number;
        var cues = new Dictionary<int, MagicQShow.Cue>();
        foreach (var cue in source.Cues)
            cues.TryAdd(cue.Id, cue);
        var seen = new HashSet<int>();
        foreach (var step in stack.Steps)
        {
            if (!seen.Add(step.CueId))
            {
                notes.Add($"cue {step.CueNumberText} plays cue id {step.CueId} a second time: left out");
                continue;
            }
            var entry = new DesignCue
            {
                Cue = step.CueNumberText,
                Id = step.CueId,
                Text = string.IsNullOrEmpty(step.Text) ? null : step.Text,
            };
            if (cues.TryGetValue(step.CueId, out var cue))
            {
                entry.Name = string.IsNullOrEmpty(cue.Name) || cue.Name == step.Text ? null : cue.Name;
                entry.FadeIn = Time(cue.Record.GetHex(6));
                entry.FadeOut = Time(cue.Record.GetHex(7));
                entry.Heads = CueLooks(source, design, cue, heads, palettes, out var loose);
                if (loose.Count > 0)
                    entry.Dmx = loose;
            }
            design.CueOrder.Add(entry);
        }
    }

    /// <summary>A cue time in seconds (null for 0).</summary>
    private static double? Time(int units) => units == 0 ? null : MagicQTimes.Seconds(units);

    /// <summary>
    /// A cue's channels as values per head: a channel whose bytes all refer to one palette that holds this value for it is
    /// that palette ("@name"); others are their values. Values of no head go to `loose` (address -&gt; DMX).
    /// </summary>
    private static Dictionary<string, Dictionary<string, LookValue>> CueLooks(MagicQShow source, DesignShow design, MagicQShow.Cue cue,
        Dictionary<int, DesignHead> heads, Dictionary<int, DesignPalette> palettes, out Dictionary<string, int> loose)
    {
        var values = new Dictionary<int, int>();
        var from = new Dictionary<int, int>();
        foreach (var (address, raw) in cue.Channels)
        {
            values[address] = source.Resolve(address, raw);
            int id = raw >> 8;
            if (id != 0 && source.FindPalette(id) is { } p && p.TryGetValue(address, out _, out _))
                from[address] = id;
        }
        var looks = Looks(design, values, heads, out _);
        foreach (var (key, look) in looks)
        {
            var head = design.Head(DesignShow.HeadOfKey(key).Value);
            var layout = design.LayoutOf(head);
            foreach (var channelKey in look.Keys.ToList())
            {
                var channel = layout[channelKey];
                var ids = channel.Offsets.Select(o => from.GetValueOrDefault(head.Address + o - 1)).Distinct().ToList();
                if (ids.Count != 1 || ids[0] == 0 || !palettes.TryGetValue(ids[0], out var palette))
                    continue;
                var held = palette.Of(head.Number)?.GetValueOrDefault(channelKey);
                if (held != null && channel.ToDmx(held) == channel.ToDmx(look[channelKey]))
                    look[channelKey] = LookValue.OfPalette(palette.Name);
            }
        }
        loose = values.Where(v => !heads.ContainsKey(v.Key))
            .ToDictionary(v => v.Key.ToString(CultureInfo.InvariantCulture), v => v.Value);
        return looks;
    }
}
