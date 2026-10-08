using System.Collections.Generic;
using System.Linq;
using Godot;
using SysVec2 = System.Numerics.Vector2;
using SysVec3 = System.Numerics.Vector3;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// ShowBuilder: the plan (top view) and the rig and patch changes made from it - moving lamps (snapping to trusses and
/// stands), flying trusses / setting stand heights (lamps stay attached and keep their focus point), adding floor
/// stands, and patching (DMX address, head number). Everything ends up in the MagicQ show (saved with Save).
/// </summary>
public partial class ShowBuilder
{
    /// <summary>Raised after a lamp was added or removed (fixture lists refresh).</summary>
    public event System.Action LampsChanged;

    /// <summary>Plan distance within which a dragged lamp snaps to a truss or stand (metres).</summary>
    public const float SnapDistance = 0.5f;

    /// <summary>The rig view (trusses, stands, lamps) of the loaded show.</summary>
    public Rig Rig => Model == null ? null : new Rig(Model.Show);

    /// <summary>Snapshot of everything the plan shows, in plan coordinates (stage x, stage z).</summary>
    public PlanData PlanData()
    {
        var show = Model.Show;
        var data = new PlanData
        {
            Show = System.IO.Path.GetFileName(Files.SourceFile),
            Venue = Venue?.Name ?? "",
            StageEdge = StageEdge,
            StageBack = Frame.BackStageZ,
            StageWidth = Venue?.StageWidth ?? 15f,
            CycWidth = Venue?.Cyc != null && !Frame.OwnPodium ? Venue.Cyc.Width : 0f,
            SeatWidth = Venue?.Auditorium?.SeatWidth ?? 0.5f,
        };
        foreach (var o in Document.RigItems.Where(v => v.ObjectKind is RigObjectKind.Proscenium or RigObjectKind.Box or RigObjectKind.Border))
            data.Shapes.Add(new PlanShape
            {
                Name = o.Name,
                Points = Footprint(o),
                Masking = o.ObjectKind is RigObjectKind.Proscenium or RigObjectKind.Border || o.Name.StartsWith("Manteau"),
            });
        foreach (var s in Rig.Supports)
            data.Supports.Add(new PlanSupport
            {
                Id = s.Id,
                Name = s.Name,
                Start = Plan(ToStage(s.Start.X, s.Start.Y, s.Start.Z)),
                End = Plan(ToStage(s.End.X, s.End.Y, s.End.Z)),
                Height = s.Height - Frame.FloorY,
                IsStand = s.IsStand,
                Selected = s.Id == SelectedSupport,
            });
        var selected = Selection.Fixtures.ToHashSet();
        foreach (var (f, vis) in _visOf)
        {
            if (!IsInstanceValid(f))
                continue;
            var model = ModelOf(f);
            int gelId = f.HasMeta("gelId") ? f.GetMeta("gelId").AsInt32() : 0;
            var gel = gelId > 0 ? Gels?[gelId] : null;
            Rgb? gelColour = null;
            if (gel != null)
            {
                float m = Mathf.Max(gel.R, Mathf.Max(gel.G, gel.B));
                gelColour = m > 0 ? new Rgb(gel.R / m, gel.G / m, gel.B / m) : Rgb.Black;
            }
            Vector3 aim = f.AutoAim ? f.FocusPoint : AimPoint(ToLocal(f.GlobalPosition), -f.GlobalBasis.Z);
            data.Lamps.Add(new PlanLamp
            {
                VisId = vis.Id,
                Position = Plan(Mount(f).Position),
                Height = Mount(f).Position.Y,
                Focus = Plan(aim),
                Kind = model?.Kind ?? (f.HasMeta("kind") ? f.GetMeta("kind").AsString() : FixtureKind.Pc),
                Model = model?.Id ?? "",
                ModelName = model?.Name ?? "",
                Small = model != null && (model.Id.Contains("par36") || model.Kind == FixtureKind.LedBar),
                Gel = gelColour,
                GelCode = GelCode(gelId),
                GelName = gel?.Name ?? "",
                Head = vis.HeadNumber,
                Address = vis.Address,
                Support = vis.ParentId,
                Selected = selected.Contains(f),
            });
        }
        if (Venue?.Auditorium != null && !Frame.OwnPodium)
            foreach (var row in Venue.Auditorium.Layout(StageEdge))
                foreach (var seat in row.Seats)
                    data.Seats.Add((new SysVec2(seat.X, row.Z), Venue.Auditorium.IsReserved(row.Name, seat.Number)));
        return data;
    }

    /// <summary>Support selected in the plan / truss-height view (0 = none).</summary>
    public int SelectedSupport { get; set; }

    private static SysVec2 Plan(Vector3 stage) => new(stage.X, stage.Z);

