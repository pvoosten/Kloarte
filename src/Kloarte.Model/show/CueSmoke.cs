namespace Kloarte.Model;

/// <summary>
/// The smoke (haze) a cue sets, as the blocking keeps it (MagicQ has no place for it in the show): from the GO of the cue
/// the haze builds or thins to <see cref="Amount"/> in <see cref="Time"/> seconds, like a hazer, and stays there until a
/// later cue sets another amount (tracking, as the lights do).
/// </summary>
public sealed class CueSmoke
{
    /// <summary>Haze amount in % of the densest haze (<see cref="ProjectLook.MaxHaze"/>), 0..100 (the Haze slider's scale).</summary>
    public double Amount { get; set; }

    /// <summary>Seconds the haze takes to reach <see cref="Amount"/>; null = <see cref="Blocking.DefaultSmokeTime"/>.</summary>
    public float? Time { get; set; }

    /// <summary>The seconds that count: <see cref="Time"/> or the default.</summary>
    public float Seconds => System.Math.Max(0f, Time ?? Blocking.DefaultSmokeTime);
}
