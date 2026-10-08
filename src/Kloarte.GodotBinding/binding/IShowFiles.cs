using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The open show and its files as the scene and the views use them: the project (name, folder), the console's show
/// file, mechanics and blocking files, loading and saving, the loaded model, the colour table and the fixture catalogue.
/// </summary>
public interface IShowFiles
{
    /// <summary>The console's show file.</summary>
    string ShowFile { get; set; }

    /// <summary>Mechanics JSON (app path, an explicit override); empty = the project's lamp files.</summary>
    string MechanicsFile { get; set; }

    /// <summary>Blocking JSON (app path, an explicit override); empty = the project's blocking files.</summary>
    string BlockingFile { get; set; }

    /// <summary>The open project's name ("" without a show); file dialogs use it.</summary>
    string ProjectName { get; }

    /// <summary>The look of the room (stray light, haze) of the open project; saved with it (null without a project).</summary>
    ProjectLook Look { get; }

    /// <summary>The open project's folder (absolute; "" without a show).</summary>
    string ProjectFolder { get; }

    /// <summary>True when the project's document has been written.</summary>
    bool ProjectSaved { get; }

    /// <summary>The show file the open file stands for (the console's working copy stands for its show).</summary>
    string SourceFile { get; }

    /// <summary>True when the open file is the console's working copy of <see cref="SourceFile"/>.</summary>
    bool IsWorkingCopy { get; }

    /// <summary>The console's show folder.</summary>
    string ShowFolder { get; }

    /// <summary>The extension of a file that came with the loaded show (MagicQ: ".xhw"), or null.</summary>
    string Companion { get; }

    /// <summary>A file of the open project's folder, e.g. "venue.json" (absolute; "" without a show).</summary>
    string ProjectFile(string name);

    /// <summary>
    /// The venue file the scene uses when the project has none (an app path, set by the scene that builds the show): Save
    /// writes a copy of it into the project as <c>venue.yaml</c>, so the project holds its own venue. Empty = none.
    /// </summary>
    string DefaultVenueFile { get; set; }

    /// <summary>True when <see cref="ShowFile"/> exists.</summary>
    bool CanLoad { get; }

    /// <summary>Opens the show the console had loaded last; false when there is none.</summary>
    bool OpenConsoleLastShow();

    /// <summary>
    /// Selects another show file: a version of the open project's show stays in the project, another show opens its own.
    /// </summary>
    void Open(string path);

    /// <summary>Every project, the most recently saved first.</summary>
    System.Collections.Generic.IReadOnlyList<KloarteProject> AllProjects();

    /// <summary>Opens a project (then <see cref="Load"/>); throws when it or its show file is missing.</summary>
    void OpenProject(string name);

    /// <summary>
    /// Opens the project in a folder, anywhere (then <see cref="Load"/>): its project document, else a native show's
    /// rig.yaml there. Throws when the folder is no project or its show is missing.
    /// </summary>
    void OpenProjectFolder(string folder);

    /// <summary>A free name for a new project (from `fromShow`'s name, or "New project").</summary>
    string ProposeProjectName(string fromShow);

    /// <summary>
    /// Makes a new project and opens it (then <see cref="Load"/>): a new show (house venue, no lamps) or a copy of
    /// `fromShow` (never written). Throws on a name that can't be used.
    /// </summary>
    void NewProject(string name, string fromShow = null);

    /// <summary>Loads <see cref="ShowFile"/> with its mechanics, blocking and colour table.</summary>
    IShowModel Load();

    /// <summary>The errors and warnings of the project's files found by the last load (each located).</summary>
    System.Collections.Generic.IReadOnlyList<FileProblem> LoadProblems { get; }

    /// <summary>
    /// Reads the project's files that changed outside Kloarte. A change without a conflict is applied now (one undo step
    /// each); a conflict (Kloarte has unsaved changes for that file) and a file with an error are returned, for the user
    /// (see <see cref="ResolveProjectChange"/>). The scene is rebuilt by the caller when one was applied.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<ProjectChange> PollProjectChanges();

    /// <summary>Settles a change with the user's choice (take the file's, keep Kloarte's, save Kloarte's first). Returns the status message.</summary>
    string ResolveProjectChange(ProjectChange change, ProjectChoice choice);

    /// <summary>The loaded show, or null.</summary>
    IShowModel Model { get; }

    /// <summary>The console's colour table for the loaded show, or null.</summary>
    IGelTable Gels { get; }

    /// <summary>The fixture catalogue.</summary>
    FixtureCatalog Catalog { get; }

    /// <summary>
    /// Records an undo entry when the show, mechanics or blocking changed since the last checkpoint (call when an edit
    /// has settled). Returns the entry's name, or null.
    /// </summary>
    string Checkpoint();

    /// <summary>Names the next undo entry ("Remove actor").</summary>
    void NameNextEdit(string name);

    /// <summary>Name of the entry Undo would undo, or null.</summary>
    string UndoName { get; }

    /// <summary>Name of the entry Redo would redo, or null.</summary>
    string RedoName { get; }

    /// <summary>
    /// Undoes the last edit in the model (the scene is rebuilt from the model next, then <see cref="ResyncHistory"/>).
    /// Returns its name, or null.
    /// </summary>
    string Undo();

    /// <summary>Redoes the last undone edit (see <see cref="Undo"/>). Returns its name, or null.</summary>
    string Redo();

    /// <summary>Takes the state after a rebuild as the current one (defaults the scene filled in are no edit).</summary>
    void ResyncHistory();

    /// <summary>Takes the state now as the saved one (after the scene is built from the files).</summary>
    void AcceptAsSaved();

    /// <summary>The parts with unsaved changes: "show", "mechanics", "blocking" (empty = all saved).</summary>
    System.Collections.Generic.IReadOnlyList<string> UnsavedChanges();

    /// <summary>
    /// Saves the whole project: mechanics, blocking and, when changed, the show as its next numbered version. Returns a
    /// status message.
    /// </summary>
    string SaveProject();

    /// <summary>Writes the mechanics JSON; returns a status message.</summary>
    string SaveMechanics();

    /// <summary>Writes the blocking JSON; returns a status message.</summary>
    string SaveBlocking();

    /// <summary>Writes the show in the console's format; returns a status message.</summary>
    string SaveShowAs(string path);

    /// <summary>Where the next numbered save of the project's show goes (the console's show folder).</summary>
    string NextShowFile { get; }

    /// <summary>Writes the show as the next numbered version and makes that the project's show. Returns the path.</summary>
    string SaveShowVersion();

    /// <summary>
    /// Imports a console show (read only) as a native show, to compare with the open one and take it
    /// (<see cref="TakeImport"/>); null when the open show is not a native one.
    /// </summary>
    ShowImport ImportConsoleShow(string path);

    /// <summary>
    /// Takes a re-imported console show into the open native show as one undo step (its rig, palettes and cues; what
    /// only Kloarte has stays). Returns a status text.
    /// </summary>
    string TakeImport(ShowImport import);

    /// <summary>The newest console show of the project in the console's show folder (a save in MagicQ, an export), or null.</summary>
    string LatestConsoleShow { get; }

    /// <summary>The status text of the last console show written (an export: what was written, what to check), or "".</summary>
    string LastConsoleShowReport { get; }

    /// <summary>Writes show, mechanics and blocking into a folder for the movie process. Returns the show path.</summary>
    string SaveForRender(string folder);
}
