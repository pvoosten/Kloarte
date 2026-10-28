namespace Kloarte.Model;

/// <summary>A set piece in <c>blocking/scenery.yaml</c> (<see cref="SceneryPiece"/>); a missing size = the shape's default.</summary>
public sealed class SceneryPieceEntry
{
    /// <summary>The piece's id.</summary>
    [FileField("The piece's id (stable across renames; blocking/steps.yaml refers to it)")]
    public string Id { get; set; }

    /// <summary>Name.</summary>
    [FileField("Name, as shown in Kloarte")]
    public string Name { get; set; }

    /// <summary>Shape.</summary>
    [FileField("Shape", Values = "block,pyramid,table,curtain")]
    public string Shape { get; set; }

    /// <summary>Centre of the bottom x, y, z (stage coordinates, m).</summary>
    [FileField("Centre of the piece's bottom [x, y, z] in stage coordinates (x to the audience's right, y up, z towards the audience); a curtain: the centre of its bottom edge", Unit = "m")]
    public double[] Position { get; set; }

    /// <summary>Rotation about the vertical (degrees).</summary>
    [FileField("Rotation around the vertical, counter-clockwise seen from above; missing = 0 (width along the stage's x)", Unit = "°", Min = -360, Max = 360)]
    public double? Rotation { get; set; }

    /// <summary>Width (m).</summary>
    [FileField("Width along the piece's own x: a table's length, a curtain's width when closed; missing = the shape's default", Unit = "m", Min = 0.01, Max = 50)]
    public double? Width { get; set; }

    /// <summary>Depth (m).</summary>
    [FileField("Depth along the piece's own z (not for a curtain); missing = the shape's default", Unit = "m", Min = 0.01, Max = 50)]
    public double? Depth { get; set; }

    /// <summary>Height (m).</summary>
    [FileField("Height: a pyramid's apex, a table's top side, a curtain's cloth; missing = the shape's default", Unit = "m", Min = 0.01, Max = 30)]
    public double? Height { get; set; }

    /// <summary>Table top thickness (m).</summary>
    [FileField("A table's top thickness; missing = 0.04", Unit = "m", Min = 0.005, Max = 1)]
    public double? TopThickness { get; set; }

    /// <summary>Table leg thickness (m).</summary>
    [FileField("A table's legs: side of their square section; missing = 0.06", Unit = "m", Min = 0.005, Max = 1)]
    public double? LegThickness { get; set; }

    /// <summary>Curtain fullness (%).</summary>
    [FileField("A curtain's fullness: the extra cloth in its pleats (0 = flat, 100 = twice the width of cloth); missing = the shape's default", Unit = "%", Min = 0, Max = 300)]
    public double? Fullness { get; set; }

    /// <summary>Split curtain.</summary>
    [FileField("A curtain in two halves that open to the sides (missing = one cloth that doesn't open)")]
    public bool? Split { get; set; }

    /// <summary>Openness before the first cue (%).</summary>
    [FileField("How far a split curtain is open before the first cue (0 = closed, 100 = open); missing = closed", Unit = "%", Min = 0, Max = 100)]
    public double? Open { get; set; }

    /// <summary>Colour "#rrggbb".</summary>
    [FileField("Colour \"#rrggbb\"; missing = the default", Quoted = true)]
    public string Color { get; set; }

    /// <summary>Material preset.</summary>
    [FileField("Material: how the colour reflects light (gauze = translucent, casts no shadow); missing = painted", Values = SceneryMaterials.Names)]
    public string Material { get; set; }
}
