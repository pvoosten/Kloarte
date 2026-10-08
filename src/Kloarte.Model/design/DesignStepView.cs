namespace Kloarte.Model;

/// <summary>A cue of a native show as a step of its cue stack.</summary>
/// <param name="CueId">The cue's id.</param>
/// <param name="CueNumber">The cue number.</param>
/// <param name="CueNumberText">The cue number as text.</param>
/// <param name="Text">The cue's text.</param>
internal sealed record DesignStepView(int CueId, float CueNumber, string CueNumberText, string Text) : ICueStep;
