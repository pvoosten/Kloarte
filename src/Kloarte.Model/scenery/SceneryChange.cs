namespace Kloarte.Model;

/// <summary>
/// What a cue changes of one set piece (<see cref="Blocking.SceneryCues"/>), tracking like the lights: the piece goes
/// out or comes in at the cue's GO (no fade), a split curtain opens or closes to <see cref="Open"/> in
/// <see cref="Time"/> seconds. What a cue doesn't say stays as the cues before left it.
/// </summary>
public sealed class SceneryChange
{
    /// <summary>True = the piece comes in, false = it goes out, null = it stays as it is.</summary>
    public bool? Visible { get; set; }

    /// <summary>How far a split curtain opens: 0 = closed, 100 = fully open; null = it stays as it is.</summary>
    public float? Open { get; set; }

    /// <summary>Seconds the curtain takes to open or close; null = <see cref="Blocking.DefaultOpenTime"/>.</summary>
    public float? Time { get; set; }

    /// <summary>The seconds that count: <see cref="Time"/> or the default.</summary>
    public float Seconds => System.Math.Max(0f, Time ?? Blocking.DefaultOpenTime);

    /// <summary>True when the change changes nothing.</summary>
    public bool IsEmpty => Visible == null && Open == null;
}
