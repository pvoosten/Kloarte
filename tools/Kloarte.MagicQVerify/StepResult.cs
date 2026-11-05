namespace Kloarte.MagicQVerify;

/// <summary>The outcome of one Go: a cue, or a cue and the steps that follow it on their own.</summary>
/// <param name="Cue">Cue numbers and texts.</param>
/// <param name="Compared">Channels compared.</param>
/// <param name="Differences">Channels that differ.</param>
/// <param name="DifferenceText">The first differences.</param>
/// <param name="ExpectedFade">The expected fade (delays included; the sum over a chain), in seconds.</param>
/// <param name="MeasuredFade">The measured fade, in seconds.</param>
/// <param name="FadeOk">True when the measured fade is the expected one.</param>
internal sealed record StepResult(string Cue, int Compared, int Differences, string DifferenceText,
    float ExpectedFade, double MeasuredFade, bool FadeOk)
{
    /// <summary>True when the cue passed.</summary>
    public bool Passed => Differences == 0 && FadeOk;
}
