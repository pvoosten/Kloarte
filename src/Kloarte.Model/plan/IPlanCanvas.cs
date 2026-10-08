using System.Collections.Generic;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Where the plan is drawn: the plan view on screen or a PDF page. Coordinates are plan metres: x = stage x (audience
/// right), y = stage z (towards the audience, i.e. down on the page). Line widths and text sizes are in device units
/// (pixels on screen, points in the PDF), so text stays readable at any scale.
/// </summary>
public interface IPlanCanvas
{
    /// <summary>Device units per metre (for sizing things that should keep a fixed device size).</summary>
    float UnitsPerMetre { get; }

    /// <summary>A filled and/or outlined polygon.</summary>
    void Polygon(IReadOnlyList<Vector2> points, Rgb? fill, Rgb? stroke, float strokeWidth = 1f);

    /// <summary>An open polyline.</summary>
    void Polyline(IReadOnlyList<Vector2> points, Rgb stroke, float width = 1f, bool dashed = false);

    /// <summary>A filled and/or outlined circle.</summary>
    void Circle(Vector2 centre, float radius, Rgb? fill, Rgb? stroke, float strokeWidth = 1f);

    /// <summary>Text with its baseline at `position`; size in device units.</summary>
    void Text(Vector2 position, string text, float size, Rgb color, TextAlign align = TextAlign.Left);

    /// <summary>Width of a text in device units.</summary>
    float TextWidth(string text, float size);
}
