using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using SysVec2 = System.Numerics.Vector2;
using Kloarte.GodotBinding;
using Kloarte.Application;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// View host test (rendered): Risquons-Tout with all views.
/// 1. workspaces Rig / Focus / Blocking: main view, two cards, the left panel's list; screenshots views_*.png and the
///    frame rate of each; a workspace remembers a changed main view, Reset workspace, Ctrl+1;
/// 2. clicking a card makes it the main view; the "next view" key cycles; the timeline docked under the 3D / audience
///    view (views_docked.png), F5 / F1, the splitter, Shift+F5 undocks;
/// 3. audience view: sits in "Regie", the seat keys move one seat / row; actor view: the camera in an actor's head,
///    its next exit shown, and announced (banner + status bar "AF cour") when GO passes it;
/// 4. key map tool: F6 opens its window, binding GO to Enter by "pressing" it; a conflicting key offers a swap (test key
///    map file); F6 in its window closes it (keys go on to the main window);
/// 5. gobo editor tool (its window): draws a line and an arc with the mouse, saves, puts the gobo in a profile.
/// Prints "VIEWS OK". The user's key map and view layout are not touched.
/// Run:  Godot_console.exe --path . --resolution 1600x900 res://tests/views_capture.tscn
/// </summary>
public partial class ViewsCapture : Node
{
    private Node _scene;
    private FixturePanel _panel;
    private ShowBuilder _builder;
    private ViewHost _host;
    private int _failures;

    /// <summary>
    /// The actor view: the camera sits at the actor's eyes; an exit at cour in the first cue is shown as the next one,
    /// and GO announces it (the view's banner and the status bar). The blocking is put back afterwards.
    /// </summary>
    private async Task ActorViewTest()
    {
        var view = _host.Find<ActorView>();
        _host.SetMain(view);
        await Frames(10);
        string id = view.ActorId;
        var actor = id == null ? null : _builder.Actors.GetValueOrDefault(id);
        float eye = actor == null ? 0 : view.Camera.GlobalPosition.Y - actor.GlobalPosition.Y;
        Check(actor != null && eye > 1.0f && eye < 1.9f
              && new Vector2(view.Camera.GlobalPosition.X - actor.GlobalPosition.X, view.Camera.GlobalPosition.Z - actor.GlobalPosition.Z).Length() < 0.3f,
              $"actor view in {id}'s head: eyes {eye:0.00} m above its feet");
        Shot("views_actor.png");

        // A right mouse drag sideways turns the actor itself (its facing is recorded: before the first cue in its initial
        // state) and the view turns with it; up / down only looks up and down; to the right = the actor turns right.
        var def = _builder.Model.Blocking.Find(id);
        float startFacing = actor.Facing;
        view.CameraInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        view.CameraInput(new InputEventMouseMotion { Relative = new Vector2(-100, 0) });
        float turned = Mathf.Wrap(actor.Facing - startFacing, -180f, 180f);
        float afterDrag = actor.Facing;
        view.CameraInput(new InputEventMouseMotion { Relative = new Vector2(0, 40) });
        bool lookedOnly = Mathf.IsEqualApprox(actor.Facing, afterDrag);
        view.CameraInput(new InputEventMouseMotion { Relative = new Vector2(250, 0) });
        float right = Mathf.Wrap(actor.Facing - afterDrag, -180f, 180f);
        view.CameraInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        await Frames(3);
        float f = Mathf.DegToRad(actor.Facing);
        var forward = -view.Camera.GlobalTransform.Basis.Z;
        bool follows = forward.Y < 0.9f && new Vector2(forward.X, forward.Z).Normalized().DistanceTo(new Vector2(Mathf.Sin(f), Mathf.Cos(f))) < 0.35f;
        Check(Mathf.IsEqualApprox(turned, Mathf.RadToDeg(0.3f), 0.1f) && lookedOnly && right < -40f && Mathf.IsEqualApprox(def.Initial.Facing ?? 0f, actor.Facing, 0.1f)
              && follows && _panel.GetNode<CuePlayer>("CuePlayer").CueId < 0,
              $"actor view: drag left turned {turned:0.0}° (expected 17.2), up / down only looks {lookedOnly}, right {right:0.0}°, recorded {def.Initial.Facing:0.0}° = {actor.Facing:0.0}°, view follows {follows}");
        def.Initial.Facing = startFacing;
        _panel.GetNode<CuePlayer>("CuePlayer").Invalidate();
        actor.Facing = startFacing;

        var player = _panel.GetNode<CuePlayer>("CuePlayer");
        player.Jump(-1);
        int cue = player.Timeline.Segments[0].CueId;
        var blocking = _builder.Model.Blocking;
        blocking.Record(cue, id, 0, new ActorState { Position = new[] { 5.5f, 0f, -2f }, Pose = ActorPose.Standing, MoveTime = 1f, Visible = false });
        player.Invalidate();
        await Frames(3);
        string next = view.NextText;
        player.Go();
        var bar = _panel.FindChildren("*", nameof(PanelContainer), true, false).OfType<StatusBar>().First();
        for (int i = 0; i < 1800 && !view.Banner.Contains("AF cour"); i++)
            await Frames(1);
        Shot("views_actor_exit.png");
        Check(next.Contains("Next: AF cour, cue ") && view.Banner.EndsWith("AF cour") && bar.Message.Contains(": AF cour (cue "),
              $"exit announced: next \"{next}\", banner \"{view.Banner}\", status \"{bar.Message}\"");
        blocking.ClearCue(cue, id);
        player.Invalidate();
        player.Jump(-1);
        await Frames(3);
    }

