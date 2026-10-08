namespace Kloarte.Model;

/// <summary>A step of a cue stack.</summary>
public interface ICueStep
{
    /// <summary>The cue (<see cref="ICue.Id"/>) this step plays.</summary>
    int CueId { get; }

    /// <summary>Cue number.</summary>
    float CueNumber { get; }

    /// <summary>Cue number as the console shows it, e.g. "12.1".</summary>
    string CueNumberText { get; }

    /// <summary>Step text.</summary>
    string Text { get; }
}
