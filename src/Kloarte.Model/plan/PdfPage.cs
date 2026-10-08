using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kloarte.Model;

/// <summary>
/// One page of a <see cref="PdfDocument"/>: a content stream of vector operations and Helvetica text. Coordinates in
/// points (1/72 inch), origin at the bottom left of the page (PDF convention).
/// </summary>
public sealed class PdfPage
{
    private readonly StringBuilder _content = new();

    /// <summary>Page width in points.</summary>
    public float Width { get; }

    /// <summary>Page height in points.</summary>
    public float Height { get; }

    /// <summary>Creates a page of the given size (points).</summary>
    public PdfPage(float width, float height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>The page's content stream.</summary>
    public string Content => _content.ToString();

    /// <summary>The pictures drawn on the page (resource names /Im1, /Im2, ... in this order).</summary>
    internal List<PdfImage> Images { get; } = new();

    /// <summary>Draws a picture into the rectangle (x, y) .. (x + width, y + height), bottom left at (x, y).</summary>
    public void Image(PdfImage image, float x, float y, float width, float height)
    {
        Images.Add(image);
        Op($"q {N(width)} 0 0 {N(height)} {N(x)} {N(y)} cm /Im{Images.Count} Do Q");
    }

    /// <summary>Fill colour.</summary>
    public void Fill(Rgb c) => Op($"{N(c.R)} {N(c.G)} {N(c.B)} rg");

    /// <summary>Stroke colour.</summary>
    public void Stroke(Rgb c) => Op($"{N(c.R)} {N(c.G)} {N(c.B)} RG");

    /// <summary>Line width (points).</summary>
    public void LineWidth(float w) => Op($"{N(w)} w");

    /// <summary>Dash pattern (points); (0, 0) = solid.</summary>
    public void Dash(float on, float off) => Op(on <= 0 ? "[] 0 d" : $"[{N(on)} {N(off)}] 0 d");

    /// <summary>Starts a new subpath.</summary>
    public void MoveTo(float x, float y) => Op($"{N(x)} {N(y)} m");

    /// <summary>Straight segment.</summary>
    public void LineTo(float x, float y) => Op($"{N(x)} {N(y)} l");

    /// <summary>Cubic Bézier segment.</summary>
    private void CurveTo(float x1, float y1, float x2, float y2, float x3, float y3) =>
        Op($"{N(x1)} {N(y1)} {N(x2)} {N(y2)} {N(x3)} {N(y3)} c");

    /// <summary>Closes the subpath.</summary>
    public void Close() => Op("h");

    /// <summary>Paints the path: fill and/or stroke (or neither: discard).</summary>
    public void Paint(bool fill, bool stroke) => Op(fill && stroke ? "B" : fill ? "f" : stroke ? "S" : "n");

    /// <summary>A circle as four Bézier arcs.</summary>
    public void CirclePath(float cx, float cy, float r)
    {
        const float k = 0.5523f;
        MoveTo(cx + r, cy);
        CurveTo(cx + r, cy + k * r, cx + k * r, cy + r, cx, cy + r);
        CurveTo(cx - k * r, cy + r, cx - r, cy + k * r, cx - r, cy);
        CurveTo(cx - r, cy - k * r, cx - k * r, cy - r, cx, cy - r);
        CurveTo(cx + k * r, cy - r, cx + r, cy - k * r, cx + r, cy);
        Close();
    }

    /// <summary>Helvetica text with its baseline at (x, y), in the current fill colour.</summary>
    public void Text(float x, float y, float size, string text) =>
        Op($"BT /F1 {N(size)} Tf {N(x)} {N(y)} Td ({Escape(text)}) Tj ET");

    private void Op(string op) => _content.Append(op).Append('\n');

    private static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>PDF string escaping; characters outside Latin-1 (WinAnsi) become '?'.</summary>
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            char c = ch > 255 ? '?' : ch;
            if (c is '(' or ')' or '\\')
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
