using System;
using System.Collections.Generic;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Where a set piece (<see cref="SceneryPiece"/>) is, from its model data alone: its footprint in the plan, a point in
/// it, and a ray (a click in a 3D view) hitting its box. The piece's own axes: x along its width, z along its depth, y
/// up; turned by <see cref="SceneryPiece.Rotation"/> about the vertical (counter-clockwise seen from above). Plan
/// coordinates are (stage x, stage z). A split curtain that is open is two halves gathered at the sides
/// (<see cref="Spans"/>). No Godot dependencies: the plan, the PDF and the 3D picking use the same geometry.
/// </summary>
public static class SceneryGeometry
{
    /// <summary>The thinnest a piece is taken to be in the plan and for picking (m): a flat cloth is still clickable.</summary>
    public const float MinThickness = 0.08f;

    /// <summary>What is left of a curtain half's width when it is fully open (it gathers at its side).</summary>
    public const float GatheredWidth = 0.15f;

    /// <summary>Cosine and sine of a piece's rotation.</summary>
    private static (float Cos, float Sin) Turn(SceneryPiece piece)
    {
        double a = piece.Rotation * Math.PI / 180.0;
        return ((float)Math.Cos(a), (float)Math.Sin(a));
    }

    /// <summary>The plan point of a point on the piece's own axes (x along its width, z along its depth).</summary>
    public static Vector2 ToPlan(SceneryPiece piece, float x, float z)
    {
        var (c, s) = Turn(piece);
        return new Vector2(piece.Position[0] + x * c + z * s, piece.Position[2] - x * s + z * c);
    }

    /// <summary>A plan point on the piece's own axes (x along its width, y = along its depth).</summary>
    public static Vector2 ToLocal(SceneryPiece piece, Vector2 plan)
    {
        var (c, s) = Turn(piece);
        float dx = plan.X - piece.Position[0], dz = plan.Y - piece.Position[2];
        return new Vector2(dx * c - dz * s, dx * s + dz * c);
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

    /// <summary>The footprint's corners in the plan (the whole width, also of an open curtain).</summary>
    public static Vector2[] Footprint(SceneryPiece piece)
    {
        float hw = Math.Max(piece.Width, MinThickness) / 2f, hd = HalfDepth(piece);
        return Box(piece, -hw, hw, hd);
    }

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

    /// <summary>True when a plan point is on the piece (in one of its <see cref="Spans"/>, within its depth).</summary>
    public static bool Contains(SceneryPiece piece, Vector2 plan, float open)
    {
        var local = ToLocal(piece, plan);
        if (Math.Abs(local.Y) > HalfDepth(piece))
            return false;
        foreach (var (from, to) in Spans(piece, open))
            if (local.X >= from && local.X <= to)
                return true;
        return false;
    }

    /// <summary>
    /// Where a ray (stage coordinates) first hits the piece's box (each of its <see cref="Spans"/>, from the floor to its
    /// height): the distance along the ray in units of `dir`, or null when it misses.
    /// </summary>
    public static float? RayHit(SceneryPiece piece, Vector3 origin, Vector3 dir, float open)
    {
        var (c, s) = Turn(piece);
        float ox = origin.X - piece.Position[0], oz = origin.Z - piece.Position[2];
        var o = new Vector3(ox * c - oz * s, origin.Y - piece.Position[1], ox * s + oz * c);
        var d = new Vector3(dir.X * c - dir.Z * s, dir.Y, dir.X * s + dir.Z * c);
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
