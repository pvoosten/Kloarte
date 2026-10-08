using System.IO;
using System.Linq;
using System.Text;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>The contact sheet PDF (UI review 32): JPEG pictures embedded, nine per page, captions under them.</summary>
public sealed class ContactSheetTests : TestBase
{
    /// <summary>The test's output folder.</summary>
    public ContactSheetTests() : base("contact_sheet") { }

    /// <summary>A JPEG-looking picture (the PDF writer embeds the bytes as they are).</summary>
    private static PdfImage Picture() => new(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9 }, 160, 90);

    /// <summary>Eleven cues: two pages, every picture an image object drawn once, the captions as text.</summary>
    [Fact]
    public void PicturesAndCaptionsOnPages()
    {
        var frames = Enumerable.Range(1, 11).Select(i => new ContactSheetFrame(Picture(), $"Cue {i}  Storm")).ToList();
        string path = Path.Combine(_out, "sheet.pdf");
        int pages = ContactSheetPdf.Export(path, "Kruimels - contact sheet", frames);
        Assert.Equal(2, pages);
        string pdf = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        Assert.Equal(11, CountOf(pdf, "/Filter /DCTDecode"));
        Assert.Equal(11, CountOf(pdf, " Do Q"));
        Assert.Contains("/Count 2", pdf);
        Assert.Contains("(Cue 11  Storm) Tj", pdf);
        Assert.Contains("/XObject << /Im1 ", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
    }

    /// <summary>Only JPEG bytes are pictures.</summary>
    [Fact]
    public void NotAJpegIsRefused() =>
        Assert.Throws<System.ArgumentException>(() => new PdfImage(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 1, 1));

    /// <summary>How often `part` occurs in `text`.</summary>
    private static int CountOf(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, System.StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, System.StringComparison.Ordinal))
            n++;
        return n;
    }
}
