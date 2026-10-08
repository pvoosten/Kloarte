using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

internal sealed partial class MagicQExporter
{
    /// <summary>
    /// The rig items as MagicVis H records, by id: what differs is set; a new item is a copy of an H record of its kind
    /// (a lamp of its personality, an object of its type: the show's, else the templates' or the house venue's); an item
    /// the design hasn't any more is removed.
    /// </summary>
    private void ExportItems()
    {
        var byId = _show.Vis.ToDictionary(v => v.Id);
        var changed = new List<string>();
        foreach (var item in _design.Rig.Items)
        {
            IRigItem rig = item;
            byId.TryGetValue(item.Id, out var vis);
            bool sameKind = vis != null && (vis.Kind == MagicQShow.VisKind.Fixture) == rig.IsFixture
                            && (rig.IsFixture || vis.ObjectType == (int)rig.ObjectKind);
            if (vis != null && !sameKind)
            {
                _show.RemoveRigItem(vis);
                vis = null;
            }
            if (vis == null)
            {
                vis = NewItem(item);
                if (vis == null)
                    continue;
                Wrote($"rig item {item.Id} ({item.Kind}{(item.Name != null ? " " + item.Name : "")}) added");
                if (rig.ObjectKind == RigObjectKind.Pipe && System.Math.Abs(rig.RotZ - 90) < 0.01f)
                    Check("floor stands (pipes standing up: rotation z 90)");
                SetItem(vis, item, true);
                continue;
            }
            if (SetItem(vis, item, false) is { Count: > 0 } fields)
                changed.Add($"{item.Id} ({string.Join(" ", fields)})");
        }
        if (changed.Count > 0)
            Wrote($"rig item{(changed.Count == 1 ? "" : "s")} changed: {List(changed, 12)}");
        foreach (var gone in _show.Vis.Where(v => _design.Rig.Items.All(i => i.Id != v.Id)).ToList())
        {
            _show.RemoveRigItem(gone);
            Wrote($"rig item {gone.Id} removed");
        }
    }

    /// <summary>A new H record for an item (id set), copied from one of its kind; null (a warning) when there is none.</summary>
    private MagicQShow.VisItem NewItem(DesignItem item)
    {
        IRigItem rig = item;
        MagicQShow.VisItem template;
        int personality = 0;
        if (rig.IsFixture)
        {
            var type = _show.EnsurePersonality(_templates, item.Type ?? _design.HeadCovering(item.Address ?? 0)?.Type ?? "");
            personality = type?.Id ?? 0;
            template = _show.Vis.FirstOrDefault(v => v.Kind == MagicQShow.VisKind.Fixture && v.Personality == personality)
                       ?? _show.Vis.FirstOrDefault(v => v.Kind == MagicQShow.VisKind.Fixture)
                       ?? _templates.Vis.FirstOrDefault(v => v.Kind == MagicQShow.VisKind.Fixture);
        }
        else
        {
            int objectType = (int)rig.ObjectKind;
            template = _show.Vis.FirstOrDefault(v => v.Kind != MagicQShow.VisKind.Fixture && v.ObjectType == objectType)
                       ?? _venue.Vis.FirstOrDefault(v => v.Kind != MagicQShow.VisKind.Fixture && v.ObjectType == objectType)
                       ?? _templates.Vis.FirstOrDefault(v => v.Kind != MagicQShow.VisKind.Fixture && v.ObjectType == objectType);
        }
        if (template == null)
        {
            Warnings.Add($"rig item {item.Id} ({item.Kind}): no MagicVis item of that kind to copy: not exported");
            return null;
        }
        var record = _show.AddRecordFrom(template.Record);
        record.SetHex(1, item.Id);
        if (rig.IsFixture && personality > 0)
            record.SetHex(22, personality);
        var vis = new MagicQShow.VisItem(record);
        _show.Vis.Add(vis);
        Check("new MagicVis items (H records copied from one of the same kind)");
        return vis;
    }

    /// <summary>Sets what differs of an H record (positions compared as MagicVis writes them); the fields it set.</summary>
    private List<string> SetItem(MagicQShow.VisItem vis, DesignItem item, bool all)
    {
        IRigItem rig = item;
        var changed = new List<string>();
        bool Differs(float now, float want) => all || System.Math.Abs(now - want) >= 0.005f;
        if (Differs(vis.X, rig.X)) { vis.X = rig.X; changed.Add("x"); }
        if (Differs(vis.Y, rig.Y)) { vis.Y = rig.Y; changed.Add("y"); }
        if (Differs(vis.Z, rig.Z)) { vis.Z = rig.Z; changed.Add("z"); }
        if (Differs(vis.RotX, rig.RotX)) { vis.RotX = rig.RotX; changed.Add("rotX"); }
        if (Differs(vis.RotY, rig.RotY)) { vis.RotY = rig.RotY; changed.Add("rotY"); }
        if (Differs(vis.RotZ, rig.RotZ)) { vis.RotZ = rig.RotZ; changed.Add("rotZ"); }
        if (all || vis.ParentId != rig.ParentId) { vis.ParentId = rig.ParentId; changed.Add("parentId"); }
        if (rig.IsFixture)
        {
            if (all || vis.Address != rig.Address) { vis.Address = rig.Address; changed.Add("address"); }
            if (all || vis.HeadNumber != rig.HeadNumber) { vis.HeadNumber = rig.HeadNumber; changed.Add("headNumber"); }
            var type = _show.Personalities.FirstOrDefault(p => p.Name == item.Type);
            if (type != null && vis.Personality != type.Id) { vis.Record.SetHex(22, type.Id); changed.Add("type"); }
            return changed;
        }
        bool Size(float now, float want) => all || System.Math.Round(now, 3) != System.Math.Round(want, 3);
        if (Size(vis.SizeA, rig.SizeA)) { vis.SizeA = rig.SizeA; changed.Add("sizeA"); }
        if (Size(vis.SizeB, rig.SizeB)) { vis.SizeB = rig.SizeB; changed.Add("sizeB"); }
        if (Size(vis.SizeC, rig.SizeC)) { vis.SizeC = rig.SizeC; changed.Add("sizeC"); }
        if (all || vis.Name != rig.Name) { vis.Name = rig.Name; changed.Add("name"); }
        return changed;
    }
}
