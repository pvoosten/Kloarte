using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// <c>cues.yaml</c> of a native show: its one cue list, in cue order. Tracking: a cue holds only what it changes. Part of
/// <see cref="DesignShow"/>.
/// </summary>
public sealed class DesignCueList
{
    /// <summary>The cue list's name.</summary>
    [FileField("The cue list's name")]
    public string Name { get; set; }

    /// <summary>The console's cue stack the list is (MagicQ: C record number), when imported.</summary>
    [FileField("The MagicQ cue stack this list is written to (its number); missing = 1", Min = 1)]
    public int? Stack { get; set; }

    /// <summary>The cues, in cue order.</summary>
    [FileField("The cues, in cue order")]
    public List<DesignCue> Cues { get; set; } = new();
}
