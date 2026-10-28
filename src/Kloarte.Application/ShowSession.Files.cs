using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// The project's YAML files in the session (user decisions of 11 Oct 2026): the project's data is read and written as
/// YAML once the project has its folders; the folder is watched, and an edit made outside Kloarte is applied as one
/// undoable step ("Edited outside Kloarte: ..."), or, when Kloarte has unsaved changes for that same file, offered as a
/// choice (take the file's, keep Kloarte's, save Kloarte's first). A file with an error is not applied: the last good
/// state stays and the error is reported with its location. A native show's own files (<c>rig.yaml</c>,
/// <c>palettes.yaml</c>, <c>cues.yaml</c>) are among them (ShowSession.ProjectShow.cs).
/// </summary>
public sealed partial class ShowSession<TShow>
{
    /// <summary>Makes the watcher of a project folder (tests replace it).</summary>
    public Func<string, IProjectWatcher> WatcherFactory { get; set; } = folder => new ProjectFolderWatcher(folder);

    /// <summary>The project's folder as files (the known contents of its files); null without a project.</summary>
    private ProjectFolder _folder;

    /// <summary>The watcher of the project folder, or null.</summary>
    private IProjectWatcher _watcher;

    /// <summary>The folder <see cref="_watcher"/> watches.</summary>
    private string _watchedFolder;

    /// <inheritdoc />
    public IReadOnlyList<FileProblem> LoadProblems { get; private set; } = Array.Empty<FileProblem>();

    /// <summary>The project's venue file (YAML; the venue of a show that has its own).</summary>
    private const string VenueFile = "venue.yaml";

    /// <summary>The project's files as <see cref="ProjectFolder"/> (null without a project).</summary>
    private ProjectFolder FolderOf()
    {
        if (Project == null)
            return null;
        if (_folder == null || !string.Equals(_folder.Root, Path.GetFullPath(Project.Folder), StringComparison.OrdinalIgnoreCase))
            _folder = new ProjectFolder(Project.Folder);
        return _folder;
    }

    /// <summary>
    /// True when the project's data is YAML: the project has its lamp / blocking folders and no explicit JSON file is
    /// set (the render process and the test scenes name JSON files).
    /// </summary>
    private bool UsesYaml => !UsesExplicitJson && FolderOf() is { } folder
                             && (folder.HasYaml || File.Exists(folder.PathOf(DesignShow.RigFile)) || Model?.Show is IProjectShow);

    /// <summary>True when the mechanics or blocking JSON file was set explicitly (not the project's).</summary>
    private bool UsesExplicitJson => !string.IsNullOrEmpty(MechanicsFile) || !string.IsNullOrEmpty(BlockingFile);

    /// <summary>Starts watching the project folder (once it exists); a watch of another folder is replaced.</summary>
    private void EnsureWatcher()
    {
        var folder = FolderOf();
        if (folder == null || !Directory.Exists(folder.Root) || WatcherFactory == null)
            return;
        if (_watcher != null && string.Equals(_watchedFolder, folder.Root, StringComparison.OrdinalIgnoreCase))
            return;
        _watcher?.Dispose();
        _watcher = WatcherFactory(folder.Root);
        _watchedFolder = folder.Root;
    }

