using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// The rig of a show: trusses / pipes and floor stands (MagicVis pipe objects) and the lamps on them (MagicVis
/// fixtures; their parent id is the support). Snapping lamps onto supports, flying a truss to another height (its lamps
/// move along) and adding floor stands. Works on the show document's rig items (<see cref="IRigDocument"/>, MagicVis
/// coordinates); aiming is pure maths (<see cref="AimRotation"/>), the caller re-aims the lamps at their focus
/// points. No Godot or MagicQ dependencies.
/// </summary>
public sealed class Rig
{
    /// <summary>Default drop from a truss to the lamp's MagicVis position (MagicVis rigs: 6.00 -&gt; 5.67).</summary>
    public const float DefaultHang = 0.33f;

    /// <summary>A lamp on a stand sits this far above the stand's top.</summary>
    public const float StandMount = 0.15f;

    /// <summary>The show document whose rig items this is.</summary>
    private readonly IRigDocument _doc;

    /// <summary>Creates the rig view of a show document.</summary>
    public Rig(IRigDocument doc) => _doc = doc;

    /// <summary>All pipe objects (trusses, pipes, stands).</summary>
    public IReadOnlyList<RigSupport> Supports =>
        _doc.RigItems.Where(v => v.ObjectKind == RigObjectKind.Pipe).Select(v => new RigSupport(v)).ToList();

    /// <summary>Horizontal trusses / pipes, upstage first.</summary>
    public IEnumerable<RigSupport> Trusses => Supports.Where(s => !s.IsStand).OrderBy(s => s.Start.Z);

    /// <summary>Floor stands / booms.</summary>
    public IEnumerable<RigSupport> Stands => Supports.Where(s => s.IsStand);

    /// <summary>The lamps (MagicVis fixtures) hanging on a support.</summary>
    public IEnumerable<IRigItem> LampsOn(int supportId) =>
        _doc.RigItems.Where(v => v.IsFixture && v.ParentId == supportId);

    /// <summary>The support with this id, or null.</summary>
    public RigSupport Find(int id) => Supports.FirstOrDefault(s => s.Id == id);

    /// <summary>
    /// The nearest support within `maxDistance` (plan distance) of a point, and where on it a lamp would go: on a
    /// truss the nearest point, keeping `hang` below it; on a stand at its top.
    /// </summary>
    public (RigSupport Support, Vector3 Position)? Snap(Vector3 p, float maxDistance, float hang = DefaultHang)
    {
        var best = Supports.Select(s => (s, d: s.PlanDistance(p))).Where(x => x.d <= maxDistance).OrderBy(x => x.d).FirstOrDefault();
        if (best.s == null)
            return null;
        var n = best.s.NearestInPlan(p);
        return (best.s, best.s.IsStand ? n + new Vector3(0, StandMount, 0) : n - new Vector3(0, hang, 0));
    }

    /// <summary>Drop of a lamp below its support (MagicVis); the default when it has no truss.</summary>
    public float HangOf(IRigItem lamp)
    {
        var s = Find(lamp.ParentId);
        if (s == null || s.IsStand)
            return DefaultHang;
        return s.NearestInPlan(new Vector3(lamp.X, lamp.Y, lamp.Z)).Y - lamp.Y;
    }

    /// <summary>Where a lamp can move: along its truss, up and down its stand, or freely (<see cref="LampMotion"/>).</summary>
    public LampMotion MotionOf(IRigItem lamp)
    {
        var position = new Vector3(lamp.X, lamp.Y, lamp.Z);
        return Find(lamp.ParentId) is { } support ? new LampMotion(support, position) : new LampMotion(position);
    }

    /// <summary>
    /// Flies a truss to a new height (or sets a stand's height): the support and every lamp on it move by the same
    /// amount, so they stay attached with the same drop. Returns the lamps that moved (the caller re-aims them at their
    /// unchanged focus points).
    /// </summary>
    public List<IRigItem> SetHeight(int supportId, float height)
    {
        var s = Find(supportId) ?? throw new ArgumentException($"no support {supportId}");
        float dy = height - s.Height;
        if (s.IsStand)
        {
            // A stand stands on the floor: its length changes.
            float bottom = Math.Min(s.Start.Y, s.End.Y);
            s.Item.SizeA = Math.Max(0.05f, height - bottom);
            if (s.Direction.Y < 0)
                s.Item.Y = height;
        }
        else
            s.Item.Y += dy;
        var moved = LampsOn(supportId).ToList();
        foreach (var lamp in moved)
            lamp.Y += dy;
        return moved;
    }

