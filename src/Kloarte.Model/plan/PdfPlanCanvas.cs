using System.Collections.Generic;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Draws the plan onto a PDF page: plan metres mapped to points (scale, offset; plan +y = down on the page), or with
/// scale 1 and no offset "device" coordinates in points from the top left (for the legend and the title block).
/// </summary>
public sealed class PdfPlanCanvas : IPlanCanvas
{
    private readonly PdfPage _page;
    private readonly float _scale;
    private readonly Vector2 _origin;   // page point (from the top left) of plan (0, 0)

    /// <summary>Creates a canvas: `scale` points per metre, plan (0, 0) at `origin` (points from the top left).</summary>
    public PdfPlanCanvas(PdfPage page, float scale, Vector2 origin)
    {
        _page = page;
        _scale = scale;
        _origin = origin;
    }

    /// <inheritdoc />
    public float UnitsPerMetre => _scale;

    private (float X, float Y) Map(Vector2 p) => (_origin.X + p.X * _scale, _page.Height - (_origin.Y + p.Y * _scale));

    /// <inheritdoc />
    public void Polygon(IReadOnlyList<Vector2> points, Rgb? fill, Rgb? stroke, float strokeWidth = 1f)
    {
        if (points.Count < 2)
            return;
        Style(fill, stroke, strokeWidth, false);
        var (x0, y0) = Map(points[0]);
        _page.MoveTo(x0, y0);
        for (int i = 1; i < points.Count; i++)
        {
            var (x, y) = Map(points[i]);
            _page.LineTo(x, y);
        }
        _page.Close();
        _page.Paint(fill != null, stroke != null);
    }

    /// <inheritdoc />
    public void Polyline(IReadOnlyList<Vector2> points, Rgb stroke, float width = 1f, bool dashed = false)
    {
        if (points.Count < 2)
            return;
        Style(null, stroke, width, dashed);
        var (x0, y0) = Map(points[0]);
        _page.MoveTo(x0, y0);
        for (int i = 1; i < points.Count; i++)
        {
            var (x, y) = Map(points[i]);
            _page.LineTo(x, y);
        }
        _page.Paint(false, true);
        if (dashed)
            _page.Dash(0, 0);
    }

    /// <inheritdoc />
    public void Circle(Vector2 centre, float radius, Rgb? fill, Rgb? stroke, float strokeWidth = 1f)
    {
        Style(fill, stroke, strokeWidth, false);
        var (x, y) = Map(centre);
        _page.CirclePath(x, y, radius * _scale);
        _page.Paint(fill != null, stroke != null);
    }

    /// <inheritdoc />
    public void Text(Vector2 position, string text, float size, Rgb color, TextAlign align = TextAlign.Left)
    {
        var (x, y) = Map(position);
        float w = TextWidth(text, size);
        x -= align == TextAlign.Center ? w / 2f : align == TextAlign.Right ? w : 0f;
        _page.Fill(color);
        _page.Text(x, y, size, text);
    }

    /// <inheritdoc />
    public float TextWidth(string text, float size) => PdfDocument.TextWidth(text, size);

    private void Style(Rgb? fill, Rgb? stroke, float width, bool dashed)
    {
        if (fill is { } f)
            _page.Fill(f);
        if (stroke is { } s)
        {
            _page.Stroke(s);
            _page.LineWidth(width * 0.75f);   // device pixel widths -> points (a bit thinner on paper)
        }
        if (dashed)
            _page.Dash(4, 3);
    }
}
