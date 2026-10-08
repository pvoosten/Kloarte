using System;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// An actor going on stage (OP) or off stage (AF) on the timeline: when its blocking makes it appear or disappear, at
/// which side (from where it appears / disappears) and in which cue. No Godot dependencies.
/// </summary>
/// <param name="ActorId">The actor.</param>
/// <param name="Time">When (timeline seconds): the start of the step it appears in, the end of the step it disappears in.</param>
/// <param name="On">True = on stage (OP), false = off stage (AF).</param>
/// <param name="Side">The side of the stage.</param>
/// <param name="Segment">Index of the cue segment.</param>
/// <param name="CueNumber">The cue's number as text.</param>
public sealed record ActorEntrance(string ActorId, double Time, bool On, StageSide Side, int Segment, string CueNumber)
{
    /// <summary>Half the width (m) of the band around the centre line where the side is upstage / downstage.</summary>
    internal const float CentreHalfWidth = 1f;

    /// <summary>"OP cour", "AF jardin", ...</summary>
    public string Text => $"{(On ? "OP" : "AF")} {SideText(Side)}";

    /// <summary>The side as said backstage ("cour", "jardin", "lointain", "face").</summary>
    public static string SideText(StageSide side) => side switch
    {
        StageSide.Cour => "cour",
        StageSide.Jardin => "jardin",
        StageSide.Lointain => "lointain",
        _ => "face",
    };

    /// <summary>The side of a stage position: by x, or near the centre line by z.</summary>
    public static StageSide SideOf(Vector3 position) =>
        Math.Abs(position.X) >= CentreHalfWidth ? position.X > 0 ? StageSide.Cour : StageSide.Jardin
        : position.Z < 0 ? StageSide.Lointain : StageSide.Face;
}
