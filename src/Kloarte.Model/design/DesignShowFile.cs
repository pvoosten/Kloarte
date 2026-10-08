namespace Kloarte.Model;

/// <summary>
/// A native show in one file (undo snapshots, the movie process): its rig, palettes and cues, as the project's
/// <c>rig.yaml</c>, <c>palettes.yaml</c> and <c>cues.yaml</c> hold them.
/// </summary>
public sealed class DesignShowFile
{
    /// <summary>The rig and patch.</summary>
    [FileField("The rig and patch (as rig.yaml)")]
    public DesignRig Rig { get; set; } = new();

    /// <summary>The palettes.</summary>
    [FileField("The palettes (as palettes.yaml)")]
    public DesignPaletteList Palettes { get; set; } = new();

    /// <summary>The cues.</summary>
    [FileField("The cues (as cues.yaml)")]
    public DesignCueList Cues { get; set; } = new();
}
