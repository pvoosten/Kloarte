using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Application;
using Kloarte.MagicQ;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// Smoke test for the FixturePanel and rigging:
///  - group and cue dropdowns filter the list;
///  - a head is moved to another truss and repositioned, saved as a MagicQ show (tests/output) and read back;
///  - another show is opened through the file dialog's signal. Screenshots in tests/output.
/// Run:  Godot_console.exe --path . res://tests/panel_capture.tscn
/// </summary>
public partial class PanelCapture : Node
{
    /// <summary>Show opened through the file dialog signal.</summary>
    /// <remarks>
    /// Opened as a copy in tests/output: a test never hands the user's own show files to a dialog (until 11 Oct 2026 the
    /// first file dialog by index was the plan's PDF dialog, and every run wrote a PDF over "la comedie.shw").
    /// </remarks>
    [Export] public string OtherShow = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show/la_comedie_001.shw";
    private int _frame;
    /// <summary>The panel.</summary>
    private FixturePanel Panel => (FixturePanel)_panel;
    /// <summary>Lamp of the live Art-Net + slider check, and frames in which its level was not held.</summary>
    private ProfileFixture _liveLamp;
    private int _liveUnstable;

    /// <summary>The lamp of the "Record into cue" steps, and the stack's step count before the cue it inserts.</summary>
    private ProfileFixture _recordLamp;

    /// <summary>The lamp of the rigging field checks.</summary>
    private ProfileFixture _rigLamp;

    /// <summary>The lamp whose head the rename check renames.</summary>
    private ProfileFixture _renamed;
    private int _recordSteps;
    private Node _scene, _panel;

    /// <summary>Layout and main view before Present mode (they must come back).</summary>
    private (Workspace Workspace, ViewBase Main) _presentBefore;

    /// <summary>The actor's X field edited at frame 200 (must survive the edit).</summary>
    private SpinBox _editedSpin;

    /// <summary>The preferences check: dialog found, UI scale applied and the preferences as text.</summary>
    private (bool Found, float Applied, string Text) _prefsApplied;

