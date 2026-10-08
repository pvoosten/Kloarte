using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Runtime UI. Left: the project box (FixturePanel.Project.cs), "Open show" (MagicQ .shw, the .xhw next to it is
/// loaded too) and three tabs:
///  - Fixtures: list filtered by a MagicQ group or cue (dropdowns; edit groups and cues in MagicQ), Solo,
///    Only the listed (filtered) fixtures can light up; Solo narrows that to the selected fixture.
///  - Playback / Actors: see FixturePanel.Playback.cs.
/// Bottom: Save (the whole project, FixturePanel.Saving.cs) and "Save show as" (a copy). Right: the inspector
/// (FixturePanel.Inspector.cs): settings of the selected fixture or actor, else the show's info and the Look. Changes are
/// live until saved.
/// </summary>
public partial class FixturePanel : CanvasLayer
{
    private OptionButton _groupFilter, _cueFilter;
    private FixtureTable _list;
    private CheckBox _solo;
    private PanelContainer _right;
    private VBoxContainer _params;
    private MeshInstance3D _marker;
    private Label _showLabel;

    /// <summary>Messages: shown in the window's status bar (<see cref="StatusBar"/>).</summary>
    private readonly StatusText _status = new();

    /// <summary>The window's status bar (bottom) and the transport bar (top, FixturePanel.Transport.cs).</summary>
    private StatusBar _statusBar;
    private Control _transport;
    private FileDialog _openDialog, _saveDialog;

    private readonly List<ProfileFixture> _all = new();
    private readonly List<ProfileFixture> _shown = new();
    /// <summary>fixtures switched off by solo / filter</summary>
    private readonly Dictionary<ProfileFixture, float> _offSaved = new();
    private bool _filterActive;

    /// <summary>Intensity of a soloed fixture (user: a solo lamp shows at 70 %).</summary>
    private const float SoloIntensity = 0.7f;

    /// <summary>The fixture set to <see cref="SoloIntensity"/> by Solo, and its own intensity to give back.</summary>
    private ProfileFixture _soloLamp;
    private float _soloSaved;
    private ProfileFixture _selected;

    /// <summary>Time (ms since start) of a window close refused because a file dialog was open; a second close
    /// within <see cref="ForceQuitWindowMs"/> quits anyway.</summary>
    private ulong _closeRefusedAt;

    /// <summary>How long after a refused close a second close forces the quit.</summary>
    private const ulong ForceQuitWindowMs = 10_000;

    /// <inheritdoc />
    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest)
            return;
        if (!NativeFileDialogs.AnyOpen)
        {
            AskToSave("closing Kloarte", () =>
            {
                GD.Print("Window closed: quitting");
                GetTree().Quit();
            });
            return;
        }
        ulong now = Time.GetTicksMsec();
        if (_closeRefusedAt > 0 && now - _closeRefusedAt < ForceQuitWindowMs)
        {
            // Godot's normal exit would hang on the open dialog: end the process instead.
            GD.Print($"Window closed again with {NativeFileDialogs.OpenTitles} open: ending the process");
            OS.Kill(OS.GetProcessId());
            return;
        }
        _closeRefusedAt = now;
        _status.Text = $"Close the file dialog {NativeFileDialogs.OpenTitles} first (it may be behind this window: see the " +
                       "taskbar). Close again within 10 s to quit anyway.";
        GD.Print($"Window close refused: {NativeFileDialogs.OpenTitles} open (Godot would hang at exit)");
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded();
        // UI scale from the preferences (UI review 18; the window doesn't stretch the UI any more).
        Preferences.Changed += OnPreferencesChanged;
        TreeExiting += () => Preferences.Changed -= OnPreferencesChanged;
        // Quit through _Notification: closing with a native file dialog open would hang Godot.
        GetTree().AutoAcceptQuit = false;
        // Menu bar on top (FixturePanel.Menu.cs), below it the panels and the views.
        var window = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        window.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        window.AddThemeConstantOverride("separation", 0);
        AddChild(window);
        var root = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        window.AddChild(MenuRow());
        window.AddChild(root);
        // The status bar first: the views and tabs post to it while they are built.
        _statusBar = new StatusBar { Name = "StatusBar" };
        window.AddChild(_statusBar);

        // Left: show and fixture list.
        var left = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        var leftBox = new VBoxContainer();
        left.AddChild(leftBox);

        _showLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _openDialog = new FileDialog
        {
            Title = "Import MagicQ show",
            ModeOverridesTitle = false,   // keep the title (else "Open a File")
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.shw ; MagicQ show" },
            UseNativeDialog = true,
        };
        _openDialog.FileSelected += OpenShow;
        AddChild(_openDialog);
        _saveDialog = new FileDialog
        {
            Title = "Export MagicQ show",
            ModeOverridesTitle = false,   // keep the title (else "Open a File")
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.shw ; MagicQ show" },
            UseNativeDialog = true,
        };
        _saveDialog.FileSelected += SaveShowAs;
        AddChild(_saveDialog);

        _groupFilter = new OptionButton { TooltipText = "Show the fixtures of a MagicQ group" };
        _groupFilter.ItemSelected += _ =>
        {
            if (_groupFilter.Selected > 0)
                _cueFilter.Select(0);
            FillList();
        };
        _cueFilter = new OptionButton { TooltipText = "Show the fixtures recorded in a cue (with their level)" };
        _cueFilter.ItemSelected += _ =>
        {
            if (_cueFilter.Selected > 0)
                _groupFilter.Select(0);
            FillList();
        };
        // Columns Head / Name / Type / Gel / Level (UI review 22); several lamps with Ctrl / Shift + click
        // (FixturePanel.Multi.cs).
        _list = new FixtureTable { Name = "FixtureTable", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list.RowsSelected += lamps => Selection.Select(lamps);
        _list.RowMenuRequested += ShowLampMenu;
        _solo = new CheckBox { Text = "Solo" };
        KeyMap.Tooltip(_solo, "Only the selected fixture lights, at 70 %", "fixture_solo");
        _solo.Toggled += _ =>
        {
            ApplyLit();
            if (_selected != null)
                BuildParams();   // the intensity slider shows the solo level
        };
        var refresh = new Button { Text = "Refresh list" };
        refresh.Pressed += Rescan;

        leftBox.AddChild(ProjectBox());
        leftBox.AddChild(_showLabel);

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var fixturesTab = new VBoxContainer { Name = "Fixtures" };
        fixturesTab.AddChild(_groupFilter);
        fixturesTab.AddChild(_cueFilter);
        fixturesTab.AddChild(_list);
        fixturesTab.AddChild(_solo);
        fixturesTab.AddChild(refresh);
        _tabs.AddChild(fixturesTab);
        _tabs.AddChild(BuildPlaybackTab());
        window.AddChild(_transport);
        window.MoveChild(_transport, 1);   // under the menu bar
        _tabs.AddChild(BuildActorsTab());
        _tabs.TabChanged += OnTabChanged;
        leftBox.AddChild(_tabs);

        leftBox.AddChild(SaveRow());
        root.AddChild(left);

        // Middle: the views (3D, audience, plan, ...).
        _host = new ViewHost();
        root.AddChild(_host);
        _left = left;
        BuildViews();
        BuildStartScreen();   // FixturePanel.StartScreen.cs

        // Right: the inspector (settings of the selection; FixturePanel.Inspector.cs).
        BuildInspector(root);
        UiFit.Watch(left);
        GetViewport().SizeChanged += FitPanels;
        ApplyUiScale();

        _marker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.45f, 0f),
                NoDepthTest = true,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_marker);

        // Fixtures are generated in the ShowBuilder's _Ready; scan once everything is in the tree.
        CallDeferred(MethodName.Rescan);
    }

    /// <summary>
    /// Side panel widths follow the window: about a quarter of the width each, within limits, so the views keep at
    /// least half of a narrow window; the inspector keeps the width it was dragged to (at most half the window).
    /// </summary>
    private void FitPanels()
    {
        float width = GetViewport().GetVisibleRect().Size.X;
        _left.CustomMinimumSize = new Vector2(Mathf.Clamp(width * 0.22f, 220f, 320f), 0);
        _right.CustomMinimumSize = new Vector2(InspectorWidth(width), 0);
    }

    // --- Show ---

    private List<ShowBuilder> Builders()
    {
        var result = new List<ShowBuilder>();
        void Walk(Node node)
        {
            if (node is ShowBuilder builder)
                result.Add(builder);
            foreach (Node child in node.GetChildren())
                Walk(child);
        }
        Walk(GetTree().Root);
        return result;
    }

    private ShowBuilder CurrentBuilder => Builders().FirstOrDefault(b => b.Model != null);

    private static ShowBuilder BuilderOf(Node node)
    {
        for (var n = node; n != null; n = n.GetParent())
            if (n is ShowBuilder builder)
                return builder;
        return null;
    }

    private void ShowOpenDialog()
    {
        string current = CurrentBuilder?.Files.SourceFile ?? "";
        string dir = current.Length > 0 ? System.IO.Path.GetDirectoryName(current) : CurrentBuilder?.Files.ShowFolder ?? "";
        if (System.IO.Directory.Exists(dir))
            _openDialog.CurrentDir = dir;
        NativeFileDialogs.Popup(_openDialog, new Vector2I(900, 600));
    }

    private void ShowSaveDialog()
    {
        var builder = CurrentBuilder;
        if (builder == null)
        {
            _status.Text = "No show loaded";
            return;
        }
        string next = builder.Files.NextShowFile;
        _saveDialog.CurrentDir = System.IO.Path.GetDirectoryName(next);
        _saveDialog.CurrentFile = System.IO.Path.GetFileName(next);
        NativeFileDialogs.Popup(_saveDialog, new Vector2I(900, 600));
    }

    private void SaveShowAs(string path)
    {
        try
        {
            _status.Text = CurrentBuilder?.SaveShowAs(path) ?? "No show loaded";
        }
        catch (Exception e)
        {
            _status.Text = $"Could not save {path}: {e.Message}";
        }
    }

    /// <summary>
    /// A MagicQ show chosen to open: one of the open native project (a numbered save of its show) is re-imported, with
    /// the list of what differs (<see cref="Reimport"/>); any other opens its project, or becomes a new one (imported).
    /// </summary>
    private void OpenShow(string path)
    {
        var builder = CurrentBuilder;
        if (CueEditing.Design(builder) != null && ShowVersions.IsVersionOf(path, builder.ProjectName))
        {
            Reimport(path);
            return;
        }
        _ = OpenShowAsync(path);
    }

    /// <summary>Loads a show (see <see cref="OpenShow"/>); done when the panel shows it.</summary>
    private System.Threading.Tasks.Task OpenShowAsync(string path) =>
        ChangeShowAsync($"open {System.IO.Path.GetFileName(path)}", builder => builder.OpenShow(path));

    /// <summary>
    /// Changes the scene's show (another show, project, new project) through the first ShowBuilder of the scene, or one
    /// added for it (e.g. in main.tscn); done when the panel shows the result. An error goes to the status line.
    /// Returns false when the change failed.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> ChangeShowAsync(string what, Action<ShowBuilder> change)
    {
        Deselect();
        try
        {
            var builder = Builders().FirstOrDefault();
            if (builder == null)
            {
                builder = new ShowBuilder { Name = "Show", FixtureConfig = "res://shows/risquons-tout.fixtures.json" };
                GetTree().CurrentScene.AddChild(builder);
            }
            change(builder);
        }
        catch (Exception e)
        {
            _status.Text = $"Could not {what}: {e.Message}";
            GD.PrintErr(_status.Text);
            return false;
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);   // old fixtures are freed at frame end
        Rescan();
        _status.Text = "";
        return true;
    }

    /// <summary>Saves the mechanics of every show in the scene (intensity is not part of the mechanics).</summary>
    private string SaveMechanics()
    {
        var builders = Builders();
        string result = builders.Count == 0 ? "No show in this scene" : string.Join("\n", builders.Select(b => b.SaveMechanics()));
        UpdateProjectBox();
        return result;
    }

    /// <summary>Saves the blocking of the current show (a first save writes the project).</summary>
    private string SaveBlocking()
    {
        string result = CurrentBuilder?.SaveBlocking() ?? "No show loaded";
        UpdateProjectBox();
        return result;
    }

    private void UpdateShowLabel()
    {
        var builder = CurrentBuilder;
        var model = builder?.Model;
        _showLabel.Text = model == null
            ? "No show loaded"
            : builder.Document is Kloarte.Model.IProjectShow
                ? $"{_all.Count} fixtures, {builder.Document.Groups.Count} groups, {builder.Document.CueStacks.Sum(s => s.Steps.Count)} cues"
                : $"{_all.Count} fixtures, {builder.Document.Groups.Count} groups, {builder.Document.CueStacks.Count} cue stacks";
        UpdateProjectBox();
        RefreshShowInfo();
        RefreshStartScreen();
    }

    // --- Fixture list ---

    private void Rescan()
    {
        CollectAll();
        WatchLamps();
        FillGroups();
        FillCues();
        FillList();
        UpdateShowLabel();
        ApplyLook();   // the project's stray light and haze (FixturePanel.Look.cs)
        RefreshPlayback();
        RefreshActors();
        ConnectViews();
    }

    /// <summary>Every lamp in the scene, sorted by DMX address.</summary>
    private void CollectAll()
    {
        _all.Clear();
        Collect(GetTree().Root);
        _all.Sort((a, b) => a.DmxAddress != b.DmxAddress
            ? a.DmxAddress.CompareTo(b.DmxAddress)
            : string.CompareOrdinal(a.Name, b.Name));
    }

    /// <summary>The show whose lamp additions / removals the list follows.</summary>
    private ShowBuilder _lampsSource;

    private void WatchLamps()
    {
        var builder = CurrentBuilder;
        if (builder == _lampsSource)
            return;
        if (_lampsSource != null && IsInstanceValid(_lampsSource))
            _lampsSource.LampsChanged -= OnLampsChanged;
        _lampsSource = builder;
        if (builder != null)
            builder.LampsChanged += OnLampsChanged;
    }

    /// <summary>A lamp was added or removed (plan): the list and counts follow; views and filters stay as they are.</summary>
    private void OnLampsChanged()
    {
        CollectAll();
        FillList();
        UpdateShowLabel();
    }

    private void Collect(Node node)
    {
        if (node is ProfileFixture fixture && !fixture.IsQueuedForDeletion())
            _all.Add(fixture);
        foreach (Node child in node.GetChildren())
            Collect(child);
    }

    private void Deselect()
    {
        _solo.ButtonPressed = false;
        _selected = null;
        ShowNothingSelected();
        _marker.Visible = false;
        _offSaved.Clear();
        Selection.Clear();
    }

    private void FillList()
    {
        string group = _groupFilter.Selected > 0 ? _groupFilter.GetItemMetadata(_groupFilter.Selected).AsString() : null;
        Dictionary<ProfileFixture, int> inCue = _cueFilter.Selected > 0
            ? CurrentBuilder?.FixturesInCue(_cueFilter.GetItemMetadata(_cueFilter.Selected).AsInt32())
            : null;

        _shown.Clear();
        var rows = new List<FixtureRow>();
        foreach (var fixture in _all)
        {
            if (group != null && !fixture.IsInGroup(group))
                continue;
            if (inCue != null && !inCue.ContainsKey(fixture))
                continue;
            rows.Add(RowOf(fixture));
            _shown.Add(fixture);
        }
        _filterActive = group != null || inCue != null;
        ApplyLit();
        // The level column: the cue's level when filtered by a cue, else the lamp's level now (as MagicQ shows it, in %).
        _list.SetRows(rows, inCue != null
            ? f => inCue.TryGetValue(f, out int level) && level >= 0 ? Mathf.RoundToInt(level * 100f / 255f) : -1
            : f => IsInstanceValid(f) ? Mathf.RoundToInt(f.Intensity * 100f) : -1, Selection.Fixtures);
    }

    /// <summary>A lamp as a technician names it: the head's name and its type ("Binnen T  C103"), no node name or id.</summary>
    private static string LampTitle(ProfileFixture f)
    {
        string name = f.HasMeta("headName") ? f.GetMeta("headName").AsString() : f.Name;
        string type = BuilderOf(f)?.ModelOf(f)?.ShortName ?? "";
        return type.Length > 0 ? $"{name}  {type}" : name;
    }

    /// <summary>A lamp as a row of the fixture table: head, the head's name, short type, gel code; details in the tooltip.</summary>
    private FixtureRow RowOf(ProfileFixture fixture)
    {
        var builder = BuilderOf(fixture);
        var model = builder?.ModelOf(fixture);
        string name = fixture.HasMeta("headName") ? fixture.GetMeta("headName").AsString() : fixture.Name;
        int gelId = fixture.HasMeta("gelId") ? fixture.GetMeta("gelId").AsInt32() : 0;
        string gelName = fixture.HasMeta("gel") ? fixture.GetMeta("gel").AsString() : "";
        string type = model?.ShortName ?? (fixture.HasMeta("kind") ? fixture.GetMeta("kind").AsString() : "");
        bool broken = IsBrokenLamp(fixture);
        string tooltip = $"Head {(fixture.HeadNumber > 0 ? fixture.HeadNumber.ToString() : "not patched")} {name}\n" +
                         $"DMX {DmxAddress.Format(fixture.DmxAddress)}\n" +
                         $"{model?.Name ?? type}\n" +
                         $"Gel: {(gelId == 0 ? "open white" : $"{GelCodes.Code(gelId)} {gelName}")}" +
                         (broken ? "\nIts mechanics file has an error (see the status bar)" : "");
        return new FixtureRow(fixture, fixture.HeadNumber, name, type, gelId == 0 ? "" : GelCodes.Code(gelId), broken, tooltip);
    }

    /// <summary>
    /// "All fixtures" + the MagicQ groups in MagicQ's order (node groups "mq:&lt;name&gt;", made by ShowBuilder).
    /// Keeps the current choice when the group still exists after a reload.
    /// </summary>
    private void FillGroups()
    {
        string current = _groupFilter.Selected > 0 ? _groupFilter.GetItemMetadata(_groupFilter.Selected).AsString() : null;
        _groupFilter.Clear();
        _groupFilter.AddItem($"All groups ({_all.Count} fixtures)");
        _groupFilter.SetItemMetadata(0, "");

        var names = Builders().SelectMany(b => b.Document?.Groups.Select(g => g.Name) ?? Enumerable.Empty<string>())
            .Distinct();
        foreach (string name in names)
        {
            string group = "mq:" + name;
            int count = _all.Count(f => f.IsInGroup(group));
            if (count == 0)
                continue;   // group of heads without a MagicVis position
            _groupFilter.AddItem($"{name} ({count})");
            _groupFilter.SetItemMetadata(_groupFilter.ItemCount - 1, group);
            if (group == current)
                _groupFilter.Select(_groupFilter.ItemCount - 1);
        }
    }

    /// <summary>
    /// "All cues" + per cue stack a separator with its name and its steps (cue number + text) in cue order.
    /// </summary>
    private void FillCues()
    {
        int current = _cueFilter.Selected > 0 ? _cueFilter.GetItemMetadata(_cueFilter.Selected).AsInt32() : -1;
        _cueFilter.Clear();
        _cueFilter.AddItem("All cues");
        _cueFilter.SetItemMetadata(0, -1);

        var show = CurrentBuilder?.Model?.Show;
        if (show == null)
            return;
        var cueNames = show.Cues.ToDictionary(c => c.Id, c => c.Name);
        foreach (var stack in show.CueStacks)
        {
            var steps = stack.Steps;
            if (steps.Count == 0)
                continue;
            _cueFilter.AddSeparator(string.IsNullOrEmpty(stack.Name) ? $"Cue stack {stack.Number}" : stack.Name);
            foreach (var step in steps)
            {
                string text = step.Text.Length > 0 ? step.Text : cueNames.GetValueOrDefault(step.CueId, "");
                _cueFilter.AddItem($"{step.CueNumberText}  {text}".TrimEnd());
                _cueFilter.SetItemMetadata(_cueFilter.ItemCount - 1, step.CueId);
                if (step.CueId == current)
                    _cueFilter.Select(_cueFilter.ItemCount - 1);
            }
        }
    }

    /// <summary>Selects a lamp (through the shared selection, so every view follows).</summary>
    private void Select(ProfileFixture fixture) => Selection.Select(fixture);

    /// <summary>The shared selection changed (here or in another view): list, marker, lit set and settings follow.</summary>
    private void OnSelectionChanged()
    {
        if (Selection.Actor != null)
        {
            ShowActor(Selection.Actor, Selection.ActorStep);
            return;
        }
        _selectedActor = null;
        _selected = Selection.Fixture;
        if (_selected == null)
        {
            ShowNothingSelected();
            _marker.Visible = false;
            _list.ShowSelected(Selection.Fixtures);
            ApplyLit();
            return;
        }
        _marker.GlobalPosition = _selected.GlobalPosition;
        _marker.Visible = true;
        ShowSelectionInList();
        _list.ScrollToLamp(_selected);
        ApplyLit();
        BuildParams();
    }

    /// <summary>
    /// Only the "lit set" may light up: the selected fixture when Solo is on, else the filtered list (group or cue),
    /// else everything. The others go to intensity 0; their own intensity comes back when they are lit again.
    /// A soloed fixture lights at <see cref="SoloIntensity"/>; that level is forgotten (its own intensity comes back)
    /// when Solo goes off or another / no fixture is selected.
    /// </summary>
    private void ApplyLit()
    {
        foreach (var (fixture, intensity) in _offSaved)
            if (IsInstanceValid(fixture))
                fixture.Intensity = intensity;
        _offSaved.Clear();
        if (_soloLamp != null && IsInstanceValid(_soloLamp))
            _soloLamp.Intensity = _soloSaved;
        _soloLamp = null;
        if (_solo.ButtonPressed && _selected != null && IsInstanceValid(_selected))
        {
            _soloLamp = _selected;
            _soloSaved = _selected.Intensity;
            _selected.Intensity = SoloIntensity;
        }

        HashSet<ProfileFixture> lit = _solo.ButtonPressed && _selected != null ? new() { _selected }
            : _filterActive ? new(_shown)
            : null;
        if (lit == null)
            return;
        foreach (var fixture in _all.Where(f => !lit.Contains(f) && IsInstanceValid(f)))
        {
            _offSaved[fixture] = fixture.Intensity;
            fixture.Intensity = 0f;
        }
    }
}
