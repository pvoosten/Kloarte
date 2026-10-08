namespace Kloarte.Model;

/// <summary>What a rig item is (the values are MagicVis object types).</summary>
public enum RigObjectKind
{
    /// <summary>A lamp (not an object).</summary>
    Fixture = 0,

    /// <summary>Proscenium beam: size = width, height, depth; centred.</summary>
    Proscenium = 1,

    /// <summary>Truss / pipe / stand: size A = length along local +X from the position.</summary>
    Pipe = 2,

    /// <summary>Box / flat: size = depth, width, height; bottom-centre at the position.</summary>
    Box = 4,

    /// <summary>Person (becomes an actor).</summary>
    Person = 6,

    /// <summary>Podium (the stage floor; provided by the venue).</summary>
    Podium = 7,

    /// <summary>Custom 3D model (MagicVis .mvm).</summary>
    Model = 8,

    /// <summary>Border (fries): laid out like a box.</summary>
    Border = 9,
}
