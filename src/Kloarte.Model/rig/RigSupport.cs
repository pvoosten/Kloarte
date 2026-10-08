using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Something lamps hang on or stand on: a horizontal truss / pipe (MagicVis object type 2) or a floor stand / boom
/// (a vertical type-2 pipe from the floor up). MagicVis coordinates (metres; +X audience right, +Y up, +Z towards the
/// audience). No Godot dependencies.
/// </summary>
public sealed class RigSupport
{
    /// <summary>The pipe object in the show document.</summary>
    public IRigItem Item { get; }

    /// <summary>Creates a support view on a MagicVis pipe object.</summary>
    public RigSupport(IRigItem item) => Item = item;

    /// <summary>MagicVis id.</summary>
    public int Id => Item.Id;

    /// <summary>Name (e.g. "LT 11", "Stand 111U #1").</summary>
    public string Name => string.IsNullOrEmpty(Item.Name) ? $"Object {Item.Id}" : Item.Name;

    /// <summary>Start point (MagicVis).</summary>
    public Vector3 Start => new(Item.X, Item.Y, Item.Z);

    /// <summary>Unit direction along the pipe (local +X rotated by the MagicVis Euler angles, order Y-X-Z).</summary>
    public Vector3 Direction => Rig.Rotate(Vector3.UnitX, Item.RotX, Item.RotY, Item.RotZ);

    /// <summary>Length in metres (size A).</summary>
    public float Length => Item.SizeA;

    /// <summary>End point (MagicVis).</summary>
    public Vector3 End => Start + Direction * Length;

    /// <summary>True for a vertical pipe (floor stand, boom).</summary>
    public bool IsStand => System.Math.Abs(Direction.Y) > 0.9f;

    /// <summary>Height of a horizontal truss, or the top of a stand.</summary>
    public float Height => IsStand ? System.Math.Max(Start.Y, End.Y) : (Start.Y + End.Y) / 2f;

    /// <summary>Nearest point on the support to a point, in plan (x/z only; y = the support's height there).</summary>
    public Vector3 NearestInPlan(Vector3 p)
    {
        if (IsStand)
            return new Vector3(Start.X, Height, Start.Z);
        var d = new Vector2(Direction.X, Direction.Z);
        float len2 = d.LengthSquared();
        float t = len2 < 1e-8f ? 0f : Vector2.Dot(new Vector2(p.X - Start.X, p.Z - Start.Z), d) / len2;
        t = System.Math.Clamp(t, 0f, Length);
        return Start + Direction * t;
    }

    /// <summary>Plan distance (x/z) from a point to the support.</summary>
    public float PlanDistance(Vector3 p)
    {
        var n = NearestInPlan(p);
        return new Vector2(n.X - p.X, n.Z - p.Z).Length();
    }
}
