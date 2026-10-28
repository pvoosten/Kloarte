using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// The open show: its project (<see cref="KloarteProject"/>: name, folder, mechanics, blocking, venue), the console's
/// show file, the loaded <see cref="ShowModel{TShow}"/> and loading / saving. The project follows the show file: a
/// numbered save of the project's show (in the console or here) stays in the project, another show opens (or starts)
/// its own project (<see cref="ProjectLibrary.ForShow"/>). The scene is built from <see cref="Model"/> by the engine side.
/// </summary>
/// <typeparam name="TShow">The console's show document.</typeparam>
public sealed partial class ShowSession<TShow> : IShowFiles where TShow : class, IShowDocument
{
    /// <summary>The console's show files.</summary>
    private readonly IShowStore<TShow> _store;

    /// <summary>The projects.</summary>
    private readonly ProjectLibrary _projects;

    /// <summary>
    /// Creates a session that opens shows through `store`, keeps projects in `projects` and undo snapshots in
    /// `historyFolder` (default: <see cref="EditHistory{TShow}.DefaultFolder"/>).
    /// </summary>
    public ShowSession(IShowStore<TShow> store, ProjectLibrary projects, string historyFolder = null)
    {
        _store = store;
        _projects = projects;
        History = new EditHistory<TShow>(store, () => Model, historyFolder ?? EditHistory<TShow>.DefaultFolder());
    }

    /// <summary>Undo / redo of the open show's edits.</summary>
    public EditHistory<TShow> History { get; }

    /// <inheritdoc />
    public string Checkpoint() => History.Checkpoint();

    /// <inheritdoc />
    public void NameNextEdit(string name) => History.NameNextEdit(name);

    /// <inheritdoc />
    public string UndoName => History.UndoName;

    /// <inheritdoc />
    public string RedoName => History.RedoName;

    /// <inheritdoc />
    public string Undo() => History.Undo();

    /// <inheritdoc />
    public string Redo() => History.Redo();

    /// <inheritdoc />
    public void ResyncHistory() => History.Resync();

    /// <summary>Creates a session with the projects in <see cref="AppPaths.Projects"/>.</summary>
    public ShowSession(IShowStore<TShow> store) : this(store, new ProjectLibrary(AppPaths.File(AppPaths.Projects)))
    {
    }

    /// <summary>The console's show file (MagicQ: .shw; the .xhw next to it is read too).</summary>
    public string ShowFile { get; set; } = "";

    /// <summary>Backing field of <see cref="Project"/>.</summary>
    private KloarteProject _project;

    /// <summary>The show file <see cref="_project"/> was found for.</summary>
    private string _projectFor;

    /// <summary>
    /// The folder name of the show file last handed to the console, when it is in a subfolder of the show folder (the
    /// console's saves of it get that folder as a prefix, see <see cref="ShowVersions.IsVersionOf"/>); else null.
    /// </summary>
    private string _launchedFolder;

    /// <summary>
    /// The open project (null without a show file). Found for <see cref="ShowFile"/> when that changes, unless the new
    /// file is a version of the open project's show.
    /// </summary>
    public KloarteProject Project
    {
        get
        {
            if (string.IsNullOrEmpty(ShowFile))
                return _project = null;
            if (_project != null && string.Equals(_projectFor, ShowFile, System.StringComparison.OrdinalIgnoreCase))
                return _project;
            if (_project == null || !IsVersionOfProject(SourceFile))
                _project = _projects.ForShow(SourceFile);
            _project.ShowFile = Path.GetFullPath(SourceFile);
            _projectFor = ShowFile;
            return _project;
        }
    }

    /// <summary>True when a show file is a version of the open project's show (<see cref="ShowVersions.IsVersionOf"/>).</summary>
    private bool IsVersionOfProject(string file) =>
        _project != null && ShowVersions.IsVersionOf(file, _project.Name, _launchedFolder);

    /// <summary>The project's name ("" without a show).</summary>
    public string ProjectName => Project?.Name ?? "";

    /// <inheritdoc />
    public ProjectLook Look => Project?.Look;

    /// <summary>The project's folder ("" without a show).</summary>
    public string ProjectFolder => Project?.Folder ?? "";

    /// <summary>True when the project's document has been written (it exists on disk).</summary>
    public bool ProjectSaved => Project != null && Project.DocumentExists;

