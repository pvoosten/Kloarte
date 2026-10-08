using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// Watches a project folder for changed files. Polled by the application (the window polls its state; the watcher
/// reports nothing on its own thread): a file is reported once its last change is older than a short quiet time, so
/// one save that writes a file several times is one change.
/// </summary>
public interface IProjectWatcher : System.IDisposable
{
    /// <summary>The files changed since the last call that have been quiet long enough (absolute paths; each once).</summary>
    IReadOnlyList<string> TakeSettled();
}
