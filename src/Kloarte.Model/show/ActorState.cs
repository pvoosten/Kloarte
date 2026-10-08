using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// One state change / movement step. All fields optional: null = unchanged (tracking).
/// Delay and MoveTime describe how the step is played, not where the actor ends up.
/// </summary>
public sealed class ActorState
{
    /// <summary>On stage (null = unchanged).</summary>
    public bool? Visible { get; set; }
    /// <summary>ActorPose</summary>
    public string Pose { get; set; }
    /// <summary>x, y, z in stage coordinates</summary>
    public float[] Position { get; set; }
    /// <summary>degrees around Y; 0 = facing the audience</summary>
    public float? Facing { get; set; }
    /// <summary>seconds to walk to the new position; null = walking speed</summary>
    public float? MoveTime { get; set; }
    /// <summary>seconds to wait before this step starts (after the previous step / GO)</summary>
    public float? Delay { get; set; }

    /// <summary>A copy (position array cloned).</summary>
    public ActorState Copy() => new()
    {
        Visible = Visible,
        Pose = Pose,
        Position = (float[])Position?.Clone(),
        Facing = Facing,
        MoveTime = MoveTime,
        Delay = Delay,
    };

    /// <summary>Applies a change: every non-null field of `change` replaces this one's.</summary>
    public void Apply(ActorState change)
    {
        if (change.Visible != null) Visible = change.Visible;
        if (change.Pose != null) Pose = change.Pose;
        if (change.Position != null) Position = (float[])change.Position.Clone();
        if (change.Facing != null) Facing = change.Facing;
        if (change.MoveTime != null) MoveTime = change.MoveTime;
        if (change.Delay != null) Delay = change.Delay;
    }
}
