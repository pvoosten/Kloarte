using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// A show document kept in the project's own files (the native show: <c>rig.yaml</c>, <c>palettes.yaml</c>,
/// <c>cues.yaml</c>) rather than in a console show file: the session writes, watches and applies them like the other
/// project files (written when changed, an outside edit one undo step, a broken file never applied nor overwritten).
/// </summary>
public interface IProjectShow
{
    /// <summary>The files that hold the show, relative to the project folder; the first is the show file.</summary>
    IReadOnlyList<string> ProjectFiles { get; }

    /// <summary>The text Kloarte writes for one of the files now.</summary>
    string TextOf(string file);

    /// <summary>
    /// Applies a file's text (at load, or an edit made outside Kloarte): false when it doesn't read (not applied; the
    /// errors are in `problems`); warnings are added and the rest applied. `path` names the file in the problems.
    /// </summary>
    bool Apply(string file, string path, string text, List<FileProblem> problems);
}
