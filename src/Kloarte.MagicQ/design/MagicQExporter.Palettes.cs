using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

internal sealed partial class MagicQExporter
{
    /// <summary>Palettes written as one MagicQ palette (all their channels): name -&gt; palette id.</summary>
    private readonly Dictionary<string, int> _onePalette = new();

    /// <summary>Palettes written as several MagicQ palettes: (name, address) -&gt; the palette id that holds the address.</summary>
    private readonly Dictionary<(string Name, int Address), int> _channelPalette = new();

    /// <summary>
    /// The palettes as F records. A palette that is one MagicQ palette (imported so) stays that one record, all its
    /// channels in it (MagicQ's combined palettes too); any other palette becomes one record per type of its channels
    /// (intensity, position, colour, beam), the ones it had first. Values are written where they differ (values of
    /// addresses of no head stay); a palette the design hasn't any more is removed, unless a cue of another stack uses it.
    /// The design's palettes get the ids they are written as.
    /// </summary>
    private void ExportPalettes()
    {
        var used = new HashSet<int>();
        foreach (var palette in _design.Palettes)
        {
            var channels = _design.ChannelsOf(palette).ToDictionary(c => c.Address, c => c.Value);
            var records = (palette.Magicq ?? new int[0]).Select(_show.FindPalette).Where(p => p != null).Distinct().ToList();
            var parts = new List<(MagicQShow.Palette Record, Dictionary<int, int> Values)>();
            if (records.Count == 1 && palette.Magicq.Length == 1)
            {
                parts.Add((records[0], channels));
                _onePalette[palette.Name] = records[0].Id;
            }
            else
            {
                // A channel stays in the record that holds it (MagicQ's combined palettes hold several types); others go
                // to the record of their type.
                var byRecord = new Dictionary<MagicQShow.Palette, Dictionary<int, int>>();
                foreach (var (address, value) in channels)
                {
                    var type = TypeOf(ClassAt(address));
                    var record = records.FirstOrDefault(r => r.TryGetValue(address, out _, out _))
                                 ?? records.FirstOrDefault(r => r.Type == type)
                                 ?? byRecord.Keys.FirstOrDefault(r => r.Type == type)
                                 ?? NewPalette(type, palette.Name);
                    if (!byRecord.TryGetValue(record, out var values))
                        byRecord[record] = values = new Dictionary<int, int>();
                    values[address] = value;
                    _channelPalette[(palette.Name, address)] = record.Id;
                }
                foreach (var (record, values) in byRecord.OrderBy(r => r.Key.Id))
                    parts.Add((record, values));
            }
            foreach (var (record, values) in parts)
            {
                used.Add(record.Id);
                var now = ValuesOf(record);
                foreach (var (address, value) in now.Where(v => _design.HeadCovering(v.Key) == null))
                    values.TryAdd(address, value);   // values of no head (left out at import) stay
                if (now.Count != values.Count || now.Any(v => values.GetValueOrDefault(v.Key, -1) != v.Value))
                {
                    record.SetValues(values.Select(v => (v.Key, v.Value)));
                    Wrote($"palette {record.Id:x4} \"{palette.Name}\": {values.Count} values");
                    Check("palettes written by Kloarte (their values; the rest of the record copied from another palette)");
                }
                string imported = MagicQImport.PaletteName(record);
                if (palette.Name != imported && !palette.Name.StartsWith(imported + " (", System.StringComparison.Ordinal))
                {
                    record.Name = Clean(palette.Name, 64);
                    Wrote($"palette {record.Id:x4} named \"{palette.Name}\"");
                }
            }
            palette.Magicq = parts.Count == 0 ? null : parts.Select(p => p.Record.Id).ToArray();
        }
        var otherStacks = OtherStackPaletteIds();
        foreach (var gone in _show.Palettes.Where(p => !used.Contains(p.Id)).ToList())
        {
            if (otherStacks.Contains(gone.Id))
            {
                Warnings.Add($"palette {gone.Id:x4} \"{gone.Name}\" is not in Kloarte but another cue stack uses it: kept");
                continue;
            }
            _show.RemovePalette(gone);
            Wrote($"palette {gone.Id:x4} \"{gone.Name}\" removed");
        }
    }

    /// <summary>The ids of the palettes the cues of the other cue stacks refer to.</summary>
    private HashSet<int> OtherStackPaletteIds()
    {
        int number = _design.CueList.Stack ?? 1;
        var ids = _show.CueStacks.Where(s => s.Number != number).SelectMany(s => s.Steps.Select(x => x.CueId)).ToHashSet();
        return _show.Cues.Where(c => ids.Contains(c.Id)).SelectMany(c => c.Channels).Select(c => c.Value >> 8).Where(i => i != 0).ToHashSet();
    }

    /// <summary>A palette's values as MagicQ uses them (a linked value is the linked palette's).</summary>
    private Dictionary<int, int> ValuesOf(MagicQShow.Palette palette)
    {
        var values = new Dictionary<int, int>();
        foreach (var (address, value, link) in palette.Values)
            values[address] = link != 0 && _show.FindPalette(link) is { } linked && linked.TryGetValue(address, out int v, out _) ? v : value;
        return values;
    }

    /// <summary>A new, empty palette of a type (the next free entry), its record copied from the simplest palette of that type.</summary>
    private MagicQShow.Palette NewPalette(MagicQShow.PaletteType type, string name)
    {
        string prefix = ((int)type).ToString("x", CultureInfo.InvariantCulture);
        var template = _show.Shw.Records.Concat(_templates.Shw.Records).Any(r => r.Type == "F" && r[1].StartsWith(prefix))
            ? SimplestRecord(r => r.Type == "F" && r.FieldCount > 3 && r[1].StartsWith(prefix))
            : SimplestRecord(r => r.Type == "F" && r.FieldCount > 3);
        int id = (int)type << 12 | _show.FreePaletteEntry(type);
        var record = _show.AddRecordFrom(template);
        var palette = new MagicQShow.Palette(record);
        palette.Record[1] = id.ToString("x4", CultureInfo.InvariantCulture);
        palette.SetValues(new (int, int)[0]);
        palette.Name = Clean(name, 64);
        _show.Palettes.Add(palette);
        Wrote($"palette {id:x4} \"{name}\" added");
        Check("palettes written by Kloarte (their values; the rest of the record copied from another palette)");
        return palette;
    }

    /// <summary>
    /// The raw value of a cue channel: a value of a palette refers to the MagicQ palette it is written in (when that
    /// palette holds the address), else the DMX value.
    /// </summary>
    private int Raw(DesignChannel channel)
    {
        if (channel.Palette == null)
            return channel.Value;
        int id = _onePalette.TryGetValue(channel.Palette, out int one) ? one
            : _channelPalette.GetValueOrDefault((channel.Palette, channel.Address));
        return id != 0 && _show.FindPalette(id) is { } p && p.TryGetValue(channel.Address, out _, out _)
            ? id << 8 | channel.Value : channel.Value;
    }
}
