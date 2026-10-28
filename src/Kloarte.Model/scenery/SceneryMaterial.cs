namespace Kloarte.Model;

/// <summary>
/// The material of a set piece (<see cref="SceneryPiece"/>): how its colour reflects light. The colour stays the piece's
/// own; the preset sets roughness, metallic and how much of the colour is reflected (<see cref="SceneryMaterials"/>).
/// </summary>
public enum SceneryMaterial
{
    /// <summary>Matt paint (the default).</summary>
    Painted,

    /// <summary>Varnished wood: a little sheen.</summary>
    Wood,

    /// <summary>Molton: absorbs nearly all light, fully matt (the colour is darkened as real molton is).</summary>
    Molton,

    /// <summary>Metal: metallic with a sheen.</summary>
    Metal,

    /// <summary>Gauze / tulle: translucent, lit from behind it glows; it casts no shadow and doesn't stop a beam.</summary>
    Gauze,
}
