using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The key map: every bindable function of the application (AppAction) with its default keys, registered in Godot's
/// InputMap. Changes made in the key map view are saved as overrides only (actions that differ from their default) in
/// user://keymap.json, so new actions in later versions still get their default. Physical keys, so WASD stays in place
/// on an AZERTY keyboard. Code asks <see cref="Pressed"/> / <see cref="Held"/>, never hard-coded keys.
/// </summary>
public static class KeyMap
{
    /// <summary>Where the overrides are saved (Godot's per-user data folder).</summary>
    public const string UserFile = "user://keymap.json";

    /// <summary>Actions valid everywhere.</summary>
    public const string Global = "global";

    private static readonly Dictionary<string, List<KeyBinding>> Current = new();
    private static bool _loaded;
    private static string _path;

    /// <summary>All actions with their defaults, in key map view order.</summary>
    public static readonly IReadOnlyList<AppAction> Actions = new List<AppAction>
    {
        // Camera (3D views)
        new("camera_forward", "Move forward", "Camera", "3d", KeyBinding.Of(Key.W)),
        new("camera_back", "Move back", "Camera", "3d", KeyBinding.Of(Key.S)),
        new("camera_left", "Move left", "Camera", "3d", KeyBinding.Of(Key.A)),
        new("camera_right", "Move right", "Camera", "3d", KeyBinding.Of(Key.D)),
        new("camera_down", "Move down", "Camera", "3d", KeyBinding.Of(Key.Q)),
        new("camera_up", "Move up", "Camera", "3d", KeyBinding.Of(Key.E)),
        new("camera_fast", "Fast (hold)", "Camera", "3d", KeyBinding.Of(Key.Shift)),
        new("camera_preset_1", "Audience view", "Camera", "3d", KeyBinding.Of(Key.Key1)),
        new("camera_preset_2", "Side view", "Camera", "3d", KeyBinding.Of(Key.Key2)),
        new("camera_preset_3", "Plan view from above", "Camera", "3d", KeyBinding.Of(Key.Key3)),
        new("camera_frame", "Frame the selection", "Camera", "3d", KeyBinding.Of(Key.F)),
        // Audience seats
        new("seat_prev", "Previous seat", "Audience", Global, KeyBinding.Of(Key.Bracketleft)),
        new("seat_next", "Next seat", "Audience", Global, KeyBinding.Of(Key.Bracketright)),
        new("row_closer", "Row closer to the stage", "Audience", Global, KeyBinding.Of(Key.Pageup)),
        new("row_further", "Row further back", "Audience", Global, KeyBinding.Of(Key.Pagedown)),
        // Workspaces (UI review 31)
        new("workspace_rig", "Workspace: Rig (plan, 3D, truss heights)", "Workspaces", Global, KeyBinding.Of(Key.Key1, ctrl: true)),
        new("workspace_focus", "Workspace: Focus (3D, audience, plan)", "Workspaces", Global, KeyBinding.Of(Key.Key2, ctrl: true)),
        new("workspace_blocking", "Workspace: Blocking (timeline, 3D, audience)", "Workspaces", Global, KeyBinding.Of(Key.Key3, ctrl: true)),
        new("workspace_reset", "Reset the workspace to its default views", "Workspaces", Global),
        // Tools (windows of their own; the ids are those of the former views, so the user's keys stay)
        new("view_keymap", "Key map (tool window)", "Tools", Global, KeyBinding.Of(Key.F6)),
        new("view_gobo", "Gobo editor (tool window)", "Tools", Global, KeyBinding.Of(Key.F7)),
        // Views
        new("view_cycle", "Next view as main view", "Views", Global, KeyBinding.Of(Key.Tab)),
        new("view_3d", "3D view", "Views", Global, KeyBinding.Of(Key.F1)),
        new("view_audience", "Audience view", "Views", Global, KeyBinding.Of(Key.F2)),
        new("view_plan", "Plan (top view)", "Views", Global, KeyBinding.Of(Key.F3)),
        new("view_heights", "Truss heights", "Views", Global, KeyBinding.Of(Key.F4)),
        new("view_timeline", "Timeline", "Views", Global, KeyBinding.Of(Key.F5)),
        new("view_actor", "Actor view (through an actor's eyes)", "Views", Global, KeyBinding.Of(Key.F8)),
        new("view_cues", "Cue table: cues x heads (tool window)", "Tools", Global, KeyBinding.Of(Key.F9)),
        new("view_palettes", "Palettes (tool window)", "Tools", Global, KeyBinding.Of(Key.F10)),
        new("small_views_toggle", "Hide / show the small views", "Views", Global, KeyBinding.Of(Key.H)),
        new("timeline_dock_toggle", "Dock the timeline under the 3D / audience / actor view (on / off)", "Views", Global, KeyBinding.Of(Key.F5, shift: true)),
        new("panels_toggle", "Hide / show the side panels", "Views", Global, KeyBinding.Of(Key.B, ctrl: true)),
        new("inspector_toggle", "Collapse / open the inspector (right panel)", "Views", Global, KeyBinding.Of(Key.I, ctrl: true)),
        // Playback
        new("playback_go", "GO", "Playback", Global, KeyBinding.Of(Key.Space)),
        new("playback_back", "Back", "Playback", Global, KeyBinding.Of(Key.Backspace)),
        new("playback_reset", "Reset (before the first cue)", "Playback", Global, KeyBinding.Of(Key.Home)),
        new("playback_auto", "Auto on / off", "Playback", Global, KeyBinding.Of(Key.P)),
        new("cue_insert", "New cue after the current one (no changes: the look tracks)", "Playback", Global, KeyBinding.Of(Key.Insert)),
        new("cue_delete", "Delete the current cue", "Playback", Global, KeyBinding.Of(Key.Delete, ctrl: true)),
        // Timeline
        new("timeline_zoom_in", "Zoom in", "Timeline", "timeline", KeyBinding.Of(Key.Equal), KeyBinding.OfMouse(MouseButton.WheelUp, ctrl: true)),
        new("timeline_zoom_out", "Zoom out", "Timeline", "timeline", KeyBinding.Of(Key.Minus), KeyBinding.OfMouse(MouseButton.WheelDown, ctrl: true)),
        new("timeline_play", "Play / pause from the playhead", "Timeline", "timeline", KeyBinding.Of(Key.Space, shift: true)),
        new("timeline_delete", "Delete the selected step", "Timeline", "timeline", KeyBinding.Of(Key.Delete)),
        new("aim_click", "Aim the selected fixtures at the clicked point (hold + click)", "3D view", "3d", KeyBinding.Of(Key.Shift)),
        new("select_add", "Add a lamp to the selection / take it out (hold + click)", "3D view", "3d", KeyBinding.Of(Key.Ctrl)),
        new("intensity_up", "Selected fixtures' head brighter in the current cue", "3D view", "3d", KeyBinding.OfMouse(MouseButton.WheelUp, ctrl: true)),
        new("intensity_down", "Selected fixtures' head darker in the current cue", "3D view", "3d", KeyBinding.OfMouse(MouseButton.WheelDown, ctrl: true)),
        new("intensity_up_fine", "Brighter, 1 DMX step", "3D view", "3d", KeyBinding.OfMouse(MouseButton.WheelUp, ctrl: true, alt: true)),
        new("intensity_down_fine", "Darker, 1 DMX step", "3D view", "3d", KeyBinding.OfMouse(MouseButton.WheelDown, ctrl: true, alt: true)),
        new("intensity_up_coarse", "Brighter, 10 %", "3D view", "3d", KeyBinding.OfMouse(MouseButton.WheelUp, ctrl: true, shift: true)),
        new("intensity_down_coarse", "Darker, 10 %", "3D view", "3d", KeyBinding.OfMouse(MouseButton.WheelDown, ctrl: true, shift: true)),
        // Plan
        new("plan_fit", "Fit the plan in the view", "Plan", "plan", KeyBinding.Of(Key.F)),
        new("plan_free", "Place freely, no snapping (hold)", "Plan", "plan", KeyBinding.Of(Key.Alt)),
        new("plan_delete", "Delete the selected stand (else the selected lamps)", "Plan", "plan", KeyBinding.Of(Key.Delete)),
        // Fixtures
        new("fixture_solo", "Solo the selected fixture", "Fixtures", Global, KeyBinding.Of(Key.L)),
        new("deselect", "Deselect", "Fixtures", Global, KeyBinding.Of(Key.Escape)),
        // Gobo editor
        new("gobo_select", "Tool: select / move", "Gobo editor", "gobo", KeyBinding.Of(Key.Key1)),
        new("gobo_line", "Tool: line", "Gobo editor", "gobo", KeyBinding.Of(Key.Key2)),
        new("gobo_arc", "Tool: arc", "Gobo editor", "gobo", KeyBinding.Of(Key.Key3)),
        new("gobo_delete", "Delete the selected shape", "Gobo editor", "gobo", KeyBinding.Of(Key.Delete)),
        new("gobo_free", "Draw freely, no snapping (hold)", "Gobo editor", "gobo", KeyBinding.Of(Key.Alt)),
        // Edit (in the gobo editor: its drawing)
        new("edit_undo", "Undo", "Edit", Global, KeyBinding.Of(Key.Z, ctrl: true)),
        new("edit_redo", "Redo", "Edit", Global, KeyBinding.Of(Key.Y, ctrl: true), KeyBinding.Of(Key.Z, ctrl: true, shift: true)),
        // File
        new("file_open", "Import MagicQ show", "File", Global, KeyBinding.Of(Key.O, ctrl: true)),
        new("project_open_folder", "Open project folder", "File", Global, KeyBinding.Of(Key.O, ctrl: true, shift: true)),
        new("file_save", "Save the project", "File", Global, KeyBinding.Of(Key.S, ctrl: true)),
        new("file_save_as", "Export MagicQ show (.shw)", "File", Global, KeyBinding.Of(Key.S, ctrl: true, shift: true)),
        new("export_keyframes", "Export (stills, movie, contact sheet)", "File", Global, KeyBinding.Of(Key.E, ctrl: true)),
        new("export_movie", "Export a movie of the show", "File", Global, KeyBinding.Of(Key.R, ctrl: true)),
        // Present mode (UI review 27)
        new("present_toggle", "Present mode on / off (audience view, full screen)", "Present", Global, KeyBinding.Of(Key.F11)),
        new("present_caption", "Present mode: cue caption on / off", "Present", "present", KeyBinding.Of(Key.C)),
        new("viewpoint_1", "Present mode: viewpoint 1 (Regie)", "Present", "present", KeyBinding.Of(Key.Key1)),
        new("viewpoint_2", "Present mode: viewpoint 2", "Present", "present", KeyBinding.Of(Key.Key2)),
        new("viewpoint_3", "Present mode: viewpoint 3", "Present", "present", KeyBinding.Of(Key.Key3)),
        new("viewpoint_4", "Present mode: viewpoint 4", "Present", "present", KeyBinding.Of(Key.Key4)),
        new("viewpoint_5", "Present mode: viewpoint 5", "Present", "present", KeyBinding.Of(Key.Key5)),
        new("viewpoint_6", "Present mode: viewpoint 6", "Present", "present", KeyBinding.Of(Key.Key6)),
        new("viewpoint_7", "Present mode: viewpoint 7", "Present", "present", KeyBinding.Of(Key.Key7)),
        new("viewpoint_8", "Present mode: viewpoint 8", "Present", "present", KeyBinding.Of(Key.Key8)),
        new("viewpoint_9", "Present mode: viewpoint 9", "Present", "present", KeyBinding.Of(Key.Key9)),
        // Help
        new("preferences", "Preferences (UI scale, PDF paper, movie size, Art-Net)", "File", Global, KeyBinding.Of(Key.Comma, ctrl: true)),
        new("command_palette", "Command palette (find any command)", "Help", Global, KeyBinding.Of(Key.P, ctrl: true, shift: true)),
    };

