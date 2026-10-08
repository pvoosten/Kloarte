namespace Kloarte.Model;

/// <summary>
/// One movement step of an actor on the timeline (absolute times): wait (Delay) from the end of the previous step or
/// the GO, walk, turn to the final facing, then (when disappearing) wait for the light to fade out.
/// </summary>
public sealed class TimelineStep
{
    /// <summary>The actor.</summary>
    public string ActorId { get; init; } = "";

    /// <summary>Index of the cue segment.</summary>
    public int Segment { get; init; }

    /// <summary>Index of the step within the actor's steps of that cue.</summary>
    public int StepIndex { get; init; }

    /// <summary>When the wait before the step began (end of the previous step, or the GO).</summary>
    public double WaitStart { get; init; }

    /// <summary>When the step starts (after its delay).</summary>
    public double Start { get; init; }

    /// <summary>Walking time (0 = no walk).</summary>
    public double Walk { get; init; }

    /// <summary>Turning time after the walk.</summary>
    public double Turn { get; init; }

    /// <summary>When the step has finished.</summary>
    public double End { get; init; }

    /// <summary>State before the step.</summary>
    public ActorFrame From { get; init; }

    /// <summary>State after the step.</summary>
    public ActorFrame To { get; init; }

    /// <summary>Facing while walking (towards where the actor goes).</summary>
    public float WalkFacing { get; init; }

    /// <summary>The actor appears at the start of this step.</summary>
    public bool Appears => !From.Visible && To.Visible;

    /// <summary>The actor disappears at the end of this step.</summary>
    public bool Disappears => From.Visible && !To.Visible;
}
