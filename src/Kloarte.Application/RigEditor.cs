using System.Collections.Generic;
using System;
using System.Linq;
using System.Numerics;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// Rig, plan and patch changes: moving lamps (snapping to trusses and stands), flying trusses / setting stand heights
/// (lamps stay attached and keep their focus point), adding and removing lamps and stands, re-hanging and re-aiming
/// lamps, patching and beam angles. Edits the show model (rig items, mechanics) and the console's patch
/// (<see cref="IShowEditor"/>), then tells the stage what changed (<see cref="IStage"/>). Everything ends up in the
/// console's show and the mechanics (both saved with Save).
/// </summary>
/// <typeparam name="TShow">The console's show document.</typeparam>
public sealed class RigEditor<TShow> : IRigCommands where TShow : class, IShowDocument
{
    /// <summary>Plan distance within which a dragged lamp snaps to a truss or stand (metres).</summary>
    public const float SnapDistance = 0.5f;

    /// <summary>The open show (model and patch editor).</summary>
    private readonly ShowSession<TShow> _session;

    /// <summary>The 3D stage to update.</summary>
    private readonly IStage _stage;

    /// <summary>Creates the editor of a session's shows.</summary>
    public RigEditor(ShowSession<TShow> session, IStage stage)
    {
        _session = session;
        _stage = stage;
    }

    /// <summary>The fixture catalogue (types of new lamps).</summary>
    private FixtureCatalog _catalog => _session.Catalog;

    /// <summary>The loaded show.</summary>
    private ShowModel<TShow> Model => _session.Model;

    /// <summary>The rig view of the show.</summary>
    private Rig Rig => new(Model.Show);

    /// <summary>Rig &lt;-&gt; stage coordinates.</summary>
    private StageFrame Frame => _stage.Frame;

    /// <summary>The lamp (rig item) with this id.</summary>
    private IRigItem Lamp(int id) =>
        Model.Show.RigItems.FirstOrDefault(i => i.IsFixture && i.Id == id) ?? throw new ArgumentException($"no lamp {id:x}");

    /// <summary>The object (not a lamp) with this id, or null.</summary>
    private IRigItem Object(int id) => id == 0 ? null : Model.Show.RigItems.FirstOrDefault(i => !i.IsFixture && i.Id == id);

    /// <summary>A lamp's rig position in stage coordinates.</summary>
    private Vector3 StagePosition(IRigItem lamp) => Frame.ToStage(lamp.X, lamp.Y, lamp.Z);

    /// <summary>The catalogue model of a lamp (from its mechanics), or null.</summary>
    private FixtureModel ModelOf(int lampId) =>
        Model.Mechanics.TryGetValue(lampId, out var m) && !string.IsNullOrEmpty(m.Model) ? _catalog?.Find(m.Model) : null;

    /// <inheritdoc />
    public int MoveLampInPlan(int lampId, Vector2 plan, bool snap)
    {
        _session.NameNextEdit("Move lamp");
        var lamp = Lamp(lampId);
        var rig = Rig;
        float y = StagePosition(lamp).Y;
        if (snap)
        {
            var target = Frame.FromStage(new Vector3(plan.X, y, plan.Y));
            if (rig.Snap(target, SnapDistance, rig.HangOf(lamp)) is { } hit)
            {
                lamp.ParentId = hit.Support.Id;
                MoveLamp(lampId, Frame.ToStage(hit.Position.X, hit.Position.Y, hit.Position.Z));
                return hit.Support.Id;
            }
        }
        lamp.ParentId = 0;
        MoveLamp(lampId, new Vector3(plan.X, y, plan.Y));
        return 0;
    }

    /// <inheritdoc />
    public void SetSupportHeight(int supportId, float stageHeight)
    {
        _session.NameNextEdit("Truss height");
        foreach (var lamp in Rig.SetHeight(supportId, stageHeight + Frame.FloorY))
            MoveLamp(lamp.Id, StagePosition(lamp));
        _stage.SupportChanged(supportId);
    }

