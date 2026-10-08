namespace Kloarte.Model;

/// <summary>
/// An item of the rig in the show document: a lamp or an object (trusses / pipes / stands are the supports). Positions
/// and rotations in the show's rig coordinates (MagicVis: metres, Euler degrees Y-X-Z, lamps point down at rotation 0).
/// </summary>
public interface IRigItem
{
    /// <summary>Id, unique in the show (lamps hang on a support by its id).</summary>
    int Id { get; }

    /// <summary>True for a lamp.</summary>
    bool IsFixture { get; }

    /// <summary>What it is (<see cref="RigObjectKind.Fixture"/> for a lamp; an unknown object type stays a number).</summary>
    RigObjectKind ObjectKind { get; }

    /// <summary>Id of the support it hangs on (0 = none).</summary>
    int ParentId { get; set; }

    /// <summary>Position X.</summary>
    float X { get; set; }

    /// <summary>Position Y (height).</summary>
    float Y { get; set; }

    /// <summary>Position Z.</summary>
    float Z { get; set; }

    /// <summary>Rotation about X (degrees).</summary>
    float RotX { get; set; }

    /// <summary>Rotation about Y (degrees).</summary>
    float RotY { get; set; }

    /// <summary>Rotation about Z (degrees).</summary>
    float RotZ { get; set; }

    /// <summary>First size of an object (a pipe's length; see <see cref="RigObjectKind"/> for the others).</summary>
    float SizeA { get; set; }

    /// <summary>Second size of an object.</summary>
    float SizeB { get; }

    /// <summary>Third size of an object.</summary>
    float SizeC { get; }

    /// <summary>Name of an object.</summary>
    string Name { get; set; }

    /// <summary>Code of an object (MagicVis: e.g. the kind of person).</summary>
    string ObjectCode { get; }

    /// <summary>File name of a custom model object.</summary>
    string ModelName { get; }

    /// <summary>DMX address of a lamp (1-based absolute; 0 = not patched).</summary>
    int Address { get; }

    /// <summary>Head (fixture) number of a lamp.</summary>
    int HeadNumber { get; }
}
