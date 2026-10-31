using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Where a set piece (<see cref="SceneryPiece"/>) is, from its model data alone: its footprint in the plan, a point in
/// it, and a ray (a click in a 3D view) hitting its box. The piece's own axes: x along its width, z along its depth, y
/// up; turned about its own z (<see cref="SceneryPiece.RotationZ"/>), x (<see cref="SceneryPiece.RotationX"/>), then
/// the vertical (<see cref="SceneryPiece.Rotation"/>, counter-clockwise seen from above). Plan coordinates are
/// (stage x, stage z): a tilted piece shows what it covers seen from above. A split curtain that is open is two halves gathered at the sides
/// (<see cref="Spans"/>). No Godot dependencies: the plan, the PDF and the 3D picking use the same geometry.
/// </summary>
public static class SceneryGeometry
{
    /// <summary>The thinnest a piece is taken to be in the plan and for picking (m): a flat cloth is still clickable.</summary>
    public const float MinThickness = 0.08f;

    /// <summary>What is left of a curtain half's width when it is fully open (it gathers at its side).</summary>
    public const float GatheredWidth = 0.15f;

    /// <summary>Where a ray straight down starts above a piece (m): higher than any piece reaches.</summary>
    private const float AboveStage = 100f;

    /// <summary>Cosine and sine of an angle in degrees.</summary>
    private static (float Cos, float Sin) Angle(float degrees)
    {
        double a = degrees * Math.PI / 180.0;
        return ((float)Math.Cos(a), (float)Math.Sin(a));
    }

    /// <summary>A vector turned about the x axis.</summary>
    private static Vector3 AboutX(Vector3 v, float degrees)
    {
        var (c, s) = Angle(degrees);
        return new Vector3(v.X, v.Y * c - v.Z * s, v.Y * s + v.Z * c);
    }

    /// <summary>A vector turned about the y axis.</summary>
    private static Vector3 AboutY(Vector3 v, float degrees)
    {
        var (c, s) = Angle(degrees);
        return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }

    /// <summary>A vector turned about the z axis.</summary>
    private static Vector3 AboutZ(Vector3 v, float degrees)
    {
        var (c, s) = Angle(degrees);
        return new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
    }

    /// <summary>A vector on the piece's own axes in stage directions.</summary>
    private static Vector3 Turned(SceneryPiece piece, Vector3 v) =>
        AboutY(AboutX(AboutZ(v, piece.RotationZ), piece.RotationX), piece.Rotation);

    /// <summary>A stage direction on the piece's own axes (undoes <see cref="Turned"/>).</summary>
    private static Vector3 Unturned(SceneryPiece piece, Vector3 v) =>
        AboutZ(AboutX(AboutY(v, -piece.Rotation), -piece.RotationX), -piece.RotationZ);

    /// <summary>True when the piece doesn't stand level (it is turned about more than the vertical).</summary>
    public static bool Tilted(SceneryPiece piece) => piece.RotationX != 0f || piece.RotationZ != 0f;

    /// <summary>The stage point of a point on the piece's own axes (from the centre of its bottom).</summary>
    public static Vector3 ToStage(SceneryPiece piece, float x, float y, float z) =>
        Turned(piece, new Vector3(x, y, z)) + new Vector3(piece.Position[0], piece.Position[1], piece.Position[2]);

    /// <summary>The plan point of a point on the piece's own axes (x along its width, z along its depth, y above its bottom).</summary>
    public static Vector2 ToPlan(SceneryPiece piece, float x, float z, float y = 0f)
    {
        var stage = ToStage(piece, x, y, z);
        return new Vector2(stage.X, stage.Z);
    }

    /// <summary>Half the piece's size along its depth as the plan shows it (a curtain: its pleats; at least half <see cref="MinThickness"/>).</summary>
    public static float HalfDepth(SceneryPiece piece) =>
        Math.Max(piece.Shape == SceneryShape.Curtain ? piece.PleatDepth : piece.Depth, MinThickness) / 2f;

    /// <summary>
    /// The parts of the piece along its width (from, to on its x axis): the whole width, or for a split curtain that is
    /// `open` % open its two halves, each gathered towards its side.
    /// </summary>
    public static IReadOnlyList<(float From, float To)> Spans(SceneryPiece piece, float open)
    {
        float hw = Math.Max(piece.Width, MinThickness) / 2f;
        if (!piece.Opens)
            return new[] { (-hw, hw) };
        float o = Math.Clamp(open, 0f, 100f) / 100f;
        float half = hw * (1f - (1f - GatheredWidth) * o);
        return new[] { (-hw, -hw + half), (hw - half, hw) };
    }

    /// <summary>The footprint in the plan: what the whole width (also of an open curtain) covers seen from above.</summary>
    public static Vector2[] Footprint(SceneryPiece piece)
    {
        float hw = Math.Max(piece.Width, MinThickness) / 2f;
        return Outline(piece, -hw, hw);
    }

    /// <summary>The corners of the piece's bottom in the plan.</summary>
    public static Vector2[] Base(SceneryPiece piece)
    {
        float hw = Math.Max(piece.Width, MinThickness) / 2f;
        return Box(piece, -hw, hw, HalfDepth(piece));
    }