    /// <summary>
    /// Reads the project's files that changed outside Kloarte (settled, not Kloarte's own writes): a change without a
    /// conflict is applied now (one undo step); a conflict and a file with an error are returned for the user (see
    /// <see cref="ResolveProjectChange"/>). The caller rebuilds the scene when one is applied.
    /// </summary>
    public IReadOnlyList<ProjectChange> PollProjectChanges()
    {
        var changes = new List<ProjectChange>();
        if (Model == null || _watcher == null || !UsesYaml)
            return changes;
        var folder = FolderOf();
        foreach (string path in _watcher.TakeSettled())
        {
            if (!File.Exists(path) || !IsProjectFile(folder, path))
                continue;
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;   // still being written: the next change of the file reports it
            }
            if (folder.IsKnown(path, text))
                continue;   // Kloarte's own write, or seen already
            var change = ChangeFor(folder, path, takeFile: false);
            if (change != null)
                changes.Add(change);
        }
        return changes;
    }

    /// <summary>The file is one of the watched project files (a lamp file, the actors, the scenery or the steps).</summary>
    private bool IsProjectFile(ProjectFolder folder, string path) =>
        Path.GetExtension(path).Equals(".yaml", StringComparison.OrdinalIgnoreCase)
        && (IsProjectShowFile(folder, path) || string.Equals(Path.GetDirectoryName(path), folder.MechanicsDir, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, folder.PathOf(ShowModel<TShow>.ActorsPath), StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, folder.PathOf(ShowModel<TShow>.StepsPath), StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, folder.PathOf(ShowModel<TShow>.SceneryPath), StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, folder.PathOf(VenueFile), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Settles a change the user chose on: the file's content is taken (one undo step), Kloarte's is kept (the next save
    /// overwrites the file) or Kloarte's is saved over the file first. Returns the status message.
    /// </summary>
    public string ResolveProjectChange(ProjectChange change, ProjectChoice choice)
    {
        var folder = FolderOf() ?? throw new InvalidOperationException("No project is open");
        string name = change.Name;
        switch (choice)
        {
            case ProjectChoice.TakeFile:
                var taken = ChangeFor(folder, change.Path, takeFile: true);
                return taken is { HasError: false } ? $"Took the file's {name} (undo: Edited outside Kloarte: {name})"
                    : $"Not applied: {taken?.Problems.FirstOrDefault(p => p.IsError)}";
            case ProjectChoice.KeepKloarte:
                if (File.Exists(change.Path))
                    folder.Remember(change.Path, File.ReadAllText(change.Path));
                return $"Kept Kloarte's {name}: the file's change is ignored, the next save overwrites it";
            case ProjectChoice.SaveKloarteFirst:
                string text = GeneratedTextOf(change);
                if (text == null)
                    return $"Not saved: {name} is not in the show";
                var write = folder.Write(change.Path, text, force: true);
                return write.Kind == FileWriteKind.Conflict ? $"Not saved: {name} {write.Message}"
                    : $"Saved Kloarte's {name} over the file's change";
            default:
                throw new ArgumentOutOfRangeException(nameof(choice), choice, null);
        }
    }

    /// <summary>What Kloarte would write to a changed file now (null when it can't).</summary>
    private string GeneratedTextOf(ProjectChange change) => change.Kind switch
    {
        ProjectFileKind.Lamp when change.RigItem is { } id => Model.TextOfLamp(id, Catalog),
        ProjectFileKind.Actors => Model.TextOfActors(),
        ProjectFileKind.Steps => Model.TextOfSteps(),
        ProjectFileKind.Scenery => Model.TextOfScenery(),
        ProjectFileKind.Show => ProjectShowText(change.Path),
        _ => null,
    };

    /// <summary>
    /// The change of a project file read from disk: null when its content is what Kloarte has already (nothing to do);
    /// otherwise a conflict (`takeFile` false and Kloarte has unsaved changes for it), an error (not applied), or the file
    /// applied (one undo step; the file's content becomes the known one).
    /// </summary>
    private ProjectChange ChangeFor(ProjectFolder folder, string path, bool takeFile)
    {
        string dir = Path.GetDirectoryName(path) ?? "";
        if (IsProjectShowFile(folder, path))
            return ProjectShowChange(folder, path, takeFile);
        if (string.Equals(dir, folder.MechanicsDir, StringComparison.OrdinalIgnoreCase))
            return LampChange(folder, path, takeFile);
        if (string.Equals(path, folder.PathOf(ShowModel<TShow>.ActorsPath), StringComparison.OrdinalIgnoreCase))
            return BlockingChange(folder, path, ProjectFileKind.Actors, takeFile);
        if (string.Equals(path, folder.PathOf(ShowModel<TShow>.SceneryPath), StringComparison.OrdinalIgnoreCase))
            return BlockingChange(folder, path, ProjectFileKind.Scenery, takeFile);
        if (string.Equals(path, folder.PathOf(VenueFile), StringComparison.OrdinalIgnoreCase))
            return VenueChange(folder, path);
        return BlockingChange(folder, path, ProjectFileKind.Steps, takeFile);
    }

    /// <summary>
    /// The change of the project's venue file: it is not model data (no undo step); the scene is rebuilt from it when the
    /// file reads, and a file with an error is not applied (the scene keeps the venue it has).
    /// </summary>
    private ProjectChange VenueChange(ProjectFolder folder, string path)
    {
        string text = File.ReadAllText(path);
        var read = folder.Read<Venue>(path);
        if (read.HasError)
            return Change(path, ProjectFileKind.Venue, null, false, false, read.Problems);
        if (folder.IsKnown(path, text))
            return null;
        folder.Remember(path, text);
        return Change(path, ProjectFileKind.Venue, null, true, false, read.Problems);
    }

    /// <summary>The change of a lamp file (see <see cref="ChangeFor"/>).</summary>
    private ProjectChange LampChange(ProjectFolder folder, string path, bool takeFile)
    {
        string text = File.ReadAllText(path);
        var read = folder.Read<LampFile>(path);
        if (read.HasError)
        {
            if (ShowModel<TShow>.LampIdIn(text) is { } broken)
                Model.SetBrokenLamp(broken, true);
            return Change(path, ProjectFileKind.Lamp, ShowModel<TShow>.LampIdIn(text), false, false, read.Problems);
        }
        int id = read.Value.RigItem;
        if (!Model.IsLampInShow(id))
        {
            if (Model.Show is DesignShow design)
                design.ReservedItemIds.Add(id);   // a lamp added later must not take this file's id
            folder.Remember(path, text);
            return Change(path, ProjectFileKind.Lamp, id, false, false,
                new[] { new FileProblem(path, 0, 0, $"no lamp {id} in the show (not applied)", false) });
        }
        string generated = Model.TextOfLamp(id, Catalog);
        if (generated != null && FileText.Hash(text) == FileText.Hash(generated))
        {
            folder.Remember(path, text);
            return null;
        }
        if (!takeFile && generated != null && !folder.IsKnown(path, generated))
            return Change(path, ProjectFileKind.Lamp, id, false, true, read.Problems);
        Checkpoint();   // Kloarte's own unsaved edit (if any) is an undo step of its own
        Model.ApplyLampFile(read.Value, path, Catalog);
        folder.Remember(path, text);
        NameNextEdit($"Edited outside Kloarte: {Path.GetFileName(path)}");
        Checkpoint();
        return Change(path, ProjectFileKind.Lamp, id, true, false, read.Problems);
    }

    /// <summary>The change of the actors, the scenery or the steps file (see <see cref="ChangeFor"/>).</summary>
    private ProjectChange BlockingChange(ProjectFolder folder, string path, ProjectFileKind kind, bool takeFile) => kind switch
    {
        ProjectFileKind.Actors => BlockingChange<ActorsFile>(folder, path, kind, takeFile, Model.TextOfActors(), Model.ApplyActors),
        ProjectFileKind.Scenery => BlockingChange<SceneryFile>(folder, path, kind, takeFile, Model.TextOfScenery(), Model.ApplyScenery),
        _ => BlockingChange<StepsFile>(folder, path, kind, takeFile, Model.TextOfSteps(), Model.ApplySteps),
    };

    /// <summary>The change of one blocking file: read as `T`, applied by `apply` (one undo step); `generated` = Kloarte's text of it.</summary>
    private ProjectChange BlockingChange<T>(ProjectFolder folder, string path, ProjectFileKind kind, bool takeFile, string generated,
        Action<T, List<FileProblem>> apply) where T : class
    {
        string text = File.ReadAllText(path);
        var problems = new List<FileProblem>();
        var read = folder.Read<T>(path);
        if (read.HasError)
            return Change(path, kind, null, false, false, read.Problems);
        problems.AddRange(read.Problems);
        if (FileText.Hash(text) == FileText.Hash(generated))
        {
            folder.Remember(path, text);
            return null;
        }
        if (!takeFile && !folder.IsKnown(path, generated))
            return Change(path, kind, null, false, true, problems);
        Checkpoint();
        apply(read.Value, problems);
        folder.Remember(path, text);
        NameNextEdit($"Edited outside Kloarte: {Path.GetFileName(path)}");
        Checkpoint();
        return Change(path, kind, null, true, false, problems);
    }

    /// <summary>A change record.</summary>
    private static ProjectChange Change(string path, ProjectFileKind kind, int? rigItem, bool applied, bool conflict,
        IEnumerable<FileProblem> problems) =>
        new(path, kind, Path.GetFileName(path), rigItem, applied, conflict, problems.ToList());

    /// <summary>
    /// The project's YAML files: the lamps' mechanics and the blocking, written when changed (refused when changed
    /// outside Kloarte). Returns the writes; the project's venue is migrated to YAML.
    /// </summary>
    private List<FileWrite> SaveYamlFiles(bool mechanics, bool blocking)
    {
        var folder = FolderOf();
        var writes = Model.SaveFiles(folder, Catalog, mechanics, blocking);
        string json = folder.PathOf("venue.json"), yaml = folder.PathOf(VenueFile);
        // The project holds its own venue (user, 12 Oct 2026): the first save writes venue.yaml, from the project's old
        // venue.json (kept), else a copy of the venue the scene uses; later saves leave it (it is the user's to edit).
        string source = File.Exists(json) ? json
            : !string.IsNullOrEmpty(DefaultVenueFile) && File.Exists(AppPaths.File(DefaultVenueFile)) ? AppPaths.File(DefaultVenueFile) : null;
        if (source != null && !File.Exists(yaml))
        {
            Venue.Load(source).Save(yaml);
            folder.Remember(yaml, File.ReadAllText(yaml));   // Kloarte's own write is not an outside edit
        }
        Project?.Save();
        EnsureWatcher();
        return writes;
    }

    /// <summary>The names of the files the save refused (changed outside Kloarte, or not parsing).</summary>
    private static string RefusedOf(IEnumerable<FileWrite> writes) =>
        string.Join(", ", writes.Where(w => w.Kind == FileWriteKind.Conflict).Select(w => Path.GetFileName(w.Path)));

    /// <summary>
    /// Copies a project's lamp files, its own gobos, venue and look into a new project (the mechanics and the room come
    /// along, not the blocking).
    /// </summary>
    private static void CopyProjectData(KloarteProject source, KloarteProject target)
    {
        if (!Directory.Exists(source.Folder))
            return;
        target.Look = source.Look.Clone();
        foreach (string dir in new[] { Kloarte.Model.ProjectFolder.MechanicsDirectory, Kloarte.Model.ProjectFolder.GobosDirectory })
            CopyDirectory(Path.Combine(source.Folder, dir), Path.Combine(target.Folder, dir));
        foreach (string file in new[] { "mechanics.json", "venue.json", "venue.yaml" })
            if (File.Exists(source.FileOf(file)))
            {
                Directory.CreateDirectory(target.Folder);
                File.Copy(source.FileOf(file), target.FileOf(file));
            }
    }

    /// <summary>Copies the files of a folder (not its subfolders) into another, created.</summary>
    private static void CopyDirectory(string from, string to)
    {
        if (!Directory.Exists(from))
            return;
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
    }
}
