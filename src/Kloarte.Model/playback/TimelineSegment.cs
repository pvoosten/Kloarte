using System;

namespace Kloarte.Model;

/// <summary>
/// One cue of the stack on the timeline: from its GO (Start) the fades (In for rising, Out for falling levels) and the
/// actors' movement steps run; then the hold until the next GO. Times in seconds from the first GO.
/// </summary>
public sealed class TimelineSegment
{
    /// <summary>Index of the step in the stack (cue-number order).</summary>
    public int Index { get; init; }

    /// <summary>MagicQ cue id (S record).</summary>
    public int CueId { get; init; }

    /// <summary>Cue number as text (e.g. "12.1").</summary>
    public string CueNumber { get; init; } = "";

    /// <summary>Cue text.</summary>
    public string Text { get; init; } = "";

    /// <summary>Time of the GO.</summary>
    public double Start { get; init; }

    /// <summary>Fade time of rising levels.</summary>
    public double FadeIn { get; init; }

    /// <summary>Fade time of falling levels.</summary>
    public double FadeOut { get; init; }

    /// <summary>When the last actor step of this cue has finished, after the GO.</summary>
    public double ActorsEnd { get; init; }

    /// <summary>When the last curtain this cue opens or closes has finished moving, after the GO.</summary>
    public double SceneryEnd { get; init; }

    /// <summary>Wait after everything has finished until the next GO.</summary>
    public double Hold { get; init; }

    /// <summary>Time everything of this cue has finished (fades, actors and curtains).</summary>
    public double ActiveEnd => Start + Math.Max(Math.Max(FadeIn, FadeOut), Math.Max(ActorsEnd, SceneryEnd));

    /// <summary>Time of the next GO.</summary>
    public double End => ActiveEnd + Hold;
}