    /// <summary>The main view's width without a selection (must stay when a lamp is selected).</summary>
    private float _mainWidth;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        ViewHost.Persist = false;   // don't touch the user's view layout
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
        _panel = _scene.GetNode("FixturePanel");
    }

    /// <summary>The fixture table (left panel).</summary>
    private FixtureTable Table => _panel.FindChildren("*", "", true, false).OfType<FixtureTable>().First();

    private T[] FindAll<T>() where T : Node =>
        _panel.FindChildren("*", typeof(T).Name, true, false).Cast<T>().ToArray();

    /// <summary>The fixture list's group filter, found by its tooltip (never by index: the transport bar's stack picker
    /// came first once).</summary>
    private OptionButton GroupFilter => FindAll<OptionButton>().First(o => o.TooltipText.StartsWith("Show the fixtures of a MagicQ group"));

    /// <summary>The fixture list's cue filter, found by its tooltip.</summary>
    private OptionButton CueFilter => FindAll<OptionButton>().First(o => o.TooltipText.StartsWith("Show the fixtures recorded in a cue"));

    private static void Choose(OptionButton button, int index)
    {
        button.Select(index);
        button.EmitSignal(OptionButton.SignalName.ItemSelected, index);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frame++;
        if (_frame == 30)
        {
            // Every file dialog keeps its own title (explored 11 Oct 2026: Godot's mode titles "Open a File" /
            // "Save a File" replaced them, also in the native dialogs).
            var titles = _scene.FindChildren("*", nameof(FileDialog), true, false).OfType<FileDialog>().Select(d => d.Title).ToList();
            GD.Print($"file dialog titles [{string.Join(", ", titles)}]: " +
                     (titles.Count >= 4 && titles.All(t => t is not ("Open a File" or "Save a File" or "Open File(s)" or "Select a Directory")) ? "OK" : "FAIL"));
            var list = Table;
            var (groups, cues) = (GroupFilter, CueFilter);
            GD.Print($"list items: {list.RowCount}, group items: {groups.ItemCount}, cue items: {cues.ItemCount}");
            for (int i = 0; i < Mathf.Min(cues.ItemCount, 14); i++)
                GD.Print($"  cue item {i}: {(cues.IsItemSeparator(i) ? "--- " : "")}{cues.GetItemText(i)}");
            int cue = Enumerable.Range(0, cues.ItemCount).First(i => cues.GetItemText(i).StartsWith("1  start"));
            Choose(cues, cue);
            GD.Print($"cue '{cues.GetItemText(cue)}': {list.RowCount} fixtures");
            for (int i = 0; i < list.RowCount; i++)
                GD.Print($"    {list.RowText(i)}");
        }
        if (_frame == 120)
        {
            GD.Print($"lit with cue filter: {LitCount()} of {Table.RowCount} listed");
            Save("panel_cue.png");
            Choose(CueFilter, 0);
            GD.Print($"lit without filter: {LitCount()}");
        }
        if (_frame == 130)
            RiggingTest();
        if (_frame == 132)
        {
            // Rigging fields (TODO 12 Oct 2026): head 150 hangs on T 1 (RiggingTest): only X is free, Y and Z are disabled
            // with their reason; X slides it along the truss, keeps its drop and focus point, and stops at the truss's end.
            var builder = _scene.GetNode<ShowBuilder>("Show");
            _rigLamp = builder.Fixtures.First(x => x.DmxAddress == 150);
            Selection.Select(_rigLamp);
        }
        if (_frame == 134)
        {
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var x = PositionField("X (right +)");
            var y = PositionField("Y (height)");
            var z = PositionField("Z (audience +)");
            bool fields = x.Editable && !y.Editable && !z.Editable && y.TooltipText.Contains("Truss heights") && z.TooltipText.Contains("along T 1");
            var truss = new Rig(builder.Document).Trusses.First(t => t.Name == "T 1");
            var from = builder.Mount(_rigLamp).Position;
            var focus = _rigLamp.FocusPoint;
            x.Value = from.X + 0.5f;
            var to = builder.Mount(_rigLamp).Position;
            bool along = Mathf.IsEqualApprox(to.X, from.X + 0.5f, 0.001f) && Mathf.IsEqualApprox(to.Y, from.Y, 0.001f)
                         && Mathf.IsEqualApprox(to.Z, from.Z, 0.001f) && _rigLamp.FocusPoint.IsEqualApprox(focus)
                         && Mathf.IsEqualApprox((float)x.Value, to.X, 0.001f);
            GD.Print($"lamp on T 1: only X editable {fields}, X + 0.5 m slides along ({from} -> {to}), focus stays {along}: {(fields && along ? "OK" : "FAIL")}");
            x.Value = 500;
            var end = builder.Mount(_rigLamp).Position;
            var bar = _panel.FindChildren("*", nameof(PanelContainer), true, false).OfType<StatusBar>().First();
            float endX = builder.ToStage(truss.End.X, truss.End.Y, truss.End.Z).X;
            bool stopped = Mathf.IsEqualApprox(end.X, endX, 0.001f) && Mathf.IsEqualApprox(end.Y, from.Y, 0.001f)
                           && bar.Message == "Stopped at the end of T 1" && Mathf.IsEqualApprox((float)x.Value, endX, 0.001f);
            GD.Print($"typed 500 m stops at the end of T 1 (x {end.X:0.00} of {endX:0.00}, status '{bar.Message}'), field shows it: {(stopped ? "OK" : "FAIL")}");
            Save("panel_rigging_truss.png");
            // Without a support all three are free again (the picker's "(not attached)").
            builder.SetParent(_rigLamp, 0);
            CallDeferred(MethodName.RebuildRigLampParams);
        }
        if (_frame == 136)
        {
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var fields = new[] { "X (right +)", "Y (height)", "Z (audience +)" }.Select(PositionField).ToList();
            var from = builder.Mount(_rigLamp).Position;
            fields[1].Value = from.Y + 1f;
            bool free = fields.All(f => f.Editable && f.TooltipText == "")
                        && Mathf.IsEqualApprox(builder.Mount(_rigLamp).Position.Y, from.Y + 1f, 0.001f);
            GD.Print($"lamp without a support: all three fields editable, Y + 1 m moves it: {(free ? "OK" : "FAIL")}");
        }
        if (_frame == 140)
        {
            // No programmer (14 Oct 2026): the lamp's intensity field sets the head's level in the current cue (undoable).
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            player.SetStack(builder.Model.Show.CueStacks.First(st => st.Steps.Count > 3));
            player.Jump(2);
            _recordLamp = builder.Fixtures.First(f => builder.MoverOf(f) == null && builder.IntensityAddress(f) > 0
                && CueTracking.LevelsAfter(builder.Model.Show, player.Stack, 2).GetValueOrDefault(builder.IntensityAddress(f)) != 94);
            _recordSteps = CueTracking.LevelsAfter(builder.Model.Show, player.Stack, 2).GetValueOrDefault(builder.IntensityAddress(_recordLamp));
            Selection.Select(_recordLamp);
        }
        if (_frame == 142)
        {
            var row = FindAll<HBoxContainer>().First(r => r.Name == "Row_intensity" && r.IsVisibleInTree());
            row.GetChildren().OfType<SpinBox>().First().Value = 37;   // 37 % = 94
        }
        if (_frame == 144)
        {
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            int address = builder.IntensityAddress(_recordLamp);
            int level = CueTracking.LevelsAfter(builder.Model.Show, player.Stack, 2).GetValueOrDefault(address);
            bool shown = Mathf.IsEqualApprox(_recordLamp.Intensity, builder.DmxAfter(player.Stack, 2).GetValueOrDefault(address) / 255f, 0.005f);
            GD.Print($"intensity into the cue: level {level}, the stage shows it {shown}: {(level == 94 && shown ? "OK" : "FAIL")}");
            KeyMap.Run("edit_undo");
        }
        if (_frame == 150)
        {
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            int level = CueTracking.LevelsAfter(builder.Model.Show, player.Stack, 2).GetValueOrDefault(builder.IntensityAddress(_recordLamp));
            GD.Print($"undo takes the level back ({level}, was {_recordSteps}): {(level == _recordSteps ? "OK" : "FAIL")}");
            player.Jump(-1);
        }
        if (_frame == 152)
        {
            bool note = FindAll<Label>().Any(l => l.IsVisibleInTree() && l.Text.StartsWith("Before the first cue"));
            GD.Print($"before the first cue the settings say to choose a cue: {(note ? "OK" : "FAIL")}");
        }
        if (_frame == 153)
            _panel.GetNode<CuePlayer>("CuePlayer").Go();
        if (_frame == 155)
        {
            // Explored 12 Oct 2026: after GO the lamp's settings still showed the cue before: they are built for a new cue.
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            string expected = "Cue " + player.Stack.Steps[player.Step].CueNumberText;
            bool shown = FindAll<Label>().Any(l => l.IsVisibleInTree() && l.Text == expected);
            GD.Print($"settings after GO: '{expected}': {(shown ? "OK" : "FAIL")}");
        }
        if (_frame == 160)
        {
            var tabs = FindAll<TabContainer>()[0];
            tabs.CurrentTab = 1;
            _panel.GetNode<CuePlayer>("CuePlayer").Jump(4);
        }
        if (_frame == 162)
        {
            // Smoke per cue (TODO 12 Oct 2026): the Playback tab's rows set the current cue's smoke (amount, build time);
            // the haze follows the timeline: settled after a jump, eased while it builds, kept by later cues.
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            player.Jump(2);
            var sets = FindAll<CheckBox>().First(c => c.Text == "This cue sets the smoke" && c.IsVisibleInTree());
            var amount = FindAll<HSlider>().First(sl => sl.TooltipText.StartsWith("Haze in the beams after this cue") && sl.IsVisibleInTree());
            string title = FindAll<Label>().First(l => l.Name == "SmokeTitle").Text;
            string info = FindAll<Label>().First(l => l.Name == "SmokeInfo").Text;
            string shown = amount.GetParent().GetChildren().OfType<Label>().Last().Text;
            float baseHaze0 = player.Timeline.BaseHaze;
            bool idle = !sets.ButtonPressed && !amount.Editable && title == $"Smoke in cue {player.Stack.Steps[2].CueNumberText}" && info.StartsWith("Leaves the smoke as it is");
            // The amount shows what the cue inherits (the project's haze here), also in its value text (explored 12 Oct 2026: it said 0 %).
            bool inherits = Mathf.IsEqualApprox((float)amount.Value, (float)ProjectLook.PercentOf(baseHaze0), 0.01f) && shown == $"{amount.Value:0} %";
            GD.Print($"smoke amount shows the inherited {amount.Value:0.#} % ('{shown}'): {(inherits ? "OK" : "FAIL")}");
            sets.ButtonPressed = true;   // emits Toggled: the cue sets the smoke
            amount.Value = 60;
            FindAll<SpinBox>().First(sp => sp.TooltipText.StartsWith("Seconds the haze takes") && sp.IsVisibleInTree()).Value = 4;
            var smoke = _scene.GetNode<ShowBuilder>("Show").Model.Blocking.Smoke;
            GD.Print($"smoke rows idle before ticking {idle}; ticked: {smoke.Count} cue, amount {smoke.Values.FirstOrDefault()?.Amount}, time {smoke.Values.FirstOrDefault()?.Time}: " +
                     (idle && smoke.Count == 1 && smoke.Values.First() is { Amount: 60, Time: 4f } ? "OK" : "FAIL"));
        }
        if (_frame == 164)
        {
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            float full = ProjectLook.DensityOf(60), baseHaze = player.Timeline.BaseHaze;
            float settled = Panel.Haze;
            var go = player.Timeline.Segments[2];
            player.Seek(go.Start + 2);
            float mid = Panel.Haze;
            player.Seek(go.Start + 5);
            float done = Panel.Haze;
            player.Seek(go.Start - 1);
            float before = Panel.Haze;
            player.Jump(1);
            float earlier = Panel.Haze;
            player.Jump(4);
            float later = Panel.Haze;
            string list = FindAll<ItemList>().First(l => l.ItemCount > 4 && l.IsVisibleInTree()).GetItemText(2);
            string info = FindAll<Label>().First(l => l.Name == "SmokeInfo").Text;
            bool ok = Mathf.IsEqualApprox(settled, full, 1e-4f) && Mathf.IsEqualApprox(mid, (baseHaze + full) / 2, 1e-4f)
                      && Mathf.IsEqualApprox(done, full, 1e-4f) && Mathf.IsEqualApprox(before, baseHaze, 1e-4f)
                      && Mathf.IsEqualApprox(earlier, baseHaze, 1e-4f) && Mathf.IsEqualApprox(later, full, 1e-4f)
                      && list.Contains("smoke 60%") && info.Contains($"from cue {player.Stack.Steps[2].CueNumberText}");
            GD.Print($"smoke: jump {settled:0.0000}, half way {mid:0.0000} (expected {(baseHaze + full) / 2:0.0000}), done {done:0.0000}, before {before:0.0000}, earlier cue {earlier:0.0000}, " +
                     $"later cue keeps it {later:0.0000}, list '{list}', info '{info}': {(ok ? "OK" : "FAIL")}");
            player.Jump(2);
            FindAll<CheckBox>().First(c => c.Text == "This cue sets the smoke" && c.IsVisibleInTree()).ButtonPressed = false;
        }
        if (_frame == 166)
        {
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            bool gone = _scene.GetNode<ShowBuilder>("Show").Model.Blocking.Smoke.Count == 0 && Mathf.IsEqualApprox(Panel.Haze, player.Timeline.BaseHaze, 1e-4f);
            GD.Print($"smoke taken out again: the haze is the project's again ({Panel.Haze:0.0000}): {(gone ? "OK" : "FAIL")}");
            player.Jump(4);   // as before for the next step
        }
        if (_frame == 170)
        {
            // "+ BO cue" inserts a blackout right after the current cue and jumps to it: nothing lit.
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            int before = player.Stack.Steps.Count, at = player.Step;
            FindAll<Button>().First(b => b.Text == "+ BO cue").EmitSignal(BaseButton.SignalName.Pressed);
            bool ok = player.Stack.Steps.Count == before + 1 && player.Step == at + 1
                      && player.Stack.Steps[player.Step].Text == "BO" && LitCount() == 0;
            GD.Print($"insert BO cue: {before} -> {player.Stack.Steps.Count} steps, now cue {player.Stack.Steps[player.Step].CueNumberText}, lit {LitCount()}: {(ok ? "OK" : "FAIL")}");
        }
        if (_frame == 180)
        {
            Save("panel_playback.png");
            FindAll<TabContainer>()[0].CurrentTab = 2;
            var actors = FindAll<TabContainer>()[0].GetNode("Actors").GetChildren().OfType<ItemList>().First();
            actors.Select(0);
            actors.EmitSignal(ItemList.SignalName.ItemSelected, 0);
        }
        if (_frame == 200)
        {
            Save("panel_actors.png");
            // Edit X, then Y of the selected actor through the panel's fields: both must stay.
            var spins = FindAll<SpinBox>().Where(sp => sp.Suffix == "m" && sp.IsVisibleInTree()).ToList();
            var (x, y) = (spins[^3], spins[^2]);   // ... X, Y, Z are the last three metre fields
            x.Value = 1.5;
            y.Value = 0.4;
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var actor = builder.Actors.Values.First();
            GD.Print($"actor after editing X then Y: {actor.Position} " +
                     (Mathf.IsEqualApprox(actor.Position.X, 1.5f) && Mathf.IsEqualApprox(actor.Position.Y, 0.4f) ? "OK" : "FAIL"));
            _editedSpin = x;
            x.GetLineEdit().GrabFocus();
        }
        if (_frame == 300)
        {
            // Present mode (UI review 27): only the audience view, panels and bars hidden; viewpoint keys; F11 back.
            FixturePanel.PresentFullScreen = false;
            _presentBefore = (Panel.Host.Workspace, Panel.Host.Main);
            Panel.TogglePresent();
        }
        if (_frame == 301)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Key2, Keycode = Key.Key2, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Key2, Keycode = Key.Key2, Pressed = false });
        }
        if (_frame == 303)
        {
            var audience = Panel.Host.Find<AudienceView>();
            bool on = Panel.Presenting && Panel.Host.Main == audience && Panel.Host.Exclusive == audience && !Panel.Host.ShowBar;
            string where = audience.Where;
            Save("panel_present.png");
            Panel.TogglePresent();
            bool back = !Panel.Presenting && Panel.Host.Exclusive == null && Panel.Host.Workspace == _presentBefore.Workspace
                        && Panel.Host.Main == _presentBefore.Main
                        && Panel.Host.ShowBar;
            GD.Print($"present mode: on {on}, key 2 -> {where}, back {back}: {(on && where == "Front centre" && back ? "OK" : "FAIL")}");
        }
        if (_frame == 208)
        {
            // The actor's settings are taller than the inspector in this window: the Facing slider (the wheel test at
            // frame 210) is scrolled into view first; the layout follows in the next frame.
            var facing = FindAll<HSlider>().Last(sl => sl.IsVisibleInTree());
            facing.GetParent().GetParent().GetParent<ScrollContainer>().EnsureControlVisible(facing);
        }
        if (_frame == 210)
        {
            // Editing must not rebuild the panel under the user (the field and its focus stay).
            bool kept = IsInstanceValid(_editedSpin) && _editedSpin.IsInsideTree() && _editedSpin.GetLineEdit().HasFocus();
            GD.Print($"actor field kept after edit (focus stays): {(kept ? "OK" : "FAIL")}");
            // The mouse wheel over a slider changes it (1 % of the range per notch) without scrolling the panel.
            var facing = FindAll<HSlider>().Last(sl => sl.IsVisibleInTree());
            double before = facing.Value;
            var scroll = facing.GetParent().GetParent().GetParent<ScrollContainer>();
            int scrollBefore = scroll.ScrollVertical;
            var at = facing.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = at, GlobalPosition = at }, true);
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false, Position = at, GlobalPosition = at }, true);
            bool wheel = facing.Value < before && scroll.ScrollVertical == scrollBefore;
            GD.Print($"wheel over slider: {before} -> {facing.Value}, scroll {scrollBefore} -> {scroll.ScrollVertical}: {(wheel ? "OK" : "FAIL")}");
            FindAll<TabContainer>()[0].CurrentTab = 0;
        }
        if (_frame == 215)
        {
            // Solo lights the selected fixture at 70 %; its own level comes back when Solo goes off.
            var lamp = _scene.GetNode<ShowBuilder>("Show").FindChildren("*", "", true, false).OfType<ProfileFixture>().First();
            lamp.Intensity = 0.25f;
            Selection.Select(lamp);
            var solo = FindAll<CheckBox>().First(c => c.Text.StartsWith("Solo"));
            solo.ButtonPressed = true;
            float during = lamp.Intensity;
            // Solo is a mode chip in the status bar; its ✕ ends it (UI review 14).
            var chip = Panel.ModeChips().FirstOrDefault(c => c.Text == "Solo");
            chip?.End();
            GD.Print($"solo: {during} then {lamp.Intensity}, chip {chip != null}, solo off {!solo.ButtonPressed}: " +
                     (Mathf.IsEqualApprox(during, 0.7f) && Mathf.IsEqualApprox(lamp.Intensity, 0.25f) && chip != null
                      && !solo.ButtonPressed ? "OK" : "FAIL"));
            // The group filter too: a chip naming the group, ✕ = all lamps again.
            Choose(GroupFilter, 1);
            var groupChip = Panel.ModeChips().FirstOrDefault(c => c.Text.StartsWith("Group: "));
            groupChip?.End();
            GD.Print($"group chip '{groupChip?.Text}', filter after ✕ {GroupFilter.Selected}: " +
                     (groupChip != null && GroupFilter.Selected == 0 && !Panel.ModeChips().Any(c => c.Text.StartsWith("Group")) ? "OK" : "FAIL"));
        }
        if (_frame == 212)
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Ctrl, Keycode = Key.Ctrl, Pressed = true });
        if (_frame == 213)
        {
            // Ctrl + click on a lamp in the 3D view adds it to the selection, a second Ctrl + click takes it out (UI
            // review 8); the settings then show both lamps.
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var view = Panel.Host.Views.OfType<View3D>().First(v => v.IsMain);
            var onScreen = builder.Fixtures.Where(f => view.ToView(f.GlobalPosition) is { } p && view.GetRect().HasPoint(p + view.GlobalPosition))
                .Take(2).ToList();
            Selection.Select(onScreen[0]);
            var at = view.ToView(onScreen[1].GlobalPosition).Value;
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, CtrlPressed = true });
            bool added = Selection.Fixtures.Count == 2 && Selection.Fixtures.Contains(onScreen[1]);
            bool multi = FindAll<Label>().Any(l => l.Text == "2 lamps selected" && l.IsVisibleInTree());
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, CtrlPressed = true });
            bool removed = Selection.Fixtures.Count == 1 && Selection.Fixture == onScreen[0];
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Ctrl, Keycode = Key.Ctrl, Pressed = false });
            GD.Print($"ctrl+click adds (settings for 2: {multi}) and takes out again: {(added && multi && removed ? "OK" : "FAIL")}");
        }
        if (_frame == 214)
        {
            // A right click without dragging on a lamp in the 3D view opens its menu (a right drag looks around); "Aim"
            // makes the next plain click on the stage aim the lamp (UI review 9).
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var view = Panel.Host.Views.OfType<View3D>().First(v => v.IsMain);
            var lamp = builder.Fixtures.First(f => builder.MoverOf(f) == null && view.ToView(f.GlobalPosition) is { } p
                                                   && view.GetRect().HasPoint(p + view.GlobalPosition));
            Selection.Clear();
            var at = view.ToView(lamp.GlobalPosition).Value + view.GetGlobalRect().Position;
            Panel.Host._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at });
            Panel.Host._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at });
            var menu = FindAll<PopupMenu>().FirstOrDefault(m => m.Visible && m.Name == "ContextMenu");
            var items = menu == null ? new List<string>() : Enumerable.Range(0, menu.ItemCount).Select(menu.GetItemText).ToList();
            bool opened = items.Any(i => i.StartsWith("Patch / gel")) && Selection.Fixture == lamp;
            int aim = items.FindIndex(i => i.StartsWith("Aim"));
            menu?.Hide();
            if (aim >= 0)
                menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(aim));
            var target = new Vector3(-1.5f, 0f, -1f);
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = view.ToView(builder.ToGlobal(target)).Value });
            bool aimed = lamp.FocusPoint.DistanceTo(target) < 0.1f;
            GD.Print($"3D right click: lamp menu {opened}, Aim + click aims {aimed} (focus {lamp.FocusPoint}): {(opened && aimed ? "OK" : "FAIL")}");
        }
        if (_frame == 216)
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Shift, Keycode = Key.Shift, Pressed = true });
        if (_frame == 217)
        {
            // Shift + click in the 3D view aims the selected lamp at the clicked spot on the stage floor.
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var lamp = builder.Fixtures.First(f => builder.MoverOf(f) == null);
            Selection.Select(lamp);
            var view = ((FixturePanel)_panel).Host.Views.OfType<View3D>().First(v => v.IsMain);
            var target = new Vector3(1.5f, 0f, -2f);
            var at = view.ToView(builder.ToGlobal(target)).Value;
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, ShiftPressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Shift, Keycode = Key.Shift, Pressed = false });
            bool ok = lamp.FocusPoint.DistanceTo(target) < 0.1f && Selection.Fixture == lamp;
            GD.Print($"shift+click aims: focus {lamp.FocusPoint}: {(ok ? "OK" : "FAIL")}");
        }
        if (_frame == 218)
        {
            // Ctrl + wheel in the 3D view with a lamp selected: its head gets brighter in the current cue (the BO cue
            // inserted above), the camera stays; Ctrl + Shift = 25. The wheel without Ctrl moves the camera.
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            var lamp = Selection.Fixture;
            var view = ((FixturePanel)_panel).Host.Views.OfType<View3D>().First(v => v.IsMain);
            var camera = _scene.FindChildren("*", "", true, false).OfType<FlyCamera>().First();
            var cameraBefore = camera.GlobalPosition;
            var show = builder.Document;
            int address = builder.IntensityAddress(lamp);
            int before = CueTracking.LevelsAfter(show, player.Stack, player.Step).GetValueOrDefault(address);
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = view.Size / 2, CtrlPressed = true });
            int after = CueTracking.LevelsAfter(show, player.Stack, player.Step).GetValueOrDefault(address);
            int output = builder.DmxAfter(player.Stack, player.Step).GetValueOrDefault(address);
            bool ok = after == before + 5 && Mathf.IsEqualApprox(lamp.Intensity, output / 255f)
                      && camera.GlobalPosition.IsEqualApprox(cameraBefore);
            GD.Print($"ctrl+wheel = intensity in the cue: {before} -> {after}, lamp {lamp.Intensity:0.000}: {(ok ? "OK" : "FAIL")}");
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = view.Size / 2, CtrlPressed = true, ShiftPressed = true });
            int coarse = CueTracking.LevelsAfter(show, player.Stack, player.Step).GetValueOrDefault(address);
            ok = coarse == Mathf.Min(255, after + 25) && camera.GlobalPosition.IsEqualApprox(cameraBefore);
            GD.Print($"ctrl+shift+wheel = coarse: {after} -> {coarse}: {(ok ? "OK" : "FAIL")}");
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = view.Size / 2 });
            int plain = CueTracking.LevelsAfter(show, player.Stack, player.Step).GetValueOrDefault(address);
            var normalMove = camera.GlobalPosition - cameraBefore;
            var shiftFrom = camera.GlobalPosition;
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = view.Size / 2, ShiftPressed = true });
            var fastMove = camera.GlobalPosition - shiftFrom;
            ok = plain == coarse && normalMove.Length() > 0.01f && fastMove.Length() > normalMove.Length() * 2
                 && CueTracking.LevelsAfter(show, player.Stack, player.Step).GetValueOrDefault(address) == coarse;
            GD.Print($"wheel with a lamp selected moves the camera ({normalMove.Length():0.00} m, Shift {fastMove.Length():0.00} m), level stays {plain}: {(ok ? "OK" : "FAIL")}");
        }
        if (_frame == 219)
        {
            // Without a selection the wheel moves the camera again.
            Selection.Clear();
            var view = ((FixturePanel)_panel).Host.Views.OfType<View3D>().First(v => v.IsMain);
            var camera = _scene.FindChildren("*", "", true, false).OfType<FlyCamera>().First();
            var cameraBefore = camera.GlobalPosition;
            view._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = view.Size / 2 });
            bool ok = !camera.GlobalPosition.IsEqualApprox(cameraBefore);
            GD.Print($"wheel without a selection moves the camera: {(ok ? "OK" : "FAIL")}");
        }
        if (_frame == 220)
        {
            // Live Art-Net: the stage shows what MagicQ outputs (no programmer on top since 14 Oct 2026), steadily; off
            // again, the stage shows the cue.
            var builder = _scene.GetNode<ShowBuilder>("Show");
            _liveLamp = builder.Fixtures.First(f => builder.MoverOf(f) == null && builder.IntensityAddress(f) is > 0 and <= 512);
            Selection.Select(_liveLamp);
            Panel.SetLiveInput(true);
        }
        if (_frame > 220 && _frame < 232)
        {
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var channels = new byte[512];
            channels[builder.IntensityAddress(_liveLamp) - 1] = 204;   // 80 %
            Panel.ArtNet.Receive(ArtNetReceiver.Packet(0, channels));
            if (_frame > 223 && !Mathf.IsEqualApprox(_liveLamp.Intensity, 0.8f, 0.01f))
                _liveUnstable++;
        }
        if (_frame == 232)
        {
            GD.Print($"live Art-Net drives the lamp (head {_liveLamp.HeadNumber}, {_liveUnstable} unstable frames): {(_liveUnstable == 0 ? "OK" : "FAIL")}");
            Panel.SetLiveInput(false);
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            player.Jump(player.Step);
        }
        if (_frame == 234)
        {
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var player = _panel.GetNode<CuePlayer>("CuePlayer");
            float cue = player.Step < 0 ? 0f : builder.DmxAfter(player.Stack, player.Step).GetValueOrDefault(builder.IntensityAddress(_liveLamp)) / 255f;
            bool back = Mathf.IsEqualApprox(_liveLamp.Intensity, cue, 0.01f);
            GD.Print($"live Art-Net off: the cue's level again ({_liveLamp.Intensity:0.00} = {cue:0.00}): {(back ? "OK" : "FAIL")}");
        }
        if (_frame == 236)
        {
            // Closing the window with a file dialog open is refused (Godot 4.7.2 hangs at exit on an open native
            // dialog); a non-native dialog stands in for it here.
            var dialog = new FileDialog { Title = "Test dialog", FileMode = FileDialog.FileModeEnum.OpenFile, UseNativeDialog = false };
            AddChild(dialog);
            NativeFileDialogs.Popup(dialog, new Vector2I(400, 300));
            _panel.Notification((int)NotificationWMCloseRequest);
            var status = FindAll<Label>().Any(l => l.Text.StartsWith("Close the file dialog"));
            dialog.EmitSignal(AcceptDialog.SignalName.Canceled);
            bool ok = status && !NativeFileDialogs.AnyOpen && !GetTree().AutoAcceptQuit;
            GD.Print($"window close with a file dialog open refused, then closed: {(ok ? "OK" : "FAIL")}");
            dialog.QueueFree();
        }
        if (_frame == 235)
            Selection.Clear();
        if (_frame == 237)
        {
            // The inspector is always there (UI review 30): without a selection "Nothing selected", the show's info and
            // the Look; selecting a lamp doesn't change the main view's width.
            bool empty = FindAll<Label>().Any(l => l.Text == "Nothing selected" && l.IsVisibleInTree())
                         && FindAll<Label>().Any(l => l.Text.StartsWith("Project: ") && l.IsVisibleInTree())
                         && FindAll<Label>().Any(l => l.Text == "Stray light" && l.IsVisibleInTree());
            _mainWidth = Panel.Host.Size.X;
            GD.Print($"inspector without a selection: {(empty ? "OK" : "FAIL")}");
            // The Look's haze slider sets the beams' haze and is an unsaved "look" of the project (UI review 24); put back,
            // nothing is unsaved for it.
            var hazeSlider = FindAll<Label>().First(l => l.Text == "Haze" && l.IsVisibleInTree())
                .GetParent().GetChildren().OfType<HSlider>().First();
            double hazeBefore = hazeSlider.Value;
            hazeSlider.Value = 40;
            float density = Panel.Haze;
            var lookBuilder = _scene.GetNode<ShowBuilder>("Show");
            bool unsavedLook = lookBuilder.UnsavedChanges().Contains("look");
            hazeSlider.Value = hazeBefore;
            bool hazeOk = Mathf.IsEqualApprox(density, 0.04f) && unsavedLook && !lookBuilder.UnsavedChanges().Contains("look");
            GD.Print($"look: haze 40 % = density {density}, unsaved look {unsavedLook}: {(hazeOk ? "OK" : "FAIL")}");
            Selection.Select(_scene.GetNode<ShowBuilder>("Show").Fixtures.First());
        }
        if (_frame == 239)
        {
            float width = Panel.Host.Size.X;
            GD.Print($"main view width with / without a selection {width} / {_mainWidth}: {(Mathf.IsEqualApprox(width, _mainWidth) ? "OK" : "FAIL")}");
        }
        if (_frame == 241)
            KeyMap.Run("inspector_toggle");
        if (_frame == 243)
        {
            var strip = (Control)_panel.FindChild("InspectorStrip", true, false);
            bool collapsed = !Panel.InspectorOpen && strip.IsVisibleInTree() && Panel.Host.Size.X > _mainWidth + 100;
            GD.Print($"inspector collapsed to its strip (main view {_mainWidth} -> {Panel.Host.Size.X}): {(collapsed ? "OK" : "FAIL")}");
            Save("panel_inspector_collapsed.png");
            KeyMap.Run("inspector_toggle");
        }
        if (_frame == 250)
        {
            // File > Preferences (UI review 18, 24): the UI scale applies to the window at once; Automatic is 1 in tests.
            KeyMap.Run("preferences");
        }
        if (_frame == 252)
        {
            var dialog = FindAll<ConfirmationDialog>().FirstOrDefault(d => d.Visible && d.Title == "Preferences");
            var scale = dialog?.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>()
                .First(o => o.TooltipText.StartsWith("Size of texts"));
            int index = Enumerable.Range(0, scale?.ItemCount ?? 0).FirstOrDefault(i => scale.GetItemText(i) == "125 %", -1);
            scale?.Select(index);
            dialog?.EmitSignal(AcceptDialog.SignalName.Confirmed);
            dialog?.Hide();
            _prefsApplied = (dialog != null && index > 0, GetTree().Root.ContentScaleFactor, Panel.PreferencesText);
            Save("panel_scale125.png");
            // Back to Automatic (the action runs in the next frame).
            KeyMap.Run("preferences");
        }
        if (_frame == 254)
        {
            var (found, applied, text) = _prefsApplied;
            var again = FindAll<ConfirmationDialog>().First(d => d.Visible && d.Title == "Preferences");
            again.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>()
                .First(o => o.TooltipText.StartsWith("Size of texts")).Select(0);
            again.EmitSignal(AcceptDialog.SignalName.Confirmed);
            again.Hide();
            bool ok = found && Mathf.IsEqualApprox(applied, 1.25f) && text.StartsWith("scale 1.25")
                      && Mathf.IsEqualApprox(GetTree().Root.ContentScaleFactor, 1f);
            GD.Print($"preferences: UI scale 125 % applied {applied}, '{text}', automatic again {GetTree().Root.ContentScaleFactor}: {(ok ? "OK" : "FAIL")}");
        }
        if (_frame == 256)
        {
            // The start screen (UI review 5): recent projects and the project buttons; hidden again while a show is open.
            Panel.ShowStartScreen();
            var names = Panel.StartScreenProjectNames;
            bool shown = Panel.StartScreenShown && FindAll<Button>().Any(b => b.Text == "New project from show..." && b.IsVisibleInTree());
            Save("panel_start.png");
            Panel.RefreshStartScreen();
            GD.Print($"start screen: shown {shown}, projects [{string.Join(", ", names)}], hidden with a show {!Panel.StartScreenShown}: " +
                     (shown && names.Length > 0 && !Panel.StartScreenShown ? "OK" : "FAIL"));
        }
        if (_frame == 156)
        {
            // Rename head from the lamp's context menu (TODO 12 Oct 2026): a dialog asks for the name; every lamp on the
            // head gets it, the list shows it, the status bar says so, and it is cut to 15 characters.
            var builder = _scene.GetNode<ShowBuilder>("Show");
            _renamed = builder.Fixtures.First(f => f.HeadNumber > 0 && builder.Fixtures.Count(x => x.HeadNumber == f.HeadNumber) > 1);
            Panel.ShowLampMenu(_renamed, new Vector2(300, 300));
            var menu = FindAll<PopupMenu>().FirstOrDefault(m => m.Visible && m.Name == "ContextMenu");
            int item = menu == null ? -1 : Enumerable.Range(0, menu.ItemCount).First(i => menu.GetItemText(i) == $"Rename head {_renamed.HeadNumber}...");
            menu?.Hide();
            menu?.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(item));
        }
        if (_frame == 158)
        {
            var dialog = FindAll<ConfirmationDialog>().FirstOrDefault(d => d.Visible && d.Title == $"Rename head {_renamed.HeadNumber}");
            var name = dialog?.GetChildren().OfType<LineEdit>().FirstOrDefault();
            bool asked = name != null && name.Text == _renamed.GetMeta("headName").AsString() && name.MaxLength == 15;
            if (name != null)
            {
                name.Text = "Spot Jardin";
                name.EmitSignal(LineEdit.SignalName.TextSubmitted, name.Text);   // Enter in the field
            }
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var sharing = builder.Fixtures.Where(f => f.HeadNumber == _renamed.HeadNumber).ToList();
            var bar = _panel.FindChildren("*", nameof(PanelContainer), true, false).OfType<StatusBar>().First();
            int row = Table.Shown.ToList().IndexOf(_renamed);
            int patched = ((IShowDocument)builder.Model.Show).HeadAt(_renamed.DmxAddress);
            bool ok = asked && _renamed.GetMeta("headName").AsString() == "Spot Jardin"
                      && row >= 0 && Table.RowText(row).Contains("Spot Jardin")
                      && bar.Message.StartsWith($"Head {patched} is now \"Spot Jardin\"")
                      && !dialog.Visible;
            GD.Print($"rename head {_renamed.HeadNumber} from the lamp menu: asked {asked}, {sharing.Count} lamps on the head renamed, row '{(row >= 0 ? Table.RowText(row) : "")}', status '{bar.Message}': {(ok ? "OK" : "FAIL")}");
        }
        if (_frame == 245)
        {
            // Dragging the grip at the inspector's left edge makes it wider.
            var grip = FindAll<Control>().First(c => c.TooltipText.StartsWith("Drag to make the inspector"));
            var inspector = (Control)grip.GetParent().GetChild(1);
            float before = inspector.Size.X;
            grip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            grip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Relative = new Vector2(-60, 0) });
            grip.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
            bool ok = Panel.InspectorOpen && Mathf.IsEqualApprox(inspector.CustomMinimumSize.X, before + 60, 1f);
            GD.Print($"inspector open again, dragged {before} -> {inspector.CustomMinimumSize.X}: {(ok ? "OK" : "FAIL")}");
        }
        if (_frame == 238)
        {
            // The model's mechanics follow a change on the lamp at once (the application reads them there), and
            // UpdateMechanics changes the model.
            var builder = _scene.GetNode<ShowBuilder>("Show");
            var lamp = builder.Fixtures.First(f => builder.MoverOf(f) == null);
            int id = builder.VisOf(lamp).Id;
            lamp.Iris = 0.42f;
            lamp.FocusPoint = new Vector3(1.25f, 0f, -2.5f);
            var m = builder.Model.Mechanics[id];
            builder.UpdateMechanics(lamp, x => x.Mounting = "bottom");
            bool ok = Mathf.IsEqualApprox(m.Iris, 0.42f) && m.FocusPoint is { Length: 3 } fp && Mathf.IsEqualApprox(fp[0], 1.25f)
                      && builder.Model.Mechanics[id].Mounting == "bottom";
            GD.Print($"model mechanics follow the lamp (iris {m.Iris:0.00}, mounting {builder.Model.Mechanics[id].Mounting}): {(ok ? "OK" : "FAIL")}");
            // A lamp edit is an unsaved change of the mechanics (UI review 3: the unsaved marker).
            var unsaved = builder.UnsavedChanges();
            GD.Print($"unsaved after the lamp edit [{string.Join(", ", unsaved)}]: {(unsaved.Contains("mechanics") ? "OK" : "FAIL")}");
        }
        if (_frame == 225)
            KeyMap.Run("view_plan");   // View > Plan from the menu (an InputEventAction, not a key)
        if (_frame == 227)
        {
            // Explored 11 Oct 2026: the view host ignored everything but keys and mouse buttons, so View menu items
            // and the palette's view commands did nothing.
            var host = Panel.Host;
            GD.Print($"View > Plan from the menu: main view {host.Main?.Title}: {(host.Main is PlanView ? "OK" : "FAIL")}");
            KeyMap.Run("view_3d");
        }
        if (_frame == 228)
        {
            // One status bar (UI review 15): messages from the panel and the views, errors in red, a history.
            var bar = (StatusBar)_panel.FindChild("StatusBar", true, false);
            int before = bar.History.Count;
            StatusBar.Post("Saved something");
            StatusBar.Post("Could not do something");
            bool ok = bar.Message == "Could not do something" && bar.History.Count == before + 2
                      && bar.History[^1].Error && !bar.History[^2].Error;
            GD.Print($"status bar: '{bar.Message}', {bar.History.Count} messages, last an error: {(ok ? "OK" : "FAIL")}");
        }
        if (_frame == 229)
        {
            // The command palette finds every menu command, also the project ones and each project by name (explored
            // 11 Oct 2026: New project / Open project were missing).
            // A project to open (a document only: Save would write the edited show into the real MagicQ folder).
            Kloarte.Model.KloarteProject.Create(AppPaths.File(AppPaths.Projects), "Palette test",
                _scene.GetNode<ShowBuilder>("Show").ShowFile).Save();
            var found = Panel.PaletteCommands("new project");
            var open = Panel.PaletteCommands("open project");
            GD.Print($"palette [{string.Join(", ", found)}] [{string.Join(", ", open.Take(3))}]: " +
                     (found.Contains("File > New project...") && open.Any(n => n.StartsWith("File > Open project: ")) ? "OK" : "FAIL"));
        }
        if (_frame == 240)
        {
            Save("panel.png");
            string copy = ProjectSettings.GlobalizePath("res://tests/output/other_show/" + System.IO.Path.GetFileName(OtherShow));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(copy)!);
            System.IO.File.Copy(OtherShow, copy, true);
            if (System.IO.File.Exists(System.IO.Path.ChangeExtension(OtherShow, ".xhw")))
                System.IO.File.Copy(System.IO.Path.ChangeExtension(OtherShow, ".xhw"), System.IO.Path.ChangeExtension(copy, ".xhw"), true);
            FindAll<FileDialog>().First(d => d.Title == "Import MagicQ show").EmitSignal(FileDialog.SignalName.FileSelected, copy);
        }
        if (_frame == 340)
        {
            // A check, not a print: the open "worked" with 0 items for days while it wrote a PDF instead.
            int items = Table.RowCount;
            string project = _scene.GetNode<ShowBuilder>("Show").ProjectName;
            GD.Print($"after open: {items} fixtures, project {project}: {(items > 0 && project == "la_comedie" ? "OK" : "FAIL")}");
            Save("panel_open.png");
            // File > New project (a freshly opened show: nothing unsaved, so no Save prompt; if there is one, Don't save
            // writes nothing).
            var menu = _scene.FindChildren("*", nameof(MenuBar), true, false).OfType<MenuBar>().First()
                .GetChildren().OfType<PopupMenu>().First(m => m.Name == "File");
            menu.EmitSignal(PopupMenu.SignalName.IdPressed, menu.GetItemId(0));
        }
        if (_frame == 343 && FindAll<ConfirmationDialog>().FirstOrDefault(d => d.Visible && d.Title == "Unsaved changes") is { } prompt)
            prompt.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == "Don't save")
                .EmitSignal(BaseButton.SignalName.Pressed);
        if (_frame == 346)
        {
            // The name dialog fits the window with its buttons on screen (explored 11 Oct 2026: its wrapping hint made it
            // as tall as the window, Create and Cancel off-screen).
            var dialog = FindAll<ConfirmationDialog>().FirstOrDefault(d => d.Visible && d.Title == "New project");
            var screen = GetViewport().GetVisibleRect().Size;
            bool fits = dialog != null && dialog.Size.Y < screen.Y * 0.6f && dialog.GetOkButton().IsVisibleInTree()
                        && dialog.Position.Y + dialog.Size.Y <= screen.Y;
            GD.Print($"new project dialog {dialog?.Size} in {screen}: {(fits ? "OK" : "FAIL")}");
            Save("panel_new_project.png");
            dialog?.Hide();
            GetTree().Quit();
        }
    }

    /// <summary>
    /// Head 150 ("Binnen T", on LT 11 at z 4.20) -&gt; truss "T 1" (z 8.40), then 1 m to the right; save + reload.
    /// </summary>
    private void RiggingTest()
    {
        var builder = _scene.GetNode<ShowBuilder>("Show");
        var f = builder.GetNode("Fixtures").GetChildren().OfType<ProfileFixture>().First(x => x.DmxAddress == 150);
        var list = Table;
        int index = list.Shown.ToList().IndexOf(f);
        // The table's rows: head, the head's name, type, gel, level; no node names or hex ids (UI review 22).
        string row = list.RowText(index);
        list.ClickRow(index);
        GD.Print($"list click selects head 150 (row '{row}'): " +
                 (Selection.Fixture == f && row.StartsWith(f.HeadNumber + "  ") && !row.Contains('#') ? "OK" : "FAIL"));
        // A click on a column title sorts by it; again = the other way round.
        list.SortBy(0);
        var heads = list.Shown.Select(x => x.HeadNumber).Where(h => h > 0).ToList();
        list.SortBy(0);
        var down = list.Shown.Select(x => x.HeadNumber).Where(h => h > 0).ToList();
        GD.Print($"sort by head: {(heads.SequenceEqual(heads.OrderBy(h => h)) && down.SequenceEqual(heads.OrderByDescending(h => h)) ? "OK" : "FAIL")}");
        // Ctrl + click adds a row, Shift + click selects a range.
        list.ClickRow(0);
        list.ClickRow(2, add: true);
        int two = Selection.Fixtures.Count;
        GD.Print($"table ctrl+click: {two} selected: {(two == 2 ? "OK" : "FAIL")}");
        list.ClickRow(index);

        var t1 = builder.RigObjects.First(o => o.Name == "T 1");
        GD.Print($"before: parent {builder.GetParent(f)}, pos {f.Position}");
        builder.SetParent(f, t1.Id);
        GD.Print($"on T 1: parent {builder.GetParent(f)}, pos {f.Position}");
        builder.MoveFixture(f, f.Position + Vector3.Right);
        GD.Print($"moved: pos {f.Position}, focus {f.FocusPoint}");

        string path = ProjectSettings.GlobalizePath("res://tests/output/roundtrip/rigged.shw");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        foreach (string file in new[] { path, System.IO.Path.ChangeExtension(path, ".xhw") })
            if (System.IO.File.Exists(file))
                System.IO.File.Delete(file);   // an export never writes over a show
        GD.Print(builder.SaveShowAs(path));
        var reread = MagicQShow.Load(path);
        var vis = reread.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture && v.Address == 150);
        bool ok = vis.ParentId == t1.Id && Mathf.Abs(vis.Z - 8.40f + 0f) < 0.011f && Mathf.Abs(vis.X - f.Position.X) < 0.011f;
        GD.Print($"re-read: parent {vis.ParentId:x}, x {vis.X}, y {vis.Y}, z {vis.Z}, rx {vis.RotX}, ry {vis.RotY}: {(ok ? "OK" : "FAIL")}");
    }

    /// <summary>The lamp settings of the selected lamp, built again (after its support changed).</summary>
    private void RebuildRigLampParams() => Selection.Touch();

    /// <summary>The position field of the lamp's rigging block with this label.</summary>
    private SpinBox PositionField(string label) =>
        FindAll<Label>().First(l => l.Text == label && l.IsVisibleInTree()).GetParent().GetChildren().OfType<SpinBox>().First();

    private int LitCount() =>
        _scene.GetNode("Show/Fixtures").GetChildren().OfType<ProfileFixture>().Count(f => f.Intensity > 0f);

    private void Save(string name)
    {
        string path = ProjectSettings.GlobalizePath("res://tests/output/" + name);
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}");
    }
}