    /// <inheritdoc />
    public int AddStand(string type, Vector2 plan, float stageHeight)
    {
        _session.NameNextEdit("Add stand");
        var p = Frame.FromStage(new Vector3(plan.X, 0, plan.Y));
        var stand = Rig.AddStand(type, p.X, p.Z, Frame.FloorY, stageHeight + Frame.FloorY);
        _stage.SupportAdded(stand.Item);
        return stand.Id;
    }

    /// <summary>
    /// The lamp hangs on the nearest truss (0.33 m below it, pointing straight down, focused on the floor below) or
    /// sits on the nearest stand (aimed at centre stage, 1.5 m high); without supports it hangs free at 6 m. It gets a
    /// head of its own on the next free head number and DMX address. The rig record is copied from a lamp with the same
    /// kind of patch (a single-channel lamp for conventional types, the same moving head in the catalogue's mode for
    /// moving types: user, T1s are added in Mode 3); a show without such a lamp gets it from the console templates.
    /// </summary>
    public int AddLamp(string modelId, Vector2 plan)
    {
        _session.NameNextEdit("Add lamp");
        var show = Model.Show;
        var model = _catalog?.Find(modelId) ?? throw new ArgumentException($"unknown fixture type '{modelId}'");
        var editor = _session.Editor;
        var lamps = show.RigItems.Where(i => i.IsFixture).ToList();
        var template = model.IsMoving
            ? lamps.FirstOrDefault(l => ModelOf(l.Id)?.Id == model.Id && show.LampPatchOf(l).Personality == model.Personality)
            : lamps.Where(l => ModelOf(l.Id) is not { IsMoving: true } && editor.ChannelCount(l.HeadNumber) == 1)
                .OrderByDescending(l => ModelOf(l.Id)?.Id == model.Id).FirstOrDefault();
        string headName = model.Name.Length > 15 ? model.Name[..15] : model.Name;

        var rig = Rig;
        var target = Frame.FromStage(new Vector3(plan.X, 6f, plan.Y));
        var hit = rig.Snap(target, float.MaxValue);
        var pos = hit?.Position ?? target;
        int parent = hit?.Support.Id ?? 0;
        var stagePos = Frame.ToStage(pos.X, pos.Y, pos.Z);
        Vector3? focus = hit is { Support.IsStand: true } ? new Vector3(0, 1.5f, -4f) : null;
        float rx = 0, ry = 0;
        if (focus is { } fc)
            (rx, ry) = Rig.AimRotation(pos, Frame.FromStage(fc));

        IRigItem lamp;
        if (template != null)
        {
            lamp = rig.AddLamp(template, pos.X, pos.Y, pos.Z, parent, rx, ry);
            try
            {
                editor.AddLampHead(lamp, headName);
            }
            catch (ShowEditException)
            {
                rig.RemoveLamp(lamp);
                throw;
            }
        }
        else
        {
            lamp = editor.ImportLamp(model.Personality, headName, model.Name);
            Rig.Place(lamp, pos.X, pos.Y, pos.Z, parent, rx, ry);
        }
        var mech = FixtureMechanics.DefaultFor(model, model.Id);
        var f = focus ?? stagePos with { Y = 0 };
        mech.FocusPoint = new[] { f.X, f.Y, f.Z };
        Model.Mechanics[lamp.Id] = mech;
        _stage.LampAdded(lamp);
        return lamp.Id;
    }

