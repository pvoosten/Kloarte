using System.Numerics;

namespace Kloarte.Model;

/// <summary>A truss / pipe (line) or floor stand (point) in the plan.</summary>
public sealed class PlanSupport
{
    /// <summary>MagicVis id.</summary>
    public int Id { get; init; }

    /// <summary>Name, e.g. "LT 11".</summary>
    public string Name { get; init; } = "";

    /// <summary>Plan start / end (equal for a stand).</summary>
    public Vector2 Start { get; init; }

    /// <summary>Plan end point.</summary>
    public Vector2 End { get; init; }

    /// <summary>Height above the stage floor (truss) or top of the stand.</summary>
    public float Height { get; init; }

    /// <summary>True for a floor stand / boom.</summary>
    public bool IsStand { get; init; }

    /// <summary>Selected (e.g. in the truss-height view).</summary>
    public bool Selected { get; init; }
}