    /// <summary>Mechanics JSON (app path); empty = the project's mechanics.json.</summary>
    public string MechanicsFile { get; set; } = "";

    /// <summary>Blocking JSON (app path); empty = <see cref="ProjectFile"/>("blocking.json").</summary>
    public string BlockingFile { get; set; } = "";

    /// <summary>The loaded show, or null.</summary>
    public ShowModel<TShow> Model { get; private set; }

    /// <inheritdoc />
    IShowModel IShowFiles.Model => Model;

    /// <inheritdoc />
    IShowModel IShowFiles.Load() => Load();

    /// <summary>The fixture catalogue (<see cref="FixtureCatalog.DefaultPath"/>; loaded once).</summary>
    public FixtureCatalog Catalog => _catalog ??= File.Exists(AppPaths.File(FixtureCatalog.DefaultPath))
        ? FixtureCatalog.Load(AppPaths.File(FixtureCatalog.DefaultPath))
        : new FixtureCatalog();

    /// <summary>Backing field of <see cref="Catalog"/>.</summary>
    private FixtureCatalog _catalog;

    /// <summary>The console's colour table for the loaded show, or null.</summary>
    public IGelTable Gels { get; private set; }

    /// <summary>
    /// Patch, cue and palette changes on the loaded show. It works on the show document the model has now: an undo or redo
    /// that puts another document into the model gets an editor of its own (until 12 Oct 2026 the editor stayed on the
    /// replaced document, and an edit made after an undo was silently lost).
    /// </summary>
    public IShowEditor Editor
    {
        get
        {
            if (Model == null)
                return null;
            if (_editor == null || !ReferenceEquals(_editorShow, Model.Show))
            {
                _editor = _store.EditorFor(Model.Show);
                _editorShow = Model.Show;
            }
            return _editor;
        }
    }

    /// <summary>The editor made for <see cref="_editorShow"/>.</summary>
    private IShowEditor _editor;

    /// <summary>The show document <see cref="_editor"/> works on.</summary>
    private TShow _editorShow;

    /// <summary>The show file the open file stands for (see <see cref="IShowStore{TShow}.SourceOf"/>).</summary>
    public string SourceFile => _store.SourceOf(ShowFile);

    /// <summary>True when the open file is the console's working copy of <see cref="SourceFile"/>.</summary>
    public bool IsWorkingCopy => _store.IsWorkingCopy(ShowFile);

    /// <summary>The console's show folder (where shows are opened from).</summary>
    public string ShowFolder => _store.ShowFolder;

    /// <summary>The extension of a file that came with the loaded show (MagicQ: ".xhw"), or null.</summary>
    public string Companion => Model == null ? null : _store.CompanionOf(Model.Show);

    /// <summary>A file of the open project's folder, e.g. "mechanics.json" (absolute; "" without a show).</summary>
    public string ProjectFile(string name) => Project?.FileOf(name) ?? "";

    /// <inheritdoc />
    public string DefaultVenueFile { get; set; } = "";

    /// <summary>File system path of the mechanics JSON: <see cref="MechanicsFile"/>, else the project's; null without a show.</summary>
    public string MechanicsPath => !string.IsNullOrEmpty(MechanicsFile) ? AppPaths.File(MechanicsFile)
        : Project != null ? ProjectFile("mechanics.json") : null;

    /// <summary>File system path of the blocking JSON.</summary>
    public string BlockingPath => !string.IsNullOrEmpty(BlockingFile) ? AppPaths.File(BlockingFile)
        : Project != null ? ProjectFile("blocking.json") : null;

    /// <summary>True when <see cref="ShowFile"/> exists.</summary>
    public bool CanLoad => !string.IsNullOrEmpty(ShowFile) && File.Exists(ShowFile);

    /// <summary>
    /// Opens the show the console had loaded last; false when there is none (the show file stays). Either way the
    /// mechanics are the show's project mechanics.
    /// </summary>
    public bool OpenConsoleLastShow()
    {
        if (_store.LastShowFile is { } last)
        {
            Open(last);
            return true;
        }
        MechanicsFile = "";
        return false;
    }

