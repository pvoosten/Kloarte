namespace Kloarte.Model;

/// <summary>
/// A set piece of the project (<see cref="Blocking.Scenery"/>) in the plan: the piece and how it is at the moment the
/// plan shows (in or out, how far a curtain is open), and whether it is selected.
/// </summary>
public sealed class PlanPiece
{
    /// <summary>The piece (its shape, sizes, place and colour).</summary>
    public SceneryPiece Piece { get; init; }

    /// <summary>On stage at this moment (out: drawn as a dashed outline).</summary>
    public bool Visible { get; init; } = true;

    /// <summary>How far a split curtain is open at this moment (0..100 %).</summary>
    public float Open { get; init; }

    /// <summary>Selected (drawn with the selection colour).</summary>
    public bool Selected { get; init; }
}
