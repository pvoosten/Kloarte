namespace Kloarte.Model;

/// <summary>A colour (0..1 per channel) for the plan drawing, independent of Godot (also used for the PDF).</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    /// <summary>Black.</summary>
    public static readonly Rgb Black = new(0, 0, 0);

    /// <summary>White.</summary>
    public static readonly Rgb White = new(1, 1, 1);

    /// <summary>Grey of the given brightness.</summary>
    public static Rgb Grey(float v) => new(v, v, v);

    /// <summary>Relative luminance (for choosing a readable label colour).</summary>
    public float Luminance => 0.2126f * R + 0.7152f * G + 0.0722f * B;
}
