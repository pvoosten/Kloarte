namespace Kloarte.Model;

/// <summary>One of the three position axes of a lamp (rig and stage axes are the same: +X audience right, +Y up, +Z towards the audience).</summary>
public enum MotionAxis
{
    /// <summary>Across the stage.</summary>
    X,

    /// <summary>Height.</summary>
    Y,

    /// <summary>Up and downstage.</summary>
    Z,
}
