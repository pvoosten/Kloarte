using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

internal sealed partial class MagicQExporter
{
    /// <summary>
    /// The heads as L records: a head that is there with its personality and channel count keeps its records (moved,
    /// renamed, gel / beam / shape set when they differ); a head of another type or a new head gets records copied from a
    /// head of its personality (the show's, else the templates'); a head the design hasn't any more loses its records.
    /// </summary>
    private void ExportPatch()
    {
        var groups = _show.Patch.GroupBy(p => (p.HeadNumber, p.HeadName, p.Personality))
            .Select(g => g.OrderBy(p => p.Address).ToList()).ToList();
        var matched = new HashSet<List<MagicQShow.PatchChannel>>();
        var designNumbers = _design.Rig.Heads.Select(h => h.Number).ToHashSet();
        foreach (var head in _design.Rig.Heads)
        {
            var personality = _show.EnsurePersonality(_templates, head.Type);
            if (personality == null)
            {
                Warnings.Add($"head {head.Number}: MagicQ has no personality \"{head.Type}\" (not in the show nor the templates): not exported");
                continue;
            }
            var match = groups.FirstOrDefault(g => !matched.Contains(g) && g[0].HeadNumber == head.Number && g[0].Address == head.Address)
                        ?? groups.FirstOrDefault(g => !matched.Contains(g) && g[0].HeadNumber == head.Number);
            if (match != null)
                matched.Add(match);
            if (match != null && match[0].Personality == personality.Id && match.Count == head.Channels)
            {
                UpdateHead(head, match);
                continue;
            }
            if (match != null)
                foreach (var channel in match)
                    _show.RemovePatchChannel(channel);
            NewHead(head, personality);
        }
        foreach (var group in groups.Where(g => !matched.Contains(g) && !designNumbers.Contains(g[0].HeadNumber)))
        {
            foreach (var channel in group)
                _show.RemovePatchChannel(channel);
            Wrote($"head {group[0].HeadNumber} removed from the patch");
        }
    }

    /// <summary>Sets what differs of a head that keeps its L records.</summary>
    private void UpdateHead(DesignHead head, List<MagicQShow.PatchChannel> channels)
    {
        var done = new List<string>();
        int offset = head.Address - channels[0].Address;
        if (offset != 0)
        {
            foreach (var channel in channels)
                channel.Address += offset;
            done.Add($"moved to {DmxAddress.Format(head.Address)}");
        }
        string name = HeadNames.Clean(head.Name);
        if (channels.Any(c => c.HeadName != head.Name && c.HeadName != name))
        {
            foreach (var channel in channels)
                channel.HeadName = name;
            done.Add($"named \"{name}\"");
        }
        int gel = head.Gel ?? 0;
        if (channels.Any(c => c.GelOrNone != gel && c.Record.FieldCount > 11))
        {
            foreach (var channel in channels.Where(c => c.Record.FieldCount > 11))
                channel.Gel = gel;
            done.Add(gel == 0 ? "no gel" : $"gel {GelCodes.Code(gel)}");
        }
        if (channels[0].BeamAngle != (head.Beam ?? 0))
        {
            foreach (var channel in channels)
                channel.BeamAngle = head.Beam ?? 0;
            done.Add($"beam angle {head.Beam ?? 0}");
        }
        if (channels[0].Shape != (head.Shape ?? 0))
        {
            foreach (var channel in channels)
                channel.Shape = head.Shape ?? 0;
            done.Add($"shape {head.Shape ?? 0}");
        }
        var type = _design.Type(head.Type);
        int intensity = System.Math.Max(type?.IntensityOffset ?? 0, 0);
        var curveChannel = channels.FirstOrDefault(c => c.Address == head.Address + intensity) ?? channels[0];
        int curve = curveChannel.Curve >= 4 ? curveChannel.Curve - 3 : 0;
        if (curve != (head.Curve ?? 0))
            Warnings.Add($"head {head.Number}: the dimmer curve is not written (set it in MagicQ: curve {head.Curve?.ToString() ?? "linear"})");
        if (done.Count > 0)
            Wrote($"head {head.Number}: {string.Join(", ", done)}");
    }