    /// <summary>
    /// What the part of the piece from `x0` to `x1` along its width covers seen from above: its four bottom corners when
    /// it stands level, else the convex outline of its corners (a pyramid's: its bottom and apex).
    /// </summary>
    public static Vector2[] Outline(SceneryPiece piece, float x0, float x1)
    {
        float hd = HalfDepth(piece), h = Math.Max(piece.Height, MinThickness);
        var bottom = Box(piece, x0, x1, hd);
        if (!Tilted(piece))
            return bottom;
        var corners = new List<Vector2>(bottom);
        if (piece.Shape == SceneryShape.Pyramid)
            corners.Add(ToPlan(piece, 0f, 0f, h));
        else
            foreach (float x in new[] { x0, x1 })
                foreach (float z in new[] { -hd, hd })
                    corners.Add(ToPlan(piece, x, z, h));
        return Hull(corners);
    }

    /// <summary>The convex hull of points (Andrew's monotone chain).</summary>
    private static Vector2[] Hull(List<Vector2> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        var hull = new List<Vector2>();
        void Add(Vector2 p, int floor)
        {
            while (hull.Count > floor && Cross(hull[^2], hull[^1], p) <= 1e-7f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        foreach (var p in sorted)
            Add(p, 1);
        int lower = hull.Count;
        for (int i = sorted.Count - 2; i >= 0; i--)
            Add(sorted[i], lower);
        hull.RemoveAt(hull.Count - 1);   // the first point again
        return hull.ToArray();
    }

    /// <summary>The z of the cross product (b - a) x (c - a).</summary>
    private static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    /// <summary>The plan corners of the part of the piece from `x0` to `x1` along its width, `hd` deep each side.</summary>
    public static Vector2[] Box(SceneryPiece piece, float x0, float x1, float hd) => new[]
    {
        ToPlan(piece, x0, -hd), ToPlan(piece, x1, -hd), ToPlan(piece, x1, hd), ToPlan(piece, x0, hd),
    };

    /// <summary>A table's leg centres on its own axes (x, z).</summary>
    public static Vector2[] Legs(SceneryPiece piece)
    {
        float lx = Math.Max(0f, piece.Width / 2f - piece.LegThickness / 2f);
        float lz = Math.Max(0f, piece.Depth / 2f - piece.LegThickness / 2f);
        return new[] { new Vector2(-lx, -lz), new Vector2(lx, -lz), new Vector2(lx, lz), new Vector2(-lx, lz) };
    }

    /// <summary>True when a plan point is on the piece seen from above (a ray straight down hits one of its <see cref="Spans"/>).</summary>
    public static bool Contains(SceneryPiece piece, Vector2 plan, float open) =>
        RayHit(piece, new Vector3(plan.X, piece.Position[1] + AboveStage, plan.Y), -Vector3.UnitY, open) != null;

    /// <summary>
    /// Where a ray (stage coordinates) first hits the piece's box (each of its <see cref="Spans"/>, from the floor to its
    /// height): the distance along the ray in units of `dir`, or null when it misses.
    /// </summary>
    public static float? RayHit(SceneryPiece piece, Vector3 origin, Vector3 dir, float open)
    {
        var o = Unturned(piece, origin - new Vector3(piece.Position[0], piece.Position[1], piece.Position[2]));
        var d = Unturned(piece, dir);
        float hd = HalfDepth(piece), h = Math.Max(piece.Height, MinThickness);
        float? best = null;
        foreach (var (from, to) in Spans(piece, open))
            if (Slab(o, d, new Vector3(from, 0f, -hd), new Vector3(to, h, hd)) is { } t && (best == null || t < best))
                best = t;
        return best;
    }

    /// <summary>Ray against an axis-aligned box: the entry distance (0 when the ray starts inside), or null.</summary>
    private static float? Slab(Vector3 o, Vector3 d, Vector3 min, Vector3 max)
    {
        float near = 0f, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float oa = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            float da = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (Math.Abs(da) < 1e-9f)
            {
                if (oa < lo || oa > hi)
                    return null;
                continue;
            }
            float t1 = (lo - oa) / da, t2 = (hi - oa) / da;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            near = Math.Max(near, t1);
            far = Math.Min(far, t2);
            if (near > far)
                return null;
        }
        return near;
    }

    /// <summary>
    /// The angle (degrees, counter-clockwise seen from above) of the direction from the piece's centre to a plan point,
    /// on the same scale as <see cref="SceneryPiece.Rotation"/> (0 = along stage +x).
    /// </summary>
    public static float AngleTo(SceneryPiece piece, Vector2 plan)
    {
        float dx = plan.X - piece.Position[0], dz = plan.Y - piece.Position[2];
        return (float)(Math.Atan2(-dz, dx) * 180.0 / Math.PI);
    }

    /// <summary>An angle in degrees brought into -180..180.</summary>
    public static float Wrap(float degrees)
    {
        float a = (degrees + 180f) % 360f;
        if (a < 0f)
            a += 360f;
        return a - 180f;
    }
}
