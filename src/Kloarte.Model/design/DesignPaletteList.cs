using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary><c>palettes.yaml</c>: the native show's palettes. Part of <see cref="DesignShow"/>.</summary>
public sealed class DesignPaletteList
{
    /// <summary>The palettes.</summary>
    [FileField("The palettes of the show")]
    public List<DesignPalette> Palettes { get; set; } = new();
}
