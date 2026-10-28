using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Draws the plan (top view) of a show: stage and forestage, cyc, scenery footprints, the project's set pieces
/// (<see cref="PlanPieces"/>), seats, trusses with name and
/// height, floor stands, lamps as symbols filled with their gel colour and labelled with head number, gel code and
/// DMX address, focus lines for the selected lamps, and a legend of the lamp types and gels. The same code draws the
/// plan view on screen and the PDF. No Godot dependencies.
/// </summary>
public static class PlanDrawing
{
    /// <summary>Paper colour.</summary>
    public static readonly Rgb Paper = new(0.98f, 0.98f, 0.96f);

    private static readonly Rgb StageColour = new(0.9f, 0.9f, 0.88f);
    private static readonly Rgb Masking = Rgb.Grey(0.35f);
    private static readonly Rgb SetPiece = new(0.8f, 0.74f, 0.66f);
    private static readonly Rgb Truss = Rgb.Grey(0.45f);
    private static readonly Rgb Selected = new(1f, 0.45f, 0f);

    /// <summary>Bounding box of the drawing (plan metres), with a margin.</summary>
    public static (Vector2 Min, Vector2 Max) Bounds(PlanData d, bool seats = true)
    {
        var pts = new List<Vector2>
        {
            new(-d.StageWidth / 2f, d.StageBack), new(d.StageWidth / 2f, d.StageEdge),
        };
        pts.AddRange(d.Supports.SelectMany(s => new[] { s.Start, s.End }));
        pts.AddRange(d.Lamps.Select(l => l.Position));
        pts.AddRange(d.Pieces.SelectMany(p => SceneryGeometry.Footprint(p.Piece)));
        if (seats)
            pts.AddRange(d.Seats.Select(s => s.Position));
        // Extra room on the left for the truss names and heights.
        var min = new Vector2(pts.Min(p => p.X), pts.Min(p => p.Y)) - new Vector2(2.6f, 1f);
        var max = new Vector2(pts.Max(p => p.X), pts.Max(p => p.Y)) + new Vector2(1f, 1f);
        return (min, max);
    }

    /// <summary>Draws the whole plan (plan coordinates).</summary>
    public static void Draw(IPlanCanvas c, PlanData d, PlanStyle style)
    {
        float u = c.UnitsPerMetre;
        float hw = d.StageWidth / 2f;
        // Stage incl. forestage.
        c.Polygon(new[] { new Vector2(-hw, d.StageBack), new Vector2(hw, d.StageBack), new Vector2(hw, d.StageEdge), new Vector2(-hw, d.StageEdge) },
            StageColour, Rgb.Grey(0.3f), 1.5f);
        c.Polyline(new[] { new Vector2(-hw - 0.5f, 0), new Vector2(hw + 0.5f, 0) }, Rgb.Grey(0.5f), 1f, dashed: true);
        c.Text(new Vector2(hw + 0.6f, 0.1f), "proscenium", style.LabelSize * 0.8f, Rgb.Grey(0.4f));
        if (d.CycWidth > 0)
        {
            c.Polyline(new[] { new Vector2(-d.CycWidth / 2f, d.StageBack), new Vector2(d.CycWidth / 2f, d.StageBack) }, Rgb.Grey(0.2f), 3f);
            c.Text(new Vector2(0, d.StageBack - 0.15f), "cyc", style.LabelSize * 0.8f, Rgb.Grey(0.3f), TextAlign.Center);
        }

        foreach (var s in d.Shapes)
            c.Polygon(s.Points, s.Masking ? Masking : SetPiece, Rgb.Grey(0.2f), 1f);
        foreach (var p in d.Pieces)
            PlanPieces.Draw(c, p, style);

        if (style.Seats)
            foreach (var (p, reserved) in d.Seats)
            {
                float h = d.SeatWidth * 0.42f;
                c.Polygon(new[] { p + new Vector2(-h, -h), p + new Vector2(h, -h), p + new Vector2(h, h), p + new Vector2(-h, h) },
                    reserved ? Rgb.Grey(0.6f) : Rgb.Grey(0.88f), Rgb.Grey(0.6f), 0.5f);
            }

        var used = d.Lamps.Select(l => l.Support).ToHashSet();
        foreach (var s in d.Supports)
        {
            // Trusses without lamps are drawn light and unlabelled (a theatre has many bars).
            bool loaded = used.Contains(s.Id) || s.Selected || s.IsStand;
            var colour = s.Selected ? Selected : loaded ? Truss : Rgb.Grey(0.78f);
            if (s.IsStand)
            {
                c.Circle(s.Start, 0.12f, Rgb.White, colour, 2f);
                c.Polyline(new[] { s.Start + new Vector2(-0.12f, 0), s.Start + new Vector2(0.12f, 0) }, colour, 1f);
                c.Polyline(new[] { s.Start + new Vector2(0, -0.12f), s.Start + new Vector2(0, 0.12f) }, colour, 1f);
                c.Text(s.Start + new Vector2(0.18f, 0.32f), $"{s.Name} {Metres(s.Height)}", style.LabelSize * 0.8f, colour);
                continue;
            }
            c.Polyline(new[] { s.Start, s.End }, colour, loaded ? Math.Max(2f, 0.05f * u) : 1f);
            if (!loaded)
                continue;
            var left = s.Start.X <= s.End.X ? s.Start : s.End;
            c.Text(left - new Vector2(0.2f, -0.06f), $"{s.Name}  {Metres(s.Height)}", style.LabelSize * 0.85f, colour, TextAlign.Right);
        }

        foreach (var l in d.Lamps.Where(l => l.Focus != null && (l.Selected || style.AllFocusLines)))
        {
            c.Polyline(new[] { l.Position, l.Focus.Value }, l.Selected ? Selected : Rgb.Grey(0.6f), 1f, dashed: true);
            c.Circle(l.Focus.Value, 0.06f, null, l.Selected ? Selected : Rgb.Grey(0.6f), 1f);
        }

        foreach (var l in d.Lamps)
        {
            float angle = PlanSymbols.Angle(l);
            if (l.Selected)
                c.Circle(l.Position, 0.38f * style.SymbolScale, null, Selected, 2.5f);
            PlanSymbols.Draw(c, l.Kind, l.Small, l.Position, angle, l.Gel, style.SymbolScale);
            // Labels on the far side of the symbol from the beam: head number, gel code, address.
            var back = new Vector2(-MathF.Cos(angle), -MathF.Sin(angle));
            var at = l.Position + back * 0.42f * style.SymbolScale;
            c.Text(at + new Vector2(0, 0.05f), l.Head.ToString(CultureInfo.InvariantCulture), style.LabelSize, Rgb.Black, TextAlign.Center);
            float line = style.LabelSize / Math.Max(u, 1e-3f) * 1.1f;
            if (l.GelCode != "")
                c.Text(at + new Vector2(0, 0.05f + line), l.GelCode, style.LabelSize * 0.85f, Darken(l.Gel ?? Rgb.Black), TextAlign.Center);
            if (style.Addresses)
                c.Text(at + new Vector2(0, 0.05f + line * (l.GelCode != "" ? 2 : 1)), DmxAddress.Format(l.Address),
                    style.LabelSize * 0.75f, Rgb.Grey(0.35f), TextAlign.Center);
        }
    }

