using System.Collections.Generic;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The console program on this PC (MagicQ PC): open the show there (an export), compare what it outputs with the cues.
/// </summary>
public interface IConsoleCommands
{
    /// <summary>The console's show folder (where the open dialog starts).</summary>
    string ShowFolder { get; }

    /// <summary>
    /// Writes the show as it is here for the console (an export) and starts the console with it (closing a running one).
    /// Returns a status text; throws when the console can't be started.
    /// </summary>
    string OpenInConsole();

    /// <summary>True while the console program runs (<see cref="OpenInConsole"/> closes it first).</summary>
    bool ConsoleRunning { get; }

    /// <summary>
    /// Compares a received look with every step of a cue stack and describes the best match (the cue, and the channels
    /// that differ).
    /// </summary>
    string Compare(Dictionary<int, int> received, ICueStack stack);
}
