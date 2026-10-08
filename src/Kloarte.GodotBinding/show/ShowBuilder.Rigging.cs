using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// ShowBuilder: rigging - which object a lamp hangs on and where (stored in the MagicVis part of the .shw), beam angle
/// in the patch.
/// </summary>
public partial class ShowBuilder
{
    /// <summary>MagicVis objects a head can hang on: trusses/pipes first, then other named objects.</summary>
    public IEnumerable<(int Id, string Name)> RigObjects =>
        Document?.RigItems.Where(v => !v.IsFixture && v.ObjectKind != RigObjectKind.Podium)
             .OrderBy(v => v.ObjectKind == RigObjectKind.Pipe ? 0 : 1)
             .Select(v => (v.Id, string.IsNullOrEmpty(v.Name) ? $"Object {v.Id}" : v.Name))
        ?? Enumerable.Empty<(int, string)>();

    /// <summary>MagicVis object a lamp hangs on (0 = none).</summary>
    public int GetParent(ProfileFixture f) => _visOf.TryGetValue(f, out var v) ? v.ParentId : 0;

    /// <summary>
    /// Hangs the head on another object (<see cref="IRigCommands.SetParent"/>). On a truss/pipe the head is moved onto
    /// it, keeping its current drop below the truss.
    /// </summary>
    public void SetParent(ProfileFixture f, int parentId)
    {
        if (VisOf(f) is { } vis)
            RigCommands.SetParent(vis.Id, parentId);
    }

    /// <summary>
    /// Moves the head (stage coordinates, <see cref="IRigCommands.MoveLamp"/>). It stays focused on its focus point,
    /// like re-hanging and re-focusing a lamp; the MagicVis rotation is updated to match (roll = 0). Moving heads keep
    /// their mounting rotation.
    /// </summary>
    public void MoveFixture(ProfileFixture f, Vector3 stagePos)
    {
        if (VisOf(f) is { } vis)
            RigCommands.MoveLamp(vis.Id, stagePos.ToSystem());
    }

    /// <summary>
    /// Where the head can move: along its truss, up and down its stand, or freely (<see cref="IRigCommands.MotionOf"/>);
    /// null for a lamp that is not in the show (the main test scene), which moves freely.
    /// </summary>
    public LampMotion MotionOf(ProfileFixture f) => VisOf(f) is { } vis ? RigCommands.MotionOf(vis.Id) : null;

    /// <summary>
    /// Sets one coordinate of the head's position (stage coordinates; <see cref="IRigCommands.MoveLampAxis"/>): on a
    /// truss it slides along it. Returns null, or why nothing moved / that it stopped at the truss's end.
    /// </summary>
    public string MoveFixtureAxis(ProfileFixture f, MotionAxis axis, float stageValue) =>
        VisOf(f) is { } vis ? RigCommands.MoveLampAxis(vis.Id, axis, stageValue) : null;

    /// <summary>
    /// Focuses a conventional lamp on a stage point (stage coordinates): it turns there now and its MagicVis rotation
    /// follows (saved with Save, in the show; the focus point with the mechanics). Moving heads are aimed with pan / tilt
    /// in the programmer instead.
    /// </summary>
    public void FocusAt(ProfileFixture f, Vector3 stagePoint)
    {
        if (VisOf(f) is { } vis)
            RigCommands.FocusAt(vis.Id, stagePoint.ToSystem());
    }

    /// <inheritdoc />
    public void LampMoved(int lampId)
    {
        if (FixtureOf(lampId) is not { } f || VisOf(f) is not { } vis)
            return;
        Mount(f).Position = ToStage(vis.X, vis.Y, vis.Z);
        if (!f.AutoAim)
            return;
        if (Model.Mechanics.TryGetValue(lampId, out var m) && m.FocusPoint is { Length: 3 } fp)
            f.FocusPoint = new Vector3(fp[0], fp[1], fp[2]);
        f.AimAtFocus();   // turn now
    }

    /// <summary>
    /// Where a ray (global coordinates, e.g. from a click in a 3D view) first hits the stage floor (or the floor
    /// plane in front of it) or the back wall / cyc, in stage coordinates; null when it hits neither.
    /// </summary>
    public Vector3? StagePointOnRay(Vector3 globalOrigin, Vector3 globalDir)
    {
        var o = ToLocal(globalOrigin);
        var d = (ToLocal(globalOrigin + globalDir) - o).Normalized();
        float best = float.MaxValue;
        if (d.Y < -1e-4f)
        {
            float t = -o.Y / d.Y;
            if (t > 0 && (o + d * t).Z >= Frame.BackStageZ)
                best = t;
        }
        if (d.Z < -1e-4f)
        {
            float t = (Frame.BackStageZ - o.Z) / d.Z;
            var p = o + d * t;
            if (t > 0 && t < best && p.Y >= 0 && p.Y <= 12f)
                best = t;
        }
        return best < float.MaxValue ? o + d * best : null;
    }

    /// <summary>
    /// Changes the beam angle of a head in the MagicQ patch (whole degrees, saved with the show) and on its lamps
    /// (<see cref="IRigCommands.SetBeamAngle"/>).
    /// </summary>
    public void SetBeamAngle(ProfileFixture f, float degrees)
    {
        if (VisOf(f) is { } vis)
            RigCommands.SetBeamAngle(vis.Id, degrees);
    }

    /// <inheritdoc />
    public void BeamAngleSet(int address, float degrees, bool inPatch)
    {
        foreach (var lamp in _visOf.Keys.Where(x => x.DmxAddress == address))
        {
            lamp.FieldAngle = degrees;
            if (inPatch)
                lamp.SetMeta("beamFromMagicQ", true);
        }
    }
}
