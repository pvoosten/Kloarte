using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.Application;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// A project's YAML files in the session (user decisions of 11 Oct 2026): saved when changed, read back, edits made
/// outside Kloarte applied as one undo step, conflicts offered as a choice, broken files not applied and never
/// overwritten, renamed and removed lamps. The folder is watched by a fake watcher: the tests say which file changed.
/// </summary>
public sealed class YamlProjectTests : TestBase
{
    /// <summary>A real show with fixtures (the lamps the mechanics are about).</summary>
    private const string FixtureShow = "RT2025_031.shw";

    /// <summary>The test's MagicQ show folder.</summary>
    private readonly string _shows;

    /// <summary>The test's projects folder.</summary>
    private readonly string _projects;

    /// <summary>Fresh folders for every test.</summary>
    public YamlProjectTests() : base("app_yaml")
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, true);
        _shows = Path.Combine(_out, "show");
        _projects = Path.Combine(_out, "projects");
        Directory.CreateDirectory(_shows);
        Directory.CreateDirectory(_projects);
    }

    /// <summary>A watcher the test drives: the files it reports are the ones it was told changed.</summary>
    private sealed class FakeWatcher : IProjectWatcher
    {
        /// <summary>The files reported by the next <see cref="TakeSettled"/>.</summary>
        public List<string> Settled { get; } = new();

        /// <inheritdoc />
        public IReadOnlyList<string> TakeSettled()
        {
            var files = Settled.ToList();
            Settled.Clear();
            return files;
        }

        /// <inheritdoc />
        public void Dispose() { }
    }

    /// <summary>A session on the test's folders, watched by `watcher`.</summary>
    private ShowSession<MagicQShow> Session(FakeWatcher watcher) =>
        new(new TestShowStore(_shows), new ProjectLibrary(_projects), Path.Combine(_out, "history"))
        {
            WatcherFactory = _ => watcher,
        };

    /// <summary>Opens (and loads) the fixture show as a new project; returns the session, model and one lamp's id.</summary>
    private (ShowSession<MagicQShow> Session, ShowModel<MagicQShow> Model, int Lamp, FakeWatcher Watcher) OpenShow()
    {
        string copy = Path.Combine(_shows, FixtureShow);
        File.Copy(TestEnvironment.Show(FixtureShow), copy, true);
        var watcher = new FakeWatcher();
        var session = Session(watcher);
        session.Open(copy);
        var model = session.Load();
        int lamp = ((IShowDocument)model.Show).RigItems.First(v => v.IsFixture).Id;
        return (session, model, lamp, watcher);
    }

    /// <summary>The lamp files of the open project.</summary>
    private static string[] LampFilesOf(ShowSession<MagicQShow> session) =>
        Directory.Exists(Path.Combine(session.ProjectFolder, "mechanics"))
            ? Directory.GetFiles(Path.Combine(session.ProjectFolder, "mechanics"), "*.yaml") : System.Array.Empty<string>();

    /// <summary>The one lamp file of the lamp `rigItem` (the file names are derived; the key is inside).</summary>
    private static string LampFileOf(ShowSession<MagicQShow> session, int rigItem) =>
        LampFilesOf(session).Single(f => File.ReadAllText(f).Contains($"rigItem: {rigItem}\n"));

    /// <summary>A lamp with a few mechanics (one catalogue model, a field angle that the file rounds to 27 degrees).</summary>
    private static FixtureMechanics LampMechanics(float angle = 27f) => new()
    {
        Model = "adb-c103", Kind = FixtureKind.Pc, FieldAngle = angle, Color = "#ff8800",
    };

    /// <summary>Saving writes the lamp's YAML file and the project document; the JSON files of before are kept.</summary>
    [Fact]
    public void SaveWritesYamlAndKeepsTheOldJson()
    {
        var (session, model, lamp, _) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        string status = session.SaveProject();

        Assert.Contains("Saved project", status);
        Assert.Single(LampFilesOf(session));
        string file = LampFileOf(session, lamp);
        Assert.StartsWith("# yaml-language-server: $schema=../.schemas/lamp.schema.json", File.ReadAllText(file));
        Assert.Contains("fieldAngle: 27", File.ReadAllText(file));
        Assert.True(File.Exists(Path.Combine(session.ProjectFolder, session.ProjectName + ".kloarte.yaml")));
        Assert.True(File.Exists(Path.Combine(session.ProjectFolder, "blocking", "actors.yaml")));
        Assert.True(File.Exists(Path.Combine(session.ProjectFolder, ".schemas", "lamp.schema.json")));
        Assert.Empty(session.UnsavedChanges());
    }

    /// <summary>A project that only has JSON loads it; the first save writes YAML and keeps the JSON.</summary>
    [Fact]
    public void AJsonProjectIsReadAndTheFirstSaveWritesYaml()
    {
        var (session, model, lamp, _) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();
        string folder = session.ProjectFolder;
        Directory.Delete(Path.Combine(folder, "mechanics"), true);
        Directory.Delete(Path.Combine(folder, "blocking"), true);
        File.Delete(Path.Combine(folder, session.ProjectName + ".kloarte.yaml"));
        File.WriteAllText(Path.Combine(folder, session.ProjectName + ".kloarte.json"),
            "{\"name\": \"" + session.ProjectName + "\", \"showFile\": \"" + session.ShowFile.Replace("\\", "\\\\") + "\", \"version\": 1}");
        File.WriteAllText(Path.Combine(folder, "mechanics.json"),
            "{\"version\": 2, \"fixtures\": {\"" + lamp + "\": {\"model\": \"adb-c103\", \"fieldAngle\": 31}}}");

        var reopened = Session(new FakeWatcher());
        reopened.OpenProject(session.ProjectName);
        var loaded = reopened.Load();
        Assert.Equal(31f, loaded.Mechanics[lamp].FieldAngle);
        Assert.Empty(reopened.UnsavedChanges());

        reopened.SaveProject();
        Assert.Single(LampFilesOf(reopened));
        Assert.True(File.Exists(Path.Combine(folder, "mechanics.json")));
        Assert.True(File.Exists(Path.Combine(folder, session.ProjectName + ".kloarte.yaml")));
    }

    /// <summary>What was saved comes back when the project is opened again; nothing is unsaved.</summary>
    [Fact]
    public void ReopenedProjectHasTheSavedValues()
    {
        var (session, model, lamp, _) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        model.Blocking.Actors.Add(new ActorDef { Id = "a1", Name = "Anna", ShirtColor = "ff8800" });
        session.SaveProject();

        var again = Session(new FakeWatcher());
        again.OpenProject(session.ProjectName);
        var loaded = again.Load();
        Assert.Equal("adb-c103", loaded.Mechanics[lamp].Model);
        Assert.Equal(27f, loaded.Mechanics[lamp].FieldAngle, 3);
        Assert.Equal("#ff8800", loaded.Mechanics[lamp].Color);
        Assert.Equal("Anna", loaded.Blocking.Find("a1").Name);
        Assert.Empty(again.UnsavedChanges());
    }

    /// <summary>
    /// The project holds its own venue (user, 12 Oct 2026): the first Save writes <c>venue.yaml</c> as a copy of the venue the
    /// scene uses (with the project's schema modeline); a later Save leaves it alone, also when the user changed it.
    /// </summary>
    [Fact]
    public void TheProjectGetsItsOwnVenueFile()
    {
        var (session, _, _, _) = OpenShow();
        session.DefaultVenueFile = "res://venue/ccdebrouckere.venue.yaml";
        string yaml = Path.Combine(session.ProjectFolder, "venue.yaml");
        Assert.False(File.Exists(yaml));
        session.SaveProject();
        Assert.True(File.Exists(yaml));
        var house = Venue.Load(AppPaths.File(session.DefaultVenueFile));
        var copy = Venue.Load(yaml);
        Assert.Equal(house.Name, copy.Name);
        Assert.Equal(house.Auditorium.Rows.Count, copy.Auditorium.Rows.Count);
        Assert.Equal(house.Hoists.Count, copy.Hoists.Count);
        Assert.StartsWith("# yaml-language-server: $schema=.schemas/venue.schema.json", File.ReadAllText(yaml));
        Assert.Empty(session.UnsavedChanges());

        copy.StageWidth = 11f;   // the user's own change stays
        copy.Save(yaml);
        session.SaveProject();
        Assert.Equal(11f, Venue.Load(yaml).StageWidth);
    }

    /// <summary>
    /// The smoke per cue (amount, build time) is part of the blocking: a change is unsaved "blocking", Save writes it into
    /// blocking/steps.yaml, and the reopened project has it, nothing unsaved.
    /// </summary>
    [Fact]
    public void SmokePerCueIsSavedWithTheBlocking()
    {
        var (session, model, _, _) = OpenShow();
        session.SaveProject();
        Assert.Empty(session.UnsavedChanges());
        int cue = model.Show.Cues.First().Id;
        model.Blocking.Smoke[cue] = new CueSmoke { Amount = 35, Time = 45f };
        Assert.Contains("blocking", session.UnsavedChanges());
        session.SaveProject();
        string steps = File.ReadAllText(Path.Combine(session.ProjectFolder, "blocking", "steps.yaml"));
        Assert.Contains("smoke: 35", steps);
        Assert.Contains("smokeTime: 45", steps);

        var again = Session(new FakeWatcher());
        again.OpenProject(session.ProjectName);
        var loaded = again.Load();
        Assert.Equal(35.0, loaded.Blocking.Smoke[cue].Amount);
        Assert.Equal(45f, loaded.Blocking.Smoke[cue].Time);
        Assert.Empty(again.UnsavedChanges());
    }

    /// <summary>
    /// The look (stray light, haze) is saved with the project (UI review 24): a change is an unsaved "look", Save writes
    /// it into the project document, and the reopened project has it; the defaults are not written.
    /// </summary>
    [Fact]
    public void TheLookIsSavedWithTheProject()
    {
        var (session, _, _, _) = OpenShow();
        session.SaveProject();
        Assert.DoesNotContain("strayLight", File.ReadAllText(session.Project.DocumentPath));
        Assert.Empty(session.UnsavedChanges());

        session.Look.StrayLight = 1.2f;
        session.Look.Haze = 0.04f;
        Assert.Equal(new[] { "look" }, session.UnsavedChanges());
        Assert.Contains("look", session.SaveProject());
        Assert.Empty(session.UnsavedChanges());
        string text = File.ReadAllText(session.Project.DocumentPath);
        Assert.Contains("strayLight: 1.2", text);
        Assert.Contains("haze: 0.04", text);

        var again = Session(new FakeWatcher());
        again.OpenProject(session.ProjectName);
        again.Load();
        Assert.Equal(1.2f, again.Look.StrayLight, 3);
        Assert.Equal(0.04f, again.Look.Haze, 5);
        Assert.Empty(again.UnsavedChanges());
    }

    /// <summary>Kloarte's own writes are not reported back as edits made outside it.</summary>
    [Fact]
    public void OwnWritesAreNotExternalEdits()
    {
        var (session, model, lamp, watcher) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();
        watcher.Settled.AddRange(LampFilesOf(session));
        watcher.Settled.Add(Path.Combine(session.ProjectFolder, "blocking", "actors.yaml"));
        Assert.Empty(session.PollProjectChanges());
    }

    /// <summary>An edit made outside Kloarte is applied at once and is one undoable step, named after the file.</summary>
    [Fact]
    public void AnEditOutsideIsOneUndoableStep()
    {
        var (session, model, lamp, watcher) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();

        string file = LampFileOf(session, lamp);
        File.WriteAllText(file, File.ReadAllText(file).Replace("fieldAngle: 27", "fieldAngle: 33"));
        watcher.Settled.Add(file);
        var changes = session.PollProjectChanges();

        var change = Assert.Single(changes);
        Assert.True(change.Applied);
        Assert.False(change.Conflict);
        Assert.False(change.HasError);
        Assert.Equal(33f, model.Mechanics[lamp].FieldAngle, 3);
        Assert.Equal("Edited outside Kloarte: " + Path.GetFileName(file), session.UndoName);
        Assert.Empty(session.UnsavedChanges());

        session.Undo();
        Assert.Equal(27f, model.Mechanics[lamp].FieldAngle, 3);
        Assert.NotEmpty(session.UnsavedChanges());   // the file still says 33: not what Kloarte has
    }

    /// <summary>A file with an error is not applied: the last good state stays, the lamp is marked, the error is located.</summary>
    [Fact]
    public void AFileWithAnErrorIsNotApplied()
    {
        var (session, model, lamp, watcher) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();

        string file = LampFileOf(session, lamp);
        File.WriteAllText(file, $"rigItem: {lamp}\nfieldAngle: [\n");
        watcher.Settled.Add(file);
        var change = Assert.Single(session.PollProjectChanges());

        Assert.True(change.HasError);
        Assert.False(change.Applied);
        Assert.Contains(change.Problems, p => p.IsError && p.Line > 0);
        Assert.Equal(27f, model.Mechanics[lamp].FieldAngle, 3);
        Assert.Contains(lamp, model.BrokenLamps);

        // Fixed by hand: applied, and the mark goes.
        File.WriteAllText(file, $"rigItem: {lamp}\nfieldAngle: 50\n");
        watcher.Settled.Add(file);
        var fixedChange = Assert.Single(session.PollProjectChanges());
        Assert.True(fixedChange.Applied);
        Assert.Equal(50f, model.Mechanics[lamp].FieldAngle, 3);
        Assert.DoesNotContain(lamp, model.BrokenLamps);
    }

    /// <summary>A save never overwrites a file that doesn't parse; it says so.</summary>
    [Fact]
    public void SaveNeverOverwritesABrokenFile()
    {
        var (session, model, lamp, _) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();

        string file = LampFileOf(session, lamp);
        string broken = $"rigItem: {lamp}\nfieldAngle: [\n";
        File.WriteAllText(file, broken);
        model.Mechanics[lamp] = LampMechanics(45f);
        string status = session.SaveProject();

        Assert.Contains("Not saved, changed outside Kloarte", status);
        Assert.Contains(Path.GetFileName(file), status);
        Assert.Equal(broken, File.ReadAllText(file));
    }

    /// <summary>
    /// Kloarte has an unsaved change for the file and the file changed outside: a conflict, nothing applied; "Keep
    /// Kloarte's" keeps the model and the next save overwrites the file.
    /// </summary>
    [Fact]
    public void ConflictKeepKloarte()
    {
        var (session, model, lamp, watcher) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();

        string file = LampFileOf(session, lamp);
        model.Mechanics[lamp] = LampMechanics(40f);   // unsaved in Kloarte
        File.WriteAllText(file, File.ReadAllText(file).Replace("fieldAngle: 27", "fieldAngle: 33"));
        watcher.Settled.Add(file);
        var change = Assert.Single(session.PollProjectChanges());

        Assert.True(change.Conflict);
        Assert.False(change.Applied);
        Assert.Equal(40f, model.Mechanics[lamp].FieldAngle, 3);

        string status = session.ResolveProjectChange(change, ProjectChoice.KeepKloarte);
        Assert.Contains("Kept Kloarte's", status);
        Assert.Empty(session.PollProjectChanges());
        Assert.Contains("mechanics", session.UnsavedChanges());

        session.SaveProject();
        Assert.Contains("fieldAngle: 40", File.ReadAllText(file));
    }

    /// <summary>
    /// "Take the file's" applies the file; Kloarte's unsaved change is an undo step of its own (undo brings it back).
    /// </summary>
    [Fact]
    public void ConflictTakeTheFile()
    {
        var (session, model, lamp, watcher) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.Checkpoint();   // the first settled state (what Undo goes back to)
        session.SaveProject();

        string file = LampFileOf(session, lamp);
        model.Mechanics[lamp] = LampMechanics(40f);
        File.WriteAllText(file, File.ReadAllText(file).Replace("fieldAngle: 27", "fieldAngle: 33"));
        watcher.Settled.Add(file);
        var change = Assert.Single(session.PollProjectChanges());

        string status = session.ResolveProjectChange(change, ProjectChoice.TakeFile);
        Assert.Contains("Took the file's", status);
        Assert.Equal(33f, model.Mechanics[lamp].FieldAngle, 3);
        Assert.Empty(session.UnsavedChanges());

        session.Undo();
        Assert.Equal(40f, model.Mechanics[lamp].FieldAngle, 3);
        session.Undo();
        Assert.Equal(27f, model.Mechanics[lamp].FieldAngle, 3);
    }

    /// <summary>"Save Kloarte's first" writes Kloarte's content over the file's change.</summary>
    [Fact]
    public void ConflictSaveKloarteFirst()
    {
        var (session, model, lamp, watcher) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();

        string file = LampFileOf(session, lamp);
        model.Mechanics[lamp] = LampMechanics(40f);
        File.WriteAllText(file, File.ReadAllText(file).Replace("fieldAngle: 27", "fieldAngle: 33"));
        watcher.Settled.Add(file);
        var change = Assert.Single(session.PollProjectChanges());

        Assert.Contains("Saved Kloarte's", session.ResolveProjectChange(change, ProjectChoice.SaveKloarteFirst));
        Assert.Contains("fieldAngle: 40", File.ReadAllText(file));
        Assert.Empty(session.UnsavedChanges());
    }

    /// <summary>A lamp renamed by hand loads (the key is the rig item id) and is renamed back on the next save.</summary>
    [Fact]
    public void AHandRenamedLampFileLoadsAndIsRenamedOnSave()
    {
        var (session, model, lamp, _) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();

        string file = LampFileOf(session, lamp);
        string mine = Path.Combine(Path.GetDirectoryName(file)!, "my-lamp.yaml");
        File.Move(file, mine);

        var again = Session(new FakeWatcher());
        again.OpenProject(session.ProjectName);
        var loaded = again.Load();
        Assert.Equal(27f, loaded.Mechanics[lamp].FieldAngle, 3);

        again.SaveProject();
        Assert.False(File.Exists(mine));
        Assert.Single(LampFilesOf(again));
        Assert.Contains($"rigItem: {lamp}", File.ReadAllText(LampFileOf(again, lamp)));
    }

    /// <summary>A lamp removed from the show's mechanics loses its file on save; nothing else is touched.</summary>
    [Fact]
    public void ARemovedLampLosesItsFile()
    {
        var (session, model, lamp, _) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();
        Assert.Single(LampFilesOf(session));

        model.Mechanics.Remove(lamp);
        session.SaveProject();
        Assert.Empty(LampFilesOf(session));
    }

    /// <summary>A comment a person added survives a save that changes nothing in the file.</summary>
    [Fact]
    public void HandWrittenCommentsSurviveUnchangedSaves()
    {
        var (session, model, lamp, _) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();

        string file = LampFileOf(session, lamp);
        File.WriteAllText(file, File.ReadAllText(file).Replace($"rigItem: {lamp}", "# check the gel with the director\nrigItem: " + lamp));
        session.SaveProject();
        Assert.Contains("# check the gel with the director", File.ReadAllText(file));
    }

    /// <summary>An actors edit outside Kloarte is one undo step; the actor's new name is what the scene uses.</summary>
    [Fact]
    public void AnActorsEditOutsideIsOneUndoableStep()
    {
        var (session, model, _, watcher) = OpenShow();
        model.Blocking.Actors.Add(new ActorDef { Id = "a1", Name = "Anna" });
        session.SaveProject();

        string file = Path.Combine(session.ProjectFolder, "blocking", "actors.yaml");
        File.WriteAllText(file, File.ReadAllText(file).Replace("Anna", "Berta"));
        watcher.Settled.Add(file);
        var change = Assert.Single(session.PollProjectChanges());

        Assert.True(change.Applied);
        Assert.Equal("Berta", model.Blocking.Find("a1").Name);
        Assert.Equal("Edited outside Kloarte: actors.yaml", session.UndoName);
        session.Undo();
        Assert.Equal("Anna", model.Blocking.Find("a1").Name);
    }

    /// <summary>
    /// The set pieces are saved in blocking/scenery.yaml (only once there are any) and their changes per cue in
    /// steps.yaml; the reopened project has them, nothing unsaved.
    /// </summary>
    [Fact]
    public void SceneryIsSavedWithTheBlocking()
    {
        var (session, model, _, _) = OpenShow();
        session.SaveProject();
        string file = Path.Combine(session.ProjectFolder, "blocking", "scenery.yaml");
        Assert.False(File.Exists(file));
        var table = SceneryPiece.New("table", "Table", SceneryShape.Table);
        table.Color = "336699";
        model.Blocking.Scenery.Add(table);
        int cue = model.Show.Cues.First().Id;
        model.Blocking.SceneryChangeOf(cue, "table", true).Visible = false;
        Assert.Contains("blocking", session.UnsavedChanges());
        session.SaveProject();
        Assert.Contains("shape: table", File.ReadAllText(file));
        Assert.Contains("table: {visible: false}", File.ReadAllText(Path.Combine(session.ProjectFolder, "blocking", "steps.yaml")));

        var again = Session(new FakeWatcher());
        again.OpenProject(session.ProjectName);
        var loaded = again.Load();
        Assert.Equal("336699", loaded.Blocking.FindPiece("table").Color);
        Assert.False(loaded.Blocking.SceneryChangeOf(cue, "table").Visible);
        Assert.Empty(again.UnsavedChanges());
    }

    /// <summary>A scenery edit outside Kloarte is one undo step; a broken scenery file is not applied.</summary>
    [Fact]
    public void ASceneryEditOutsideIsOneUndoableStep()
    {
        var (session, model, _, watcher) = OpenShow();
        model.Blocking.Scenery.Add(SceneryPiece.New("box", "Box", SceneryShape.Block));
        session.SaveProject();

        string file = Path.Combine(session.ProjectFolder, "blocking", "scenery.yaml");
        string text = File.ReadAllText(file);
        string indent = text.Split('\n').First(l => l.Contains("name: Box"))[..^"name: Box".Length].TrimEnd('\r');
        File.WriteAllText(file, text.Replace("name: Box", $"name: Big box\n{indent.Replace('-', ' ')}width: 3"));
        watcher.Settled.Add(file);
        var change = Assert.Single(session.PollProjectChanges());

        Assert.True(change.Applied);
        Assert.Equal(ProjectFileKind.Scenery, change.Kind);
        Assert.Equal(("Big box", 3f), (model.Blocking.FindPiece("box").Name, model.Blocking.FindPiece("box").Width));
        Assert.Equal("Edited outside Kloarte: scenery.yaml", session.UndoName);
        session.Undo();
        Assert.Equal(("Box", 1f), (model.Blocking.FindPiece("box").Name, model.Blocking.FindPiece("box").Width));

        File.WriteAllText(file, "pieces:\n  - id: box\n    width: [oops\n");
        watcher.Settled.Add(file);
        var broken = Assert.Single(session.PollProjectChanges());
        Assert.False(broken.Applied);
        Assert.Equal("Box", model.Blocking.FindPiece("box").Name);
    }

    /// <summary>A saved project with its own venue: an old project's venue.json, migrated to venue.yaml by a save.</summary>
    private (ShowSession<MagicQShow> Session, FakeWatcher Watcher, string Venue) ProjectWithVenue()
    {
        var (session, model, lamp, watcher) = OpenShow();
        model.Mechanics[lamp] = LampMechanics();
        session.SaveProject();
        Venue.Load(TestEnvironment.File("res://venue/ccdebrouckere.venue.yaml"))
            .Save(Path.Combine(session.ProjectFolder, "venue.json"));
        session.SaveProject();
        return (session, watcher, Path.Combine(session.ProjectFolder, "venue.yaml"));
    }

    /// <summary>The venue Kloarte migrates to YAML on a save is its own write, not an edit made outside it.</summary>
    [Fact]
    public void AMigratedVenueIsNotAnOutsideEdit()
    {
        var (session, watcher, venue) = ProjectWithVenue();
        Assert.True(File.Exists(venue));
        Assert.True(File.Exists(Path.ChangeExtension(venue, ".json")));   // the JSON is kept
        watcher.Settled.Add(venue);
        Assert.Empty(session.PollProjectChanges());
    }

    /// <summary>
    /// The project's venue is watched (user decision of 12 Oct 2026): an outside edit is applied (the scene is rebuilt
    /// from it), without an undo step (it is not model data); a broken venue is not applied until it is fixed.
    /// </summary>
    [Fact]
    public void AVenueEditOutsideIsReloadedAndABrokenOneIsNot()
    {
        var (session, watcher, venue) = ProjectWithVenue();
        string undo = session.UndoName;
        string good = File.ReadAllText(venue);

        File.WriteAllText(venue, good.Replace("forestage: 2.15", "forestage: 2.5"));
        watcher.Settled.Add(venue);
        var change = Assert.Single(session.PollProjectChanges());
        Assert.Equal(ProjectFileKind.Venue, change.Kind);
        Assert.True(change.Applied);
        Assert.False(change.Conflict);
        Assert.Equal(undo, session.UndoName);
        Assert.Equal(2.5f, Venue.Load(venue).Forestage);

        File.WriteAllText(venue, "forestage: [\n");
        watcher.Settled.Add(venue);
        var broken = Assert.Single(session.PollProjectChanges());
        Assert.True(broken.HasError);
        Assert.False(broken.Applied);

        File.WriteAllText(venue, good);
        watcher.Settled.Add(venue);
        Assert.True(Assert.Single(session.PollProjectChanges()).Applied);
    }

    /// <summary>The text of a cue of the show.</summary>
    private static string TextOf(ShowModel<MagicQShow> model, int cueId) =>
        model.Show.CueStacks.SelectMany(s => s.Steps).First(st => st.CueId == cueId).Text;

}
