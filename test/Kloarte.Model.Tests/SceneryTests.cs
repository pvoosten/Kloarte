using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// Scenery (set pieces, user decisions of 27 Oct 2026): the pieces and their changes per cue round trip through
/// <c>blocking/scenery.yaml</c> and <c>blocking/steps.yaml</c> (defaults left out, broken entries reported), they track
/// per cue like the smoke, and a curtain opens over its time on the timeline.
/// </summary>
public sealed class SceneryTests : TestBase
{
    /// <summary>Fresh output folder for every test.</summary>
    public SceneryTests() : base("model_scenery")
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, true);
        Directory.CreateDirectory(_out);
    }

    /// <summary>A blocking with one piece of each shape and changes in cues 5 and 7.</summary>
    private static Blocking Sample()
    {
        var blocking = new Blocking();
        var block = SceneryPiece.New("block", "Block", SceneryShape.Block);
        block.Position = new[] { 1.5f, 0f, -2f };
        block.Rotation = 30f;
        block.Width = 2f;
        block.Color = "ff0000";
        block.Material = SceneryMaterial.Wood;
        blocking.Scenery.Add(block);
        blocking.Scenery.Add(SceneryPiece.New("pyramid", "Pyramid", SceneryShape.Pyramid));
        var table = SceneryPiece.New("table", "Table", SceneryShape.Table);
        table.LegThickness = 0.08f;
        blocking.Scenery.Add(table);
        var curtain = SceneryPiece.New("curtain", "Front curtain", SceneryShape.Curtain);
        curtain.Split = true;
        curtain.Open = 0f;
        curtain.Material = SceneryMaterial.Gauze;
        blocking.Scenery.Add(curtain);
        blocking.SceneryChangeOf(5, "block", true).Visible = false;
        var open = blocking.SceneryChangeOf(5, "curtain", true);
        open.Open = 100f;
        open.Time = 4f;
        blocking.SceneryChangeOf(7, "block", true).Visible = true;
        return blocking;
    }

    /// <summary>The pieces and the changes per cue come back from their files; values equal to the shape's default are not written.</summary>
    [Fact]
    public void SceneryRoundTripsThroughItsFiles()
    {
        var blocking = Sample();
        var cues = CueIndex.Of(null);
        string scenery = FileCodec.Current.Write(BlockingFiles.SceneryOf(blocking), null, Enumerable.Empty<string>());
        string steps = FileCodec.Current.Write(BlockingFiles.StepsOf(blocking, cues), null, Enumerable.Empty<string>());
        Assert.DoesNotContain("topThickness", scenery);   // the table's default
        Assert.Contains("legThickness: 0.08", scenery);
        Assert.Contains("material: gauze", scenery);
        Assert.Contains("\"#ff0000\"", scenery);

        var (sceneryFile, p1) = FileCodec.Current.Read<SceneryFile>("scenery.yaml", scenery);
        var (stepsFile, p2) = FileCodec.Current.Read<StepsFile>("steps.yaml", steps);
        Assert.Empty(p1.Concat(p2));
        var problems = new List<FileProblem>();
        var back = BlockingFiles.FromFiles(null, stepsFile, sceneryFile, cues, "steps.yaml", "scenery.yaml", problems);
        Assert.Empty(problems);
        Assert.Equal(new[] { "block", "pyramid", "table", "curtain" }, back.Scenery.Select(p => p.Id));
        var block = back.FindPiece("block");
        Assert.Equal((SceneryShape.Block, 2f, 30f, "ff0000", SceneryMaterial.Wood), (block.Shape, block.Width, block.Rotation, block.Color, block.Material));
        Assert.Equal(new[] { 1.5f, 0f, -2f }, block.Position);
        Assert.Equal(0.08f, back.FindPiece("table").LegThickness, 3);
        Assert.Equal(0.75f, back.FindPiece("table").Height, 3);
        var curtain = back.FindPiece("curtain");
        Assert.True(curtain.Split && curtain.Opens);
        Assert.Equal((SceneryShape.Curtain, 50f, SceneryMaterial.Gauze), (curtain.Shape, curtain.Fullness, curtain.Material));
        Assert.False(back.SceneryChangeOf(5, "block").Visible);
        Assert.Equal((100f, 4f), (back.SceneryChangeOf(5, "curtain").Open.Value, back.SceneryChangeOf(5, "curtain").Time.Value));
        Assert.True(back.SceneryChangeOf(7, "block").Visible);
        Assert.Equal(BlockingFiles.SceneryOf(blocking).Pieces.Count, BlockingFiles.SceneryOf(back).Pieces.Count);
        Assert.Equal(FileCodec.Current.Write(BlockingFiles.SceneryOf(blocking), null, Enumerable.Empty<string>()),
            FileCodec.Current.Write(BlockingFiles.SceneryOf(back), null, Enumerable.Empty<string>()));
    }

    /// <summary>A piece without an id, with an unknown shape or a duplicate id is left out with a warning; the rest loads.</summary>
    [Fact]
    public void BrokenPiecesAreReported()
    {
        var file = new SceneryFile
        {
            Pieces =
            {
                new SceneryPieceEntry { Name = "nameless" },
                new SceneryPieceEntry { Id = "x", Shape = "sphere" },
                new SceneryPieceEntry { Id = "a", Shape = "block" },
                new SceneryPieceEntry { Id = "a", Shape = "table" },
                new SceneryPieceEntry { Id = "b" },
            },
        };
        var problems = new List<FileProblem>();
        var blocking = BlockingFiles.FromFiles(null, null, file, CueIndex.Of(null), "", "scenery.yaml", problems);
        Assert.Equal(new[] { "a", "b" }, blocking.Scenery.Select(p => p.Id));
        Assert.Equal(SceneryShape.Block, blocking.FindPiece("a").Shape);
        Assert.Equal(SceneryShape.Block, blocking.FindPiece("b").Shape);   // missing shape = a block
        Assert.Equal(3, problems.Count);
        Assert.All(problems, p => Assert.False(p.IsError));
    }

    /// <summary>Tracking: a piece's state after cues = its own + all changes up to them; removing a piece removes its changes.</summary>
    [Fact]
    public void SceneryTracksPerCue()
    {
        var blocking = Sample();
        Assert.Equal(new SceneryFrame(true, 0f), blocking.SceneryAfter("curtain", Array.Empty<int>()));
        Assert.Equal(new SceneryFrame(false, 0f), blocking.SceneryAfter("block", new[] { 5 }));
        Assert.Equal(new SceneryFrame(false, 0f), blocking.SceneryAfter("block", new[] { 5, 6 }));
        Assert.Equal(new SceneryFrame(true, 0f), blocking.SceneryAfter("block", new[] { 5, 6, 7 }));
        Assert.Equal(new SceneryFrame(true, 100f), blocking.SceneryAfter("curtain", new[] { 5, 7 }));
        blocking.RemovePiece("block");
        Assert.Null(blocking.FindPiece("block"));
        Assert.False(blocking.SceneryCues.ContainsKey(7));
        Assert.True(blocking.SceneryCues[5].ContainsKey("curtain"));
    }

    /// <summary>
    /// The timeline: a piece goes out at its cue's GO, a curtain eases open over its time from where it is, a later cue
    /// starting while it moves goes on from there, a jump to a cue gives the settled state.
    /// </summary>
    [Fact]
    public void SceneryOnTheTimeline()
    {
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var stack = show.CueStacks.First(s => s.Name == "show");
        var ids = stack.Steps.Select(s => s.CueId).ToList();
        var blocking = new Blocking { Holds = { [ids[1]] = 20f } };
        blocking.Scenery.Add(SceneryPiece.New("box", "Box", SceneryShape.Block));
        var curtain = SceneryPiece.New("curtain", "Curtain", SceneryShape.Curtain);
        curtain.Split = true;
        blocking.Scenery.Add(curtain);
        blocking.SceneryChangeOf(ids[1], "box", true).Visible = false;
        blocking.SceneryChangeOf(ids[1], "curtain", true).Open = 100f;
        blocking.SceneryChangeOf(ids[1], "curtain").Time = 10f;
        blocking.SceneryChangeOf(ids[2], "curtain", true).Open = 0f;
        blocking.SceneryChangeOf(ids[2], "curtain").Time = 2f;
        var tl = new PlaybackTimeline(show, stack, blocking, 3f);
        var go2 = tl.Segments[1];
        var go3 = tl.Segments[2];

        Assert.Equal(new[] { "box", "curtain" }, tl.Scenery);
        Assert.True(go2.ActiveEnd >= go2.Start + 10 - 1e-9, "a GO runs until its curtain has finished moving");
        Assert.Equal(new SceneryFrame(true, 0f), tl.SceneryAt("box", -1));
        Assert.True(tl.SceneryAt("box", go2.Start - 0.01).Visible);
        Assert.False(tl.SceneryAt("box", go2.Start).Visible);
        Assert.False(tl.SceneryAt("box", tl.Length).Visible);   // tracks
        Assert.Equal(50f, tl.SceneryAt("curtain", go2.Start + 5).Open, 3);   // smoothstep: halfway at half time
        Assert.Equal(100f, tl.SceneryAt("curtain", go3.Start).Open, 3);   // open by the next GO (hold 20 s)
        Assert.Equal(50f, tl.SceneryAt("curtain", go3.Start + 1).Open, 3);   // closing from there
        Assert.Equal(0f, tl.SceneryAt("curtain", go3.Start + 2).Open, 3);
        Assert.Equal(100f, tl.SceneryAfter("curtain", 1).Open);
        Assert.Equal(0f, tl.SceneryAfter("curtain", 2).Open);
        Assert.Equal(new SceneryFrame(true, 0f), tl.SceneryAfter("curtain", -1));
        Assert.Contains(tl.KeyFrames(), k => k.What.Contains("curtain 0 % open"));
    }

    /// <summary>
    /// The scenery survives the blocking JSON (undo snapshots and the movie process, which builds the scene from the
    /// blocking.json the app saved for it).
    /// </summary>
    [Fact]
    public void SceneryRoundTripsThroughTheBlockingJson()
    {
        var model = ShowModel<MagicQShow>.Load(MagicQShow.Load(Show("RT2025_031.shw")), Show("RT2025_031.shw"));
        foreach (var piece in Sample().Scenery)
            model.Blocking.Scenery.Add(piece);
        model.Blocking.SceneryChangeOf(5, "curtain", true).Open = 80f;
        string path = Path.Combine(_out, "blocking.json");
        model.SaveBlocking(path);
        var loaded = ShowModel<MagicQShow>.Load(model.Show, Show("RT2025_031.shw"), blockingPath: path);

        Assert.Equal(model.Blocking.Scenery.Select(p => (p.Id, p.Shape, p.Width, p.Material, p.Split)),
            loaded.Blocking.Scenery.Select(p => (p.Id, p.Shape, p.Width, p.Material, p.Split)));
        Assert.Equal(80f, loaded.Blocking.SceneryChangeOf(5, "curtain").Open);
    }

    /// <summary>The pleats: a flat cloth has no depth, more fullness gives deeper pleats.</summary>
    [Fact]
    public void FullnessGivesPleatDepth()
    {
        var curtain = SceneryPiece.New("c", "C", SceneryShape.Curtain);
        curtain.Fullness = 0f;
        Assert.Equal(0f, curtain.PleatDepth);
        curtain.Fullness = 50f;
        float half = curtain.PleatDepth;
        curtain.Fullness = 100f;
        Assert.True(half > 0.05f && curtain.PleatDepth > half && curtain.PleatDepth < 0.5f, $"pleat depth {half:0.000} / {curtain.PleatDepth:0.000}");
    }
}
