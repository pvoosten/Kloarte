using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Playback test: programs some blocking on the "show" stack, plays it with GO and checks fades and actor moves
/// halfway and at the end; screenshots in tests/output/playback_*.png. Nothing is saved.
/// Run:  Godot_console.exe --path . --resolution 1280x720 res://tests/playback_test.tscn
/// </summary>
public partial class PlaybackTest : Node
{
    private Node _scene;
    private ShowBuilder _builder;
    private CuePlayer _player;
    private int _frame, _failures;
    private string _walker, _lier, _leaver, _newcomer;
    private ProfileFixture _probe;
    private float _probeFrom, _probeTo, _closestToWaypoint = float.MaxValue;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        _scene = TestShow.Load("playback");   // a copy of the show: the scene opens none by itself
        AddChild(_scene);
        _scene.GetNode<CanvasLayer>("FixturePanel").Visible = false;
        _builder = _scene.GetNode<ShowBuilder>("Show");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frame++;
        if (_frame > 40 && _frame < 340 && _walker != null)
            _closestToWaypoint = Mathf.Min(_closestToWaypoint, _builder.Actors[_walker].Position.DistanceTo(new Vector3(2f, 0f, -2f)));
        if (_frame == 10)
            Setup();
        if (_frame == 40)
        {
            Shot("playback_0_before");
            _probeFrom = _probe.Intensity;
            _player.Go();   // -> "1 start" (3 s fades)
            GD.Print($"GO {_player.Stack.Steps[_player.Step].CueNumberText}: running={_player.Running}");
        }
        if (_frame == 40 + 45)   // ~1.5 s at 30 fps... frame rate varies; check that we're mid-way
        {
            var w = _builder.Actors[_walker];
            GD.Print($"mid: probe intensity {_probe.Intensity:0.00} (from {_probeFrom:0.00} to {_probeTo:0.00}), " +
                     $"walker at {w.Position}, pose {w.Pose}, running={_player.Running}");
            Check(_player.Running, "cue still running mid-way");
            Shot("playback_1_mid");
        }
        if (_frame == 40 + 300)
        {
            var w = _builder.Actors[_walker];
            var l = _builder.Actors[_lier];
            var n = _builder.Actors[_newcomer];
            GD.Print($"end: probe {_probe.Intensity:0.00}, walker {w.Position} {w.Pose} facing {w.Facing:0}, " +
                     $"lier {l.Pose}, newcomer visible={n.Visible} at {n.Position}");
            Check(Mathf.IsEqualApprox(_probe.Intensity, _probeTo), "probe reached its level");
            Check(w.Position.DistanceTo(new Vector3(2f, 0f, -4f)) < 0.01f && w.Pose == ActorPose.Seated, "walker seated at the second waypoint");
            Check(Mathf.Abs(Mathf.Wrap(w.Facing - 90f, -180f, 180f)) < 1f, "walker turned to 90°");
            Check(_closestToWaypoint < 0.05f, $"walker passed the first waypoint (closest {_closestToWaypoint:0.00} m)");
            Check(Mathf.IsEqualApprox(w.GetChild<Node3D>(0).Scale.Y, 1.95f / 1.76f), "walker is 1.95 m tall");
            Check(l.Pose == ActorPose.Lying, "lier is lying");
            Check(n.Visible, "newcomer appeared");
            Shot("playback_2_end");
            _player.Go();   // -> "1.1": leaver disappears, newcomer walks downstage in 4 s
        }
        if (_frame == 40 + 300 + 300)
        {
            var leaver = _builder.Actors[_leaver];
            var n = _builder.Actors[_newcomer];
            GD.Print($"cue 1.1: leaver visible={leaver.Visible}, newcomer at {n.Position}");
            Check(!leaver.Visible, "leaver disappeared");
            Check(n.Position.DistanceTo(new Vector3(-1f, 0f, -1f)) < 0.01f, "newcomer walked downstage");
            Shot("playback_3_next");
            _player.Back();
            Check(leaver.Visible && n.Position.DistanceTo(new Vector3(0f, 0f, -4f)) < 0.01f, "Back restores the previous cue");
            GD.Print(_failures == 0 ? "PLAYBACK OK" : $"PLAYBACK FAILURES: {_failures}");
            GetTree().Quit(_failures == 0 ? 0 : 1);
        }
    }

    private void Setup()
    {
        _player = new CuePlayer { Builder = _builder };
        AddChild(_player);
        var stack = _builder.Document.CueStacks.First(s => s.Name == "show");
        _player.Select(stack);
        _player.Jump(1);   // "0.1 FO": everything off
        var steps = stack.Steps;
        int start = steps.First(s => s.CueNumberText == "1").CueId;
        int next = steps.First(s => s.CueNumberText == "1.1").CueId;

        var blocking = _builder.Model.Blocking;
        var ids = _builder.Actors.Keys.ToList();
        (_walker, _lier, _leaver) = (ids[0], ids[1], ids[2]);
        // Two movement steps in one cue: walk to (2, -2), wait 0.5 s, walk to (2, -4), sit down facing 90°.
        blocking.Record(start, _walker, 0, new ActorState { Position = new[] { 2f, 0f, -2f }, MoveTime = 1.5f });
        blocking.Record(start, _walker, 1, new ActorState { Delay = 0.5f, Position = new[] { 2f, 0f, -4f }, Pose = ActorPose.Seated, Facing = 90f });
        blocking.Find(_walker).Height = 1.95f;
        _builder.Actors[_walker].SetHeight(1.95f);
        blocking.Record(start, _lier, 0, new ActorState { Pose = ActorPose.Lying });
        var newcomer = _builder.AddActor("Newcomer", true, new Vector3(0, 0, -4));
        _newcomer = newcomer.ActorId;
        blocking.Find(_newcomer).Initial.Visible = false;
        blocking.Record(start, _newcomer, 0, new ActorState { Visible = true });
        blocking.Record(next, _leaver, 0, new ActorState { Visible = false });
        blocking.Record(next, _newcomer, 0, new ActorState { Position = new[] { -1f, 0f, -1f }, MoveTime = 4f });
        newcomer.Apply(_builder.ActorStateAt(_newcomer, stack, _player.Step));
        _player.Invalidate();   // the blocking changed: lay out the timeline again

        // A fixture that goes up in cue "1": Binnen T (address 150).
        _probe = _builder.GetNode("Fixtures").GetChildren().OfType<ProfileFixture>().First(f => f.DmxAddress == 150);
        _probeTo = _builder.CueLevels(stack, steps.ToList().FindIndex(s => s.CueId == start))[_probe];
        var cam = _scene.GetNode<FlyCamera>("Camera");
        cam.GlobalPosition = new Vector3(0, 1.8f, 6.5f);
        cam.LookAt(new Vector3(0, 1.0f, -4f));
    }

    private void Check(bool ok, string what)
    {
        if (!ok)
        {
            _failures++;
            GD.PrintErr($"FAIL: {what}");
        }
    }

    private void Shot(string name) =>
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://tests/output/{name}.png"));
}
