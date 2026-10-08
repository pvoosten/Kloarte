namespace Kloarte.Model;

/// <summary>A wheel slot: name, colour (CIE xyY, Y 0..100) and optionally an image file (gobo).</summary>
public sealed record GdtfSlot(string Name, float X, float Y, float Brightness, string Media)
{
    /// <summary>Linear RGB (max channel 1) of the slot's colour; white for open / gobo slots.</summary>
    public (float R, float G, float B) Rgb
    {
        get
        {
            if (Y <= 0)
                return (1, 1, 1);
            // CIE xyY -> XYZ (Y = 1) -> linear sRGB, normalised to the brightest channel.
            float cx = X / Y, cz = (1 - X - Y) / Y;
            float r = 3.2406f * cx - 1.5372f - 0.4986f * cz;
            float g = -0.9689f * cx + 1.8758f + 0.0415f * cz;
            float b = 0.0557f * cx - 0.2040f + 1.0570f * cz;
            r = System.Math.Max(r, 0);
            g = System.Math.Max(g, 0);
            b = System.Math.Max(b, 0);
            float m = System.Math.Max(r, System.Math.Max(g, b));
            return m <= 0 ? (1, 1, 1) : (r / m, g / m, b / m);
        }
    }
}
