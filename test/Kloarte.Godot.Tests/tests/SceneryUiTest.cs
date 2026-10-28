using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;
using SysVec2 = System.Numerics.Vector2;

namespace Kloarte.Tests;

/// <summary>
/// The set pieces' editing UI (rendered), Risquons-Tout, stack "show", Blocking workspace:
/// 1. "+ Piece" > Block in the Scenery tab adds a piece and selects it; the inspector edits its width and colour;
/// 2. in the plan a drag moves it (5 cm grid), a drag with the turn key held turns it (15° steps);
/// 3. in cue 1 the inspector sets it Out (the plan draws it as out), then Undo takes that back;
/// 4. in the 3D view a click on it selects it and a drag moves it over the floor;
/// 5. Duplicate and Delete through the key map, Undo after the delete brings it back.
/// Screenshots tests/output/scenery_*.png. Prints "SCENERY UI OK".
/// Run:  Godot_console.exe --path . --resolution 1600x900 res://tests/scenery_ui_test.tscn
/// </summary>
public partial class SceneryUiTest : Node
{
    /// <summary>The show scene.</summary>
    private Node _scene;

    /// <summary>The scene's panel.</summary>
    private FixturePanel _panel;

    /// <summary>The scene's show.</summary>
    private ShowBuilder _builder;

    /// <summary>The panel's playback.</summary>
    private CuePlayer _player;

    /// <summary>Failed checks.</summary>
    private int _failures;

    /// <summary>Loads the show and runs the steps.</summary>
    public override async void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        ViewHost.Persist = false;   // don't touch the user's view layout
        GoboLibrary.Folder = "res://tests/output/gobos";
        // A copy of the show in a folder of the test's own: nothing is written to MagicQ's folder.
        string output = ProjectSettings.GlobalizePath("res://tests/output/scenery");
        if (Directory.Exists(output))
            Directory.Delete(output, true);
        _scene = TestShow.Load("scenery");
        AddChild(_scene);
        _panel = _scene.GetNode<FixturePanel>("FixturePanel");
        _builder = _scene.GetNode<ShowBuilder>("Show");
        await Frames(20);
        _player = _panel.GetNode<CuePlayer>("CuePlayer");
        _player.SetStack(_builder.Document.CueStacks.FirstOrDefault(s => s.Name == "show")
                         ?? _builder.Document.CueStacks.OrderByDescending(s => s.Steps.Count).First());
        _panel.Host.SetWorkspace(Workspace.Blocking);
        await Frames(5);

        try
        {
            string id = await AddAndEdit();
            await PlanDrag(id);
            await OutInCue(id);
            await Click3D(id);
            await DuplicateAndDelete(id);
            await ShapeChange(id);
        }
        catch (Exception e)
        {
            Check(false, $"exception: {e}");
        }
        GD.Print(_failures == 0 ? "SCENERY UI OK" : $"SCENERY UI FAILURES: {_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>The blocking of the show.</summary>
    private Blocking Blocking => _builder.Model.Blocking;

    /// <summary>Adds a block through "+ Piece", edits its width and colour in the inspector; returns its id.</summary>
    private async Task<string> AddAndEdit()
    {
        int before = Blocking.Scenery.Count;
        var add = All<MenuButton>().First(b => b.Text == "+ Piece");
        add.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed, (long)(int)SceneryShape.Block);
        await Frames(10);
        Check(Blocking.Scenery.Count == before + 1, $"+ Piece > Block adds a piece ({Blocking.Scenery.Count})");
        var piece = Blocking.Scenery.Last();
        Check(piece.Shape == SceneryShape.Block && Selection.Piece == piece.Id, $"the new {piece.Name} is selected");
        Check(Visible<Label>().Any(l => l.Text == piece.Name), "the inspector shows the piece");
        Check(Visible<Label>().All(l => l.Text != "Top thickness") && Visible<Label>().Any(l => l.Text == "Depth"),
              "a block has width / depth / height, no table fields");

        Spin("Width").Value = 2.5;
        await Frames(12);   // the scene shows it (rebuild) and the inspector is built again
        Check(Mathf.IsEqualApprox(piece.Width, 2.5f), $"width typed: {piece.Width}");
        var colour = All<ColorPickerButton>().First(c => c.IsVisibleInTree() && c.TooltipText.StartsWith("The piece's colour"));
        colour.Color = new Color(1f, 0f, 0f);
        colour.EmitSignal(ColorPickerButton.SignalName.PopupClosed);
        await Frames(12);
        Check(piece.Color == "ff0000", $"colour picked: {piece.Color}");
        Check(Selection.Piece == piece.Id, "still selected after the scene was rebuilt");
        Shot("scenery_inspector.png");
        return piece.Id;
    }

