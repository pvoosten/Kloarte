using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// A show kept in the project's own files (<see cref="IProjectShow"/>: the native show, decided 14 Oct 2026): its files
/// are read at load and remembered, saved with the project (written when changed, never over an outside edit or a broken
/// file), watched like the other project files (an outside edit = one undo step), and the project's show file becomes the
/// project's <c>rig.yaml</c> at its first save (a project of before, still pointing at a console show, becomes native).
/// The console show is written by an export (<see cref="IShowStore{TShow}.WriteConsoleShow"/>).
/// </summary>
public sealed partial class ShowSession<TShow>
{
    /// <summary>The loaded show as a show of project files, or null (a console show).</summary>
    private IProjectShow ProjectShow => Model?.Show as IProjectShow;

    /// <summary>The status text of the last console show written (an export's report), or "".</summary>
    public string LastConsoleShowReport { get; private set; } = "";

    /// <summary>True when the open show file is the project's own show file (not a console show it is imported from).</summary>
    private bool ShowIsInProject(IProjectShow show) =>
        FolderOf() is { } folder && string.Equals(Path.GetFullPath(ShowFile), folder.PathOf(show.ProjectFiles[0]), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// After loading a show of project files: each file is applied with its problems and remembered when it reads (a broken
    /// one stays as it is on disk, never overwritten). A show imported from a console show (a project of before) is not
    /// in the files yet: only a cues file of before is remembered, so the first save may write the cue list over it.
    /// </summary>
    private void ApplyProjectShowAtLoad(List<FileProblem> problems)
    {
        var show = ProjectShow;
        var folder = FolderOf();
        if (show == null || folder == null)
            return;
        bool own = ShowIsInProject(show);
        foreach (string file in show.ProjectFiles)
        {
            string path = folder.PathOf(file);
            if (!File.Exists(path))
                continue;
            string text = File.ReadAllText(path);
            if (!own)
            {
                if (FileText.IsKloarte(text))
                    folder.Remember(path, text);
                continue;
            }
            var found = new List<FileProblem>();
            if (show.Apply(file, path, text, found))
                folder.Remember(path, text);
            problems.AddRange(found);
        }
    }

    /// <summary>The last check of the show files' unsaved state: the inputs it was made from and the answer.</summary>
    private (long Show, int Folder, bool Unsaved)? _projectShowChecked;

    /// <summary>True when a show file's text differs from what Kloarte last read or wrote (or the show is not in the files yet).</summary>
    private bool ProjectShowUnsaved()
    {
        var show = ProjectShow;
        var folder = FolderOf();
        long hash = Model.Show.ContentHash();
        if (_projectShowChecked is { } c && c.Show == hash && c.Folder == folder.Version)
            return c.Unsaved;
        bool unsaved = !ShowIsInProject(show) || show.ProjectFiles.Any(f => !folder.IsKnown(folder.PathOf(f), show.TextOf(f)));
        _projectShowChecked = (hash, folder.Version, unsaved);
        return unsaved;
    }

    /// <summary>
    /// Writes the show's files that changed (refused when changed outside Kloarte or broken). When all are written the
    /// project's show file is its own <c>rig.yaml</c>. Returns the writes.
    /// </summary>
    private List<FileWrite> SaveProjectShow()
    {
        var show = ProjectShow;
        var folder = FolderOf();
        folder.WriteSchemas();
        // A file of a project of before (the cues of its MagicQ show) is never the show's: written over, whatever happened to it.
        var writes = show.ProjectFiles.Select(f => folder.Write(folder.PathOf(f), show.TextOf(f), force: IsFileOfBefore(folder.PathOf(f)))).ToList();
        if (writes.All(w => w.Kind is FileWriteKind.Written or FileWriteKind.Unchanged) && !ShowIsInProject(show))
        {
            string path = folder.PathOf(show.ProjectFiles[0]);
            Project.ShowFile = path;
            _project = Project;
            ShowFile = path;
            _projectFor = path;
            Project.Save();
        }
        _savedShow = Model.Show.ContentHash();
        _showBaseline = null;
        _projectShowChecked = null;
        return writes;
    }

    /// <summary>True for a show file that holds what a project of before had there (the cues of its MagicQ show).</summary>
    private static bool IsFileOfBefore(string path) => File.Exists(path) && DesignShow.IsConsoleCues(File.ReadAllText(path));

    /// <summary>True when a file is one of the show's files.</summary>
    private bool IsProjectShowFile(ProjectFolder folder, string path) =>
        ProjectShow is { } show && show.ProjectFiles.Any(f => string.Equals(folder.PathOf(f), path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The change of a show file read from disk (see <see cref="ChangeFor"/>): null when it is what Kloarte has; a conflict
    /// when Kloarte has unsaved changes for it (and `takeFile` is false); an error (not applied); else applied as one undo step.
    /// </summary>
    private ProjectChange ProjectShowChange(ProjectFolder folder, string path, bool takeFile)
    {
        var show = ProjectShow;
        string file = show.ProjectFiles.First(f => string.Equals(folder.PathOf(f), path, StringComparison.OrdinalIgnoreCase));
        string text = File.ReadAllText(path);
        string generated = show.TextOf(file);
        if (FileText.Hash(text) == FileText.Hash(generated))
        {
            folder.Remember(path, text);
            return null;
        }
        if (!takeFile && !folder.IsKnown(path, generated))
            return Change(path, ProjectFileKind.Show, null, false, true, Array.Empty<FileProblem>());
        Checkpoint();   // Kloarte's own unsaved edit (if any) is an undo step of its own
        var problems = new List<FileProblem>();
        if (!show.Apply(file, path, text, problems))
            return Change(path, ProjectFileKind.Show, null, false, false, problems);
        folder.Remember(path, text);
        NameNextEdit($"Edited outside Kloarte: {file}");
        Checkpoint();
        return Change(path, ProjectFileKind.Show, null, true, false, problems);
    }

    /// <inheritdoc />
    public ShowImport ImportConsoleShow(string path) =>
        _store is DesignStore store && Model?.Show is DesignShow ? store.Exchange.Import(path, Catalog) : null;

    /// <inheritdoc />
    public string TakeImport(ShowImport import)
    {
        if (Model?.Show is not DesignShow show || import?.Show == null)
            return "No native show open";
        var changes = DesignDiff.Describe(show, import.Show);
        Checkpoint();   // an edit not recorded yet is an undo step of its own
        NameNextEdit($"Re-import {Path.GetFileName(import.Show.Rig.Source)}");
        DesignDiff.TakeInto(show, import.Show);
        Checkpoint();
        return changes.Count == 0 ? $"Re-imported {Path.GetFileName(import.Show.Rig.Source)}: no changes"
            : $"Re-imported {Path.GetFileName(import.Show.Rig.Source)}: {changes.Count} change{(changes.Count == 1 ? "" : "s")} (undo takes them back)";
    }

    /// <inheritdoc />
    public string LatestConsoleShow
    {
        get
        {
            if (Project == null || !Directory.Exists(_store.ShowFolder))
                return null;
            return Directory.EnumerateFiles(_store.ShowFolder, "*.shw")
                .Where(f => ShowVersions.IsVersionOf(f, Project.Name))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
    }

    /// <summary>The text Kloarte writes for a show file now (a "Save Kloarte's first"), or null.</summary>
    private string ProjectShowText(string path)
    {
        var folder = FolderOf();
        string file = ProjectShow?.ProjectFiles.FirstOrDefault(f => string.Equals(folder.PathOf(f), path, StringComparison.OrdinalIgnoreCase));
        return file == null ? null : ProjectShow.TextOf(file);
    }
}
