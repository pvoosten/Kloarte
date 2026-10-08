using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>MagicQ's record order (first occurrence) up to the MagicVis records: personalities, patch, MagicVis.</summary>
    private const string RecordOrder = "VTYZKUPLHFGWSXC";

    /// <summary>
    /// Adds a lamp of another show (e.g. data/magicq_templates.shw) to this one as a new lamp on a new head: the
    /// MagicVis fixture (H), its head's patch (L records) and the personality (P; reused when this show has one with the
    /// same name, else copied with a free id). The patch goes to `address` as head `headNumber` named `headName`, open
    /// white with the personality's default beam angle; the lamp hangs on nothing, at the source lamp's position (the
    /// caller places it). Records are copied unchanged otherwise and inserted where MagicQ keeps them. Returns the lamp.
    /// </summary>
    public VisItem ImportLamp(MagicQShow source, VisItem lamp, int headNumber, int address, string headName)
    {
        var sourcePersonality = source.Personalities.FirstOrDefault(p => p.Id == lamp.Personality)
                                ?? throw new ArgumentException($"the lamp's personality {lamp.Personality} is not in its show");
        var channels = source.Patch.Where(p => p.HeadNumber == lamp.HeadNumber && p.Personality == lamp.Personality)
                                   .OrderBy(p => p.Address).ToList();
        if (channels.Count == 0)
            throw new ArgumentException($"head {lamp.HeadNumber} of the lamp is not patched in its show");

        var personality = Personalities.FirstOrDefault(p => p.Name == sourcePersonality.Name);
        if (personality == null)
        {
            var record = Shw.InsertRecordAfter(LastRecordOf("P"), sourcePersonality.Record.CopyLines());
            record.SetHex(1, Personalities.Count == 0 ? 1 : Personalities.Max(p => p.Id) + 1);
            personality = new Personality(record);
            Personalities.Add(personality);
        }

        var after = LastRecordOf("L");
        int offset = address - channels[0].Address;
        foreach (var ch in channels)
        {
            var record = Shw.InsertRecordAfter(after, ch.Record.CopyLines());
            var copy = new PatchChannel(record)
            {
                Address = ch.Address + offset,
                HeadNumber = headNumber,
                HeadName = headName,
            };
            record.SetHex(4, personality.Id);
            if (copy.GelOrNone != 0)
                copy.Gel = 0;
            if (copy.BeamAngle != 0)
                copy.BeamAngle = 0;
            Patch.Add(copy);
            after = record;
        }

        var visRecord = Shw.InsertRecordAfter(LastRecordOf("H"), lamp.Record.CopyLines());
        visRecord.SetHex(1, Vis.Count == 0 ? 1 : Vis.Max(v => v.Id) + 1);
        visRecord.SetHex(22, personality.Id);
        var vis = new VisItem(visRecord) { ParentId = 0, Address = address, HeadNumber = headNumber };
        Vis.Add(vis);
        return vis;
    }

    /// <summary>
    /// Adds a copy of a record (of this show or another, e.g. the templates) where MagicQ keeps records of its type: after
    /// the last one of that type, else after the types MagicQ writes before it. The caller fills it in.
    /// </summary>
    public ShwRecord AddRecordFrom(ShwRecord template) => Shw.InsertRecordAfter(LastRecordOf(template.Type), template.CopyLines());

    /// <summary>
    /// The personality of this name: this show's, else a copy of the one in `templates` (a free id). Null when neither
    /// has it.
    /// </summary>
    public Personality EnsurePersonality(MagicQShow templates, string name)
    {
        if (Personalities.FirstOrDefault(p => p.Name == name) is { } own)
            return own;
        var source = templates?.Personalities.FirstOrDefault(p => p.Name == name);
        if (source == null)
            return null;
        var record = AddRecordFrom(source.Record);
        record.SetHex(1, Personalities.Count == 0 ? 1 : Personalities.Max(p => p.Id) + 1);
        var personality = new Personality(record);
        Personalities.Add(personality);
        return personality;
    }

    /// <summary>Removes a cue (S record).</summary>
    public void RemoveCue(Cue cue)
    {
        Shw.RemoveRecord(cue.Record);
        Cues.Remove(cue);
    }

    /// <summary>Removes a palette (F record).</summary>
    public void RemovePalette(Palette palette)
    {
        Shw.RemoveRecord(palette.Record);
        Palettes.Remove(palette);
    }

    /// <summary>Removes a group (G record).</summary>
    public void RemoveGroup(Group group)
    {
        Shw.RemoveRecord(group.Record);
        Groups.Remove(group);
    }

    /// <summary>Removes a patch channel (L record).</summary>
    public void RemovePatchChannel(PatchChannel channel)
    {
        Shw.RemoveRecord(channel.Record);
        Patch.Remove(channel);
    }

    /// <summary>
    /// The record a new record of `type` goes after: the last record of that type, else the last record of the types
    /// MagicQ writes before it (<see cref="RecordOrder"/>), else the last record of the file.
    /// </summary>
    private ShwRecord LastRecordOf(string type)
    {
        string before = RecordOrder[..(RecordOrder.IndexOf(type, StringComparison.Ordinal) + 1)];
        for (int i = before.Length - 1; i >= 0; i--)
            if (Shw.Records.LastOrDefault(r => r.Type == before[i].ToString(CultureInfo.InvariantCulture)) is { } record)
                return record;
        return Shw.Records.LastOrDefault() ?? throw new InvalidOperationException("the show has no records");
    }
}
