using System.Collections.Generic;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>A scenery footprint in the plan (box, border, proscenium...): a polygon with a name.</summary>
public sealed class PlanShape
{
    /// <summary>Name of the MagicVis object.</summary>
    public string Name { get; init; } = "";

    /// <summary>Corner points in plan coordinates.</summary>
    public IReadOnlyList<Vector2> Points { get; init; } = new List<Vector2>();

    /// <summary>Black masking (borders, legs) rather than a set piece.</summary>
    public bool Masking { get; init; }
}
