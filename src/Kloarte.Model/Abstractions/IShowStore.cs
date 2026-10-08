namespace Kloarte.Model;

/// <summary>
/// Show files: loading a show document and where its files are kept (the console binding's show files, or the native
/// show of a project). The application opens and saves shows only through this interface.
/// </summary>
/// <typeparam name="TShow">The console's show document.</typeparam>
public interface IShowStore<TShow> where TShow : class, IShowDocument
{
    /// <summary>Loads a show document (and the files that belong to it).</summary>
    TShow Load(string path);

    /// <summary>The colour table that goes with a show, or null.</summary>
    IGelTable LoadGels(string showPath);

    /// <summary>The editor of a loaded show's patch.</summary>
    IShowEditor EditorFor(TShow show);

    /// <summary>The show a file stands for (the console's working copy stands for the show it is a copy of).</summary>
    string SourceOf(string path);

    /// <summary>True when the file is the console's working copy of another show.</summary>
    bool IsWorkingCopy(string path);

    /// <summary>The show the console had loaded last, or null.</summary>
    string LastShowFile { get; }

    /// <summary>
    /// The console's show folder: where shows are opened from, and where the numbered saves of a project's show go.
    /// </summary>
    string ShowFolder { get; }

    /// <summary>Writes a new show: the house venue, no lamps, no cues.</summary>
    void CreateShow(string path);

    /// <summary>Copies a show file with the files that belong to it (MagicQ: the .xhw) to `path`.</summary>
    void CopyShow(string from, string path);

    /// <summary>
    /// Where the show of a new project goes: the console's show folder (a console show, numbered like its saves) or the
    /// project folder (a native show's <c>rig.yaml</c>).
    /// </summary>
    string NewShowFile(string projectFolder, string projectName);

    /// <summary>Writes the show as a console show file (a native show: an export). Returns a status text.</summary>
    string WriteConsoleShow(TShow show, string path);

    /// <summary>What the last <see cref="Load"/> has to say (an imported show's notes), as warnings.</summary>
    System.Collections.Generic.IReadOnlyList<FileProblem> LoadNotes { get; }

    /// <summary>The extension of a file that came with the show (MagicQ: ".xhw"), or null.</summary>
    string CompanionOf(TShow show);
}
