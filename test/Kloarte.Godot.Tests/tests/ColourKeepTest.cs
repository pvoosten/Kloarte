using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;
using SysVec2 = System.Numerics.Vector2;

namespace Kloarte.Tests;

/// <summary>
/// Real UI test of "colour wheel preset -> Raw DMX keeps the colour in RGBW" on LEDBeam 150s in a native project:
/// 1. cue values (lamp settings, <see cref="LookEditor"/>): preset, then Raw DMX, one undo step, same tint on screen;
/// 2. two heads selected; 3. a palette in the palette window. Shots colour_keep_*.png in tests/output.
/// Prints "COLOURKEEP OK". Run: Godot_console.exe --path . --resolution 1600x900 res://tests/colour_keep_test.tscn
/// </summary>
public partial class ColourKeepTest : Node
{
    private Node _scene;
    private FixturePanel _panel;
    private ShowBuilder _builder;
    private PlanView _plan;
    /// <summary>Colour mix control set first (null = left unset: MagicQ home = mix has priority).</summary>
    private string MixName = System.Environment.GetEnvironmentVariable("CK_MIX");
    private int _failures;
    private static readonly string[] Emitters = { "ColorAdd_R", "ColorAdd_G", "ColorAdd_B", "ColorAdd_W" };

    /// <summary>Runs the scenarios.</summary>
    public override async void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");
        ViewHost.Persist = false;
        _scene = TestShow.Load("colourkeep");
        AddChild(_scene);
        _panel = _scene.GetNode<FixturePanel>("FixturePanel");
        _builder = _scene.GetNode<ShowBuilder>("Show");
        await Frames(40);
        _plan = _panel.Host.Find<PlanView>();
        _panel.Host.SetWorkspace(Workspace.Rig);
        _panel.Host.SetMain(_plan);
        await Frames(20);
        _plan.Fit();
        var t1 = _builder.Rig.Trusses.First(t => t.Name == "T 1");
        var beams = new List<ProfileFixture>();
        foreach (float x in new[] { -1.3f, 1.3f })
        {
            _plan.BeginLamp("robe-ledbeam150");
            var at = _plan.ToView(new SysVec2(x, _builder.ToStage(t1.Start.X, t1.Start.Y, t1.Start.Z).Z - 0.1f));
            _plan._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
            _plan._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
            beams.Add(Selection.Fixture);
        }
        _plan._GuiInput(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });
        Check(beams.Count == 2 && beams.All(b => b != null && _builder.ModelOf(b)?.Id == "robe-ledbeam150"), "two LEDBeam 150 added");
        var design = CueEditing.Design(_builder);
        Check(design != null, "native project");
        int cueIndex = 2;
        foreach (var b in beams)
            Check(design.LayoutOf(b.HeadNumber)?["ColorMacro1"] != null && design.LayoutOf(b.HeadNumber)["ColorAdd_W"] != null, $"head {b.HeadNumber} has wheel + RGBW");
        CueEditing.Edit(_builder, "test intensity", d =>
        {
            foreach (var b in beams)
                DesignEdits.Set(d.CueOrder[cueIndex].Heads, b.HeadNumber, "intensity", LookValue.Of(100));
        });
        CueEditing.Jump(cueIndex);
        await Frames(20);

        // 1. one head
        Selection.Select(beams[0]);
        await Frames(20);
        Check(CueEditing.Cue == cueIndex, $"current cue {CueEditing.Cue}");
        await Scenario("one head", new[] { beams[0] }, "Filter 103", "colour_keep_one");

        // 2. two heads (the second not touched yet)
        Selection.Select(beams.Select(Live).ToList());
        await Frames(20);
        await Scenario("two heads", beams, "Filter 26", "colour_keep_two");

        // 3. palette
        await PaletteScenario(beams[0]);
        GD.Print(_failures == 0 ? "COLOURKEEP OK" : $"COLOURKEEP FAILURES: {_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>Preset then Raw DMX on the selected heads in the lamp settings.</summary>
    private async Task Scenario(string title, IReadOnlyList<ProfileFixture> heads, string preset, string shotName)
    {
        var design = CueEditing.Design(_builder);
        var cue = design.CueOrder[CueEditing.Cue];
        var first = heads[0];
        if (MixName != null)
        {
            var mixPicker = RowOf("ColorMixMode").GetChildren().OfType<OptionButton>().First();
            GD.Print($"  ColorMixMode items: {string.Join(" | ", Enumerable.Range(0, mixPicker.ItemCount).Select(mixPicker.GetItemText))}");
            await Pick("ColorMixMode", MixName);
        }
        await Pick("Colour wheel", preset);
        await Frames(15);
        var withPreset = Tint(first);
        Shot(shotName + "_1_preset");
        foreach (var h in heads)
            Check(!Emitters.Any(k => cue.Of(h.HeadNumber)?.ContainsKey(k) == true), $"{title}: head {h.HeadNumber} preset set, no RGBW values yet");
        await Pick("Colour wheel", "Raw DMX");
        await Frames(15);
        var raw = Tint(first);
        Shot(shotName + "_2_raw");
        foreach (var h in heads)
        {
            var values = Emitters.Select(k => cue.Of(h.HeadNumber)?.GetValueOrDefault(k)).ToList();
            Check(values.All(v => v != null), $"{title}: head {h.HeadNumber} RGBW set: {string.Join(", ", Emitters.Zip(values, (k, v) => $"{k}={v}"))}");
        }
        var panelValues = Emitters.Select(k => Spin(k)?.Value).ToList();
        GD.Print($"  panel values {string.Join(", ", panelValues)}");
        Check(panelValues.All(v => v != null) && panelValues.Any(v => v > 0), $"{title}: panel shows RGBW values");
        GD.Print($"  tint preset {withPreset}, raw {raw}");
        float ratio = Brightness(raw) / System.Math.Max(Brightness(withPreset), 1e-4f);
        Check(Hue(withPreset, raw) < 0.08f && ratio <= 1.02f && ratio >= 0.49f, $"{title}: same tint (dist {Hue(withPreset, raw):0.000}), brightness ratio {ratio:0.00}");
        // one undo step restores everything (ask the history; the undo name is the last edit's)
        string undone = await MenuUndo();
        await Frames(15);
        GD.Print($"  undo: '{undone}'");
        foreach (var h in heads)
            Check(!Emitters.Any(k => cue.Of(h.HeadNumber)?.ContainsKey(k) == true) || design.CueOrder[CueEditing.Cue].Of(h.HeadNumber) == null
                  || !Emitters.Any(k => ((DesignShow)_builder.Document).CueOrder[CueEditing.Cue].Of(h.HeadNumber)?.ContainsKey(k) == true),
                  $"{title}: one undo ('{undone}') removes the RGBW values");
        Shot(shotName + "_3_undone");
        var back = Tint(first);
        Check(Hue(withPreset, back) < 0.02f && System.Math.Abs(Brightness(withPreset) - Brightness(back)) < 0.02f, $"{title}: after undo the preset tint is back ({back})");
        var wheel = Value(first, "ColorMacro1");
        Check(wheel == preset || wheel?.StartsWith(preset) == true, $"{title}: wheel after undo = {wheel}");
        // redo goes forward again
        await MenuItem("Redo");
        await Frames(15);
        Check(Emitters.All(k => ((DesignShow)_builder.Document).CueOrder[CueEditing.Cue].Of(first.HeadNumber)?.ContainsKey(k) == true), $"{title}: redo brings the RGBW values back");
        await MenuUndo();
        await Frames(15);
        Selection.Select(heads.Select(Live).ToList());
        await Frames(20);
    }

    /// <summary>The palette window: "From the cue" of a head with a preset, then Raw DMX in the palette.</summary>
    private async Task PaletteScenario(ProfileFixture beam)
    {
        beam = Live(beam);
        Selection.Select(beam);
        await Frames(20);
        await Pick("ColorMixMode", "Virtual colors (Virtual");
        await Pick("Colour wheel", "Filter 103");
        var tool = _panel.PaletteTool;
        tool.Toggle();
        await Frames(20);
        var view = tool.View;
        var name = view.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>().First(e => e.PlaceholderText == "palette name");
        name.Text = "colourkeep";
        view.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == "From the cue").EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(20);
        var list = view.FindChildren("*", nameof(ItemList), true, false).OfType<ItemList>().First();
        int row = Enumerable.Range(0, list.ItemCount).First(i => list.GetItemText(i) == "colourkeep");
        list.Select(row);
        list.EmitSignal(ItemList.SignalName.ItemSelected, row);
        await Frames(20);
        var palette = ((DesignShow)_builder.Document).Palette("colourkeep");
        GD.Print($"  palette values: {string.Join(", ", palette.Of(beam.HeadNumber).Select(kv => kv.Key + "=" + kv.Value))}");
        Check(palette.Of(beam.HeadNumber)["ColorMacro1"].ToString().StartsWith("Filter 103") && !palette.Of(beam.HeadNumber).ContainsKey("ColorAdd_R"), "palette starts with the preset, no RGBW");
        await Pick("Colour wheel", "Raw DMX", view);
        await Frames(15);
        palette = ((DesignShow)_builder.Document).Palette("colourkeep");
        var vals = Emitters.Select(k => palette.Of(beam.HeadNumber).GetValueOrDefault(k)).ToList();
        Check(vals.All(v => v != null), $"palette: RGBW set: {string.Join(", ", Emitters.Zip(vals, (k, v) => $"{k}={v}"))}");
        tool.GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://tests/output/colour_keep_palette.png"));
        await MenuUndo();
        palette = ((DesignShow)_builder.Document).Palette("colourkeep");
        Check(palette.Of(beam.HeadNumber).ContainsKey("ColorMacro1") && !palette.Of(beam.HeadNumber).ContainsKey("ColorAdd_R")
              && palette.Of(beam.HeadNumber)["ColorMacro1"].ToString().StartsWith("Filter 103"), "palette: one undo restores the preset, RGBW gone");
    }

    /// <summary>An item of the Edit menu (as a click on it).</summary>
    private async Task MenuItem(string start)
    {
        var popup = _scene.FindChildren("*", nameof(MenuBar), true, false).OfType<MenuBar>()
            .SelectMany(m => m.GetChildren().OfType<PopupMenu>()).First(p => p.Name == "Edit");
        popup.EmitSignal(Window.SignalName.AboutToPopup);
        int i = Enumerable.Range(0, popup.ItemCount).First(n => popup.GetItemText(n).StartsWith(start));
        GD.Print($"  Edit menu: {popup.GetItemText(i)}");
        popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(i));
        await Frames(15);
    }

    private async Task<string> MenuUndo()
    {
        await MenuItem("Undo");
        return "Edit > Undo";
    }

    private static string Value2(LookValue v) => v.ToString();

    /// <summary>Current value text of a wheel in the lamp settings.</summary>
    private string Value(ProfileFixture f, string key) =>
        ((DesignShow)_builder.Document).CueOrder[CueEditing.Cue].Of(f.HeadNumber)?.GetValueOrDefault(key)?.ToString();

    /// <summary>The row (found by its label text) in the lamp settings or the given view.</summary>
    private HBoxContainer RowOf(string label, Node under = null)
    {
        under ??= _panel;
        return under.FindChildren("*", nameof(Label), true, false).OfType<Label>()
                   .Where(l => l.Text == label && l.GetParent() is HBoxContainer).Select(l => (HBoxContainer)l.GetParent())
                   .FirstOrDefault() ?? throw new System.InvalidOperationException($"no row '{label}'");
    }

    /// <summary>The spin box of a channel (by its label).</summary>
    private SpinBox Spin(string key) => RowOf(LookEditor.Label(key)).GetChildren().OfType<SpinBox>().FirstOrDefault();

    /// <summary>Chooses an item of a row's picker (starting with the text).</summary>
    private async Task Pick(string label, string item, Node under = null)
    {
        var picker = RowOf(label, under).GetChildren().OfType<OptionButton>().First();
        int index = Enumerable.Range(0, picker.ItemCount).First(i => picker.GetItemText(i).StartsWith(item));
        picker.Selected = index;
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        await Frames(10);
    }

    /// <summary>The colour the lamp shows (the moving head's state, times its gel).</summary>
    private (float R, float G, float B) Tint(ProfileFixture f)
    {
        f = Live(f);
        var s = _builder.MoverOf(f).State;
        return (s.R, s.G, s.B);
    }

    /// <summary>The lamp of this head now (undo rebuilds the scene: lamps are new nodes).</summary>
    private ProfileFixture Live(ProfileFixture f) => _builder.Fixtures.First(x => x.HeadNumber == f.HeadNumber && IsInstanceValid(x) && !x.IsQueuedForDeletion());

    private static float Brightness((float R, float G, float B) c) => System.Math.Max(c.R, System.Math.Max(c.G, c.B));

    /// <summary>Distance of the normalized colours (hue).</summary>
    private static float Hue((float R, float G, float B) a, (float R, float G, float B) b)
    {
        float na = System.Math.Max(Brightness(a), 1e-4f), nb = System.Math.Max(Brightness(b), 1e-4f);
        return System.Math.Abs(a.R / na - b.R / nb) + System.Math.Abs(a.G / na - b.G / nb) + System.Math.Abs(a.B / na - b.B / nb);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Shot(string name)
    {
        string path = ProjectSettings.GlobalizePath($"res://tests/output/{name}.png");
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
