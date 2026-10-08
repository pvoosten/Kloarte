using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.Application;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// Native projects in the session (decided 14 Oct 2026): a new project's show is its rig.yaml, palettes.yaml and
/// cues.yaml; saved, read back, edited outside Kloarte (one undo step), a broken file never applied nor overwritten; a
/// project of before (a MagicQ show) becomes native at its first save; the MagicQ show is an export.
/// </summary>
public sealed partial class NativeProjectTests : TestBase
{
    /// <summary>The test's MagicQ show folder.</summary>
    private readonly string _shows;

    /// <summary>The test's projects folder.</summary>
    private readonly string _projects;

    /// <summary>The watcher the sessions get.</summary>
    private readonly FakeWatcher _watcher = new();

    /// <summary>Fresh folders for every test.</summary>
    public NativeProjectTests() : base("app_native")
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

    /// <summary>A session of native shows on the test's folders.</summary>
    private ShowSession<DesignShow> Session()
    {
        var exchange = new MagicQExchange { ShowFolderOverride = _shows };
        ShowSession<DesignShow> session = null;
        session = new ShowSession<DesignShow>(new DesignStore(exchange, () => session.Catalog), new ProjectLibrary(_projects),
            Path.Combine(_out, "history"))
        {
            WatcherFactory = _ => _watcher,
        };
        return session;
    }

    /// <summary>A new project: the house venue, saved as its own files, reads back the same.</summary>
    [Fact]
    public void NewProjectIsNative()
    {
        var session = Session();
        session.NewProject("Test");
        var model = session.Load();
        Assert.Empty(session.LoadProblems.Where(p => p.IsError));
        Assert.EndsWith("rig.yaml", session.ShowFile);
        Assert.Contains(model.Show.Rig.Items, i => i.Kind == "pipe");
        Assert.Empty(session.UnsavedChanges());

        // A lamp, a cue: unsaved, saved, read back.
        var lamp = (DesignItem)session.Editor.ImportLamp(DesignTypes.GenericName, "Front", "PC");
        session.Editor.InsertCue(((IShowDocument)model.Show).CueStacks[0], 1, "Open", blackout: false);
        model.Show.CueOrder[0].Heads[DesignShow.Key(lamp.Head.Value)] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(80) };
        model.Show.Changed();
        Assert.Contains("show", session.UnsavedChanges());
        string saved = session.SaveProject();
        Assert.DoesNotContain("Not saved", saved);
        Assert.Empty(session.UnsavedChanges());
        Assert.Contains("intensity: 80", File.ReadAllText(Path.Combine(session.ProjectFolder, "cues.yaml")));

