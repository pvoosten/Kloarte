namespace Kloarte.Application;

/// <summary>
/// The state of the open show at one moment (an undo step's before or after): the show document as a file in the
/// history's temp folder (written with the console's own lossless save; shared by snapshots with the same show), and the
/// mechanics and blocking as JSON.
/// </summary>
internal sealed class EditSnapshot
{
    /// <summary>Fingerprint of the show document.</summary>
    public long ShowHash { get; init; }

    /// <summary>The show document as it was (a file in the history's temp folder).</summary>
    public string ShowFile { get; init; } = "";

    /// <summary>The mechanics as JSON.</summary>
    public string Mechanics { get; init; } = "";

    /// <summary>The blocking as JSON.</summary>
    public string Blocking { get; init; } = "";
}