    /// <summary>A new head: L records copied from a head of the personality, patched as the design has it.</summary>
    private void NewHead(DesignHead head, MagicQShow.Personality personality)
    {
        var source = _show.Patch.Where(p => p.Personality == personality.Id).GroupBy(p => p.HeadNumber).FirstOrDefault()?.OrderBy(p => p.Address).ToList();
        if (source == null)
        {
            var templateType = _templates.Personalities.FirstOrDefault(p => p.Name == personality.Name);
            source = templateType == null ? null
                : _templates.Patch.Where(p => p.Personality == templateType.Id).GroupBy(p => p.HeadNumber).FirstOrDefault()?.OrderBy(p => p.Address).ToList();
        }
        if (source == null || source.Count == 0)
        {
            Warnings.Add($"head {head.Number}: no patched head of \"{personality.Name}\" to copy the patch from: not exported");
            return;
        }
        if (source.Count != head.Channels)
            Warnings.Add($"head {head.Number}: {head.Channels} channels in Kloarte, MagicQ's \"{personality.Name}\" has {source.Count}");
        int start = source[0].Address;
        foreach (var template in source)
        {
            var record = _show.AddRecordFrom(template.Record);
            record.SetHex(4, personality.Id);
            var channel = new MagicQShow.PatchChannel(record)
            {
                Address = head.Address + template.Address - start,
                HeadNumber = head.Number,
                HeadName = HeadNames.Clean(head.Name),
            };
            if (record.FieldCount > 11)
                channel.Gel = head.Gel ?? 0;
            channel.BeamAngle = head.Beam ?? 0;
            channel.Shape = head.Shape ?? 0;
            _show.Patch.Add(channel);
        }
        Wrote($"head {head.Number} \"{head.Name}\" ({personality.Name}) patched at {DmxAddress.Format(head.Address)}");
        Check("new heads in the patch (L records copied from a head of the same personality)");
    }

    /// <summary>The groups: names and members set where they differ; new ones copied from a group record; gone ones removed.</summary>
    private void ExportGroups()
    {
        foreach (var group in _design.Rig.Groups)
        {
            var existing = _show.Groups.FirstOrDefault(g => g.Number == group.Number);
            if (existing == null)
            {
                var record = _show.AddRecordFrom(TemplateRecord(r => r.Type == "G" && r.FieldCount > 3));
                record.SetHex(1, group.Number);
                existing = new MagicQShow.Group(record);
                _show.Groups.Add(existing);
                existing.Name = Clean(group.Name, 64);
                existing.SetHeads(group.Heads ?? new int[0]);
                Wrote($"group {group.Number} \"{group.Name}\" added");
                Check("new groups (G records copied from another group)");
                continue;
            }
            if (existing.Name != Clean(group.Name, 64))
            {
                existing.Name = Clean(group.Name, 64);
                Wrote($"group {group.Number} renamed \"{group.Name}\"");
            }
            if (!existing.Heads.SequenceEqual(group.Heads ?? new int[0]))
            {
                existing.SetHeads(group.Heads ?? new int[0]);
                Wrote($"group {group.Number}: heads {List(group.Heads.Select(h => h.ToString()))}");
            }
        }
        foreach (var gone in _show.Groups.Where(g => _design.Rig.Groups.All(d => d.Number != g.Number)).ToList())
        {
            _show.RemoveGroup(gone);
            Wrote($"group {gone.Number} \"{gone.Name}\" removed");
        }
    }

    /// <summary>The dimmer curves: MagicQ's user curves are not written (a warning when they differ).</summary>
    private void ExportCurves()
    {
        var curves = _show.UserCurves.ToDictionary(c => c.Id, c => c.Table);
        foreach (var curve in _design.Rig.Curves)
            if (!curves.TryGetValue(curve.Id, out var table) || !table.SequenceEqual(curve.Table))
                Warnings.Add($"dimmer curve {curve.Id} \"{curve.Name}\" is not written (edit curves in MagicQ)");
    }
}
