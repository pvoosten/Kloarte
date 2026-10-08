using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The show files of native projects (decided 14 Oct 2026): a project's show is its <c>rig.yaml</c>,
/// <c>palettes.yaml</c> and <c>cues.yaml</c> (<see cref="DesignShow"/>, read and written by the session as project
/// files); a console show (<see cref="IShowExchange"/>) is imported when a project still points at one (a project of
/// before: it becomes native at its next save) or when a project is made from one; a native show in one file is an undo
/// snapshot or the movie process's copy.
/// </summary>
public sealed class DesignStore : IShowStore<DesignShow>
{
    /// <summary>The console's show files.</summary>
    private readonly IShowExchange _exchange;

    /// <summary>The fixture catalogue (for the GDTF of imported moving heads).</summary>
    private readonly Func<FixtureCatalog> _catalog;

    /// <summary>A store on the console's show files, with the catalogue the session has.</summary>
    public DesignStore(IShowExchange exchange, Func<FixtureCatalog> catalog)
    {
        _exchange = exchange;
        _catalog = catalog;
    }

    /// <summary>The console's show files.</summary>
    public IShowExchange Exchange => _exchange;

    /// <inheritdoc />
    public IReadOnlyList<FileProblem> LoadNotes { get; private set; } = Array.Empty<FileProblem>();

    /// <summary>
    /// A native project's show (its <c>rig.yaml</c>: the files that read; the session applies them again with their
    /// problems), a native show in one file (<see cref="DesignShow.IsShowFile"/>), else a console show, imported (MagicQ:
    /// .shw, or its autosave .sbk).
    /// </summary>
    public DesignShow Load(string path)
    {
        LoadNotes = Array.Empty<FileProblem>();
        bool project = string.Equals(Path.GetFileName(path), DesignShow.RigFile, StringComparison.OrdinalIgnoreCase);
        if (!project && !DesignShow.IsShowFile(path))   // a console show (.shw, its autosave .sbk, ...)
        {
            var import = _exchange.Import(path, _catalog());
            LoadNotes = import.Notes.Select(n => new FileProblem(path, 0, 0, "import: " + n, false)).ToList();
            return import.Show;
        }
        if (project)
            return DesignShow.LoadProject(path, new List<FileProblem>());
        return DesignShow.Load(path);
    }

    /// <inheritdoc />
    public IGelTable LoadGels(string showPath) => _exchange.LoadGels(_exchange.IsConsoleShow(showPath) ? showPath : null);

    /// <inheritdoc />
    public IShowEditor EditorFor(DesignShow show) => new DesignShowEditor(show);

    /// <inheritdoc />
    public string SourceOf(string path) => path;

    /// <inheritdoc />
    public bool IsWorkingCopy(string path) => false;

    /// <summary>None: a native project opens its own show.</summary>
    public string LastShowFile => null;

    /// <inheritdoc />
    public string ShowFolder => _exchange.ShowFolder;

    /// <summary>The project folder's <c>rig.yaml</c>.</summary>
    public string NewShowFile(string projectFolder, string projectName) => Path.Combine(projectFolder, DesignShow.RigFile);

    /// <summary>Writes a new native show (the console's new show imported: the house venue, no lamps) into the folder of `path`.</summary>
    public void CreateShow(string path)
    {
        var show = _exchange.Import(_exchange.NewShow, _catalog()).Show;
        show.Rig.Source = null;
        Write(show, path);
    }

    /// <summary>Writes a console show (imported; it is the source of later exports) as a native show into the folder of `path`.</summary>
    public void CopyShow(string from, string path) => Write(_exchange.Import(from, _catalog()).Show, path);

    /// <summary>Writes a native show's project files into the folder of `path`.</summary>
    private static void Write(DesignShow show, string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        Directory.CreateDirectory(folder);
        foreach (string file in show.ProjectFiles)
            File.WriteAllText(Path.Combine(folder, file), show.TextOf(file));
    }

    /// <summary>The last export (null before the first).</summary>
    public ShowExport LastExport { get; private set; }

    /// <summary>Exports the show as a console show; the status says what was written and what to check in the console.</summary>
    public string WriteConsoleShow(DesignShow show, string path)
    {
        LastExport = _exchange.Export(show, path, _catalog());
        return Describe(LastExport);
    }

    /// <summary>A status text of an export: where, how much, the warnings, what to check in the console program.</summary>
    public static string Describe(ShowExport export)
    {
        var lines = new List<string>
        {
            $"Exported {Path.GetFileName(export.Path)}: " +
            (export.Written.Count == 0 ? "the same as the show it came from" : $"{export.Written.Count} change{(export.Written.Count == 1 ? "" : "s")}"),
        };
        lines.AddRange(export.Warnings.Select(w => "Warning: " + w));
        if (export.CheckInConsole.Count > 0)
            lines.Add("Check in MagicQ PC: " + string.Join("; ", export.CheckInConsole));
        return string.Join("\n", lines);
    }

    /// <inheritdoc />
    public string CompanionOf(DesignShow show) => null;
}
