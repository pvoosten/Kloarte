using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Exports the plan as a PDF page (A3 or A4 landscape), as large as fits: the scale is rounded up to a multiple of 5
/// (e.g. 1:85), so the plan fills the drawing area within a few percent; the scale bar gives real distances. A narrow
/// legend column on the right (lamp types and gels, small print), a scale bar and a title block (show, venue, date,
/// scale). No Godot dependencies.
/// </summary>
public static class PlanPdf
{
    /// <summary>Page margin and legend column width (points).</summary>
    private const float Margin = 20f, LegendWidth = 150f;

    /// <summary>Legend text size (points).</summary>
    private const float LegendText = 6.5f;

    /// <summary>Room below the plan for the scale bar (points).</summary>
    private const float BarRoom = 22f;

    /// <summary>Writes the plan; returns the scale used (e.g. 100 for 1:100).</summary>
    public static int Export(PlanData data, string path, bool a3 = true, bool seats = false)
    {
        var doc = new PdfDocument { Title = $"Lighting plan {data.Show}" };
        var page = doc.AddPage(a3 ? PdfDocument.A3Width : PdfDocument.A4Width, a3 ? PdfDocument.A3Height : PdfDocument.A4Height);
        int scale = Draw(page, data, seats);
        doc.Save(path);
        return scale;
    }

    /// <summary>Draws plan, legend, scale bar and title block on a page; returns the scale.</summary>
    public static int Draw(PdfPage page, PlanData data, bool seats)
    {
        var paper = new PdfPlanCanvas(page, 1f, Vector2.Zero);
        var (min, max) = PlanDrawing.Bounds(data, seats);
        float areaW = page.Width - 2 * Margin - LegendWidth, areaH = page.Height - 2 * Margin - BarRoom;
        const float pointsPerMetreAt1 = 72f / 0.0254f;   // 1:1
        float exact = Math.Max((max.X - min.X) * pointsPerMetreAt1 / areaW, (max.Y - min.Y) * pointsPerMetreAt1 / areaH);
        int scale = Math.Max(20, (int)MathF.Ceiling(exact / 5f) * 5);
        float ppm = pointsPerMetreAt1 / scale;

        // Frame and plan, centred in the drawing area.
        var frame = new[] { new Vector2(Margin / 2, Margin / 2), new Vector2(page.Width - Margin / 2, Margin / 2),
                            new Vector2(page.Width - Margin / 2, page.Height - Margin / 2), new Vector2(Margin / 2, page.Height - Margin / 2) };
        paper.Polygon(frame, PlanDrawing.Paper, Rgb.Black, 1.2f);
        var size = (max - min) * ppm;
        var origin = new Vector2(Margin + (areaW - size.X) / 2f, Margin + (areaH - size.Y) / 2f) - min * ppm;
        var plan = new PdfPlanCanvas(page, ppm, origin);
        PlanDrawing.Draw(plan, data, new PlanStyle { LabelSize = scale <= 75 ? 8f : 6.5f, Seats = seats });

        // Legend column, title block and scale bar.
        float lx = page.Width - Margin - LegendWidth + 10f;
        paper.Polyline(new[] { new Vector2(lx - 10f, Margin / 2), new Vector2(lx - 10f, page.Height - Margin / 2) }, Rgb.Black, 1f);
        float ty = page.Height - Margin - 46f;
        // Legend height per point of text size (rows of DrawLegend); smaller print when a long legend would run into
        // the title block.
        float perPoint = 1.3f + 2.4f * (0.6f + 0.3f + 0.4f) + 2.4f * PlanSymbols.Types(data).Count() + 1.9f * PlanSymbols.Gels(data).Count();
        float legendText = Math.Min(LegendText, (ty - 16f - Margin) / perPoint);
        PlanDrawing.DrawLegend(paper, data, new Vector2(lx, Margin), legendText, LegendWidth - 16f);
        paper.Polyline(new[] { new Vector2(lx - 10f, ty - 12f), new Vector2(page.Width - Margin / 2, ty - 12f) }, Rgb.Black, 1f);
        paper.Text(new Vector2(lx, ty), data.Show, 10f, Rgb.Black);
        paper.Text(new Vector2(lx, ty + 12f), data.Venue, 7f, Rgb.Black);
        paper.Text(new Vector2(lx, ty + 23f), $"Lighting plan, scale 1:{scale}", 7f, Rgb.Black);
        paper.Text(new Vector2(lx, ty + 34f), DateTime.Now.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + "  -  Kloarte up de plankn", 6f, Rgb.Grey(0.3f));
        ScaleBar(paper, new Vector2(Margin, page.Height - Margin - 6f), ppm);
        return scale;
    }

    /// <summary>A 5 m scale bar with 1 m ticks (device coordinates).</summary>
    private static void ScaleBar(IPlanCanvas c, Vector2 at, float pointsPerMetre)
    {
        for (int m = 0; m < 5; m++)
        {
            var a = at + new Vector2(m * pointsPerMetre, 0);
            c.Polygon(new[] { a, a + new Vector2(pointsPerMetre, 0), a + new Vector2(pointsPerMetre, 5f), a + new Vector2(0, 5f) },
                m % 2 == 0 ? Rgb.Black : Rgb.White, Rgb.Black, 0.8f);
            c.Text(a + new Vector2(0, -3f), m.ToString(CultureInfo.InvariantCulture), 7f, Rgb.Black, TextAlign.Center);
        }
        c.Text(at + new Vector2(5 * pointsPerMetre, -3f), "5 m", 7f, Rgb.Black, TextAlign.Center);
    }
}
