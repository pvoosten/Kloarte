using System.IO;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;

namespace Kloarte.Tests;

/// <summary>BindingTests: the key map (text form, overrides file, Godot's InputMap, conflicts, reset).</summary>
public partial class BindingTests
{
    private void KeyMapTest()
    {
        // Text form round trip.
        foreach (string t in new[] { "Ctrl+Shift+S", "F5", "Space", "Mouse:WheelUp", "Ctrl+Mouse:WheelDown", "Shift", "BracketLeft" })
        {
            var b = KeyBinding.Parse(t);
            Check(b != null && KeyBinding.Parse(b.ToString()) == b, $"binding '{t}' -> {b} round trip");
        }
        Check(KeyBinding.Parse("NoSuchKey") == null, "unknown key name is rejected");

        // Keys as menus show them (explored 11 Oct 2026: "Ctrl+O - Physical").
        string shown = KeyBinding.Of(Key.O, ctrl: true).ToDisplayEvent().AsText();
        Check(shown == "Ctrl+O", $"menu text of Ctrl+O: '{shown}'");
        Check(KeyBinding.OfMouse(MouseButton.WheelUp, ctrl: true).Display == "Ctrl+Wheel up", "wheel bindings in words");
        // Explored 11 Oct 2026: Present mode's hint said "BracketLeft BracketRight seats".
        string keys = string.Join(" ", new[] { Key.Bracketleft, Key.Bracketright, Key.Pageup, Key.Pagedown, Key.Escape }
            .Select(k => KeyBinding.Of(k).Display));
        Check(keys == "[ ] PgUp PgDn Esc", $"keys as on the key caps: '{keys}'");
        Check(KeyMap.WithKeys("GO", "playback_go").StartsWith("GO ("), $"tooltip with keys: {KeyMap.WithKeys("GO", "playback_go")}");

        // Defaults, overrides file with one change and an unknown action.
        string file = "res://tests/output/binding/keymap.json";
        string global = ProjectSettings.GlobalizePath(file);
        File.WriteAllText(global, "{ \"playback_go\": [\"Enter\"], \"no_such_action\": [\"F9\"] }");
        KeyMap.EnsureLoaded(file);
        Check(KeyMap.Actions.All(a => InputMap.HasAction(a.Id)), "every action is registered in the InputMap");
        Check(KeyMap.Bindings("playback_go").SequenceEqual(new[] { KeyBinding.Of(Key.Enter) }), "override: GO = Enter");
        Check(KeyMap.Bindings("playback_back").SequenceEqual(new[] { KeyBinding.Of(Key.Backspace) }), "other actions keep their default");
        Check(KeyMap.IsChanged("playback_go") && !KeyMap.IsChanged("playback_back"), "changed flags");
        var go = new InputEventKey { PhysicalKeycode = Key.Enter, Pressed = true };
        var space = new InputEventKey { PhysicalKeycode = Key.Space, Pressed = true };
        var shiftSpace = new InputEventKey { PhysicalKeycode = Key.Space, ShiftPressed = true, Pressed = true };
        Check(KeyMap.Pressed(go, "playback_go", null) && !KeyMap.Pressed(space, "playback_go", null), "Enter fires GO, Space doesn't");
        Check(KeyMap.Pressed(shiftSpace, "timeline_play", null), "Shift+Space = timeline play");

        // Conflicts: same context or global; different views may share a key.
        Check(KeyMap.Conflicts("playback_back", KeyBinding.Of(Key.Enter)).Any(a => a.Id == "playback_go"), "conflict with GO is found");
        Check(!KeyMap.Conflicts("plan_fit", KeyBinding.Of(Key.F)).Any(a => a.Id == "camera_frame"), "F in the plan and in 3D don't conflict");

        // Bind, save (only overrides), reset.
        KeyMap.Bind("fixture_solo", 0, KeyBinding.Of(Key.K, ctrl: true));
        string json = File.ReadAllText(global);
        Check(json.Contains("fixture_solo") && json.Contains("Ctrl+K") && json.Contains("playback_go") && !json.Contains("no_such_action")
              && !json.Contains("playback_back"), "saved file holds only the overrides: " + json.ReplaceLineEndings(" "));
        KeyMap.ResetAll();
        Check(!KeyMap.Actions.Any(a => KeyMap.IsChanged(a.Id)) && File.ReadAllText(global).Trim() == "{}", "reset all");
        KeyMap.EnsureLoaded(KeyMap.UserFile);
    }
}
