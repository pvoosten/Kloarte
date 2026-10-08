using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.Application;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// Undo / redo (<see cref="EditHistory{TShow}"/>, UI review 6): snapshots of show, mechanics and blocking, recorded at
/// checkpoints, put back into the model.
/// </summary>
public sealed class UndoTests : TestBase
{
    /// <summary>The test's show and projects folders.</summary>
    private readonly string _shows, _projects;

    /// <summary>Fresh folders for every test.</summary>
    public UndoTests() : base("app_undo")
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, true);
        _shows = Path.Combine(_out, "show");
        _projects = Path.Combine(_out, "projects");
        Directory.CreateDirectory(_shows);
    }

    /// <summary>A session with RT2025_031 (+ .xhw) copied into the test's show folder, loaded.</summary>
    private (ShowSession<MagicQShow> Session, ShowModel<MagicQShow> Model, string Show) Open()
    {
        string show = Path.Combine(_shows, "Kruimels_001.shw");
        File.Copy(Show("RT2025_031.shw"), show);
        string xhw = Path.ChangeExtension(Show("RT2025_031.shw"), ".xhw");
        if (File.Exists(xhw))
            File.Copy(xhw, Path.ChangeExtension(show, ".xhw"));
        var session = new ShowSession<MagicQShow>(new TestShowStore(_shows), new ProjectLibrary(_projects),
            Path.Combine(_out, "history"));
        session.Open(show);
        return (session, session.Load(), show);
    }

    /// <summary>A blocking edit is recorded at the checkpoint and undone / redone.</summary>
    [Fact]
    public void UndoesAndRedoesTheBlocking()
    {
        var (session, model, _) = Open();
        Assert.Null(session.Checkpoint());
        model.Blocking.Actors.Add(new ActorDef { Id = "a1", Name = "Anna" });
        session.NameNextEdit("Add actor");
        Assert.Equal("Add actor", session.Checkpoint());
        Assert.Null(session.Checkpoint());   // nothing new

        Assert.Equal("Add actor", session.Undo());
        Assert.Null(model.Blocking.Find("a1"));
        Assert.Equal("Add actor", session.RedoName);
        Assert.Equal("Add actor", session.Redo());
        Assert.Equal("Anna", model.Blocking.Find("a1")?.Name);
        Assert.Null(session.RedoName);
    }

    /// <summary>
    /// A show edit (an inserted cue) is undone to a show that saves byte-identical to the original (the snapshot goes
    /// through MagicQ's lossless save and load, .xhw included); afterwards nothing is unsaved.
    /// </summary>
    [Fact]
    public void UndoRestoresTheShowLosslessly()
    {
        var (session, model, show) = Open();
        var stack = model.Show.CueStacks[0];
        model.Show.InsertCue(stack, stack.Steps[^1].CueNumber + 1, "end", blackout: true);
        Assert.Contains("show", session.UnsavedChanges());
        Assert.Equal("Show (rig, patch or cues)", session.Checkpoint());

        session.Undo();
        Assert.Empty(session.UnsavedChanges());
        string again = Path.Combine(_out, "after_undo.shw");
        session.Model.Show.Save(again);
        Assert.Equal(File.ReadAllBytes(show), File.ReadAllBytes(again));
        if (File.Exists(Path.ChangeExtension(show, ".xhw")))
            Assert.Equal(File.ReadAllBytes(Path.ChangeExtension(show, ".xhw")), File.ReadAllBytes(Path.ChangeExtension(again, ".xhw")));
    }

    /// <summary>
    /// An entry without a name says what changed; an edit not yet checkpointed is the one Undo undoes; a new edit after
    /// an undo drops the redo.
    /// </summary>
    [Fact]
    public void NamesPendingEditsAndRedoAfterAnEdit()
    {
        var (session, model, _) = Open();
        int[] ids = ((IShowDocument)model.Show).RigItems.Where(r => r.IsFixture).Take(2).Select(r => r.Id).ToArray();
        foreach (int id in ids)
            model.Mechanics[id] = new FixtureMechanics { Model = "c103", Iris = 0.5f };
        Assert.Equal("Settings of 2 lamps", session.Undo());   // recorded and undone at once
        Assert.False(model.Mechanics.ContainsKey(ids[0]));

        model.Blocking.Actors.Add(new ActorDef { Id = "a2", Name = "Bert" });
        session.Checkpoint();
        Assert.Null(session.RedoName);
        Assert.Null(session.Redo());
        Assert.Equal("Blocking", session.UndoName);
    }

    /// <summary>
    /// The gel is set per head (every L record of the lamps' heads, as MagicQ keeps it), named for undo, and undone.
    /// </summary>
    [Fact]
    public void SetsTheGelPerHeadUndoably()
    {
        var (session, model, _) = Open();
        var lamps = ((IShowDocument)model.Show).RigItems.Where(r => r.IsFixture && r.HeadNumber > 0).Take(2).ToList();
        var rig = new RigEditor<MagicQShow>(session, new NullStage());
        string status = rig.SetGel(lamps.Select(l => l.Id), 1201);
        Assert.StartsWith("Gel L201 on head", status);
        foreach (int head in lamps.Select(l => l.HeadNumber).Distinct())
            Assert.All(model.Show.Patch.Where(p => p.HeadNumber == head), p => Assert.Equal(1201, p.Gel));
        Assert.Equal("Gel L201", session.Checkpoint());
        session.Undo();
        Assert.Contains(session.Model.Show.Patch.Where(p => p.HeadNumber == lamps[0].HeadNumber), p => p.Gel != 1201);
    }

    /// <summary>
    /// A level set by hand goes into the current cue (no programmer, decided 14 Oct 2026) as one named undo step; undone,
    /// the show saves byte-identical to the original.
    /// </summary>
    [Fact]
    public void SetsALevelInACueUndoably()
    {
        var (session, model, show) = Open();
        IShowDocument doc = model.Show;
        var playback = new ShowPlayback<MagicQShow>(session, new NullStage());
        var stack = model.Show.CueStacks.First(s => s.Name == "show");
        var lamp = doc.RigItems.First(r => r.IsFixture && doc.IntensityAddressOf(r.Address) > 0);
        int channel = doc.IntensityAddressOf(lamp.Address);
        int step = Enumerable.Range(0, stack.Steps.Count).First(i => model.Show.LevelsAfter(stack, i - 1).GetValueOrDefault(channel) != 123);
        Assert.Equal(-1, playback.SetLevel(stack, -1, lamp.Id, 0.5f));   // before the first cue: no cue to set it in

        Assert.Equal(123, playback.SetLevel(stack, step, lamp.Id, 123 / 255f));
        Assert.Equal(123, model.Show.LevelsAfter(stack, step)[channel]);
        Assert.Equal($"Level in cue {stack.Steps[step].CueNumberText}", session.Checkpoint());

        session.Undo();
        string again = Path.Combine(_out, "after_level_undo.shw");
        session.Model.Show.Save(again);
        Assert.Equal(File.ReadAllBytes(show), File.ReadAllBytes(again));
    }

    /// <summary>
    /// A lamp on a truss slides along it: the fixed axes are refused (nothing changes, no undo entry), one coordinate
    /// moves it along the truss as one named undo step, it stops at the truss's end, and undone the show saves
    /// byte-identical to the original.
    /// </summary>
    [Fact]
    public void MovesALampAlongItsTrussAsOneUndoableStep()
    {
        var (session, model, show) = Open();
        IShowDocument doc = model.Show;
        var rig = new Rig(doc);
        var truss = rig.Trusses.First(t => Math.Abs(t.Direction.X) > 0.9f && rig.LampsOn(t.Id).Any());
        var lamp = rig.LampsOn(truss.Id).First();
        var editor = new RigEditor<MagicQShow>(session, new NullStage());
        Assert.Null(session.Checkpoint());

        Assert.Contains("Truss heights", editor.MoveLampAxis(lamp.Id, MotionAxis.Y, lamp.Y + 1f));
        Assert.Contains("along " + truss.Name, editor.MoveLampAxis(lamp.Id, MotionAxis.Z, lamp.Z + 1f));
        Assert.Null(session.Checkpoint());   // refused: nothing changed

        var before = (lamp.X, lamp.Y, lamp.Z);
        float inwards = lamp.X < truss.Start.X + truss.Length / 2 ? 0.5f : -0.5f;
        Assert.Null(editor.MoveLampAxis(lamp.Id, MotionAxis.X, lamp.X + inwards));
        Assert.Equal((before.X + inwards, before.Y, before.Z), (lamp.X, lamp.Y, lamp.Z));
        string name = session.Checkpoint();
        Assert.StartsWith("Move ", name);
        Assert.EndsWith("along " + truss.Name, name);

        string stopped = editor.MoveLampAxis(lamp.Id, MotionAxis.X, truss.Start.X - 20f);
        Assert.Equal($"Stopped at the end of {truss.Name}", stopped);
        Assert.Equal(truss.Start.X, lamp.X, 3);
        Assert.Equal(before.Y, lamp.Y, 3);

        session.Undo();   // the stop
        session.Undo();   // the first move
        Assert.Empty(session.UnsavedChanges());
        string again = Path.Combine(_out, "after_move_undo.shw");
        session.Model.Show.Save(again);
        Assert.Equal(File.ReadAllBytes(show), File.ReadAllBytes(again));
    }

    /// <summary>
    /// Renaming a head names every lamp on it, as one named undo step (a refusal leaves no step); undone, the show saves
    /// byte-identical to the original.
    /// </summary>
    [Fact]
    public void RenamesAHeadUndoably()
    {
        var (session, model, show) = Open();
        IShowDocument doc = model.Show;
        var editor = new RigEditor<MagicQShow>(session, new NullStage());
        var lamp = doc.RigItems.First(r => r.IsFixture && r.HeadNumber > 0);
        Assert.Null(session.Checkpoint());

        Assert.StartsWith("Could not rename: give the head a name", editor.RenameHead(lamp.Id, "  \"\" "));
        Assert.Null(session.Checkpoint());   // refused: nothing changed

        string status = editor.RenameHead(lamp.Id, "Spot Jardin");
        Assert.StartsWith($"Head {lamp.HeadNumber} is now \"Spot Jardin\"", status);
        Assert.All(doc.RigItems.Where(r => r.IsFixture && r.HeadNumber == lamp.HeadNumber), r => Assert.Equal("Spot Jardin", doc.LampPatchOf(r).HeadName));
        Assert.Equal($"Rename head {lamp.HeadNumber}", session.Checkpoint());
        Assert.Contains("already named", editor.RenameHead(lamp.Id, "Spot Jardin"));

        // A lamp whose MagicVis head differs from the head patched at its address (RT2025_031: address 212, head 402) shows
        // the name of the patch at its address: that is the one renamed.
        var odd = doc.RigItems.First(r => r.IsFixture && r.HeadNumber > 0 && doc.HeadAt(r.Address) > 0 && doc.HeadAt(r.Address) != r.HeadNumber);
        int patched = doc.HeadAt(odd.Address);
        Assert.StartsWith($"Head {patched} is now \"Mitte\"", editor.RenameHead(odd.Id, "Mitte"));
        Assert.Equal("Mitte", doc.LampPatchOf(odd).HeadName);
        Assert.Equal($"Rename head {patched}", session.Checkpoint());

        session.Undo();
        session.Undo();
        Assert.Empty(session.UnsavedChanges());
        string again = Path.Combine(_out, "after_rename_undo.shw");
        session.Model.Show.Save(again);
        Assert.Equal(File.ReadAllBytes(show), File.ReadAllBytes(again));
    }

    /// <summary>The smoke of a cue is blocking: one named undo step, undone it is gone again (and so is the unsaved mark).</summary>
    [Fact]
    public void SmokeInACueIsOneUndoStep()
    {
        var (session, model, _) = Open();
        int cue = model.Show.CueStacks[0].Steps[0].CueId;
        Assert.Null(session.Checkpoint());
        session.NameNextEdit("Smoke in cue 1");
        model.Blocking.Smoke[cue] = new CueSmoke { Amount = 50, Time = 10f };
        Assert.Equal("Smoke in cue 1", session.Checkpoint());
        Assert.Contains("blocking", session.UnsavedChanges());

        Assert.Equal("Smoke in cue 1", session.Undo());
        Assert.Empty(model.Blocking.Smoke);
        Assert.Empty(session.UnsavedChanges());
        Assert.Equal("Smoke in cue 1", session.Redo());
        Assert.Equal(50.0, model.Blocking.Smoke[cue].Amount);
        Assert.Equal(10f, model.Blocking.Smoke[cue].Time);
    }

    /// <summary>Loading a show starts a new history.</summary>
    [Fact]
    public void LoadingForgetsTheHistory()
    {
        var (session, model, _) = Open();
        model.Blocking.Actors.Add(new ActorDef { Id = "a3", Name = "Cis" });
        session.Checkpoint();
        session.Load();
        Assert.Null(session.UndoName);
        Assert.Null(session.Undo());
    }

    /// <summary>
    /// After an undo or redo that puts another show document into the model, the editors work on that document: an edit
    /// made afterwards (a gel, a cue level, a cue's text) is in the show the model has, not in the one that was replaced
    /// (until 12 Oct 2026 the session's editor kept the replaced document and such edits were silently lost).
    /// </summary>
    [Fact]
    public void EditsAfterAnUndoGoIntoTheShowTheModelHas()
    {
        var (session, model, _) = Open();
        IShowDocument doc = model.Show;
        var lamp = doc.RigItems.First(r => r.IsFixture && r.HeadNumber > 0);
        var editor = new RigEditor<MagicQShow>(session, new NullStage());
        editor.SetGel(new[] { lamp.Id }, 1201);
        session.Checkpoint();
        session.Undo();   // the show document is replaced by the one before the gel
        Assert.NotSame(doc, session.Model.Show);

        editor.SetGel(new[] { lamp.Id }, 1202);
        IShowDocument now = session.Model.Show;
        int head = now.HeadAt(lamp.Address);
        Assert.All(session.Model.Show.Patch.Where(p => p.HeadNumber == head), p => Assert.Equal(1202, p.Gel));

        Assert.True(session.Editor.RenameHead(head, "after undo") > 0);
        Assert.All(session.Model.Show.Patch.Where(p => p.HeadNumber == head), p => Assert.Equal("after undo", p.HeadName));
    }
}