        var again = Session();
        again.OpenProject("Test");
        var back = again.Load();
        Assert.Equal(model.Show.ToText(), back.Show.ToText());
        Assert.Empty(again.UnsavedChanges());
    }

    /// <summary>
    /// A project opens from its folder, anywhere (File > Open project folder): a copy outside the projects folder reads the
    /// same and saves into that folder; a folder with only a native show's rig.yaml opens as a project named after it; any
    /// other folder is refused.
    /// </summary>
    [Fact]
    public void ProjectOpensFromAnyFolder()
    {
        var session = Session();
        session.NewProject("Moved");
        var model = session.Load();
        session.Editor.ImportLamp(DesignTypes.GenericName, "Front", "PC");
        session.SaveProject();
        string elsewhere = Path.Combine(_out, "elsewhere", "Moved");
        foreach (string file in Directory.EnumerateFiles(session.ProjectFolder, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(elsewhere, Path.GetRelativePath(session.ProjectFolder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target);
        }

        var other = Session();
        other.OpenProjectFolder(elsewhere);
        var back = other.Load();
        Assert.Equal(Path.GetFullPath(elsewhere), Path.GetFullPath(other.ProjectFolder));
        Assert.Equal(Path.Combine(Path.GetFullPath(elsewhere), "rig.yaml"), other.ShowFile);
        Assert.Equal(model.Show.ToText(), back.Show.ToText());
        Assert.Empty(other.UnsavedChanges());
        back.Show.CueOrder.Add(new DesignCue { Cue = "1", Id = 1, Text = "There" });
        back.Show.Changed();
        other.SaveProject();
        Assert.Contains("There", File.ReadAllText(Path.Combine(elsewhere, "cues.yaml")));
        Assert.DoesNotContain("There", File.ReadAllText(Path.Combine(session.ProjectFolder, "cues.yaml")));

        string bare = Path.Combine(_out, "elsewhere", "Bare");
        Directory.CreateDirectory(bare);
        foreach (string file in new[] { "rig.yaml", "palettes.yaml", "cues.yaml" })
            File.Copy(Path.Combine(elsewhere, file), Path.Combine(bare, file));
        var third = Session();
        third.OpenProjectFolder(bare);
        Assert.Equal("Bare", third.ProjectName);
        Assert.Single(third.Load().Show.CueOrder);

        Assert.Throws<System.InvalidOperationException>(() => Session().OpenProjectFolder(_shows));
    }

    /// <summary>An edit of a show file outside Kloarte is one undo step; a broken one is not applied and not overwritten.</summary>
    [Fact]
    public void OutsideEditsOfTheShowFiles()
    {
        var session = Session();
        session.NewProject("Edits");
        var model = session.Load();
        session.Editor.InsertCue(((IShowDocument)model.Show).CueStacks[0], 1, "One", blackout: false);
        session.SaveProject();
        string cues = Path.Combine(session.ProjectFolder, "cues.yaml");

        File.WriteAllText(cues, File.ReadAllText(cues).Replace("text: One", "text: Changed"));
        _watcher.Settled.Add(cues);
        var changes = session.PollProjectChanges();
        Assert.True(changes.Single().Applied);
        Assert.Equal("Changed", model.Show.CueOrder[0].Text);
        Assert.Equal("Edited outside Kloarte: cues.yaml", session.UndoName);
        session.Undo();
        Assert.Equal("One", model.Show.CueOrder[0].Text);
        session.Redo();

        string broken = File.ReadAllText(cues) + "\n  - cue: [unclosed\n";
        File.WriteAllText(cues, broken);
        _watcher.Settled.Add(cues);
        var error = session.PollProjectChanges().Single();
        Assert.False(error.Applied);
        Assert.Contains(error.Problems, p => p.IsError);
        Assert.Equal("Changed", model.Show.CueOrder[0].Text);
        model.Show.CueOrder[0].FadeIn = 5;
        model.Show.Changed();
        string saved = session.SaveProject();
        Assert.Contains("cues.yaml", saved);   // refused: the broken file stays
        Assert.Equal(broken, File.ReadAllText(cues));
    }

    /// <summary>
    /// The definition of done (14 Oct 2026), without the UI: a project "Test", 10 lamps (8 conventionals, 2 T1s), 5 cues
    /// with intensities, colours, shutters, gobos and smoke, using palettes; edited through cues.yaml; played back with
    /// fades (tracking); saved, opened again the same; exported as a MagicQ show that reads back the same.
    /// </summary>
    [Fact]
    public void DefinitionOfDone()
    {
        var session = Session();
        session.NewProject("Test");
        var model = session.Load();
        var design = model.Show;
        var editor = session.Editor;
        var t1Type = DesignTypes.Catalogue.First(t => t.Gdtf?.Contains("T1") == true);
        var dimmers = Enumerable.Range(0, 8).Select(i => (DesignItem)editor.ImportLamp(DesignTypes.GenericName, $"Front {i + 1}", "PC")).ToList();
        var spots = Enumerable.Range(0, 2).Select(i => (DesignItem)editor.ImportLamp(t1Type.Name, $"Spot {i + 1}", "T1")).ToList();
        Assert.Equal(10, design.Rig.Items.Count(i => i.IsLamp));
        editor.SetGel(dimmers[0].Head.Value, 1201);

        var layout = design.LayoutOf(spots[0].Head.Value);
        string gobo = layout["Gobo1"].SetNames.First(n => n != "Open");
        string shutterOpen = layout["Shutter1"].SetNames.First(n => n.Contains("Open"));
        design.Palettes.Add(new DesignPalette
        {
            Name = "Warm centre",
            Heads = spots.ToDictionary(s => DesignShow.Key(s.Head.Value), _ => new Dictionary<string, LookValue>
            {
                ["Pan"] = LookValue.Of(10), ["Tilt"] = LookValue.Of(-40), ["CTO"] = LookValue.Of(3200),
            }),
        });
        var stack = ((IShowDocument)design).CueStacks[0];
        for (int c = 1; c <= 5; c++)
            editor.InsertCue(stack, c, $"Cue {c}", blackout: false);
        var cues = design.CueOrder;
        foreach (var lamp in dimmers)
            DesignEdits.Set(cues[0].Heads, lamp.Head.Value, "intensity", LookValue.Of(60));
        foreach (var spot in spots)
        {
            var values = new Dictionary<string, LookValue>
            {
                ["intensity"] = LookValue.Of(100), ["Pan"] = LookValue.OfPalette("Warm centre"), ["Tilt"] = LookValue.OfPalette("Warm centre"),
                ["CTO"] = LookValue.OfPalette("Warm centre"), ["Gobo1"] = LookValue.Of(gobo), ["Shutter1"] = LookValue.Of(shutterOpen),
                ["ColorAdd_R"] = LookValue.Of(0.6), ["Blade1A"] = LookValue.Of(0.3),
            };
            cues[1].Heads[DesignShow.Key(spot.Head.Value)] = values;
        }
        DesignEdits.Set(cues[2].Heads, dimmers[0].Head.Value, "intensity", LookValue.Of(20));
        cues[3].Heads = DesignEdits.Blackout(design);
        cues[4].Heads[DesignShow.Key(spots[1].Head.Value)] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(45), ["Pan"] = LookValue.Of(-30) };
        foreach (var cue in cues)
            cue.FadeIn = cue.FadeOut = 2;
        model.Blocking.Smoke[cues[1].Id.Value] = new CueSmoke { Amount = 40, Time = 10 };
        design.Changed();
        Assert.Empty(design.Problems());

        // Played back: tracking (cue 3 changes one dimmer, the rest of cue 1 and 2 stays), palette values in DMX.
        int dimmer0 = design.Head(dimmers[0].Head.Value).Address, dimmer1 = design.Head(dimmers[1].Head.Value).Address;
        var after3 = CueTracking.LevelsAfter(design, stack, 2);
        Assert.Equal(51, after3[dimmer0]);
        Assert.Equal(153, after3[dimmer1]);
        var tilt = layout["Tilt"];
        Assert.Equal(tilt.ToDmx(LookValue.Of(-40)), tilt.Read(after3, design.Head(spots[0].Head.Value).Address));
        var timeline = new PlaybackTimeline(design, stack, model.Blocking, 3f);
        Assert.Equal(5, timeline.Segments.Count);
        Assert.True(timeline.HasSmoke);
        var cue1 = timeline.Segments[0];
        int halfway = timeline.DmxAt(cue1.Start + 1).GetValueOrDefault(dimmer1);   // 2 s fade in to 60 %: about half way
        Assert.InRange(halfway, 60, 95);
        Assert.Equal(153, timeline.DmxAt(cue1.Start + 2.5).GetValueOrDefault(dimmer1));

        // Saved, opened again: the same.
        Assert.DoesNotContain("Not saved", session.SaveProject());
        Assert.Empty(session.UnsavedChanges());
        string cuesFile = Path.Combine(session.ProjectFolder, "cues.yaml");

        // cues.yaml edited outside Kloarte: one undo step.
        File.WriteAllText(cuesFile, File.ReadAllText(cuesFile).Replace("text: Cue 3", "text: Edited"));
        _watcher.Settled.Add(cuesFile);
        Assert.True(session.PollProjectChanges().Single().Applied);
        Assert.Equal("Edited", design.CueOrder[2].Text);
        session.SaveProject();
        var again = Session();
        again.OpenProject("Test");
        var back = again.Load();
        Assert.Equal(design.ToText(), back.Show.ToText());
        Assert.Equal(40, back.Blocking.Smoke[cues[1].Id.Value].Amount);

        // Exported as a MagicQ show: it reads back the same cues and DMX (the export's own check finds nothing).
        string exported = again.SaveShowVersion();
        Assert.DoesNotContain("check after writing", again.LastConsoleShowReport);
        var imported = MagicQImport.Import(exported, again.Catalog).Show;
        Assert.Equal(design.CueOrder.Select(c => c.Text), imported.CueOrder.Select(c => c.Text));
        var stackBack = ((IShowDocument)imported).CueStacks[0];
        for (int i = 0; i < 5; i++)
            Assert.Equal(CueTracking.LevelsAfter(design, stack, i).OrderBy(kv => kv.Key), CueTracking.LevelsAfter(imported, stackBack, i).OrderBy(kv => kv.Key));
        Assert.Contains(imported.Palettes, p => p.Name == "Warm centre");
    }

    /// <summary>
    /// The user's repro (12 Oct 2026), native: Kruimels, the lamp of head ZAAL, cue 0.01, 42 %, Save: the level is in the
    /// project's cues.yaml, and nothing is unsaved after.
    /// </summary>
    [Fact]
    public void KruimelsZaalLevelInCueIsSaved()
    {
        string source = TestEnvironment.Show("Kruimels_010.shw");
        if (!File.Exists(source))
            return;
        string copy = Path.Combine(_shows, "Kruimels_010.shw");
        File.Copy(source, copy, true);
        File.Copy(Path.ChangeExtension(source, ".xhw"), Path.ChangeExtension(copy, ".xhw"), true);
        var session = Session();
        session.Open(copy);
        var model = session.Load();
        IShowDocument doc = model.Show;
        var lamp = doc.RigItems.First(r => r.IsFixture && doc.LampPatchOf(r)?.HeadName == "ZAAL");
        var stack = doc.CueStacks.Single();
        int step = stack.Steps.ToList().FindIndex(st => st.CueNumberText == "0.01");
        var playback = new ShowPlayback<DesignShow>(session, new NullStage());
        Assert.Equal(107, playback.SetLevel(stack, step, lamp.Id, 0.42f));
        session.SaveProject();
        Assert.Contains($"\"{lamp.HeadNumber}\": {{intensity: 42", File.ReadAllText(Path.Combine(session.ProjectFolder, "cues.yaml")));
        Assert.Empty(session.UnsavedChanges());
    }

    /// <summary>
    /// A project of before (its show a MagicQ show) is imported at load and becomes native at its first save; the
    /// MagicQ show is then an export, written as the next numbered show, the source untouched.
    /// </summary>
    [Fact]
    public void ProjectOfBeforeBecomesNative()
    {
        string copy = Path.Combine(_shows, "Rt_001.shw");
        File.Copy(TestEnvironment.Show("RT2025_031.shw"), copy);
        string original = File.ReadAllText(copy);
        var session = Session();
        session.Open(copy);
        var model = session.Load();
        Assert.True(model.Show.CueOrder.Count > 10);
        Assert.Contains("show", session.UnsavedChanges());
        session.SaveProject();
        Assert.EndsWith("rig.yaml", session.ShowFile);
        Assert.Empty(session.UnsavedChanges());
        Assert.Equal(Path.GetFullPath(copy), model.Show.Rig.Source);

        model.Show.CueOrder[0].Text = "Kloarte";
        model.Show.Changed();
        string exported = session.SaveShowVersion();
        Assert.Equal("Rt_002.shw", Path.GetFileName(exported));
        Assert.Contains("Exported Rt_002.shw", session.LastConsoleShowReport);
        Assert.Equal(original, File.ReadAllText(copy));
        Assert.Equal("Kloarte", MagicQImport.Import(exported, session.Catalog).Show.CueOrder[0].Text);
        Assert.EndsWith("rig.yaml", session.ShowFile);   // the project's show stays its own files
    }
}
