using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;
using Kloarte.Application;
using Kloarte.Host;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Timeline test (rendered): Risquons-Tout, stack "show", an actor walking in cue "1".
/// 1. timeline as the main view, screenshot tests/output/timeline.png;
/// 2. drag the end of the walk 1 s further -&gt; the step's MoveTime grows by ~1 s; drag on the ruler -&gt; the playhead
///    follows; the right-click menu (hold, delete step); the lane header (+ Actor, select / rename / actor menu on a
///    name, the inspector's step timing);
/// 3. key frames: a PNG per key frame + index.csv in tests/output/keyframes;
/// 4. movie: 5 s of the show (640x360) through the Movie Maker process -&gt; AVI with ~151 frames.
/// Prints "TIMELINE OK".
/// Run:  Godot_console.exe --path . --resolution 1600x900 res://tests/timeline_capture.tscn
/// </summary>
public partial class TimelineCapture : Node
{
    private Node _scene;
    private FixturePanel _panel;
    private ShowBuilder _builder;
    private CuePlayer _player;
    private TimelineView _view;
    private int _failures;
    private string _walker;

    /// <summary>Loads the show and runs the steps.</summary>
    public override async void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        ViewHost.Persist = false;   // don't touch the user's view layout
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
        _panel = _scene.GetNode<FixturePanel>("FixturePanel");
        _builder = _scene.GetNode<ShowBuilder>("Show");
        await Frames(20);

        _player = _panel.GetNode<CuePlayer>("CuePlayer");
        var stack = _builder.Document.CueStacks.First(s => s.Name == "show");
        _player.SetStack(stack);
        int start = stack.Steps.First(s => s.CueNumberText == "1").CueId;
        _walker = _builder.Model.Blocking.Actors[0].Id;
        _builder.Model.Blocking.Record(start, _walker, 0, new ActorState { Position = new[] { 2f, 0f, -2f }, MoveTime = 2f });
        _builder.Model.Blocking.Record(start, _walker, 1, new ActorState { Delay = 1f, Position = new[] { 2f, 0f, -4f }, Pose = ActorPose.Seated });
        _player.Invalidate();

        _view = _panel.Host.Find<TimelineView>();
        _panel.Host.SetWorkspace(Workspace.Blocking);
        _panel.Host.SetMain(_view);
        _panel.SetSidePanels(false);
        await Frames(10);
        _view.Fit();
        await Frames(5);
        Shot("timeline.png");

        DragWalk(start);
        await Menu(start);
        await LaneHeader();
        Scrub();
        await Frames(5);
        Shot("timeline_edited.png");

