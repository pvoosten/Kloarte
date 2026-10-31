using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Draws the project's set pieces in the plan (<see cref="PlanDrawing"/>, on screen and in the PDF): a dashed grey outline
/// of the footprint, without colour, so the plan stays about the lamps; a pyramid with lines from its corners to its apex,
/// a table with its legs. Only a curtain that hangs vertically has its colour: a zigzag of its pleats along its width (its
/// two halves when it is split and open); a tilted one is an outline like the other pieces. A piece that is out at this
/// moment is lighter grey; the selected one is drawn in the selection colour. The geometry is
/// <see cref="SceneryGeometry"/>'s.
/// </summary>
public static class PlanPieces
{
    /// <summary>The selection colour (as the selected lamps).</summary>
    private static readonly Rgb Selected = new(1f, 0.45f, 0f);

    /// <summary>The outline of a piece that is on stage.</summary>
    private static readonly Rgb In = Rgb.Grey(0.4f);

    /// <summary>The outline of a piece that is out.</summary>
    private static readonly Rgb Out = Rgb.Grey(0.7f);

    /// <summary>The colour of a piece ("rrggbb" as an <see cref="Rgb"/>).</summary>
    public static Rgb ColourOf(SceneryPiece piece) =>
        ShirtColors.Parse(piece.ColorOrDefault) is var (r, g, b) ? new Rgb(r, g, b) : Rgb.Grey(0.5f);

    /// <summary>Draws one piece.</summary>
    public static void Draw(IPlanCanvas c, PlanPiece p, PlanStyle style)
    {
        var piece = p.Piece;
        var edge = p.Selected ? Selected : p.Visible ? In : Out;
        float width = p.Selected ? 2.5f : 1.2f;
        switch (piece.Shape)
        {
            case SceneryShape.Curtain when SceneryGeometry.Tilted(piece):
                foreach (var (from, to) in SceneryGeometry.Spans(piece, p.Open))
                    Outline(c, SceneryGeometry.Outline(piece, from, to), edge, width);
                break;
            case SceneryShape.Curtain:
                Curtain(c, p, p.Visible ? ColourOf(piece) : Out, edge, width);
                break;
            case SceneryShape.Table:
                Outline(c, SceneryGeometry.Footprint(piece), edge, width);
                float leg = Math.Max(piece.LegThickness, 0.02f) / 2f;
                foreach (var l in SceneryGeometry.Legs(piece))
                {
                    var corners = new[]
                    {
                        SceneryGeometry.ToPlan(piece, l.X - leg, l.Y - leg), SceneryGeometry.ToPlan(piece, l.X + leg, l.Y - leg),
                        SceneryGeometry.ToPlan(piece, l.X + leg, l.Y + leg), SceneryGeometry.ToPlan(piece, l.X - leg, l.Y + leg),
                    };
                    Outline(c, corners, edge, 0.8f);
                }
                break;
            case SceneryShape.Pyramid:
                Outline(c, SceneryGeometry.Footprint(piece), edge, width);
                var apex = SceneryGeometry.ToPlan(piece, 0f, 0f, piece.Height);
                foreach (var corner in SceneryGeometry.Base(piece))
                    c.Polyline(new[] { corner, apex }, edge, 0.8f, dashed: true);
                break;
            default:
                Outline(c, SceneryGeometry.Footprint(piece), edge, width);
                break;
        }
        if (style.LabelSize <= 0 || string.IsNullOrEmpty(piece.Name))
            return;
        // The name in the middle of the piece; a curtain's in front of it (it is thin).
        var at = piece.Shape == SceneryShape.Curtain
            ? SceneryGeometry.ToPlan(piece, 0f, SceneryGeometry.HalfDepth(piece)) + new Vector2(0f, 0.35f)
            : new Vector2(piece.Position[0], piece.Position[2]) + new Vector2(0f, style.LabelSize * 0.35f / Math.Max(c.UnitsPerMetre, 1e-3f));
        c.Text(at, piece.Name, style.LabelSize * 0.85f, p.Visible ? Rgb.Grey(0.1f) : Out, TextAlign.Center);
    }

    /// <summary>A closed dashed outline.</summary>
    private static void Outline(IPlanCanvas c, IReadOnlyList<Vector2> points, Rgb edge, float width) =>
        c.Polyline(points.Append(points[0]).ToList(), edge, width, dashed: true);

    /// <summary>A vertically hanging curtain: the zigzag of its pleats along each of its spans (both halves of a split one), in its colour.</summary>
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
                c.Polygon(SceneryGeometry.Outline(piece, from, to), null, edge, width);
        }
    }
}
