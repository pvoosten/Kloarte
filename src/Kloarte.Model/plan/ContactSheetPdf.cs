using System;
using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// A contact sheet (UI review 32): the pictures of the cues on A4 landscape pages, three by three, each with its caption
/// under it; a title line at the top and the page number at the bottom.
/// </summary>
public static class ContactSheetPdf
{
    /// <summary>Pictures per row and rows per page.</summary>
    private const int Columns = 3, Rows = 3;

    /// <summary>Page margin, gap between pictures, caption line height (points).</summary>
    private const float Margin = 28f, Gap = 12f, CaptionHeight = 14f, TitleHeight = 22f;

    /// <summary>Writes the contact sheet of `frames` to `path`; returns the number of pages.</summary>
    public static int Export(string path, string title, IReadOnlyList<ContactSheetFrame> frames)
    {
        var pdf = new PdfDocument { Title = title };
        int perPage = Columns * Rows;
        int pages = Math.Max(1, (frames.Count + perPage - 1) / perPage);
        float width = PdfDocument.A4Width, height = PdfDocument.A4Height;
        float cellW = (width - 2 * Margin - (Columns - 1) * Gap) / Columns;
        float cellH = (height - 2 * Margin - TitleHeight - (Rows - 1) * Gap) / Rows;
        for (int p = 0; p < pages; p++)
        {
            var page = pdf.AddPage(width, height);
            page.Fill(Rgb.Black);
            page.Text(Margin, height - Margin - 12, 14, title);
            string number = $"{p + 1} / {pages}";
            page.Text(width - Margin - PdfDocument.TextWidth(number, 9), Margin / 2, 9, number);
            for (int i = 0; i < perPage && p * perPage + i < frames.Count; i++)
            {
                var frame = frames[p * perPage + i];
                int col = i % Columns, row = i / Columns;
                float left = Margin + col * (cellW + Gap);
                float top = height - Margin - TitleHeight - row * (cellH + Gap);
                float maxW = cellW, maxH = cellH - CaptionHeight;
                float aspect = frame.Image.Height > 0 ? (float)frame.Image.Width / frame.Image.Height : 16f / 9f;
                float w = Math.Min(maxW, maxH * aspect), h = w / aspect;
                page.Image(frame.Image, left, top - h, w, h);
                if (frame.Caption.Length > 0)
                    page.Text(left, top - h - 11, 9, Fit(frame.Caption, maxW, 9));
            }
        }
        pdf.Save(path);
        return pages;
    }

    /// <summary>The text, cut with "..." to fit `width` at `size`.</summary>
    private static string Fit(string text, float width, float size)
    {
        if (PdfDocument.TextWidth(text, size) <= width)
            return text;
        while (text.Length > 1 && PdfDocument.TextWidth(text + "...", size) > width)
            text = text[..^1];
        return text + "...";
    }
}
