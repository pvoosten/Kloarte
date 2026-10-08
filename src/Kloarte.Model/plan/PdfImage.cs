using System;

namespace Kloarte.Model;

/// <summary>A JPEG picture for a PDF page (embedded as it is: DCTDecode, RGB).</summary>
public sealed class PdfImage
{
    /// <summary>A picture from JPEG bytes of `width` x `height` pixels.</summary>
    public PdfImage(byte[] jpeg, int width, int height)
    {
        if (jpeg == null || jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new ArgumentException("Not a JPEG picture", nameof(jpeg));
        Jpeg = jpeg;
        Width = width;
        Height = height;
    }

    /// <summary>The JPEG file's bytes.</summary>
    public byte[] Jpeg { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }
}
