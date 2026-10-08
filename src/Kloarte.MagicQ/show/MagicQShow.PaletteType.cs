namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>Palette window (type): the upper nibble of a palette id.</summary>
    public enum PaletteType
    {
        /// <summary>Intensity palettes (ids 9xxx).</summary>
        Intensity = 9,

        /// <summary>Position palettes (ids axxx); combined palettes live here too.</summary>
        Position = 0xa,

        /// <summary>Colour palettes (ids bxxx).</summary>
        Colour = 0xb,

        /// <summary>Beam palettes (ids cxxx).</summary>
        Beam = 0xc,
    }
}