    /// <summary>Moves the piece in the plan by dragging, then turns it with the turn key held.</summary>
    private async Task PlanDrag(string id)
    {
        var plan = _panel.Host.Find<PlanView>();
        _panel.Host.SetMain(plan);
        await Frames(10);
        plan.Fit();
        await Frames(5);
        var piece = Blocking.FindPiece(id);
        var from = plan.ToView(new SysVec2(piece.Position[0], piece.Position[2]));
        var to = plan.ToView(new SysVec2(piece.Position[0] + 1f, piece.Position[2] + 0.5f));
        float x0 = piece.Position[0], z0 = piece.Position[2];
        Drag(plan, from, to);
        await Frames(12);
        Check(Mathf.Abs(piece.Position[0] - (x0 + 1f)) < 0.06f && Mathf.Abs(piece.Position[2] - (z0 + 0.5f)) < 0.06f,
              $"dragged in the plan: ({x0:0.00}, {z0:0.00}) -> ({piece.Position[0]:0.00}, {piece.Position[2]:0.00})");
        Check(Mathf.IsEqualApprox(piece.Position[0] / 0.05f, Mathf.Round(piece.Position[0] / 0.05f)), "on the 5 cm grid");

        // Turn: grab 1 m to the right of the centre, go round to 1 m upstage of it (90° counter-clockwise from above).
        plan.Refresh();   // the selection as it is after the scene was rebuilt
        var centre = new SysVec2(piece.Position[0], piece.Position[2]);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = true });
        await Frames(2);
        Drag(plan, plan.ToView(centre + new SysVec2(0.6f, 0f)), plan.ToView(centre + new SysVec2(0f, -0.6f)));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Shift, PhysicalKeycode = Key.Shift, Pressed = false });
        await Frames(12);
        Check(Mathf.Abs(piece.Rotation - 90f) < 0.5f, $"turned in the plan with the turn key: {piece.Rotation:0.#}°");
        Check(plan.Data.Pieces.Any(p => p.Piece.Id == id && p.Selected), "the plan draws it selected");
        Shot("scenery_plan.png");
    }

    /// <summary>Sets the piece Out in cue 1 in the inspector; the plan draws it as out; Undo takes it back.</summary>
    private async Task OutInCue(string id)
    {
        var steps = _player.Stack.Steps;
        int index = Math.Max(0, steps.ToList().FindIndex(s => s.CueNumberText == "1"));
        _player.Jump(index);
        await Frames(10);
        Selection.SelectPiece(id);
        await Frames(3);
        int cue = steps[index].CueId;
        var inOut = Row<OptionButton>("In this cue");
        inOut.Select(2);
        inOut.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        await Frames(40);   // recorded when settled (the unsaved poll)
        Check(Blocking.SceneryChangeOf(cue, id)?.Visible == false, "Out in cue 1");
        var plan = _panel.Host.Find<PlanView>();
        plan.Refresh();
        Check(plan.Data.Pieces.Any(p => p.Piece.Id == id && !p.Visible), "the plan draws it as out in cue 1");
        Check(Visible<Label>().Any(l => l.Text.Contains("This cue takes it out")), "the inspector says what the cue does");
        Shot("scenery_out.png");

        Input.ParseInputEvent(new InputEventAction { Action = "edit_undo", Pressed = true });
        await Frames(15);
        Check(Blocking.SceneryChangeOf(cue, id) == null, "Undo takes the Out back");
        Check(Blocking.FindPiece(id) is { Rotation: > 89f }, "... and only that (the turn stays)");
        _player.Jump(-1);
        await Frames(5);
    }

    /// <summary>In the 3D view a click on the piece selects it and a drag moves it over the floor.</summary>
    private async Task Click3D(string id)
    {
        var view = _panel.Host.Find<View3D>();   // the first 3D view: "3D"
        _panel.Host.SetMain(view);
        await Frames(10);
        Selection.Clear();
        var piece = Blocking.FindPiece(id);
        float x0 = piece.Position[0];
        Vector2? at = null;
        // A point on the piece's top that no lamp claims (lamps within 24 px of a click win).
        foreach (var (dx, dz) in new[] { (0f, 0f), (0.3f, 0.2f), (-0.3f, -0.2f), (0.2f, -0.3f), (-0.2f, 0.3f) })
        {
            var world = _builder.ToGlobal(new Vector3(piece.Position[0] + dx, piece.Position[1] + piece.Height, piece.Position[2] + dz));
            if (view.ToView(world) is not { } p)
                continue;
            Selection.Clear();
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p });
            if (Selection.Piece == id)
            {
                at = p;
                break;
            }
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p });
        }
        Check(at != null, "a click on the piece in 3D selects it");
        if (at is not { } start)
            return;
        view._GuiInput(new InputEventMouseMotion { Position = start + new Vector2(60, 0), ButtonMask = MouseButtonMask.Left });
        view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = start + new Vector2(60, 0) });
        await Frames(12);
        Check(Mathf.Abs(Blocking.FindPiece(id).Position[0] - x0) > 0.05f, $"dragged in 3D: x {x0:0.00} -> {Blocking.FindPiece(id).Position[0]:0.00}");
        Shot("scenery_3d.png");
    }

    /// <summary>Duplicate and Delete through the key map; Undo brings the deleted copy back.</summary>
    private async Task DuplicateAndDelete(string id)
    {
        Selection.SelectPiece(id);
        int count = Blocking.Scenery.Count;
        Input.ParseInputEvent(new InputEventAction { Action = "scenery_duplicate", Pressed = true });
        await Frames(12);
        Check(Blocking.Scenery.Count == count + 1 && Selection.Piece != id, "Duplicate adds a copy and selects it");
        string copy = Selection.Piece;
        Input.ParseInputEvent(new InputEventAction { Action = "scenery_delete", Pressed = true });
        await Frames(12);
        Check(Blocking.Scenery.Count == count && Blocking.FindPiece(copy) == null, "Delete removes the copy");
        Input.ParseInputEvent(new InputEventAction { Action = "edit_undo", Pressed = true });
        await Frames(15);
        Check(Blocking.FindPiece(copy) != null, "Undo brings it back");
    }

    /// <summary>Another shape in the inspector: the inspector is built again with that shape's fields.</summary>
    private async Task ShapeChange(string id)
    {
        Selection.SelectPiece(id);
        await Frames(5);
        var shape = Row<OptionButton>("Shape");
        int table = shape.GetItemIndex((int)SceneryShape.Table);
        shape.Select(table);
        shape.EmitSignal(OptionButton.SignalName.ItemSelected, (long)table);
        await Frames(12);
        Check(Blocking.FindPiece(id).Shape == SceneryShape.Table, "the shape is a table");
        Check(Visible<Label>().Any(l => l.Text == "Top thickness") && Visible<Label>().Any(l => l.Text == "Length"),
              "the inspector shows the table's fields after the shape change");
    }

    /// <summary>A left drag in a view: press, two moves, release.</summary>
    private static void Drag(Control view, Vector2 from, Vector2 to)
    {
        view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from });
        view._GuiInput(new InputEventMouseMotion { Position = (from + to) / 2, ButtonMask = MouseButtonMask.Left });
        view._GuiInput(new InputEventMouseMotion { Position = to, ButtonMask = MouseButtonMask.Left });
        view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to });
    }

    /// <summary>The control of type T in the visible inspector row labelled `label`.</summary>
    private T Row<T>(string label) where T : Control =>
        Visible<Label>().First(l => l.Text == label).GetParent().GetChildren().OfType<T>().First();

    /// <summary>The spin box of the visible row labelled `label`.</summary>
    private SpinBox Spin(string label) => Row<SpinBox>(label);

    /// <summary>Every control of a type in the scene.</summary>
    private T[] All<T>() where T : Node => _scene.FindChildren("*", typeof(T).Name, true, false).OfType<T>().ToArray();

    /// <summary>Every visible control of a type in the scene.</summary>
    private T[] Visible<T>() where T : CanvasItem => All<T>().Where(c => c.IsVisibleInTree()).ToArray();

    /// <summary>Waits n frames.</summary>
    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>Saves a screenshot in tests/output.</summary>
    private void Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://tests/output/" + name);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }

    /// <summary>Prints a check's result and counts a failure.</summary>
    private void Check(bool ok, string what)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
