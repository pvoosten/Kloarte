using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// <c>blocking/scenery.yaml</c>: the set pieces on stage (Kloarte's own, not exported to MagicQ). What a cue changes of
/// them (out / in, a curtain opening) is in <see cref="StepsFile"/>.
/// </summary>
public sealed class SceneryFile
{
    /// <summary>The set pieces.</summary>
    [FileField("The set pieces on stage (all on stage before the first cue; the cues in steps.yaml take them out / in)")]
    public List<SceneryPieceEntry> Pieces { get; set; } = new();
}
