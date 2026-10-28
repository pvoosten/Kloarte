namespace Kloarte.Model;

/// <summary>The shape of a set piece (<see cref="SceneryPiece"/>).</summary>
public enum SceneryShape
{
    /// <summary>A block: width x depth x height.</summary>
    Block,

    /// <summary>A pyramid: a rectangular base width x depth, the apex at height above its centre.</summary>
    Pyramid,

    /// <summary>A table: a top width (length) x depth and four square legs at the corners, height = the top's upper side.</summary>
    Table,

    /// <summary>A curtain: a cloth width x height, pleated by its fullness, in one piece or split in two halves that open to the sides.</summary>
    Curtain,
}