    /// <summary>
    /// The timeline docked under the main view (Focus workspace, 3D view main): under it, as wide, not a card, its
    /// toolbar in the strip; it stays docked under the audience view, not under the plan; F5 makes it the main view and
    /// F1 docks it again; dragging the splitter makes it taller; Shift+F5 undocks it (a card again).
    /// </summary>
    private async Task DockTest(Control strip)
    {
        var timeline = _host.Find<TimelineView>();
        _host.TimelineDocked = true;
        await Frames(30);
        bool Under(ViewBase main) =>
            timeline.Visible && timeline.IsDocked && !timeline.Compact && Mathf.IsEqualApprox(timeline.Position.X, main.Position.X)
            && Mathf.IsEqualApprox(timeline.Size.X, main.Size.X) && timeline.Position.Y >= main.Position.Y + main.Size.Y;
        var main3D = _host.Main;
        bool under3D = main3D is View3D && Under(main3D);
        bool bars = strip.Visible && timeline.Toolbar.Visible;
        Shot("views_docked.png");
        Check(under3D && bars && _host.DockedTimeline == timeline,
              $"timeline docked under {main3D.Title}: under it {under3D}, toolbar in the strip {bars}");

        _host.SetMain(_host.Find<AudienceView>());
        await Frames(30);
        bool underAudience = Under(_host.Main);
        _host.SetMain(_host.Find<PlanView>());
        await Frames(30);
        bool notUnderPlan = !timeline.IsDocked && _host.DockedTimeline == null;
        Check(underAudience && notUnderPlan, $"docked under the audience view {underAudience}, not under the plan {notUnderPlan}");

        _host._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.F5, Pressed = true });
        await Frames(30);
        bool timelineMain = _host.Main == timeline && timeline.IsMain && !timeline.IsDocked;
        _host._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.F1, Pressed = true });
        await Frames(30);
        Check(timelineMain && Under(_host.Main), $"F5: timeline main view {timelineMain}; F1: docked again under {_host.Main.Title}");

        var splitter = _host.FindChild("TimelineSplitter", true, false) as Control;
        float before = timeline.Size.Y;
        splitter?.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Position = new Vector2(10, -100), ButtonMask = MouseButtonMask.Left });
        await Frames(2);
        Check(splitter is { Visible: true } && timeline.Size.Y > before + 50 && Under(_host.Main),
              $"splitter dragged up 100 px: timeline {before:0} -> {timeline.Size.Y:0} px high");

        _host._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.F5, ShiftPressed = true, Pressed = true });
        await Frames(30);
        Check(!_host.TimelineDocked && !timeline.IsDocked && timeline.Visible && timeline.Compact && !splitter.Visible,
              $"Shift+F5 undocks: a card again {timeline.Compact}");
    }

    /// <summary>Runs the steps.</summary>
    public override async void _Ready()
    {
        ViewHost.Persist = false;
        GoboLibrary.Folder = "res://tests/output/gobos";   // not the user's project gobos
        KeyMap.EnsureLoaded("res://tests/output/views_keymap.json");
        KeyMap.ResetAll();
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
        _panel = _scene.GetNode<FixturePanel>("FixturePanel");
        _builder = _scene.GetNode<ShowBuilder>("Show");
        _host = _panel.Host;
        await Frames(20);
        Check(_host.Views.Count == 6, $"{_host.Views.Count} views: {string.Join(", ", _host.Views.Select(v => v.Title))}");

        // Workspaces: their main view, cards and the left panel's list.
        var tabs = _panel.FindChildren("*", "TabContainer", true, false).OfType<TabContainer>().First();
        foreach (var (workspace, main, list, cardCount) in new[]
                 {
                     (Workspace.Rig, typeof(PlanView), "Fixtures", 2), (Workspace.Focus, typeof(View3D), "Fixtures", 2),
                     (Workspace.Blocking, typeof(TimelineView), "Actors", 3),
                 })
        {
            _host.SetWorkspace(workspace);
            await Frames(30);
            ulong t0 = Time.GetTicksMsec();
            await Frames(60);
            double fps = 60000.0 / System.Math.Max(1, Time.GetTicksMsec() - t0);
            Shot($"views_{workspace.ToString().ToLowerInvariant()}.png");
            int cards = _host.Views.Count(v => v.Visible && v != _host.Main);
            Check(_host.Main.GetType() == main && cards == cardCount && tabs.GetCurrentTabControl().Name == list,
                  $"workspace {workspace}: main {_host.Main.Title}, {cards} cards, list {tabs.GetCurrentTabControl().Name} ({fps:0} fps)");
        }
        // Each workspace remembers its changes; Reset workspace gives the default back; Ctrl+1 is Rig.
        _host.SetMain(_host.Find<AudienceView>());
        _host._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Key1, CtrlPressed = true, Pressed = true });
        Check(_host.Workspace == Workspace.Rig && _host.Main is PlanView, $"Ctrl+1: workspace {_host.Workspace}, main {_host.Main.Title}");
        _host.SetWorkspace(Workspace.Blocking);
        Check(_host.Main is AudienceView, $"Blocking remembers the audience view as its main view: {_host.Main.Title}");
        _host.ResetWorkspace();
        Check(_host.Main is TimelineView, $"Reset workspace: main {_host.Main.Title}");

        // Card click -> main view; the next-view key cycles.
        _host.SetWorkspace(Workspace.Focus);
        await Frames(20);
        var plan = _host.Find<PlanView>();
        var overlay = _host.GetChildren().OfType<Control>().SelectMany(c => c.GetChildren().OfType<Button>())
            .FirstOrDefault(b => b.TooltipText == $"Show {plan.Title} as the main view");
        overlay?.EmitSignal(BaseButton.SignalName.Pressed);
        Check(_host.Main == plan, "clicking the plan card makes it the main view");
        // The main view's toolbar is in the strip under the view bar, not over the drawing (UI review 29); a view without
        // one hides the strip.
        var strip = _host.GetNode<Control>("ToolStrip");
        bool planBar = strip.Visible && plan.Toolbar is { Visible: true } && strip.IsAncestorOf(plan.Toolbar)
                       && !plan.IsAncestorOf(plan.Toolbar);
        _host.SetMain(_host.Find<View3D>());
        await Frames(2);
        Check(planBar && !strip.Visible, $"tool strip: plan's toolbar in it {planBar}, hidden for the 3D view {!strip.Visible}");
        await DockTest(strip);
        _host.SetMain(plan);
        var before = _host.Main;
        _host._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Tab, Pressed = true });
        Check(_host.Main != before, $"Tab: main view {before.Title} -> {_host.Main.Title}");

        // Audience view.
        var aud = _host.Find<AudienceView>();
        _host.SetMain(aud);
        await Frames(25);
        Check(aud.Row == "N" && aud.SeatNumber == 20, $"audience view sits in {aud.Row} {aud.SeatNumber} (Regie)");
        aud.HandleKey(new InputEventKey { PhysicalKeycode = Key.Bracketright, Pressed = true });
        aud.HandleKey(new InputEventKey { PhysicalKeycode = Key.Pagedown, Pressed = true });
        Check(aud.Row == "O", $"seat keys: now {aud.Row} {aud.SeatNumber}");
        await Frames(10);
        Shot("views_audience.png");
        await ActorViewTest();

        // Key map tool: its key opens its own window; the key pressed in that window goes on to the main window and
        // closes it again.
        var keysTool = _panel.KeymapTool;
        _panel._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.F6, Pressed = true });
        await Frames(10);
        Check(keysTool.Visible && _host.Views.All(v => v is not KeymapView), $"F6 opens the key map window (visible {keysTool.Visible})");
        var keys = (KeymapView)keysTool.View;
        keys.BeginCapture("playback_go", 0);
        keys._Input(new InputEventKey { PhysicalKeycode = Key.Enter, Pressed = true });
        Check(KeyMap.Bindings("playback_go").First() == KeyBinding.Of(Key.Enter), "GO rebound to Enter by pressing it");
        keys.Offer("playback_back", 0, KeyBinding.Of(Key.Enter));
        keys.Swap();
        Check(KeyMap.Bindings("playback_back").First() == KeyBinding.Of(Key.Enter) && KeyMap.Bindings("playback_go").First() == KeyBinding.Of(Key.Backspace),
              "conflict: Back takes Enter, GO gets Back's old key (swap)");
        await Frames(5);
        ShotWindow(keysTool, "views_keymap.png");
        KeyMap.ResetAll();
        keysTool.PushInput(new InputEventKey { PhysicalKeycode = Key.F6, Pressed = true });
        await Frames(2);
        Check(!keysTool.Visible, "F6 in the key map window closes it (keys go on to the main window)");

        // Gobo editor tool.
        _panel.GoboTool.Toggle();
        await Frames(10);
        var gobo = (GoboEditorView)_panel.GoboTool.View;
        int shapes = gobo.Document.Shapes.Count;
        gobo.HandleKey(new InputEventKey { PhysicalKeycode = Key.Key2, Pressed = true });   // line tool
        gobo.Press(new SysVec2(-0.5f, 0.52f));
        gobo.Move(new SysVec2(0.5f, 0.49f), true);
        gobo.Release(new SysVec2(0.5f, 0.49f));
        gobo.HandleKey(new InputEventKey { PhysicalKeycode = Key.Key3, Pressed = true });   // arc tool
        gobo.Press(new SysVec2(0f, 0f));
        gobo.Move(new SysVec2(0.6f, 0f), true);
        gobo.Release(new SysVec2(0.6f, 0f));
        gobo.Move(new SysVec2(0f, -0.6f), false);
        gobo.Press(new SysVec2(0f, -0.6f));
        var line = gobo.Document.Shapes[^2];
        var arc = gobo.Document.Shapes[^1];
        Check(gobo.Document.Shapes.Count == shapes + 2 && line.Kind == "line" && line.A[1] == 0.5f && line.B[1] == 0.5f
              && arc.Kind == "arc" && Mathf.Abs(arc.Radius - 0.625f) < 0.001f && Mathf.Abs(arc.End - arc.Start - 270f) < 0.01f,
              $"drawn: snapped line at y {line.A[1]}, arc r {arc.Radius} sweep {arc.End - arc.Start}°");
        gobo.HandleKey(new InputEventKey { PhysicalKeycode = Key.Z, CtrlPressed = true, Pressed = true });
        Check(gobo.Document.Shapes.Count == shapes + 1, "undo removes the arc");
        gobo.HandleKey(new InputEventKey { PhysicalKeycode = Key.Y, CtrlPressed = true, Pressed = true });
        gobo.Document.Name = "zz_test_gobo";
        var profile = _builder.Fixtures.First(f => _builder.ModelOf(f)?.GoboSize != null);
        Selection.Select(profile);
        string msg = gobo.UseOnSelectedLamp();
        // A new gobo goes into the project's folder, its path relative to the project ("gobos/..."), listed first.
        string own = System.IO.Path.Combine(GoboLibrary.ProjectFolder ?? "", "gobos");
        Check(profile.Gobo != null && profile.Gobo.ResourcePath == "gobos/zz_test_gobo.png"
              && File.Exists(System.IO.Path.Combine(own, "zz_test_gobo.png")) && GoboLibrary.Images().First() == "gobos/zz_test_gobo.png"
              && GoboLibrary.Drawings().Contains("zz_test_gobo"), $"{msg} ({profile.Gobo?.ResourcePath} in {own})");
        await Frames(5);
        ShotWindow(_panel.GoboTool, "views_gobo.png");
        _panel.GoboTool.Toggle();
        foreach (var f in new[] { "zz_test_gobo.png", "zz_test_gobo.gobo.json" })
            File.Delete(System.IO.Path.Combine(own, f));

        KeyMap.EnsureLoaded(KeyMap.UserFile);
        GD.Print(_failures == 0 ? "VIEWS OK" : $"VIEWS FAILURES: {_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://tests/output/" + name);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }

    /// <summary>A tool window's own picture (it is a native window, not part of the main window's).</summary>
    private static void ShotWindow(Window window, string name)
    {
        string path = ProjectSettings.GlobalizePath("res://tests/output/" + name);
        window.GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }

    private void Check(bool ok, string what)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