    /// <summary>Raised after the key map changed (a binding set, reset or all reset): tooltips and menus show keys.</summary>
    public static event System.Action Changed;

    /// <summary>The keys of an action as shown to the user ("Ctrl+S", "Space / Mouse 4"); "" without keys.</summary>
    public static string KeysText(string id) => string.Join(" / ", Bindings(id).Select(b => b.Display));

    /// <summary>`text` with the action's keys in brackets ("GO (Space)"), or `text` when it has none.</summary>
    public static string WithKeys(string text, string id) =>
        KeysText(id) is { Length: > 0 } keys ? $"{text} ({keys})" : text;

    /// <summary>"(Save: Ctrl+S)": what to do and the action's current keys, for status lines; "(Save)" without keys.</summary>
    public static string Hint(string what, string id) =>
        KeysText(id) is { Length: > 0 } keys ? $"({what}: {keys})" : $"({what})";

    /// <summary>
    /// Sets a tooltip that names the action's current keys (UI review 12: never hard-coded keys), again whenever the key
    /// map changes.
    /// </summary>
    public static void Tooltip(Control control, string text, string id)
    {
        void Update()
        {
            if (GodotObject.IsInstanceValid(control))
                control.TooltipText = WithKeys(text, id);
            else
                Changed -= Update;
        }
        Update();
        Changed += Update;
    }

