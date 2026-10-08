using System.IO;
using System.Linq;
using System.Text.Json;
using Kloarte.Application;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// Projects (<see cref="ProjectLibrary"/>, <see cref="ShowSession{TShow}"/>): project data stays with the project while
/// the show file's name changes with every numbered save, here and in MagicQ (user: keep file names consistent).
/// </summary>
public sealed class ProjectTests : TestBase
{
    /// <summary>A small MagicQ show (venue objects only).</summary>
    private const string SmallShow = "Theaterzaal_Brouckere.shw";

    /// <summary>The test's MagicQ show folder.</summary>
    private readonly string _shows;

    /// <summary>The test's projects folder.</summary>
    private readonly string _projects;

    /// <summary>Fresh show and projects folders for every test.</summary>
    public ProjectTests() : base("app_projects")
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, true);
        _shows = Path.Combine(_out, "show");
        _projects = Path.Combine(_out, "projects");
        Directory.CreateDirectory(_shows);
        Directory.CreateDirectory(_projects);
    }

    /// <summary>Copies a show of the MagicQ folder (default: the small one) into the test's show folder as `name`.</summary>
    private string CopyShow(string name, string folder = null, string source = SmallShow)
    {
        string dir = folder == null ? _shows : Path.Combine(_shows, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.Copy(Show(source), path, true);
        return path;
    }

    /// <summary>A session on the test's folders.</summary>
    private ShowSession<MagicQShow> Session() =>
        new(new TestShowStore(_shows), new ProjectLibrary(_projects));

    /// <summary>
    /// A show opens its project, named after its base name; saved project data goes to the project folder, and a new
    /// session finds the project again.
    /// </summary>
    [Fact]
    public void ShowOpensItsProject()
    {
        string show = CopyShow("Kruimels_001.shw");
        var session = Session();
        session.Open(show);
        session.Load();
        Assert.Equal("Kruimels", session.ProjectName);
        Assert.Equal(Path.Combine(_projects, "Kruimels"), session.ProjectFolder);
        Assert.Equal(Path.Combine(_projects, "Kruimels", "mechanics.json"), session.MechanicsPath);
        Assert.False(session.ProjectSaved);
        session.SaveMechanics();
        session.SaveBlocking();
        Assert.True(session.ProjectSaved);
        Assert.True(File.Exists(Path.Combine(_projects, "Kruimels", "blocking", "actors.yaml")));
        Assert.True(File.Exists(Path.Combine(_projects, "Kruimels", "blocking", "steps.yaml")));
        var again = Session();
        again.Open(show);
        Assert.Equal("Kruimels", again.ProjectName);
        Assert.Equal(Path.GetFullPath(show), KloarteProject.Load(again.Project.DocumentPath).ShowFile);
    }

    /// <summary>
    /// Project paths are clean file system paths, also when the projects folder is given with forward slashes (Godot's
    /// paths): explored 11 Oct 2026, the panel showed "C:/.../Projects\Kruimels".
    /// </summary>
    [Fact]
    public void ProjectPathsAreNormalised()
    {
        var library = new ProjectLibrary(_projects.Replace('\\', '/'));
        var project = library.ForShow(CopyShow("Kruimels_001.shw"));
        Assert.Equal(Path.Combine(_projects, "Kruimels"), project.Folder);
        Assert.DoesNotContain('/', project.FileOf("mechanics.json"));
    }

    /// <summary>
    /// Kloarte's save of the show is the next number in the show root and becomes the project's show; MagicQ's save of
    /// that (the number after it) stays in the project: mechanics, blocking and venue never move. The drift seen
    /// 9-10 Oct 2026 (Kruimels_001 -&gt; Kloarte_Kruimels_002 -&gt; ... each with new, empty project files) can't happen.
    /// </summary>
    [Fact]
    public void NumberedSavesStayInTheProject()
    {
        var session = Session();
        session.Open(CopyShow("Kruimels_001.shw"));
        session.Load();
        session.SaveMechanics();
        string mechanics = session.MechanicsPath;

        string ours = session.SaveShowVersion();
        Assert.Equal(Path.Combine(_shows, "Kruimels_002.shw"), ours);
        Assert.True(File.Exists(Path.ChangeExtension(ours, ".xhw")) || session.Model.Show.Xhw == null);
        Assert.Equal(ours, KloarteProject.Load(session.Project.DocumentPath).ShowFile);

        // MagicQ saves it as the next number; the watcher hands it to Open.
        string magicq = CopyShow("Kruimels_003.shw");
        session.Open(magicq);
        session.Load();
        Assert.Equal("Kruimels", session.ProjectName);
        Assert.Equal(mechanics, session.MechanicsPath);
        Assert.Equal(magicq, KloarteProject.Load(session.Project.DocumentPath).ShowFile);
        Assert.Equal(Path.Combine(_shows, "Kruimels_004.shw"), session.NextShowFile);

        // After a restart the latest version opens the same project.
        var restarted = Session();
        restarted.Open(magicq);
        Assert.Equal(mechanics, restarted.MechanicsPath);
    }

    /// <summary>
    /// A show from a subfolder that is handed to MagicQ as it is comes back from MagicQ's save with the folder as a
    /// prefix (Risquons-Tout\RT2025_031 -&gt; Risquons-Tout_RT2025_032): still the same project.
    /// </summary>
    [Fact]
    public void ConsoleSaveFromSubfolderStaysInTheProject()
    {
        var session = Session();
        string original = CopyShow("RT2025_031.shw", "Risquons-Tout", source: "RT2025_031.shw");
        session.Open(original);
        session.Load();
        Assert.Equal("RT2025", session.ProjectName);
        var link = new ConsoleLink<MagicQShow>(session, null, new TestConsole(_shows));
        link.OpenInConsole();
        Assert.Equal(original, session.ShowFile);   // unchanged: handed over as it is, nothing written

        string magicq = CopyShow("Risquons-Tout_RT2025_032.shw");
        session.Open(magicq);
        Assert.Equal("RT2025", session.ProjectName);

        // Another show is another project.
        session.Open(CopyShow("la comedie.shw"));
        Assert.Equal("la comedie", session.ProjectName);
    }

    /// <summary>
    /// "Open in MagicQ PC": the show file as it is when it holds the show as Kloarte has it, else the next numbered
    /// version (never a write over an existing file).
    /// </summary>
    [Fact]
    public void OpenInConsoleWritesANewVersionOnlyWhenChanged()
    {
        var session = Session();
        string show = CopyShow("Kruimels_001.shw");
        session.Open(show);
        session.Load();
        var console = new TestConsole(_shows);
        var link = new ConsoleLink<MagicQShow>(session, null, console);
        link.OpenInConsole();
        Assert.Equal(show, console.Launched[^1]);

        // The file on disk differs from the show here (e.g. edited after loading): a new version is written.
        File.AppendAllText(show, "\\ changed\r\n");
        link.OpenInConsole();
        Assert.Equal(Path.Combine(_shows, "Kruimels_002.shw"), console.Launched[^1]);
        Assert.Equal(console.Launched[^1], session.ShowFile);
        Assert.Equal(new FileInfo(Show(SmallShow)).Length, new FileInfo(console.Launched[^1]).Length);
    }

    /// <summary>
    /// "New project": a new show (the house venue, no lamps) as &lt;name&gt;.shw in the show root, the project written
    /// and open; names that can't be a folder or that a project has are refused.
    /// </summary>
    [Fact]
    public void NewBlankProject()
    {
        var session = Session();
        session.NewProject("Kruimels");
        var model = session.Load();
        Assert.Equal(Path.Combine(_shows, "Kruimels.shw"), session.ShowFile);
        Assert.True(session.ProjectSaved);
        Assert.Empty(model.Show.Patch);
        Assert.NotEmpty(((IShowDocument)model.Show).RigItems);   // the house venue's objects
        Assert.Throws<System.InvalidOperationException>(() => session.NewProject("kruimels"));
        Assert.Throws<System.ArgumentException>(() => session.NewProject("a/b"));
        Assert.Throws<System.ArgumentException>(() => session.NewProject(" "));
        Assert.Equal("Kruimels", session.ProjectName);

        // A second project; then back to the first by name.
        session.NewProject("Second");
        Assert.Equal("Second", session.ProjectName);
        session.OpenProject("Kruimels");
        Assert.Equal(Path.Combine(_shows, "Kruimels.shw"), session.ShowFile);
        Assert.Equal(Path.Combine(_projects, "Kruimels", "mechanics.json"), session.MechanicsPath);
        Assert.Equal(2, session.AllProjects().Count);
    }

    /// <summary>
    /// "New project from show": the show is copied (with its .xhw) under the project's name, the original never
    /// changes; from another project's show the mechanics, its own gobos and the look come along, the blocking doesn't.
    /// </summary>
    [Fact]
    public void NewProjectFromShow()
    {
        var session = Session();
        string original = CopyShow("RT2025_031.shw", "Risquons-Tout", source: "RT2025_031.shw");
        File.WriteAllText(Path.ChangeExtension(original, ".xhw"), "xhw");
        session.Open(original);
        var source = session.Load();
        int lamp = ((IShowDocument)source.Show).RigItems.First(v => v.IsFixture).Id;
        source.Mechanics[lamp] = new FixtureMechanics { Model = "c103" };
        session.SaveMechanics();
        session.SaveBlocking();
        session.Look.Haze = 0.05f;
        session.SaveProject();
        Directory.CreateDirectory(Path.Combine(session.ProjectFolder, ProjectFolder.GobosDirectory));
        File.WriteAllText(Path.Combine(session.ProjectFolder, ProjectFolder.GobosDirectory, "star.png"), "png");
        byte[] before = File.ReadAllBytes(original);

        session.NewProject("Kruimels 2027", original);
        session.Load();
        Assert.Equal("Kruimels 2027", session.ProjectName);
        Assert.Equal(Path.Combine(_shows, "Kruimels_2027.shw"), session.ShowFile);
        Assert.True(File.Exists(Path.Combine(_shows, "Kruimels_2027.xhw")));
        Assert.Equal(before, File.ReadAllBytes(original));
        Assert.True(Directory.Exists(Path.Combine(session.ProjectFolder, "mechanics")));
        Assert.False(Directory.Exists(Path.Combine(session.ProjectFolder, "blocking")));
        Assert.True(File.Exists(Path.Combine(session.ProjectFolder, ProjectFolder.GobosDirectory, "star.png")));
        Assert.Equal(0.05f, session.Look.Haze, 5);
    }

    /// <summary>
    /// The name proposed for a new project from a show is free (explored 11 Oct 2026: from RT2025_044 it proposed
    /// "RT2025", the existing project of that show, and was refused); a name ending in MagicQ's version number would
    /// make the project's own numbered saves ambiguous and is refused.
    /// </summary>
    [Fact]
    public void ProposedNamesAreFree()
    {
        var session = Session();
        string show = CopyShow("RT2025_044.shw", "Risquons-Tout");
        Assert.Equal("RT2025", session.ProposeProjectName(show));
        session.NewProject("RT2025");
        Assert.Equal("RT2025 2", session.ProposeProjectName(show));
        session.NewProject("RT2025 2", show);
        Assert.Equal("RT2025 3", session.ProposeProjectName(show));
        Assert.Equal("New project", session.ProposeProjectName(null));
        Assert.Throws<System.ArgumentException>(() => session.NewProject("RT2025_044"));
    }

    /// <summary>
    /// Unsaved changes per part, and one Save for the project (UI review 2, 3): mechanics and blocking always, the show
    /// only when it changed, as the next numbered version.
    /// </summary>
    [Fact]
    public void SaveProjectSavesWhatChanged()
    {
        var session = Session();
        session.Open(CopyShow("Kruimels_001.shw", source: "RT2025_031.shw"));
        var model = session.Load();
        Assert.Empty(session.UnsavedChanges());

        model.Blocking.Actors.Add(new ActorDef { Id = "a1", Name = "Anna" });
        Assert.Equal(new[] { "blocking" }, session.UnsavedChanges());
        string status = session.SaveProject();
        Assert.Empty(session.UnsavedChanges());
        Assert.Equal(Path.Combine(_shows, "Kruimels_001.shw"), session.ShowFile);   // show unchanged: not written
        Assert.DoesNotContain("show as", status);
        Assert.True(session.ProjectSaved);

        var cue = model.Show.CueStacks[0];
        model.Show.InsertCue(cue, model.Show.CueStacks[0].Steps[^1].CueNumber + 1, "end", blackout: true);
        int lamp = ((IShowDocument)model.Show).RigItems.First(v => v.IsFixture).Id;
        model.Mechanics[lamp] = new FixtureMechanics { Model = "c103" };
        Assert.Equal(new[] { "show", "mechanics" }, session.UnsavedChanges());
        status = session.SaveProject();
        Assert.Empty(session.UnsavedChanges());
        Assert.Equal(Path.Combine(_shows, "Kruimels_002.shw"), session.ShowFile);
        Assert.Contains("show as Kruimels_002.shw", status);

        // Reopened: what was saved comes back, nothing is unsaved.
        var again = Session();
        again.OpenProject("Kruimels");
        var reloaded = again.Load();
        Assert.Empty(again.UnsavedChanges());
        Assert.Equal("Anna", reloaded.Blocking.Find("a1")?.Name);
        Assert.Equal("c103", reloaded.Mechanics[lamp].Model);
    }

    /// <summary>
    /// A show file that isn't there (a MagicQ save that was moved away before Kloarte read it) is refused and changes
    /// nothing: explored 11 Oct 2026, it became the project's show and the project could not be opened any more.
    /// </summary>
    [Fact]
    public void MissingShowIsRefused()
    {
        var session = Session();
        string show = CopyShow("Kruimels_001.shw");
        session.Open(show);
        session.Load();
        session.SaveProject();
        Assert.Throws<FileNotFoundException>(() => session.Open(Path.Combine(_shows, "Kruimels_009.shw")));
        Assert.Equal(show, session.ShowFile);
        Assert.Equal(Path.GetFullPath(show), KloarteProject.Load(session.Project.DocumentPath).ShowFile);
    }

    /// <summary>
    /// Without a mechanics file of its own the scene uses the project's (like the blocking).
    /// </summary>
    [Fact]
    public void MechanicsDefaultToTheProjects()
    {
        var session = Session();
        session.ShowFile = CopyShow("Kruimels_001.shw");
        Assert.Equal(Path.Combine(_projects, "Kruimels", "mechanics.json"), session.MechanicsPath);
    }

    /// <summary>
    /// The flat files from before projects are taken over into the project folder: of the drifted names the newest one
    /// with content (the blocking with actors, not the empty one written after the drift).
    /// </summary>
    [Fact]
    public void OldFlatFilesAreTakenOver()
    {
        void Write(string name, string json, int minutesAgo)
        {
            string path = Path.Combine(_projects, name);
            File.WriteAllText(path, json);
            File.SetLastWriteTimeUtc(path, System.DateTime.UtcNow.AddMinutes(-minutesAgo));
        }
        Write("Kruimels_001.blocking.json", "{\"actors\":[{\"id\":\"a\"}]}", 30);
        Write("Kloarte_Kruimels_003.blocking.json", "{\"actors\":[]}", 10);
        Write("Kruimels_001.mechanics.json", "{\"version\":2,\"fixtures\":{\"1\":{}}}", 30);
        Write("Kloarte_Kruimels_003.mechanics.json", "{\"version\":2,\"fixtures\":{\"1\":{},\"2\":{}}}", 10);
        Write("la comedie.mechanics.json", "{\"version\":2,\"fixtures\":{\"9\":{}}}", 5);

        var session = Session();
        session.Open(CopyShow("Kloarte_Kloarte_Kruimels_006.shw"));
        session.Load();
        Assert.Equal("Kruimels", session.ProjectName);
        using var blocking = JsonDocument.Parse(File.ReadAllText(session.BlockingPath));
        Assert.Equal(1, blocking.RootElement.GetProperty("actors").GetArrayLength());
        using var mechanics = JsonDocument.Parse(File.ReadAllText(session.MechanicsPath));
        Assert.True(mechanics.RootElement.GetProperty("fixtures").TryGetProperty("2", out _));
        Assert.True(File.Exists(Path.Combine(_projects, "Kruimels_001.blocking.json")));   // the old files stay
    }
}