        await KeyFrames();
        await Exports();
        await Movie();
        GD.Print(_failures == 0 ? "TIMELINE OK" : $"TIMELINE FAILURES: {_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Mouse(InputEvent e) => _view._GuiInput(e);

    private void DragWalk(int cueId)
    {
        var step = _player.Timeline.Steps.First(s => s.ActorId == _walker && s.StepIndex == 0);
        var seg = _player.Timeline.Segments[step.Segment];
        _view.ZoomTo(seg.Start - 1, seg.End + 1);
        float y = _view.LaneCentre(0);
        var from = new Vector2(_view.TimeToX(step.Start + step.Walk), y);
        var to = new Vector2(_view.TimeToX(step.Start + step.Walk + 1.0), y);
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        Mouse(new InputEventMouseMotion { Position = to, ButtonMask = MouseButtonMask.Left });
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to });
        float move = _builder.Model.Blocking.Steps(cueId, _walker)[0].MoveTime ?? 0;
        Check(Mathf.Abs(move - 3f) < 0.15f, $"dragging the walk end: MoveTime 2 s -> {move:0.00} s");
        Check(Selection.Actor == _walker && Selection.ActorStep == 0, "the dragged step is selected");
    }

    /// <summary>
    /// Right click in the timeline (UI review 9): on a step the menu has Add / Delete step and the cue's hold; the hold
    /// is typed in a dialog; Delete step removes it.
    /// </summary>
    private async Task Menu(int cueId)
    {
        var step = _player.Timeline.Steps.First(s => s.ActorId == _walker && s.StepIndex == 1);
        var at = new Vector2(_view.TimeToX(step.Start + 0.1), _view.LaneCentre(0));
        PopupMenu Open()
        {
            Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
            return _view.FindChildren("*", nameof(PopupMenu), true, false).OfType<PopupMenu>().First(m => m.Visible);
        }
        var menu = Open();
        var items = Enumerable.Range(0, menu.ItemCount).Select(menu.GetItemText).ToList();
        Check(items.Contains("Add step here") && items.Contains("Delete step 2") && items.Any(i => i.StartsWith("Hold after cue")),
              $"timeline menu [{string.Join(" | ", items)}]");
        menu.Hide();
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(items.FindIndex(i => i.StartsWith("Hold after cue"))));
        await Frames(2);
        var dialog = _view.FindChildren("*", nameof(ConfirmationDialog), true, false).OfType<ConfirmationDialog>().First(d => d.Visible);
        dialog.FindChildren("*", nameof(SpinBox), true, false).OfType<SpinBox>().First().Value = 4.5;
        dialog.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        int holdCue = _player.Timeline.Segments[step.Segment].CueId;
        Check(_builder.Model.Blocking.Holds.TryGetValue(holdCue, out float hold) && Mathf.IsEqualApprox(hold, 4.5f),
              $"hold typed in the dialog: {hold} s");

        step = _player.Timeline.Steps.First(s => s.ActorId == _walker && s.StepIndex == 1);
        at = new Vector2(_view.TimeToX(step.Start + 0.1), _view.LaneCentre(0));
        menu = Open();
        items = Enumerable.Range(0, menu.ItemCount).Select(menu.GetItemText).ToList();
        menu.Hide();
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(items.IndexOf("Delete step 2")));
        Check(_builder.Model.Blocking.Steps(cueId, _walker).Count == 1, "Delete step from the menu");
    }

    /// <summary>
    /// The lane header (UI review 28): + Actor > Woman adds a lane; a click on a name selects the actor and the inspector
    /// says where its step is on the timeline; a double click renames (dialog); a right click opens the actor menu, whose
    /// Remove takes the lane away again.
    /// </summary>
    private async Task LaneHeader()
    {
        var blocking = _builder.Model.Blocking;
        int count = blocking.Actors.Count;
        var add = _view.FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>().First(b => b.Text == "+ Actor");
        add.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, 1);
        await Frames(2);
        var added = blocking.Actors[^1];
        Check(blocking.Actors.Count == count + 1 && added.Female, $"+ Actor > Woman adds a lane: {added.Name}");

        Vector2 NameOf(int lane) => new(20, _view.LaneCentre(lane));
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = NameOf(0) });
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = NameOf(0) });
        await Frames(2);
        var timing = _panel.FindChildren("*", nameof(Label), true, false).OfType<Label>()
            .FirstOrDefault(l => l.Text.StartsWith("On the timeline") || l.Text.StartsWith("Not on the timeline"));
        Check(Selection.Actor == _walker && timing != null && timing.Text.StartsWith("On the timeline"),
              $"a click on the name selects the actor; inspector: {timing?.Text}");

        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true, Position = NameOf(count) });
        await Frames(2);
        var rename = _panel.FindChildren("*", nameof(ConfirmationDialog), true, false).OfType<ConfirmationDialog>()
            .FirstOrDefault(d => d.Visible && d.Title == "Rename actor");
        if (rename != null)
        {
            rename.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>().First().Text = "Lane test";
            rename.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        }
        Check(added.Name == "Lane test", $"a double click on the name renames: {added.Name}");

        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = NameOf(count) });
        await Frames(2);
        var menu = _panel.FindChildren("*", nameof(PopupMenu), true, false).OfType<PopupMenu>().FirstOrDefault(m => m.Visible);
        var items = menu == null ? new() : Enumerable.Range(0, menu.ItemCount).Select(menu.GetItemText).ToList();
        Check(items.Contains("Rename Lane test...") && items.Contains("Remove Lane test"), $"right click on the name: [{string.Join(" | ", items)}]");
        if (menu != null)
        {
            menu.Hide();
            menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(items.IndexOf("Remove Lane test")));
        }
        await Frames(2);
        Check(blocking.Actors.Count == count && blocking.Find(added.Id) == null, "Remove from the actor menu takes the lane away");
    }

    private void Scrub()
    {
        _view.Fit();
        double t = _player.Timeline.Segments[3].Start + 3.0;
        var at = new Vector2(_view.TimeToX(t), 10);
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        Mouse(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        Check(System.Math.Abs(_player.Time - t) < 0.15 && _player.Step == 3, $"scrub: playhead {_player.Time:0.00} s (asked {t:0.00}), cue index {_player.Step}");
    }

    private async Task KeyFrames()
    {
        string dir = ProjectSettings.GlobalizePath("res://tests/output/keyframes");
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        int expected = _player.Timeline.KeyFrames().Count;
        string msg = await _panel.ExportKeyFrames(dir);
        GD.Print(msg);
        int pngs = Directory.GetFiles(dir, "*.png").Length;
        int rows = File.ReadAllLines(Path.Combine(dir, "index.csv")).Length - 1;
        var img = Image.LoadFromFile(Directory.GetFiles(dir, "*.png").OrderBy(f => f).Last());
        Check(pngs == expected && rows == expected && img.GetWidth() > 100, $"{pngs} key frame PNGs + index.csv (expected {expected})");
    }

    /// <summary>
    /// The export dialog (UI review 32): opened by the movie key with Movie chosen and the venue's viewpoints; stills per
    /// actor from Regie with captions (a folder per actor, only their name on their shirt); a contact sheet PDF.
    /// </summary>
    private async Task Exports()
    {
        KeyMap.Run("export_movie");
        await Frames(3);
        var dialog = _panel.FindChildren("*", nameof(ConfirmationDialog), true, false).OfType<ConfirmationDialog>()
            .FirstOrDefault(d => d.Visible && d.Title == "Export");
        var pickers = dialog?.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>().ToList();
        var what = pickers?.FirstOrDefault(o => o.TooltipText.StartsWith("Stills:"));
        var viewpoint = pickers?.FirstOrDefault(o => o.TooltipText.StartsWith("Where the camera is"));
        var names = viewpoint == null ? new System.Collections.Generic.List<string>() : Enumerable.Range(0, viewpoint.ItemCount).Select(viewpoint.GetItemText).ToList();
        Check(what?.Selected == (int)ExportKind.Movie && names.Contains("Regie") && names[0].StartsWith("3D view"),
            $"export dialog: kind {what?.Selected}, viewpoints [{string.Join(", ", names)}]");
        dialog?.Hide();

        var builder = _panel.ExportBuilder;
        int actors = builder.Model.Blocking.Actors.Count;
        string stills = ProjectSettings.GlobalizePath("res://tests/output/export_stills");
        if (Directory.Exists(stills))
            Directory.Delete(stills, true);
        int regie = names.IndexOf("Regie");
        string msg = await _panel.Export(new ExportOptions(ExportKind.Stills, "vp:0", 0, 1, true, new Vector2I(320, 180), true, stills));
        GD.Print(msg);
        var folders = Directory.Exists(stills) ? Directory.GetDirectories(stills) : System.Array.Empty<string>();
        var png = folders.SelectMany(f => Directory.GetFiles(f, "*.png")).OrderBy(f => f).FirstOrDefault();
        var img = png == null ? null : Image.LoadFromFile(png);
        bool names2 = builder.Actors.All(a => a.Value.IsInsideTree());
        Check(folders.Length == actors && actors > 0 && img?.GetWidth() == 320 && img.GetHeight() == 180 && regie > 0,
            $"stills per actor: {folders.Length} folders for {actors} actors, {img?.GetWidth()}x{img?.GetHeight()}");

        string sheet = ProjectSettings.GlobalizePath("res://tests/output/export_sheet");
        if (Directory.Exists(sheet))
            Directory.Delete(sheet, true);
        msg = await _panel.Export(new ExportOptions(ExportKind.ContactSheet, "3d", -1, 2, true, new Vector2I(320, 180), false, sheet));
        GD.Print(msg);
        var pdf = Directory.Exists(sheet) ? Directory.GetFiles(sheet, "*.pdf").FirstOrDefault() : null;
        string text = pdf == null ? "" : System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
        int pictures = (text.Length - text.Replace("/DCTDecode", "").Length) / "/DCTDecode".Length;
        Check(pictures == 4 && text.Contains("(Before the first cue) Tj") && names2, $"contact sheet: {pdf}, {pictures} pictures (expected 4)");
    }

    private async Task Movie()
    {
        string dir = ProjectSettings.GlobalizePath("res://tests/output/movie");
        Directory.CreateDirectory(dir);
        string avi = Path.Combine(dir, "test.avi");
        if (File.Exists(avi))
            File.Delete(avi);
        _player.Jump(-1);
        GD.Print(_panel.StartMovie(avi, "640x360", end: 4.0));
        // Its progress is a task in the status bar with Cancel (UI review 16).
        var bar = _panel.FindChildren("*", nameof(PanelContainer), true, false).OfType<StatusBar>().First();
        string task = null;
        for (int i = 0; i < 240 && _panel.RecordingMovie; i++)
        {
            await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
            if (bar.Task is { } t && t.Contains("frame"))
                task = t;
        }
        Check(task != null && bar.Task == null, $"movie progress in the status bar: '{task}', after: '{bar.Task}'");
        int frames = File.Exists(avi) ? AviFrames(avi) : -1;
        int expected = (int)((4.0 + ShowRender.LeadIn) * FixturePanel.MovieFps) + 1;
        Check(System.Math.Abs(frames - expected) <= 2, $"movie: {frames} frames (expected {expected}), {(File.Exists(avi) ? new FileInfo(avi).Length / 1024 : 0)} kB");
    }

    /// <summary>dwTotalFrames from the AVI main header ("avih").</summary>
    private static int AviFrames(string path)
    {
        var bytes = File.ReadAllBytes(path);
        for (int i = 0; i + 28 < bytes.Length && i < 4096; i++)
            if (bytes[i] == 'a' && bytes[i + 1] == 'v' && bytes[i + 2] == 'i' && bytes[i + 3] == 'h')
                return System.BitConverter.ToInt32(bytes, i + 8 + 16);
        return -1;
    }

    private void Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://tests/output/" + name);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }

    private void Check(bool ok, string what)
    {
        GD.Print((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
