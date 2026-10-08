namespace Kloarte.Model;

/// <summary>
/// An actor's state or movement step in the blocking files (<see cref="ActorState"/>): every field optional, a missing
/// one doesn't change (tracking, as MagicQ cues).
/// </summary>
public sealed class ActorStateEntry
{
    /// <summary>Waits before this step starts (s).</summary>
    [FileField("Wait before this step starts, after GO or the previous step", Unit = "s", Min = 0, Max = 600)]
    public double? Delay { get; set; }

    /// <summary>Time to walk to the position (s).</summary>
    [FileField("Time to walk to this step's position; missing = walking speed", Unit = "s", Min = 0, Max = 600)]
    public double? MoveTime { get; set; }

    /// <summary>On stage.</summary>
    [FileField("On stage (false = off stage, not drawn)")]
    public bool? Visible { get; set; }

    /// <summary>Pose.</summary>
    [FileField("Pose", Values = "standing,seated,lying")]
    public string Pose { get; set; }

    /// <summary>Position x, y, z (stage coordinates, m).</summary>
    [FileField("Position [x, y, z] in stage coordinates (x to the audience's right, y up, z towards the audience)", Unit = "m")]
    public double[] Position { get; set; }

    /// <summary>Facing (degrees, 0 = the audience).</summary>
    [FileField("Facing, around the vertical: 0 = towards the audience", Unit = "°", Min = -360, Max = 360)]
    public double? Facing { get; set; }
}