    /// <summary>
    /// Loads the user's overrides and registers every action in Godot's InputMap, unless a key map is loaded already
    /// (a test's own key map stays: tests must not use the user's bindings).
    /// </summary>
    public static void EnsureLoaded()
    {
        if (!_loaded)
            EnsureLoaded(UserFile);
    }

    /// <summary>Loads the overrides of this file (once per file) and registers every action in Godot's InputMap.</summary>
    public static void EnsureLoaded(string path)
    {
        if (_loaded && path == _path)
            return;
        _loaded = true;
        _path = path;
        Current.Clear();
        foreach (var a in Actions)
            Current[a.Id] = a.Defaults.ToList();
        string file = ProjectSettings.GlobalizePath(path);
        if (File.Exists(file))
        {
            try
            {
                var overrides = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(file)) ?? new();
                foreach (var (id, texts) in overrides)
                    if (Current.ContainsKey(id))   // unknown actions (older / newer versions) are ignored
                        Current[id] = texts.Select(KeyBinding.Parse).Where(b => b != null).ToList();
            }
            catch (JsonException e)
            {
                GD.PushWarning($"KeyMap: {file} is not valid JSON, using the defaults ({e.Message})");
            }
        }
        foreach (var a in Actions)
            Register(a.Id);
    }

    /// <summary>The action definition, or null.</summary>
    public static AppAction Find(string id) => Actions.FirstOrDefault(a => a.Id == id);

    /// <summary>Current bindings of an action.</summary>
    public static IReadOnlyList<KeyBinding> Bindings(string id)
    {
        EnsureLoaded(_path ?? UserFile);
        return Current.TryGetValue(id, out var list) ? list : new List<KeyBinding>();
    }

    /// <summary>Sets binding `index` (0 or 1) of an action; null removes it. Saves the overrides.</summary>
    public static void Bind(string id, int index, KeyBinding binding)
    {
        EnsureLoaded(_path ?? UserFile);
        var list = Current[id];
        if (binding == null)
        {
            if (index < list.Count)
                list.RemoveAt(index);
        }
        else if (index < list.Count)
            list[index] = binding;
        else
            list.Add(binding);
        Register(id);
        Save();
    }

    /// <summary>Back to the default keys of one action.</summary>
    public static void Reset(string id)
    {
        Current[id] = Find(id).Defaults.ToList();
        Register(id);
        Save();
    }

    /// <summary>Back to the default key map.</summary>
    public static void ResetAll()
    {
        foreach (var a in Actions)
        {
            Current[a.Id] = a.Defaults.ToList();
            Register(a.Id);
        }
        Save();
    }

    /// <summary>Other actions that use the same binding where both can be active (same context, or one is global).</summary>
    public static IEnumerable<AppAction> Conflicts(string id, KeyBinding binding)
    {
        var me = Find(id);
        return Actions.Where(a => a.Id != id
                                  && (a.Context == me.Context || a.Context == Global || me.Context == Global)
                                  && Bindings(a.Id).Contains(binding));
    }

    /// <summary>True when the action differs from its default.</summary>
    public static bool IsChanged(string id) => !Bindings(id).SequenceEqual(Find(id).Defaults);

    /// <summary>Writes the overrides (only changed actions).</summary>
    public static void Save()
    {
        var overrides = Actions.Where(a => IsChanged(a.Id))
            .ToDictionary(a => a.Id, a => Bindings(a.Id).Select(b => b.ToString()).ToList());
        string file = ProjectSettings.GlobalizePath(_path ?? UserFile);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(overrides, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // "Ctrl+S", not an escaped plus
        }));
        Changed?.Invoke();
    }

    private static void Register(string id)
    {
        if (!InputMap.HasAction(id))
            InputMap.AddAction(id);
        InputMap.ActionEraseEvents(id);
        foreach (var b in Current[id])
            InputMap.ActionAddEvent(id, b.ToEvent());
    }

    /// <summary>
    /// True when the event presses the action (not an echo) with exactly its modifiers, so Shift+Space doesn't
    /// trigger Space. Ignored while typing in a text field. An <see cref="InputEventAction"/> of the action (sent by
    /// <see cref="Run"/>) counts too.
    /// </summary>
    public static bool Pressed(InputEvent e, string id, Viewport viewport)
    {
        EnsureLoaded(_path ?? UserFile);
        // A menu or the command palette sends the action itself (also when it has no key).
        if (e is InputEventAction action)
            return action.Pressed && action.Action == id;
        if (Typing(viewport) || e is InputEventKey { Echo: true })
            return false;
        var b = KeyBinding.FromEvent(e);
        return b != null && e.IsPressed() && Bindings(id).Contains(b);
    }

    /// <summary>True while the action's key is held (modifier-only bindings like Shift included).</summary>
    public static bool Held(string id, Viewport viewport)
    {
        EnsureLoaded(_path ?? UserFile);
        if (Typing(viewport))
            return false;
        foreach (var b in Bindings(id))
        {
            if (b.Mouse != MouseButton.None)
                continue;
            if (!Input.IsPhysicalKeyPressed(b.Key))
                continue;
            if (b.IsModifierOnly)
                return true;
            // Extra modifiers (e.g. Ctrl+S = save) don't move the camera.
            if (Input.IsKeyPressed(Key.Ctrl) == b.Ctrl && Input.IsKeyPressed(Key.Alt) == b.Alt)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Runs an action as if its key was pressed (menus, command palette): an <see cref="InputEventAction"/> goes through
    /// the normal input handling, so the same code reacts as for the key.
    /// </summary>
    public static void Run(string id) => Input.ParseInputEvent(new InputEventAction { Action = id, Pressed = true });

    /// <summary>True while a text field has the keyboard focus (keys type text, they are not actions).</summary>
    public static bool Typing(Viewport viewport) =>
        viewport?.GuiGetFocusOwner() is LineEdit or TextEdit;
}
