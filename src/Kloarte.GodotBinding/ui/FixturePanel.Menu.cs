using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, menu bar and command palette (UI review 11, 12, 13): File (projects, save, exports, quit), Edit (undo /
/// redo with the entry's name), View (the
/// views, layout, side panels), Playback, Help (key map, command palette, About). Items that are key map actions show
/// their current keys and run through the key map (<see cref="KeyMap.Run"/>), so a menu item and its key do exactly the
/// same; the menus never handle keys themselves. The command palette (Ctrl+Shift+P) finds every menu command by name.
/// </summary>
public partial class FixturePanel
{
    /// <summary>A menu command: where it is, its name, the key map action it runs (or null) and what it does.</summary>
    private sealed record MenuCommand(string Menu, string Name, string ActionId, Action Run);

    /// <summary>Every menu command (the command palette lists them).</summary>
    private readonly List<MenuCommand> _commands = new();

    /// <summary>First id of the menu bar's own items (the project items use lower ids).</summary>
    private const int CommandIdBase = 1000;

    /// <summary>The command palette: a search field and the matching commands.</summary>
    private PopupPanel _palette;
    private LineEdit _paletteSearch;
    private ItemList _paletteList;

    /// <summary>The commands listed in the palette, in list order.</summary>
    private readonly List<MenuCommand> _paletteShown = new();

    /// <summary>Help > About.</summary>
    private AcceptDialog _about;

    /// <summary>The menu bar.</summary>
    private Control MenuRow()
    {
        var bar = new MenuBar { PreferGlobalMenu = false };
        _menuBar = bar;
        var file = Menu(bar, "File");
        AddProjectItems(file);
        // The project items have their own ids: the palette gets them as commands of its own.
        PaletteOnly("File", "New project...", () => AskToSave("making a new project", () => AskProjectName(null)));
        PaletteOnly("File", "New project from show...", () => AskToSave("making a new project", PickShowForProject));
        PaletteOnly("File", "Import MagicQ show (.shw)...", () => KeyMap.Run("file_open"), "file_open");
        file.AddSeparator();
        Item(file, "Save", "file_save");
        Item(file, "Export MagicQ show...", "file_save_as");
        file.AddSeparator();
        Item(file, "Export...", "export_keyframes");
        Item(file, "Record a movie...", "export_movie");
        file.AddSeparator();
        Item(file, "Preferences...", "preferences");
        file.AddSeparator();
        Item(file, "Quit", null, () => Notification((int)NotificationWMCloseRequest));

        var edit = Menu(bar, "Edit");
        int undoId = Item(edit, "Undo", "edit_undo");
        int redoId = Item(edit, "Redo", "edit_redo");
        edit.AboutToPopup += () => UpdateEditMenu(edit, undoId, redoId);

        var view = Menu(bar, "View");
        Item(view, "Workspace: Rig", "workspace_rig");
        Item(view, "Workspace: Focus", "workspace_focus");
        Item(view, "Workspace: Blocking", "workspace_blocking");
        Item(view, "Reset workspace", "workspace_reset");
        view.AddSeparator();
        foreach (string id in new[] { "view_3d", "view_audience", "view_plan", "view_heights", "view_timeline", "view_actor" })
            Item(view, KeyMap.Find(id).Name, id);
        view.AddSeparator();
        Item(view, "Next view as main view", "view_cycle");
        Item(view, "Hide / show the small views", "small_views_toggle");
        Item(view, "Hide / show the side panels", "panels_toggle");
        Item(view, "Collapse / open the inspector", "inspector_toggle");
        view.AddSeparator();
        Item(view, "Present mode", "present_toggle");

        var playback = Menu(bar, "Playback");
        Item(playback, "GO", "playback_go");
        Item(playback, "Back", "playback_back");
        Item(playback, "Reset (before the first cue)", "playback_reset");
        Item(playback, "Auto on / off", "playback_auto");
        playback.AddSeparator();
        Item(playback, "New cue after the current one", "cue_insert");
        Item(playback, "Delete the current cue", "cue_delete");
        playback.AddSeparator();
        Item(playback, "Open in MagicQ PC", null, OpenInMagicQ);

        var tools = Menu(bar, "Tools");
        Item(tools, "Cue table", "view_cues");
        Item(tools, "Palettes", "view_palettes");
        Item(tools, "Gobo editor", "view_gobo");
        Item(tools, "Key map", "view_keymap");

        var help = Menu(bar, "Help");
        Item(help, "Keys (key map)", "view_keymap");
        Item(help, "Command palette...", "command_palette");
        help.AddSeparator();
        Item(help, "About Kloarte...", null, ShowAbout);

        BuildPalette();
        KeyMap.Changed += () => RefreshMenuKeys(bar);
        RefreshMenuKeys(bar);
        return bar;
    }

    /// <summary>A menu of the bar (its title is the node name).</summary>
    private static PopupMenu Menu(MenuBar bar, string title)
    {
        var menu = new PopupMenu { Name = title };
        bar.AddChild(menu);
        return menu;
    }

    /// <summary>
    /// Adds a command: a key map action (`actionId`, run through <see cref="KeyMap.Run"/>) or `run`. Its id is its index
    /// in <see cref="_commands"/> plus <see cref="CommandIdBase"/>; returns it.
    /// </summary>
    private int Item(PopupMenu menu, string name, string actionId, Action run = null)
    {
        int id = CommandIdBase + _commands.Count;
        _commands.Add(new MenuCommand(menu.Name, name, actionId, run ?? (() => KeyMap.Run(actionId))));
        menu.AddItem(name, id);
        if (menu.GetItemCount() == 1 || !menu.HasMeta("commands"))
        {
            menu.SetMeta("commands", true);
            menu.IdPressed += pressed =>
            {
                if (pressed >= CommandIdBase && pressed < CommandIdBase + _commands.Count)
                    _commands[(int)pressed - CommandIdBase].Run();
            };
        }
        return id;
    }

    /// <summary>A command only in the palette (its menu item is made elsewhere).</summary>
    private void PaletteOnly(string menu, string name, Action run, string actionId = null) =>
        _commands.Add(new MenuCommand(menu, name, actionId, run));

    /// <summary>Shows the current keys of every action item of the bar's menus.</summary>
    private void RefreshMenuKeys(MenuBar bar)
    {
        foreach (var menu in bar.GetChildren().OfType<PopupMenu>())
            for (int i = 0; i < menu.ItemCount; i++)
            {
                int id = menu.GetItemId(i);
                if (id >= CommandIdBase && _commands[id - CommandIdBase].ActionId is { } action)
                    ShowKeys(menu, id, action);
            }
    }

    /// <summary>
    /// Shows an action's first key on a menu item. The item's shortcut is disabled: the key map handles keys, the menu
    /// only shows them.
    /// </summary>
    private static void ShowKeys(PopupMenu menu, int id, string actionId)
    {
        int index = menu.GetItemIndex(id);
        var binding = KeyMap.Bindings(actionId).FirstOrDefault(b => b.Mouse == MouseButton.None);
        if (binding == null)
        {
            menu.SetItemShortcut(index, null);
            return;
        }
        var shortcut = new Shortcut();
        shortcut.Events.Add(binding.ToDisplayEvent());
        menu.SetItemShortcut(index, shortcut, false);
        menu.SetItemShortcutDisabled(index, true);
    }

    /// <summary>Builds the command palette (hidden until <see cref="ShowPalette"/>).</summary>
    private void BuildPalette()
    {
        _palette = new PopupPanel { Title = "Commands" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(520, 360) };
        _paletteSearch = new LineEdit { PlaceholderText = "Type to find a command", ClearButtonEnabled = true };
        _paletteSearch.TextChanged += FillPalette;
        _paletteSearch.TextSubmitted += _ => RunPalette(_paletteList.GetSelectedItems().FirstOrDefault());
        _paletteSearch.GuiInput += e =>
        {
            // Up / down move through the list while typing.
            if (e is InputEventKey { Pressed: true } k && k.Keycode is Key.Down or Key.Up && _paletteList.ItemCount > 0)
            {
                int current = _paletteList.GetSelectedItems().FirstOrDefault();
                int next = Math.Clamp(current + (k.Keycode == Key.Down ? 1 : -1), 0, _paletteList.ItemCount - 1);
                _paletteList.Select(next);
                _paletteList.EnsureCurrentIsVisible();
                _paletteSearch.AcceptEvent();
            }
        };
        _paletteList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _paletteList.ItemActivated += index => RunPalette((int)index);
        box.AddChild(_paletteSearch);
        box.AddChild(_paletteList);
        _palette.AddChild(box);
        AddChild(_palette);
    }

    /// <summary>Shows the command palette with every command.</summary>
    internal void ShowPalette()
    {
        _paletteSearch.Text = "";
        FillPalette("");
        _palette.PopupCentered();
        _paletteSearch.GrabFocus();
    }

    /// <summary>The palette's commands for `filter` as listed ("File > Save"; tests).</summary>
    internal List<string> PaletteCommands(string filter)
    {
        FillPalette(filter);
        return _paletteShown.Select(c => $"{c.Menu} > {c.Name}").ToList();
    }

    /// <summary>
    /// Lists the commands whose menu, name or keys contain every word of `filter`, and "Open project: name" for each
    /// project (most recently saved first).
    /// </summary>
    private void FillPalette(string filter)
    {
        _paletteList.Clear();
        _paletteShown.Clear();
        var words = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var projects = (CurrentBuilder?.Files.AllProjects() ?? Array.Empty<Kloarte.Model.KloarteProject>())
            .Select(p => p.Name)
            .Select(name => new MenuCommand("File", $"Open project: {name}", null,
                () => AskToSave($"opening project {name}", () => OpenProject(name))));
        foreach (var c in _commands.Concat(projects))
        {
            string keys = c.ActionId != null ? KeyMap.KeysText(c.ActionId) : "";
            string text = $"{c.Menu} > {c.Name}";
            if (!words.All(w => (text + " " + keys).Contains(w, StringComparison.OrdinalIgnoreCase)))
                continue;
            _paletteList.AddItem(keys.Length > 0 ? $"{text}    ({keys})" : text);
            _paletteShown.Add(c);
        }
        if (_paletteList.ItemCount > 0)
            _paletteList.Select(0);
    }

    /// <summary>Runs the palette's command at `index` (closing the palette first).</summary>
    private void RunPalette(int index)
    {
        if (index < 0 || index >= _paletteShown.Count)
            return;
        _palette.Hide();
        _paletteShown[index].Run();
    }

    /// <summary>Help > About: version (build date and git commit), author, and the licences of what Kloarte uses.</summary>
    private void ShowAbout()
    {
        _about ??= AddAbout();
        _aboutText.Text = $"Kloarte up de plankn\n\nVersion {Version}\n\nAuthor: Philip van Oosten, all rights reserved.\n\n" +
                          $"Godot {Engine.GetVersionInfo()["string"]}, .NET {System.Environment.Version}";
        _about.PopupCentered(new Vector2I(760, 560));
    }

    /// <summary>The label of the About dialog with the version.</summary>
    private Label _aboutText;

    /// <summary>Makes the About dialog: the version above a read-only text with the licences.</summary>
    private AcceptDialog AddAbout()
    {
        var dialog = new AcceptDialog { Title = "About Kloarte" };
        var box = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _aboutText = new Label();
        box.AddChild(_aboutText);
        box.AddChild(new Label { Text = "Licences and third-party notices" });
        box.AddChild(new TextEdit
        {
            Text = LicenseText(),
            Editable = false,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(700, 340),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });
        dialog.AddChild(box);
        AddChild(dialog);
        return dialog;
    }

    /// <summary>
    /// The licence texts: Kloarte's notices of the libraries and data it ships (<c>data/third_party_notices.txt</c>), then
    /// Godot's own licence and the licences of the components inside the engine (the engine knows them).
    /// </summary>
    private static string LicenseText()
    {
        var text = new System.Text.StringBuilder();
        string file = AppPaths.File("res://data/third_party_notices.txt");
        text.AppendLine(System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file) : "(third_party_notices.txt is missing)");
        text.AppendLine().AppendLine("=== Godot Engine licence ===").AppendLine().AppendLine(Engine.GetLicenseText());
        text.AppendLine("=== Components inside Godot ===").AppendLine();
        foreach (var part in Engine.GetCopyrightInfo())
        {
            text.AppendLine(part["name"].AsString());
            foreach (var p in part["parts"].AsGodotArray<Godot.Collections.Dictionary>())
            {
                text.AppendLine($"  {string.Join(", ", p["files"].AsStringArray())}");
                text.AppendLine($"  (c) {string.Join("; ", p["copyright"].AsStringArray())}  [{p["license"].AsString()}]");
            }
            text.AppendLine();
        }
        foreach (var (name, body) in Engine.GetLicenseInfo())
            text.AppendLine($"=== {name} ===").AppendLine().AppendLine(body.AsString()).AppendLine();
        return text.ToString();
    }

    /// <summary>
    /// The build: date and git commit, stamped into this assembly at build time (Kloarte.GodotBinding.csproj, target
    /// StampVersion; "+ changes" when the working tree had uncommitted changes).
    /// </summary>
    internal static string Version
    {
        get
        {
            var meta = typeof(FixturePanel).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToDictionary(a => a.Key, a => a.Value);
            string date = meta.GetValueOrDefault("BuildDate") ?? "?";
            string commit = meta.GetValueOrDefault("Commit") is { Length: > 0 } c ? c : "unknown commit";
            return $"{date} ({commit})";
        }
    }
}