    /// <inheritdoc />
    public string RemoveLamp(int lampId)
    {
        _session.NameNextEdit("Remove lamp");
        var lamp = Lamp(lampId);
        // As a technician names it: its type and head (no internal ids, UI review 22).
        string type = Model.Mechanics.TryGetValue(lampId, out var mech) && _session.Catalog?.Resolve(mech.Model) is { } model
            ? model.ShortName : "lamp";
        Rig.RemoveLamp(lamp);
        bool unpatched = _session.Editor.RemoveUnusedHead(lamp.HeadNumber);
        Model.Mechanics.Remove(lampId);
        _stage.LampRemoved(lampId);
        return $"Removed {type} of head {lamp.HeadNumber}" + (unpatched ? " (the head too)" : " (the head stays patched)");
    }

    /// <inheritdoc />
    public string SetGel(IEnumerable<int> lampIds, int gelId)
    {
        var heads = lampIds.Select(Lamp).Where(l => l is { HeadNumber: > 0 }).Select(l => l.HeadNumber).Distinct()
            .OrderBy(h => h).ToList();
        if (heads.Count == 0)
            return "No patched lamps selected";
        string gel = gelId == 0 ? GelCodes.OpenWhite : GelCodes.Code(gelId);
        _session.NameNextEdit($"Gel {gel}");
        int channels = heads.Sum(h => _session.Editor.SetGel(h, gelId));
        _stage.PatchChanged();
        string list = string.Join(", ", heads.Take(8)) + (heads.Count > 8 ? ", ..." : "");
        return channels == 0 ? $"Head{(heads.Count == 1 ? "" : "s")} {list} already {gel}"
            : $"Gel {gel} on head{(heads.Count == 1 ? "" : "s")} {list}";
    }

    /// <inheritdoc />
    public string RenameHead(int lampId, string name)
    {
        var lamp = Lamp(lampId);
        // The name the lamp shows is the one of the patch at its address (LampPatchOf), so that head is renamed: the
        // MagicVis head field of a lamp can differ from it (RT2025_031: address 212, head 402).
        IShowDocument doc = Model.Show;
        int head = doc.HeadAt(lamp.Address);
        if (head <= 0)
            return "Could not rename: the lamp has no head (patch it first)";
        string clean = HeadNames.Clean(name);
        if (clean.Length == 0)
            return "Could not rename: give the head a name";
        _session.NameNextEdit($"Rename head {head}");
        int channels = _session.Editor.RenameHead(head, clean);
        _stage.PatchChanged();
        int onHead = doc.RigItems.Count(l => l.IsFixture && doc.HeadAt(l.Address) == head);
        return channels == 0 ? $"Head {head} is already named \"{clean}\""
            : $"Head {head} is now \"{clean}\"" + (onHead > 1 ? $" ({onHead} lamps share the head)" : "");
    }

    /// <inheritdoc />
    public void RemoveSupport(int supportId)
    {
        _session.NameNextEdit("Remove stand");
        Rig.Remove(supportId);
        _stage.SupportRemoved(supportId);
    }

    /// <inheritdoc />
    public string ApplyLampPatch(int lampId, int head, int address)
    {
        _session.NameNextEdit("Patch");
        string done = _session.Editor.ApplyLampPatch(Lamp(lampId), head, address);
        _stage.PatchChanged();
        return done;
    }

    /// <inheritdoc />
    public void SetParent(int lampId, int parentId)
    {
        _session.NameNextEdit("Hang lamp");
        var lamp = Lamp(lampId);
        var oldParent = Object(lamp.ParentId);
        var newParent = Object(parentId);
        lamp.ParentId = parentId;
        if (newParent is not { ObjectKind: RigObjectKind.Pipe })
            return;
        var position = StagePosition(lamp);
        float hang = oldParent is { ObjectKind: RigObjectKind.Pipe }
            ? Frame.ToStage(oldParent.X, oldParent.Y, oldParent.Z).Y - position.Y
            : Rig.DefaultHang;
        var start = Frame.ToStage(newParent.X, newParent.Y, newParent.Z);
        var dir = Vector3.Normalize(Rig.Rotate(Vector3.UnitX, newParent.RotX, newParent.RotY, newParent.RotZ));
        float t = Math.Clamp(Vector3.Dot(position - start, dir), 0f, newParent.SizeA);
        MoveLamp(lampId, start + dir * t - Vector3.UnitY * hang);
    }

