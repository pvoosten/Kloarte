using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kloarte.Model;

/// <summary>
/// A minimal PDF writer (PDF 1.4): pages with vector graphics and text in the standard Helvetica font (one of the 14
/// base fonts every PDF reader has, so nothing is embedded; WinAnsi encoding covers é, ë, ° ...). Enough for printing
/// the plan; no images. No Godot dependencies.
/// </summary>
public sealed class PdfDocument
{
    /// <summary>A3 landscape width / height in points.</summary>
    public const float A3Width = 1190.55f, A3Height = 841.89f;

    /// <summary>A4 landscape width / height in points.</summary>
    public const float A4Width = 841.89f, A4Height = 595.28f;

    private readonly List<PdfPage> _pages = new();

    /// <summary>Document title (PDF info).</summary>
    public string Title { get; set; } = "";

    /// <summary>The pages.</summary>
    private IReadOnlyList<PdfPage> Pages => _pages;

    /// <summary>Adds a page (points).</summary>
    public PdfPage AddPage(float width, float height)
    {
        var page = new PdfPage(width, height);
        _pages.Add(page);
        return page;
    }

    /// <summary>Writes the PDF file.</summary>
    public void Save(string path) => File.WriteAllBytes(path, ToBytes());

    /// <summary>The PDF as bytes.</summary>
    private byte[] ToBytes()
    {
        // Objects: 1 catalog, 2 pages, 3 font, 4 info, then per page: page object + content stream + its pictures
        // (a string is an object as it is; a picture is a dictionary + its JPEG stream).
        var objects = new List<object>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            null,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Title ({Escape(Title)}) /Producer (Kloarte up de plankn) >>",
        };
        var kids = new StringBuilder();
        foreach (var page in _pages)
        {
            int pageId = objects.Count + 1, contentId = objects.Count + 2;
            kids.Append($"{pageId} 0 R ");
            var xobjects = new StringBuilder();
            for (int i = 0; i < page.Images.Count; i++)
                xobjects.Append($"/Im{i + 1} {contentId + 1 + i} 0 R ");
            string resources = page.Images.Count == 0 ? "<< /Font << /F1 3 0 R >> >>"
                : $"<< /Font << /F1 3 0 R >> /XObject << {xobjects.ToString().Trim()} >> >>";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(page.Width)} {N(page.Height)}] " +
                        $"/Resources {resources} /Contents {contentId} 0 R >>");
            string content = page.Content;
            objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
            foreach (var image in page.Images)
                objects.Add(($"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} " +
                             $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {image.Jpeg.Length} >>",
                             image.Jpeg));
        }
        objects[1] = $"<< /Type /Pages /Kids [{kids.ToString().Trim()}] /Count {_pages.Count} >>";

        using var ms = new MemoryStream();
        void Write(string s)
        {
            var bytes = Encoding.Latin1.GetBytes(s);
            ms.Write(bytes, 0, bytes.Length);
        }
        Write("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            if (objects[i] is (string dictionary, byte[] data))
            {
                Write($"{i + 1} 0 obj\n{dictionary}\nstream\n");
                ms.Write(data, 0, data.Length);
                Write("\nendstream\nendobj\n");
            }
            else
                Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = ms.Position;
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long o in offsets)
            sb.Append($"{o:0000000000} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info 4 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Write(sb.ToString());
        return ms.ToArray();
    }

    private static string N(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>Width of a text in Helvetica (points) at the given size (standard AFM widths).</summary>
    public static float TextWidth(string text, float size)
    {
        float w = 0;
        foreach (char c in text)
            w += c >= 32 && c < 127 ? HelveticaWidths[c - 32] : 556;
        return w * size / 1000f;
    }

    /// <summary>Helvetica glyph widths (1/1000 em) for ASCII 32..126.</summary>
    private static readonly short[] HelveticaWidths =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };
}