    /// <summary>
    /// Selects another show file and its project's mechanics: a version of the open project's show (saved in the
    /// console) stays in the project and becomes its show; another show opens the project it belongs to, or a new one.
    /// </summary>
    public void Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("the file is not there (any more)", path);
        ShowFile = path;
        if (Project is { } project && File.Exists(project.DocumentPath))
            project.Save();   // remember the project's current show
        MechanicsFile = "";
    }

    /// <summary>Every project, the most recently saved first.</summary>
    public IReadOnlyList<KloarteProject> AllProjects() =>
        _projects.All().OrderByDescending(p => p.Saved).ToList();

    /// <summary>
    /// Opens a project: its current show file, mechanics and blocking (the caller loads). Throws when there is no such
    /// project or its show file is gone.
    /// </summary>
    public void OpenProject(string name)
    {
        var project = _projects.Find(name) ?? throw new InvalidOperationException($"There is no project '{name}'");
        if (!File.Exists(project.ShowFile))
            throw new FileNotFoundException($"The show of project {name} is missing: {project.ShowFile}", project.ShowFile);
        _project = project;
        ShowFile = project.ShowFile;
        _projectFor = ShowFile;
        _launchedFolder = null;
        MechanicsFile = "";
        BlockingFile = "";
    }

    /// <summary>
    /// Opens the project in a folder, anywhere (the caller loads): its project document (<c>&lt;name&gt;.kloarte.yaml</c>, or
    /// the JSON one of before), else a native show's <c>rig.yaml</c> there (the project is named after the folder). Throws
    /// when the folder holds neither, or the project's show is missing.
    /// </summary>
    public void OpenProjectFolder(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"There is no folder {folder}");
        string document = Directory.EnumerateFiles(folder, "*" + KloarteProject.Extension)
                              .Concat(Directory.EnumerateFiles(folder, "*" + KloarteProject.LegacyExtension))
                              .OrderByDescending(f => Path.GetFileName(f).StartsWith(Path.GetFileName(folder) + ".", StringComparison.OrdinalIgnoreCase))
                              .FirstOrDefault();
        string rig = Path.Combine(folder, DesignShow.RigFile);
        KloarteProject project;
        if (document != null)
        {
            project = KloarteProject.Load(document);   // a native project's show: the folder's rig.yaml
        }
        else if (File.Exists(rig))
            project = KloarteProject.Create(Path.GetDirectoryName(folder) ?? folder, Path.GetFileName(folder), rig);
        else
            throw new InvalidOperationException($"{folder} is not a Kloarte project (no {KloarteProject.Extension} and no {DesignShow.RigFile})");
        if (!File.Exists(project.ShowFile))
            throw new FileNotFoundException($"The show of project {project.Name} is missing: {project.ShowFile}", project.ShowFile);
        _project = project;
        ShowFile = project.ShowFile;
        _projectFor = ShowFile;
        _launchedFolder = null;
        MechanicsFile = "";
        BlockingFile = "";
    }

    /// <summary>
    /// A free name for a new project: the base name of `fromShow` (no version number), else "New project"; with " 2",
    /// " 3"... when a project has it already.
    /// </summary>
    public string ProposeProjectName(string fromShow)
    {
        string name = string.IsNullOrEmpty(fromShow) ? "New project" : ShowVersions.ProjectNameFor(fromShow);
        string candidate = name;
        for (int n = 2; _projects.Find(candidate) != null || Directory.Exists(Path.Combine(_projects.Root, candidate)); n++)
            candidate = $"{name} {n}";
        return candidate;
    }

    /// <summary>
    /// Makes a new project and opens it (the caller loads): its show is a new show (the house venue, no lamps) or a copy
    /// of `fromShow` (with its .xhw; the original is never written), as &lt;name&gt;.shw in the console's show folder
    /// (the next number when that name is taken). From a show of another project its mechanics and venue come along
    /// (same lamps), not its blocking. Throws on a name that can't be a folder or that a project has already.
    /// </summary>
    public void NewProject(string name, string fromShow = null)
    {
        name = name?.Trim() ?? "";
        if (!KloarteProject.IsValidName(name))
            throw new ArgumentException($"'{name}' can't be a project name (not empty, none of \\ / : * ? \" < > |)");
        if (ShowVersions.Split(name).Version != 1 || name.EndsWith("_001", StringComparison.Ordinal))
            throw new ArgumentException($"'{name}' ends like a MagicQ version number (_ and three digits): the project's " +
                                        "own numbered shows would look like another project's");
        if (_projects.Find(name) != null || Directory.Exists(Path.Combine(_projects.Root, name)))
            throw new InvalidOperationException($"There is a project '{name}' already");
        string show = _store.NewShowFile(Path.Combine(_projects.Root, name), name);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(show)) ?? _store.ShowFolder);
        if (fromShow == null)
            _store.CreateShow(show);
        else
            _store.CopyShow(fromShow, show);
        var project = KloarteProject.Create(_projects.Root, name, show);
        if (fromShow != null && _projects.ExistingFor(_store.SourceOf(fromShow)) is { } source)
            CopyProjectData(source, project);
        project.Save();
        OpenProject(name);
    }

    /// <summary>Loads <see cref="ShowFile"/> with its mechanics, blocking and colour table (see <see cref="CanLoad"/>).</summary>
    public ShowModel<TShow> Load()
    {
        if (Project != null && !Project.DocumentExists)
            _projects.TakeOverOldFiles(Project);
        var show = _store.Load(ShowFile);
        _showBaseline = null;
        var problems = new List<FileProblem>();
        Model = UsesYaml
            ? ShowModel<TShow>.LoadFiles(show, ShowFile, FolderOf(), Catalog, problems)
            : ShowModel<TShow>.Load(show, ShowFile, MechanicsPath, BlockingPath);
        LoadProblems = problems;
        EnsureWatcher();
        Gels = _store.LoadGels(ShowFile);
        _editor = null;   // made for the loaded show on first use (Editor)
        AcceptAsSaved();
        History.Reset();
        ApplyProjectShowAtLoad(problems);   // a native show's own files, with their problems
        problems.AddRange(_store.LoadNotes);
        _projectShowChecked = null;
        return Model;
    }

    /// <summary>Fingerprints of the show, mechanics and blocking as they were last loaded or saved.</summary>
    private long _savedShow, _savedMechanics, _savedBlocking;

    /// <summary>
    /// The fingerprint of the show as its file has it, when cues of <c>cues.yaml</c> were applied to it at load: they are not in
    /// the show file yet, so <see cref="AcceptAsSaved"/> (the scene accepts what it filled in) must not take them as saved.
    /// Null once the show is saved.
    /// </summary>
    private long? _showBaseline;

    /// <summary>
    /// Takes the loaded state as the saved one: after loading, and after the scene has filled in what the files leave
    /// open (e.g. default mechanics of lamps without saved ones), which is not an edit of the user's.
    /// </summary>
    public void AcceptAsSaved()
    {
        if (Model == null)
            return;
        _savedShow = _showBaseline ?? Model.Show.ContentHash();
        _savedMechanics = Model.MechanicsHash();
        _savedBlocking = Model.BlockingHash();
        History.Resync();
    }

    /// <summary>The parts with changes that are not saved: "show", "mechanics", "blocking" (empty = all saved).</summary>
    public IReadOnlyList<string> UnsavedChanges()
    {
        var parts = new List<string>();
        if (Model == null)
            return parts;
        if (ProjectShow != null ? FolderOf() != null && ProjectShowUnsaved() : Model.Show.ContentHash() != _savedShow)
            parts.Add("show");
        if (Project?.LookUnsaved == true)
            parts.Add("look");
        if (UsesYaml)
        {
            parts.AddRange(YamlUnsavedParts());
            return parts;
        }
        if (Model.MechanicsHash() != _savedMechanics)
            parts.Add("mechanics");
        if (Model.BlockingHash() != _savedBlocking)
            parts.Add("blocking");
        return parts;
    }

    /// <summary>
    /// The unsaved parts of a YAML project ("mechanics", "blocking"). The check runs every poll of the window, but the
    /// files are only compared again when the show, the mechanics, the blocking or the project's files changed (the
    /// text of 70 lamps takes about 17 ms).
    /// </summary>
    private List<string> YamlUnsavedParts()
    {
        var folder = FolderOf();
        long show = Model.Show.ContentHash(), mechanics = Model.MechanicsHash(), blocking = Model.BlockingHash();
        if (_yamlChecked is { } c && c.Show == show && c.Mechanics == mechanics && c.Blocking == blocking && c.Folder == folder.Version)
            return c.Parts.ToList();
        var parts = new List<string>();
        if (Model.HasUnsavedLamps(folder, Catalog))
            parts.Add("mechanics");
        if (BlockingUnsaved())
            parts.Add("blocking");
        _yamlChecked = (show, mechanics, blocking, folder.Version, parts);
        return parts.ToList();
    }

    /// <summary>The last check of a YAML project's unsaved parts, with the inputs it was made from (see <see cref="YamlUnsavedParts"/>).</summary>
    private (long Show, long Mechanics, long Blocking, int Folder, List<string> Parts)? _yamlChecked;

    /// <summary>True when the actors, the scenery or the steps differ from their files (as last read or written).</summary>
    private bool BlockingUnsaved()
    {
        var folder = FolderOf();
        if (!File.Exists(folder.PathOf(ShowModel<TShow>.ActorsPath)) && !File.Exists(folder.PathOf(ShowModel<TShow>.StepsPath))
            && !File.Exists(folder.PathOf(ShowModel<TShow>.SceneryPath))
            && Model.BlockingJson() == System.Text.Json.JsonSerializer.Serialize(new Blocking(), ShowModel<TShow>.Json))
            return false;   // no blocking yet, none in the files: nothing to save
        return !folder.IsKnown(folder.PathOf(ShowModel<TShow>.ActorsPath), Model.TextOfActors())
               || !folder.IsKnown(folder.PathOf(ShowModel<TShow>.StepsPath), Model.TextOfSteps())
               || (Model.Blocking.Scenery.Count > 0 || File.Exists(folder.PathOf(ShowModel<TShow>.SceneryPath)))
                  && !folder.IsKnown(folder.PathOf(ShowModel<TShow>.SceneryPath), Model.TextOfScenery());
    }

    /// <summary>
    /// Saves the whole project (user: one Save): mechanics, blocking, the project document and, when it changed, the
    /// show as the next numbered version (<see cref="SaveShowVersion"/>; never over an existing file). Returns a status
    /// message.
    /// </summary>
    public string SaveProject()
    {
        if (Model == null)
            return "No show loaded";
        var unsaved = UnsavedChanges();
        var done = new List<string>();
        var showWrites = new List<FileWrite>();
        if (unsaved.Contains("show") && ProjectShow != null)
        {
            showWrites = SaveProjectShow();
            done.Add("show");
        }
        else if (unsaved.Contains("show"))
            done.Add($"show as {Path.GetFileName(SaveShowVersion())}");
        if (unsaved.Contains("look"))
            done.Add("look");   // in the project document, written below
        if (!UsesExplicitJson && FolderOf() != null)
        {
            var writes = SaveYamlFiles(mechanics: true, blocking: true);
            done.Add("mechanics");
            done.Add("blocking");
            return $"Saved project {ProjectName}: {string.Join(", ", done)}" + RefusedSuffix(writes.Concat(showWrites));
        }
        if (MechanicsPath != null)
        {
            Model.SaveMechanics(MechanicsPath);
            _savedMechanics = Model.MechanicsHash();
            done.Add("mechanics");
        }
        if (BlockingPath != null)
        {
            Model.SaveBlocking(BlockingPath);
            _savedBlocking = Model.BlockingHash();
            done.Add("blocking");
        }
        Project?.Save();
        return $"Saved project {ProjectName}: {string.Join(", ", done)}" + RefusedSuffix(showWrites);
    }

    /// <summary>Writes the mechanics JSON (the caller updates <see cref="ShowModel{TShow}.Mechanics"/> first).</summary>
    public string SaveMechanics()
    {
        if (Model == null)
            return "No show loaded";
        if (!UsesExplicitJson && FolderOf() != null)
            return $"Saved the mechanics of {Model.Mechanics.Count} fixtures in project {ProjectName}" +
                   RefusedSuffix(SaveYamlFiles(mechanics: true, blocking: false));
        if (MechanicsPath == null)
            return "No MechanicsFile set";
        Model.SaveMechanics(MechanicsPath);
        _savedMechanics = Model.MechanicsHash();
        SaveProjectIfOwn(MechanicsPath);
        return $"Saved the mechanics of {Model.Mechanics.Count} fixtures {Where(MechanicsPath)}";
    }

    /// <summary>Writes the blocking JSON; returns a status message.</summary>
    public string SaveBlocking()
    {
        if (Model == null)
            return "No show loaded";
        if (!UsesExplicitJson && FolderOf() != null)
            return $"Saved the blocking ({Model.Blocking.Actors.Count} actors) in project {ProjectName}" +
                   RefusedSuffix(SaveYamlFiles(mechanics: false, blocking: true));
        if (BlockingPath == null)
            return "No blocking file";
        Model.SaveBlocking(BlockingPath);
        _savedBlocking = Model.BlockingHash();
        SaveProjectIfOwn(BlockingPath);
        return $"Saved the blocking ({Model.Blocking.Actors.Count} actors) {Where(BlockingPath)}";
    }

    /// <summary>Writes the show (incl. rigging, patch and palette changes) in the console's format.</summary>
    public string SaveShowAs(string path)
    {
        if (Model == null)
            return "No show loaded";
        return LastConsoleShowReport = _store.WriteConsoleShow(Model.Show, path);
    }

    /// <summary>
    /// Where the next save of the project's show goes: the next numbered file in the console's show folder
    /// (<see cref="ShowVersions.NextFile"/>; user: the show root, numbered like the console's own saves).
    /// </summary>
    public string NextShowFile => Project == null ? ""
        : ShowVersions.NextFile(_store.ShowFolder, Project.Name, ProjectShow != null ? null : SourceFile);

    /// <summary>
    /// Writes the show as it is here as the next numbered version of the project's show (<see cref="NextShowFile"/>)
    /// and makes that the project's show file. Nothing is overwritten. Returns the path.
    /// </summary>
    public string SaveShowVersion()
    {
        string path = NextShowFile;
        Directory.CreateDirectory(_store.ShowFolder);
        if (ProjectShow != null)
        {
            // A native show: an export (its project files stay the project's show).
            LastConsoleShowReport = _store.WriteConsoleShow(Model.Show, path);
            return path;
        }
        Model.SaveShow(path);
        _savedShow = Model.Show.ContentHash();
        _showBaseline = null;   // the cues of cues.yaml (if any were applied at load) are in the show file now
        ShowFile = path;
        Project.Save();
        return path;
    }

    /// <summary>
    /// The show file is about to be opened in the console: its saves of it may come back with the file's folder as a
    /// prefix (see <see cref="ShowVersions.IsVersionOf"/>).
    /// </summary>
    public void HandedToConsole(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        bool inRoot = string.Equals(Path.TrimEndingDirectorySeparator(folder),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_store.ShowFolder)), System.StringComparison.OrdinalIgnoreCase);
        _launchedFolder = inRoot ? null : Path.GetFileName(folder);
    }

    /// <summary>" - not saved, changed outside Kloarte: x.yaml" when a save refused files (else "").</summary>
    private static string RefusedSuffix(IEnumerable<FileWrite> writes)
    {
        string refused = RefusedOf(writes);
        return refused.Length == 0 ? "" : $". Not saved, changed outside Kloarte (see the prompt): {refused}";
    }

    /// <summary>"in project X" for a file of the project, else "to &lt;path&gt;" (status lines).</summary>
    private string Where(string path) => InProject(path) ? $"in project {Project.Name}" : $"to {path}";

    /// <summary>True when a file is in the project's folder.</summary>
    private bool InProject(string path) => Project != null && Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Project.Folder)) + Path.DirectorySeparatorChar,
        System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes the project document when a saved file is one of the project's (not a scene's own file).</summary>
    private void SaveProjectIfOwn(string path)
    {
        if (InProject(path))
            Project.Save();
    }

    /// <summary>
    /// Writes everything needed to render the show elsewhere (the movie process) into a folder: show.shw (+ .xhw),
    /// mechanics.json and blocking.json (the caller updates the mechanics first). Returns the .shw path.
    /// </summary>
    public string SaveForRender(string folder)
    {
        Directory.CreateDirectory(folder);
        string shw = Path.Combine(folder, ProjectShow != null ? "show.kshow.yaml" : "show.shw");
        Model.SaveShow(shw);
        Model.SaveMechanics(Path.Combine(folder, "mechanics.json"));
        Model.SaveBlocking(Path.Combine(folder, "blocking.json"));
        return shw;
    }
}
