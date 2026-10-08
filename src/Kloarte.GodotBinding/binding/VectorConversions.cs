namespace Kloarte.GodotBinding;

/// <summary>Conversions between the model's System.Numerics vectors and Godot's vectors (same axes and units).</summary>
public static class VectorConversions
{
    /// <summary>A model vector as a Godot vector.</summary>
    public static Godot.Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>A Godot vector as a model vector.</summary>
    public static System.Numerics.Vector3 ToSystem(this Godot.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>A model vector as a Godot vector.</summary>
    public static Godot.Vector2 ToGodot(this System.Numerics.Vector2 v) => new(v.X, v.Y);

    /// <summary>A Godot vector as a model vector.</summary>
    public static System.Numerics.Vector2 ToSystem(this Godot.Vector2 v) => new(v.X, v.Y);
}
