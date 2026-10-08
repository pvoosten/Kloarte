using System.Collections.Generic;
using System.Linq;
using Godot;
using SysVec2 = System.Numerics.Vector2;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>Draws the plan on a Godot CanvasItem (inside its _Draw): plan metres -&gt; pixels (offset + p * scale).</summary>
public sealed class GodotPlanCanvas : IPlanCanvas
{
    private readonly CanvasItem _item;
    private readonly Vector2 _offset;
    private readonly float _scale;
    private readonly Font _font;

    /// <summary>Creates a canvas: `scale` pixels per metre, plan (0, 0) at pixel `offset`.</summary>
    public GodotPlanCanvas(CanvasItem item, Vector2 offset, float scale, Font font = null)
    {
        _item = item;
        _offset = offset;
        _scale = scale;
        _font = font ?? ThemeDB.FallbackFont;
    }

    /// <inheritdoc />
    public float UnitsPerMetre => _scale;

    private Vector2 Map(SysVec2 p) => _offset + new Vector2(p.X, p.Y) * _scale;

    private static Color C(Rgb c) => new(c.R, c.G, c.B);

    /// <inheritdoc />
    public void Polygon(IReadOnlyList<SysVec2> points, Rgb? fill, Rgb? stroke, float strokeWidth = 1f)
    {
        if (points.Count < 2)
            return;
        var pts = points.Select(Map).ToArray();
        if (fill is { } f && points.Count >= 3)
            _item.DrawColoredPolygon(pts, C(f));
        if (stroke is { } s)
            _item.DrawPolyline(pts.Append(pts[0]).ToArray(), C(s), strokeWidth, true);
    }

    /// <inheritdoc />
    public void Polyline(IReadOnlyList<SysVec2> points, Rgb stroke, float width = 1f, bool dashed = false)
    {
        if (points.Count < 2)
            return;
        var pts = points.Select(Map).ToArray();
        if (!dashed)
        {
            _item.DrawPolyline(pts, C(stroke), width, true);
            return;
        }
        for (int i = 0; i + 1 < pts.Length; i++)
            _item.DrawDashedLine(pts[i], pts[i + 1], C(stroke), width, 6f);
    }

    /// <inheritdoc />
    public void Circle(SysVec2 centre, float radius, Rgb? fill, Rgb? stroke, float strokeWidth = 1f)
    {
        var c = Map(centre);
        float r = radius * _scale;
        if (fill is { } f)
            _item.DrawCircle(c, r, C(f));
        if (stroke is { } s)
            _item.DrawArc(c, r, 0, Mathf.Tau, Mathf.Clamp((int)r, 12, 64), C(s), strokeWidth, true);
    }

    /// <inheritdoc />
    public void Text(SysVec2 position, string text, float size, Rgb color, TextAlign align = TextAlign.Left)
    {
        int px = Mathf.Max(6, Mathf.RoundToInt(size));
        var p = Map(position);
        float w = TextWidth(text, size);
        p.X -= align == TextAlign.Center ? w / 2f : align == TextAlign.Right ? w : 0f;
        _item.DrawString(_font, p, text, HorizontalAlignment.Left, -1, px, C(color));
    }

    /// <inheritdoc />
    public float TextWidth(string text, float size) =>
        _font.GetStringSize(text, HorizontalAlignment.Left, -1, Mathf.Max(6, Mathf.RoundToInt(size))).X;
}