    /// <summary>
    /// Moves the lamp's rig position; a lamp that aims at its focus point gets the rig rotation that points it there
    /// (roll 0), like re-hanging and re-focusing a lamp. Moving heads keep their mounting rotation.
    /// </summary>
    public void MoveLamp(int lampId, Vector3 stagePosition)
    {
        _session.NameNextEdit("Move lamp");
        Place(lampId, stagePosition);
    }

    /// <inheritdoc />
    public LampMotion MotionOf(int lampId) => Rig.MotionOf(Lamp(lampId));

    /// <inheritdoc />
    public string MoveLampAxis(int lampId, MotionAxis axis, float stageValue)
    {
        var lamp = Lamp(lampId);
        var motion = Rig.MotionOf(lamp);
        var here = StagePosition(lamp);
        var asked = Frame.FromStage(axis switch
        {
            MotionAxis.X => here with { X = stageValue },
            MotionAxis.Y => here with { Y = stageValue },
            _ => here with { Z = stageValue },
        });
        float rigValue = axis switch { MotionAxis.X => asked.X, MotionAxis.Y => asked.Y, _ => asked.Z };
        if (motion.Move(axis, rigValue, out bool stopped) is not { } target)
            return motion.WhyFixed(axis);
        var support = motion.Support;
        string name = ModelOf(lampId) is { } type ? $"{type.ShortName} head {lamp.HeadNumber}" : $"lamp {lamp.HeadNumber}";
        _session.NameNextEdit(support == null ? $"Move {name}" : $"Move {name} {(support.IsStand ? "on" : "along")} {support.Name}");
        Place(lampId, Frame.ToStage(target.X, target.Y, target.Z));
        return stopped ? $"Stopped at the end of {support.Name}" : null;
    }

    /// <summary>
    /// Puts the lamp at a stage position (no undo name): a lamp that aims at its focus point gets the rig rotation that
    /// points it there (roll 0), like re-hanging and re-focusing a lamp. Moving heads keep their mounting rotation.
    /// </summary>
    private void Place(int lampId, Vector3 stagePosition)
    {
        var lamp = Lamp(lampId);
        var rig = Frame.FromStage(stagePosition);
        (lamp.X, lamp.Y, lamp.Z) = (rig.X, rig.Y, rig.Z);
        if (_stage.AimsAtFocus(lampId) && Model.Mechanics.TryGetValue(lampId, out var m) && m.FocusPoint is { Length: 3 } fp)
        {
            var focus = new Vector3(fp[0], fp[1], fp[2]);
            if ((focus - stagePosition).LengthSquared() > 1e-12f)
            {
                var (rx, ry) = Rig.AimRotation(stagePosition, focus);
                (lamp.RotX, lamp.RotY, lamp.RotZ) = (rx, ry, 0f);
            }
        }
        _stage.LampMoved(lampId);
    }

    /// <inheritdoc />
    public void FocusAt(int lampId, Vector3 stagePoint)
    {
        _session.NameNextEdit("Focus lamp");
        if (!_stage.AimsAtFocus(lampId) || !Model.Mechanics.TryGetValue(lampId, out var m))
            return;
        m.FocusPoint = new[] { stagePoint.X, stagePoint.Y, stagePoint.Z };
        MoveLamp(lampId, StagePosition(Lamp(lampId)));
    }

    /// <inheritdoc />
    public void SetBeamAngle(int lampId, float degrees)
    {
        _session.NameNextEdit("Beam angle");
        int address = Lamp(lampId).Address;
        bool inPatch = _session.Editor.SetBeamAngle(address, Math.Clamp((int)MathF.Round(degrees), 1, 255));
        _stage.BeamAngleSet(address, degrees, inPatch);
    }
}