    /// <summary>
    /// Draws the legend in device coordinates (canvas with 1 unit per "metre") from `origin` (top left): the lamp types
    /// with their symbol and count, then the gels with a swatch, code, name and count. Returns the size used.
    /// </summary>
    public static Vector2 DrawLegend(IPlanCanvas c, PlanData d, Vector2 origin, float size = 11f, float width = 230f)
    {
        float row = size * 2.4f, y = origin.Y + size * 1.3f;
        c.Text(new Vector2(origin.X, y), "Fixture types", size * 1.1f, Rgb.Black);
        y += row * 0.6f;
        foreach (var (kind, small, name, count) in PlanSymbols.Types(d))
        {
            PlanSymbols.Draw(c, kind, small, new Vector2(origin.X + size * 1.8f, y + row * 0.3f), 0f, Rgb.White, size * 4f);
            c.Text(new Vector2(origin.X + size * 4.2f, y + row * 0.45f), $"{Shorten(c, name, size, width - size * 7.5f)}  x{count}", size, Rgb.Black);
            y += row;
        }
        y += row * 0.3f;
        c.Text(new Vector2(origin.X, y), "Gels", size * 1.1f, Rgb.Black);
        y += row * 0.4f;
        foreach (var (code, name, colour, count) in PlanSymbols.Gels(d))
        {
            var p = new Vector2(origin.X + size * 0.6f, y + size * 0.2f);
            c.Polygon(new[] { p, p + new Vector2(size * 2.2f, 0), p + new Vector2(size * 2.2f, size * 1.3f), p + new Vector2(0, size * 1.3f) },
                colour ?? Rgb.White, Rgb.Black, 0.8f);
            string shown = name.StartsWith(code) ? name[code.Length..].Trim() : name;   // MagicQ names start with the code
            c.Text(new Vector2(origin.X + size * 3.6f, y + size * 1.3f), $"{code}  {Shorten(c, shown, size, width - size * 9f)}  x{count}", size, Rgb.Black);
            y += size * 1.9f;
        }
        return new Vector2(width, y - origin.Y);
    }

    private static string Shorten(IPlanCanvas c, string text, float size, float max)
    {
        if (c.TextWidth(text, size) <= max)
            return text;
        while (text.Length > 3 && c.TextWidth(text + "...", size) > max)
            text = text[..^1];
        return text + "...";
    }

    /// <summary>A gel colour dark enough to read as text on paper.</summary>
    private static Rgb Darken(Rgb c)
    {
        float l = c.Luminance;
        return l < 0.45f ? c : new Rgb(c.R * 0.45f / l, c.G * 0.45f / l, c.B * 0.45f / l);
    }

    private static string Metres(float v) => v.ToString("0.00", CultureInfo.InvariantCulture) + " m";
}
