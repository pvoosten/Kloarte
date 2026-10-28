using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary><c>blocking/steps.yaml</c>: per cue (in cue order) the hold after it, the smoke, the scenery changes and the actors' movement steps.</summary>
public sealed class StepsFile
{
    /// <summary>The cues with a hold or movement.</summary>
    [FileField("The cues with a hold or actor movement, in cue order")]
    public List<CueEntry> Cues { get; set; } = new();
}
