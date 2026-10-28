using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Draws the project's set pieces in the plan (<see cref="PlanDrawing"/>, on screen and in the PDF): the footprint in the
/// piece's colour with an outline; a pyramid with its diagonals, a table's top with its legs, a curtain as a zigzag of
/// its pleats along its width (its two halves when it is split and open). A piece that is out at this moment is a dashed
/// outline; the selected one is outlined in the selection colour. The geometry is <see cref="SceneryGeometry"/>'s.
/// </summary>
public static class PlanPieces
{
    /// <summary>The selection colour (as the selected lamps).</summary>
    private static readonly Rgb Selected = new(1f, 0.45f, 0f);

    /// <summary>The outline of a piece that is out.</summary>
    private static readonly Rgb Out = Rgb.Grey(0.55f);

    /// <summary>The colour of a piece ("rrggbb" as an <see cref="Rgb"/>).</summary>
    public static Rgb ColourOf(SceneryPiece piece) =>
        ShirtColors.Parse(piece.ColorOrDefault) is var (r, g, b) ? new Rgb(r, g, b) : Rgb.Grey(0.5f);

    /// <summary>Draws one piece.</summary>
    public static void Draw(IPlanCanvas c, PlanPiece p, PlanStyle style)
    {
        var piece = p.Piece;
        var colour = ColourOf(piece);
        Rgb? fill = p.Visible ? colour : null;
        var edge = p.Selected ? Selected : p.Visible ? Edge(colour) : Out;
        float width = p.Selected ? 2.5f : 1.2f;
        switch (piece.Shape)
        {
            case SceneryShape.Curtain:
                Curtain(c, p, p.Visible ? colour : Out, edge, width);
                break;
            case SceneryShape.Table:
                Outline(c, SceneryGeometry.Footprint(piece), fill, edge, width, p.Visible);
                float leg = Math.Max(piece.LegThickness, 0.02f) / 2f;
                foreach (var l in SceneryGeometry.Legs(piece))
                {
                    var corners = new[]
                    {
                        SceneryGeometry.ToPlan(piece, l.X - leg, l.Y - leg), SceneryGeometry.ToPlan(piece, l.X + leg, l.Y - leg),
                        SceneryGeometry.ToPlan(piece, l.X + leg, l.Y + leg), SceneryGeometry.ToPlan(piece, l.X - leg, l.Y + leg),
                    };
                    Outline(c, corners, null, edge, 0.8f, p.Visible);
                }
                break;
            case SceneryShape.Pyramid:
                var f = SceneryGeometry.Footprint(piece);
                Outline(c, f, fill, edge, width, p.Visible);
                c.Polyline(new[] { f[0], f[2] }, edge, 0.8f, dashed: !p.Visible);
                c.Polyline(new[] { f[1], f[3] }, edge, 0.8f, dashed: !p.Visible);
                break;
            default:
                Outline(c, SceneryGeometry.Footprint(piece), fill, edge, width, p.Visible);
                break;
        }
        if (style.LabelSize <= 0 || string.IsNullOrEmpty(piece.Name))
            return;
        // The name in the middle of the piece; a curtain's in front of it (it is thin).
        bool onFill = p.Visible && piece.Shape != SceneryShape.Curtain;
        var at = piece.Shape == SceneryShape.Curtain
            ? SceneryGeometry.ToPlan(piece, 0f, SceneryGeometry.HalfDepth(piece)) + new Vector2(0f, 0.35f)
            : new Vector2(piece.Position[0], piece.Position[2]) + new Vector2(0f, style.LabelSize * 0.35f / Math.Max(c.UnitsPerMetre, 1e-3f));
        var text = !p.Visible ? Out : onFill && colour.Luminance < 0.4f ? Rgb.White : Rgb.Grey(0.1f);
        c.Text(at, piece.Name, style.LabelSize * 0.85f, text, TextAlign.Center);
    }

    /// <summary>A closed outline: filled and solid when the piece is in, a dashed line when it is out.</summary>
    private static void Outline(IPlanCanvas c, IReadOnlyList<Vector2> points, Rgb? fill, Rgb edge, float width, bool visible)
    {
        if (visible)
        {
            c.Polygon(points, fill, edge, width);
            return;
        }
        c.Polyline(points.Append(points[0]).ToList(), edge, width, dashed: true);
    }

    /// <summary>A curtain: the zigzag of its pleats along each of its spans (both halves of a split one), in its colour.</summary>
    private static void Curtain(IPlanCanvas c, PlanPiece p, Rgb cloth, Rgb edge, float width)
    {
        var piece = p.Piece;
        float amplitude = piece.PleatDepth / 2f;
        float line = Math.Max(2f, 0.03f * c.UnitsPerMetre);
        foreach (var (from, to) in SceneryGeometry.Spans(piece, p.Open))
        {
            var points = new List<Vector2>();
            float step = SceneryPiece.PleatSpacing / 2f;
            int n = amplitude <= 0f ? 1 : Math.Max(1, (int)MathF.Round((to - from) / step));
            for (int i = 0; i <= n; i++)
            {
                float x = from + (to - from) * i / n;
                float z = amplitude <= 0f ? 0f : i % 2 == 0 ? -amplitude : amplitude;
                points.Add(SceneryGeometry.ToPlan(piece, x, z));
            }
            c.Polyline(points, cloth, line, dashed: !p.Visible);
            if (p.Selected)
                c.Polygon(SceneryGeometry.Box(piece, from, to, SceneryGeometry.HalfDepth(piece)), null, edge, width);
        }
    }

    /// <summary>An outline colour that stands out against the fill (darker, or light grey for a dark fill).</summary>
    private static Rgb Edge(Rgb fill) =>
        fill.Luminance < 0.2f ? Rgb.Grey(0.45f) : new Rgb(fill.R * 0.45f, fill.G * 0.45f, fill.B * 0.45f);
}
