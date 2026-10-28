using System;

namespace Kloarte.Model;

/// <summary>
/// A set piece on stage (Kloarte's own data, <c>blocking/scenery.yaml</c>; not exported to MagicQ): a shape with its
/// sizes, where it stands, its colour and material. Position = the centre of the piece's bottom in stage coordinates
/// (a curtain: the centre of its bottom edge); the rotation turns it about the vertical axis. Sizes along the piece's
/// own axes: <see cref="Width"/> = x (a table's length), <see cref="Depth"/> = z, <see cref="Height"/> = y. Per cue a
/// piece can go out / come in and a split curtain can open or close (<see cref="Blocking.SceneryCues"/>). No Godot
/// dependencies.
/// </summary>
public sealed class SceneryPiece
{
    /// <summary>The default colour of a new piece ("rrggbb").</summary>
    public const string DefaultColor = "8c7355";

    /// <summary>The distance between two pleats of a curtain (m).</summary>
    public const float PleatSpacing = 0.3f;

    /// <summary>The piece's id (a slug of its first name, <see cref="ActorIds.For"/>; renaming keeps it).</summary>
    public string Id { get; set; }

    /// <summary>Name as shown in Kloarte.</summary>
    public string Name { get; set; }

    /// <summary>The shape.</summary>
    public SceneryShape Shape { get; set; }

    /// <summary>Centre of the bottom, stage coordinates (m): x, y, z.</summary>
    public float[] Position { get; set; } = { 0f, 0f, 0f };

    /// <summary>Rotation about the vertical axis (degrees, counter-clockwise seen from above, like an actor's facing).</summary>
    public float Rotation { get; set; }

    /// <summary>Size along the piece's x axis (m): a table's length, a curtain's width (closed).</summary>
    public float Width { get; set; } = 1f;

    /// <summary>Size along the piece's z axis (m); not used by a curtain (its depth is that of its pleats, <see cref="PleatDepth"/>).</summary>
    public float Depth { get; set; } = 1f;

    /// <summary>Height (m): a table's top side, a curtain's cloth.</summary>
    public float Height { get; set; } = 1f;

    /// <summary>A table's top thickness (m).</summary>
    public float TopThickness { get; set; } = 0.04f;

    /// <summary>A table's legs: the side of their square section (m).</summary>
    public float LegThickness { get; set; } = 0.06f;

    /// <summary>A curtain's fullness in %: the extra cloth in the pleats (0 = a flat cloth, 100 = twice the width of cloth).</summary>
    public float Fullness { get; set; }

    /// <summary>A curtain in two halves that open to the sides (else one cloth that doesn't open).</summary>
    public bool Split { get; set; }

    /// <summary>How far a split curtain is open before the first cue: 0 = closed, 100 = fully open.</summary>
    public float Open { get; set; }

    /// <summary>Colour "rrggbb" (null = <see cref="DefaultColor"/>).</summary>
    public string Color { get; set; }

    /// <summary>The material preset.</summary>
    public SceneryMaterial Material { get; set; }

    /// <summary>The colour that counts: <see cref="Color"/> when it is one, else <see cref="DefaultColor"/>.</summary>
    public string ColorOrDefault => ShirtColors.Parse(Color) != null ? Color.TrimStart('#').ToLowerInvariant() : DefaultColor;

    /// <summary>True for a curtain that can open (split in two halves).</summary>
    public bool Opens => Shape == SceneryShape.Curtain && Split;

    /// <summary>
    /// The depth a curtain's pleats take (m, front to back): twice the amplitude of a sine fold of period
    /// <see cref="PleatSpacing"/> whose cloth is (1 + fullness) times as long as it is wide. 0 for a flat cloth.
    /// </summary>
    public float PleatDepth => Fullness <= 0f ? 0f : 2f * PleatAmplitude(Fullness / 100f);

    /// <summary>
    /// The amplitude (m) of a sine fold of period <see cref="PleatSpacing"/> whose arc is (1 + `fullness`) times its
    /// width (found by bisection on the arc length; up to an arc about 30 times the width, a curtain gathered open).
    /// </summary>
    public static float PleatAmplitude(float fullness)
    {
        double lo = 0, hi = PleatSpacing * 8;
        for (int i = 0; i < 40; i++)
        {
            double a = (lo + hi) / 2;
            if (ArcRatio(a) < 1 + fullness)
                lo = a;
            else
                hi = a;
        }
        return (float)lo;
    }

    /// <summary>Arc length / width of one period of a sine with amplitude `a` and period <see cref="PleatSpacing"/>.</summary>
    private static double ArcRatio(double a)
    {
        const int n = 64;
        double k = 2 * Math.PI / PleatSpacing, sum = 0;
        for (int i = 0; i < n; i++)
        {
            double slope = a * k * Math.Cos(k * (i + 0.5) * PleatSpacing / n);
            sum += Math.Sqrt(1 + slope * slope);
        }
        return sum / n;
    }

    /// <summary>A new piece of a shape with the sizes it has by default (a 1 m block or pyramid, a 1.6 m table, a 6 x 4 m black curtain).</summary>
    public static SceneryPiece New(string id, string name, SceneryShape shape)
    {
        var piece = new SceneryPiece { Id = id, Name = name, Shape = shape };
        switch (shape)
        {
            case SceneryShape.Table:
                (piece.Width, piece.Depth, piece.Height) = (1.6f, 0.8f, 0.75f);
                break;
            case SceneryShape.Curtain:
                (piece.Width, piece.Depth, piece.Height, piece.Fullness) = (6f, 0f, 4f, 50f);
                (piece.Color, piece.Material) = ("1a1a1a", SceneryMaterial.Molton);
                break;
        }
        return piece;
    }

    /// <summary>A copy (for duplicating; the id and name are the caller's to change).</summary>
    public SceneryPiece Copy()
    {
        var copy = (SceneryPiece)MemberwiseClone();
        copy.Position = (float[])Position.Clone();
        return copy;
    }
}
