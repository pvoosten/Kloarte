using System;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// A stroke of a gobo drawing: a straight line (A to B) or an arc (centre, radius, start and end angle in degrees,
/// counter-clockwise, 0 = +x). Gobo coordinates: the gobo disc has radius 1, +y = up (top of the beam). Width in the
/// same units. Cut = the stroke lets light through (white); otherwise it is steel (black). No Godot dependencies.
/// </summary>
public sealed class GoboShape
{
    /// <summary>"line" or "arc".</summary>
    public string Kind { get; set; } = "line";

    /// <summary>Line start.</summary>
    public float[] A { get; set; } = { -0.5f, 0f };

    /// <summary>Line end.</summary>
    public float[] B { get; set; } = { 0.5f, 0f };

    /// <summary>Arc centre.</summary>
    public float[] Centre { get; set; } = { 0f, 0f };

    /// <summary>Arc radius.</summary>
    public float Radius { get; set; } = 0.5f;

    /// <summary>Arc start angle (degrees).</summary>
    public float Start { get; set; }

    /// <summary>Arc end angle (degrees, counter-clockwise from Start; may exceed 360 for a full circle).</summary>
    public float End { get; set; } = 180f;

    /// <summary>Stroke width.</summary>
    public float Width { get; set; } = 0.06f;

    /// <summary>True: light passes (white); false: steel (black).</summary>
    public bool Cut { get; set; } = true;

    /// <summary>A line.</summary>
    public static GoboShape Line(Vector2 a, Vector2 b, float width, bool cut) =>
        new() { Kind = "line", A = new[] { a.X, a.Y }, B = new[] { b.X, b.Y }, Width = width, Cut = cut };

    /// <summary>An arc.</summary>
    public static GoboShape Arc(Vector2 centre, float radius, float start, float end, float width, bool cut) =>
        new() { Kind = "arc", Centre = new[] { centre.X, centre.Y }, Radius = radius, Start = start, End = end, Width = width, Cut = cut };

    /// <summary>Line start as a vector.</summary>
    public Vector2 PointA => new(A[0], A[1]);

    /// <summary>Line end as a vector.</summary>
    public Vector2 PointB => new(B[0], B[1]);

    /// <summary>Arc centre as a vector.</summary>
    public Vector2 CentrePoint => new(Centre[0], Centre[1]);

    /// <summary>Point on the arc at an angle (degrees).</summary>
    public Vector2 ArcPoint(float degrees)
    {
        float r = degrees * MathF.PI / 180f;
        return CentrePoint + new Vector2(MathF.Cos(r), MathF.Sin(r)) * Radius;
    }

    /// <summary>Distance from a point to the stroke's centre line.</summary>
    public float Distance(Vector2 p)
    {
        if (Kind == "arc")
        {
            var d = p - CentrePoint;
            float angle = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
            float sweep = End - Start;
            if (sweep >= 360f || Within(angle, Start, sweep))
                return MathF.Abs(d.Length() - Radius);
            return MathF.Min(Vector2.Distance(p, ArcPoint(Start)), Vector2.Distance(p, ArcPoint(End)));
        }
        var ab = PointB - PointA;
        float len2 = ab.LengthSquared();
        float t = len2 < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - PointA, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, PointA + ab * t);
    }

    private static bool Within(float angle, float start, float sweep)
    {
        float rel = ((angle - start) % 360f + 360f) % 360f;
        return sweep >= 0 ? rel <= sweep : 360f - rel <= -sweep;
    }

    /// <summary>A copy.</summary>
    public GoboShape Copy() => new()
    {
        Kind = Kind, A = (float[])A.Clone(), B = (float[])B.Clone(), Centre = (float[])Centre.Clone(),
        Radius = Radius, Start = Start, End = End, Width = Width, Cut = Cut,
    };
}
