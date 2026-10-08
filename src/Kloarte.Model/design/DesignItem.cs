using System;
using System.Globalization;

namespace Kloarte.Model;

/// <summary>
/// An item of the native show's rig (<c>rig.yaml</c>): a lamp or an object (truss, pipe, box, person, model...), as
/// MagicVis has them (positions in metres, rotations in degrees Y-X-Z, lamps point down at rotation 0). Imported items
/// keep the MagicVis id.
/// </summary>
[FileFlow]
public sealed class DesignItem : IRigItem
{
    /// <summary>The kinds as written in the file, by <see cref="RigObjectKind"/>.</summary>
    private static readonly (RigObjectKind Kind, string Name)[] Kinds =
    {
        (RigObjectKind.Fixture, "fixture"), (RigObjectKind.Proscenium, "proscenium"), (RigObjectKind.Pipe, "pipe"),
        (RigObjectKind.Box, "box"), (RigObjectKind.Person, "person"), (RigObjectKind.Podium, "podium"),
        (RigObjectKind.Model, "model"), (RigObjectKind.Border, "border"),
    };

    /// <summary>Id, unique in the show.</summary>
    [FileField("The item's id, unique in the show (lamp files and blocking refer to it; the MagicVis id when imported)", Min = 1)]
    public int Id { get; set; }

    /// <summary>What it is: fixture, proscenium, pipe, box, person, podium, model, border (or a MagicVis object type number).</summary>
    [FileField("What it is: fixture, proscenium, pipe, box, person, podium, model, border (or a MagicVis object type number)")]
    public string Kind { get; set; } = "fixture";

    /// <summary>Id of the support it hangs on, or null.</summary>
    [FileField("Id of the truss / pipe / stand it hangs on; missing = none", Min = 1)]
    public int? Parent { get; set; }

    /// <summary>Position x, y, z (m).</summary>
    [FileField("Position x, y, z", Unit = "m")]
    public double[] At { get; set; } = { 0, 0, 0 };

    /// <summary>Rotation about x, y, z (degrees; Euler order Y-X-Z).</summary>
    [FileField("Rotation about x, y, z (Euler order Y-X-Z; lamps point down at 0)", Unit = "°")]
    public double[] Rot { get; set; }

    /// <summary>Sizes of an object (a pipe's length first).</summary>
    [FileField("Sizes of an object (a pipe: length; a box: depth, width, height)", Unit = "m")]
    public double[] Size { get; set; }

    /// <summary>Name of an object.</summary>
    [FileField("Name of an object (e.g. the truss name LX1)")]
    public string Name { get; set; }

    /// <summary>MagicVis object code (e.g. the kind of person).</summary>
    [FileField("MagicVis object code (e.g. 20001 truss, 60001 person)")]
    public string Code { get; set; }

    /// <summary>File name of a custom model.</summary>
    [FileField("File name of a custom model (MagicVis .mvm)")]
    public string Model { get; set; }

    /// <summary>DMX address of a lamp (1-based absolute; 513 = universe 2 channel 1).</summary>
    [FileField("DMX address of a lamp (absolute, 1-based: 513 = universe 2, channel 1); missing = not patched", Min = 1)]
    public int? Address { get; set; }

    /// <summary>Head number of a lamp.</summary>
    [FileField("Head number of a lamp (the head of the patch it belongs to)", Min = 1)]
    public int? Head { get; set; }

    /// <summary>Personality (fixture type) of a lamp, by name.</summary>
    [FileField("Fixture type of a lamp (a personality of the rig, by name)")]
    public string Type { get; set; }

    /// <summary>The kind as an enum value.</summary>
    private RigObjectKind ObjectKind
    {
        get
        {
            if (IsLamp)
                return RigObjectKind.Fixture;
            foreach (var (kind, name) in Kinds)
                if (string.Equals(name, Kind, StringComparison.OrdinalIgnoreCase))
                    return kind;
            return int.TryParse(Kind, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? (RigObjectKind)n : RigObjectKind.Box;
        }
    }

    /// <summary>True for a lamp.</summary>
    public bool IsLamp => string.Equals(Kind, "fixture", StringComparison.OrdinalIgnoreCase);

    /// <summary>The file name of a kind (an object of MagicVis type 0 is "0", not a lamp).</summary>
    public static string KindName(RigObjectKind kind, bool lamp = true)
    {
        if (!lamp && kind == RigObjectKind.Fixture)
            return "0";
        foreach (var (k, name) in Kinds)
            if (k == kind)
                return name;
        return ((int)kind).ToString(CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    bool IRigItem.IsFixture => IsLamp;

    /// <inheritdoc />
    RigObjectKind IRigItem.ObjectKind => ObjectKind;

    /// <inheritdoc />
    int IRigItem.ParentId { get => Parent ?? 0; set => Parent = value > 0 ? value : null; }

    /// <inheritdoc />
    float IRigItem.X { get => Get(At, 0); set => At = Set(At, 0, value); }

    /// <inheritdoc />
    float IRigItem.Y { get => Get(At, 1); set => At = Set(At, 1, value); }

    /// <inheritdoc />
    float IRigItem.Z { get => Get(At, 2); set => At = Set(At, 2, value); }

    /// <inheritdoc />
    float IRigItem.RotX { get => Get(Rot, 0); set => Rot = Set(Rot, 0, value); }

    /// <inheritdoc />
    float IRigItem.RotY { get => Get(Rot, 1); set => Rot = Set(Rot, 1, value); }

    /// <inheritdoc />
    float IRigItem.RotZ { get => Get(Rot, 2); set => Rot = Set(Rot, 2, value); }

    /// <inheritdoc />
    float IRigItem.SizeA { get => Get(Size, 0); set => Size = Set(Size, 0, value); }

    /// <inheritdoc />
    float IRigItem.SizeB => Get(Size, 1);

    /// <inheritdoc />
    float IRigItem.SizeC => Get(Size, 2);

    /// <inheritdoc />
    string IRigItem.Name { get => Name ?? ""; set => Name = string.IsNullOrEmpty(value) ? null : value; }

    /// <inheritdoc />
    string IRigItem.ObjectCode => Code ?? "";

    /// <inheritdoc />
    string IRigItem.ModelName => Model ?? "";

    /// <inheritdoc />
    int IRigItem.Address => Address ?? 0;

    /// <inheritdoc />
    int IRigItem.HeadNumber => Head ?? 0;

    /// <summary>Value i of a triple (0 when missing).</summary>
    private static float Get(double[] values, int i) => values != null && i < values.Length ? (float)values[i] : 0f;

    /// <summary>A triple with value i set (rounded to 3 decimals: mm, 0.001°).</summary>
    private static double[] Set(double[] values, int i, float value)
    {
        var result = new double[3];
        if (values != null)
            Array.Copy(values, result, Math.Min(3, values.Length));
        result[i] = Round(value);
        return result;
    }

    /// <summary>A number of a position, rotation or size as the file keeps it (3 decimals; never -0).</summary>
    public static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero) + 0.0;

    /// <summary>A copy (same values; arrays copied).</summary>
    public DesignItem Copy() => new()
    {
        Id = Id, Kind = Kind, Parent = Parent, At = (double[])At?.Clone(), Rot = (double[])Rot?.Clone(),
        Size = (double[])Size?.Clone(), Name = Name, Code = Code, Model = Model, Address = Address, Head = Head,
        Type = Type,
    };
}