    /// <summary>
    /// Adds a floor stand (a vertical MagicVis pipe from the floor `floorY` up to `height`) at x/z, named
    /// "Stand &lt;type&gt; #n". The record layout is copied from an existing pipe of the show.
    /// </summary>
    public RigSupport AddStand(string type, float x, float z, float floorY, float height)
    {
        var template = _doc.RigItems.LastOrDefault(v => v.ObjectKind == RigObjectKind.Pipe)
                       ?? throw new InvalidOperationException("the show has no truss / pipe to copy the record layout from");
        var vis = _doc.AddRigCopy(template);
        vis.ParentId = 0;
        vis.X = x;
        vis.Y = floorY;
        vis.Z = z;
        vis.RotX = 0;
        vis.RotY = 0;
        vis.RotZ = 90;                    // local +X (the pipe) points up
        vis.SizeA = Math.Max(0.05f, height - floorY);
        int n = Stands.Count(st => st.Name.StartsWith("Stand ")) + 1;
        vis.Name = $"Stand {type} #{n}".Replace("\"", "");
        return new RigSupport(vis);
    }

    /// <summary>
    /// Adds a lamp (MagicVis fixture) at x/y/z hanging on `parentId` (0 = none), aimed with MagicVis rotation rx / ry.
    /// The record is a copy of `template` (a lamp of the same personality), so it starts on the template's head and
    /// address until it is patched.
    /// </summary>
    public IRigItem AddLamp(IRigItem template, float x, float y, float z, int parentId, float rx, float ry)
    {
        var vis = _doc.AddRigCopy(template);
        Place(vis, x, y, z, parentId, rx, ry);
        return vis;
    }

    /// <summary>Puts a lamp at x/y/z hanging on `parentId` (0 = none), aimed with MagicVis rotation rx / ry (roll 0).</summary>
    public static void Place(IRigItem lamp, float x, float y, float z, int parentId, float rx, float ry)
    {
        lamp.ParentId = parentId;
        lamp.X = x;
        lamp.Y = y;
        lamp.Z = z;
        lamp.RotX = rx;
        lamp.RotY = ry;
        lamp.RotZ = 0;
    }

    /// <summary>Removes a lamp (MagicVis fixture).</summary>
    public void RemoveLamp(IRigItem lamp) => _doc.RemoveRigItem(lamp);

    /// <summary>Removes a stand (or pipe) object; its lamps become unattached.</summary>
    public void Remove(int supportId)
    {
        var s = Find(supportId);
        if (s == null)
            return;
        foreach (var lamp in LampsOn(supportId))
            lamp.ParentId = 0;
        _doc.RemoveRigItem(s.Item);
    }

    /// <summary>
    /// MagicVis rotation (degrees) that aims a fixture at `focus` from `position`: fixtures point down (-Y) at zero
    /// rotation, beam = Ry(ry) * Rx(rx) * down, so dir = (-sin rx sin ry, -cos rx, -sin rx cos ry); roll = 0.
    /// </summary>
    public static (float RotX, float RotY) AimRotation(Vector3 position, Vector3 focus)
    {
        var d = Vector3.Normalize(focus - position);
        float rx = MathF.Acos(Math.Clamp(-d.Y, -1f, 1f));
        float ry = MathF.Abs(MathF.Sin(rx)) < 1e-4f ? 0f : MathF.Atan2(-d.X, -d.Z);
        return (rx * 180f / MathF.PI, ry * 180f / MathF.PI);
    }

    /// <summary>Beam direction of a MagicVis fixture rotation.</summary>
    public static Vector3 BeamDirection(float rotX, float rotY, float rotZ) => Rotate(-Vector3.UnitY, rotX, rotY, rotZ);

    /// <summary>Rotates a vector by MagicVis Euler angles (degrees, order Y-X-Z: v' = Ry * Rx * Rz * v).</summary>
    public static Vector3 Rotate(Vector3 v, float rx, float ry, float rz)
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, ry * MathF.PI / 180f)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rx * MathF.PI / 180f)
                * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rz * MathF.PI / 180f);
        return Vector3.Transform(v, q);
    }

    /// <summary>"LT 11 (5.10 m from the datum)" style label helper: height text in metres.</summary>
    public static string Metres(float v) => v.ToString("0.00", CultureInfo.InvariantCulture) + " m";
}