    /// <summary>Plan footprint of a box / border / proscenium object.</summary>
    private List<SysVec2> Footprint(IRigItem o)
    {
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(o.RotX), Mathf.DegToRad(o.RotY), Mathf.DegToRad(o.RotZ)), EulerOrder.Yxz);
        var pos = ToStage(o.X, o.Y, o.Z);
        // Box / border: size = (depth z, width x, height); proscenium: size = (width x, height, depth z).
        bool proscenium = o.ObjectKind == RigObjectKind.Proscenium;
        float w = proscenium ? o.SizeA : o.SizeB, d = proscenium ? o.SizeC : o.SizeA;
        w = Mathf.Max(w, 0.02f);
        d = Mathf.Max(d, 0.02f);
        return new[] { new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0, -d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(-w / 2, 0, d / 2) }
            .Select(c => Plan(pos + basis * c)).ToList();
    }

    /// <summary>Rig, plan and patch changes (the application's editor).</summary>
    public IRigCommands RigCommands => Services.Rig;

    /// <summary>
    /// Moves a lamp in the plan (<see cref="IRigCommands.MoveLampInPlan"/>). Returns the support it snapped to (null if
    /// none).
    /// </summary>
    public RigSupport MoveLampInPlan(ProfileFixture f, SysVec2 plan, bool snap)
    {
        var vis = VisOf(f);
        if (vis == null)
            return null;
        int support = RigCommands.MoveLampInPlan(vis.Id, plan, snap);
        return support == 0 ? null : Rig.Find(support);
    }

    /// <summary>Flies a truss / sets a stand's height (<see cref="IRigCommands.SetSupportHeight"/>).</summary>
    public void SetSupportHeight(int supportId, float stageHeight) => RigCommands.SetSupportHeight(supportId, stageHeight);

    /// <summary>Adds a floor stand of a house type at a plan position, `height` above the stage floor.</summary>
    public RigSupport AddStand(string type, SysVec2 plan, float height) => Rig.Find(RigCommands.AddStand(type, plan, height));

    /// <summary>
    /// Adds a lamp of a catalogue type at a plan position (<see cref="IRigCommands.AddLamp"/>). Returns the new lamp,
    /// or throws with the reason.
    /// </summary>
    public ProfileFixture AddLampInPlan(string modelId, SysVec2 plan) => FixtureOf(RigCommands.AddLamp(modelId, plan));

    /// <summary>Removes a lamp (<see cref="IRigCommands.RemoveLamp"/>). Returns a status text.</summary>
    public string RemoveLamp(ProfileFixture f) => VisOf(f) is { } vis ? RigCommands.RemoveLamp(vis.Id) : "No lamp selected";

    /// <summary>Removes a floor stand; lamps on it stay where they are, unattached.</summary>
    public void RemoveSupport(int supportId) => RigCommands.RemoveSupport(supportId);

    /// <summary>
    /// Patches a lamp to a head number and DMX address (<see cref="IRigCommands.ApplyLampPatch"/>). Returns null when it
    /// worked (`done` says what was done), else the reason it was refused.
    /// </summary>
    public string ApplyLampPatch(ProfileFixture f, int head, int address, out string done)
    {
        done = null;
        if (VisOf(f) is not { } vis)
            return "No lamp selected";
        try
        {
            done = RigCommands.ApplyLampPatch(vis.Id, head, address);
            return null;
        }
        catch (ShowEditException e)
        {
            return e.Message;
        }
    }

    /// <summary>Number of lamps on a head.</summary>
    public int LampsOnHead(int head) => _visOf.Values.Count(v => v.HeadNumber == head);

    // --- IStage: the application changed the model, the nodes follow ---

    /// <inheritdoc />
    public bool AimsAtFocus(int lampId) => FixtureOf(lampId) is { } f && f.AutoAim;

    /// <inheritdoc />
    public void LampAdded(IRigItem lamp)
    {
        _intensityAddress.Clear();
        AddFixture(GetNode("Fixtures"), lamp, Document, LoadConfig());
        LampsChanged?.Invoke();
    }

    /// <inheritdoc />
    public void LampRemoved(int lampId)
    {
        if (FixtureOf(lampId) is not { } f)
            return;
        _visOf.Remove(f);
        _fixtureOf.Remove(lampId);
        _movers.Remove(f);
        _intensityAddress.Clear();
        f.QueueFree();
        LampsChanged?.Invoke();
    }

    /// <inheritdoc />
    public void SupportAdded(IRigItem support) => AddObject(GetNode("Scenery"), support);

    /// <inheritdoc />
    public void SupportChanged(int supportId) => RebuildSupportNode(supportId);

    /// <inheritdoc />
    public void SupportRemoved(int supportId) => RemoveSupportNode(supportId);

    /// <inheritdoc />
    public void PatchChanged()
    {
        _intensityAddress.Clear();
        foreach (var (f, vis) in _visOf.ToList())
            RefreshLampPatch(f, vis);
        AssignGroups();
    }

    private void RemoveSupportNode(int id)
    {
        foreach (var node in GetNode("Scenery").GetChildren().Where(n => n.HasMeta("vis") && n.GetMeta("vis").AsInt32() == id))
            node.QueueFree();
    }

    private void RebuildSupportNode(int id)
    {
        RemoveSupportNode(id);
        if (Rig.Find(id) is { } s)
            AddObject(GetNode("Scenery"), s.Item);
    }

    private void RefreshLampPatch(ProfileFixture f, IRigItem vis)
    {
        var patch = Document.LampPatchOf(vis);
        f.DmxAddress = vis.Address;
        f.HeadNumber = vis.HeadNumber;
        int universe = (vis.Address - 1) / 512 + 1, channel = (vis.Address - 1) % 512 + 1;
        f.Name = $"{universe}.{channel:000} {patch.HeadName} #{vis.Id:x}".Replace("/", "-");
        f.SetMeta("headName", patch.HeadName);
        ApplyGel(f, patch.GelId);
    }
}
