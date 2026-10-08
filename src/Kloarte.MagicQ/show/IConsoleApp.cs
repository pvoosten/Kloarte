using System.Collections.Generic;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// The console program on this PC (MagicQ PC): start it with a show and compare what it
/// outputs with the cues.
/// </summary>
/// <typeparam name="TShow">The console's show document.</typeparam>
public interface IConsoleApp<in TShow> where TShow : class, IShowDocument
{
    /// <summary>The console's show folder.</summary>
    string ShowFolder { get; }

    /// <summary>
    /// Starts the console (closing a running one) with this show file and its DMX output (the show's patched universes)
    /// to this PC. Returns a status line; throws when the console can't be started.
    /// </summary>
    string Launch(string showPath, TShow show);

    /// <summary>True while the console program runs on this PC (starting it again closes it first).</summary>
    bool Running { get; }

    /// <summary>True when the file holds exactly this show (the application's own write, or saved without changes).</summary>
    bool Matches(TShow show, string path);

    /// <summary>
    /// Which of the expected looks (index 0 = before the first cue, then one per step) the received DMX matches best:
    /// the step (-1 before the first cue), the number of channels compared and the differing channels as text.
    /// </summary>
    (int Step, int Compared, int Differences, string Text) BestMatch(Dictionary<int, int> received,
        IReadOnlyList<Dictionary<int, int>> expected, TShow show);
}
