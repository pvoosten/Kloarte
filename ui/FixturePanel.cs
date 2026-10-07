using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

// Runtime UI. Left: "Open show" (MagicQ .shw, the .xhw next to it is loaded too) and three tabs:
//  - Fixtures: list filtered by a MagicQ group or cue (dropdowns; edit groups and cues in MagicQ), Solo,
//    "Save mechanics". Only the listed (filtered) fixtures can light up; Solo narrows that to the selected fixture.
//  - Playback / Actors: see FixturePanel.Playback.cs.
// Bottom: stray light, "Save show as" (MagicQ .shw/.xhw). Right: settings of the selected fixture or actor.
// Changes are live; mechanics go to the mechanics JSON, rigging to the MagicQ show (Save show as).
public partial class FixturePanel : CanvasLayer
{
    private OptionButton _groupFilter, _cueFilter;
    private ItemList _list;
    private CheckBox _solo;
    private PanelContainer _right;
    private VBoxContainer _params;
    private MeshInstance3D _marker;
    private Label _showLabel, _status;
    private FileDialog _openDialog, _saveDialog;

    private const string DefaultShowFolder = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show";

    private readonly List<ProfileFixture> _all = new();
    private readonly List<ProfileFixture> _shown = new();
    private readonly Dictionary<ProfileFixture, float> _offSaved = new();   // fixtures switched off by solo / filter
    private bool _filterActive;
    private ProfileFixture _selected;

    public override void _Ready()
    {
        var root = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Left: show and fixture list.
        var left = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
        var leftBox = new VBoxContainer();
        left.AddChild(leftBox);

        var open = new Button { Text = "Open show (.shw)..." };
        open.Pressed += ShowOpenDialog;
        _showLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _openDialog = new FileDialog
        {
            Title = "Open MagicQ show",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.shw ; MagicQ show" },
            UseNativeDialog = true,
        };
        _openDialog.FileSelected += OpenShow;
        AddChild(_openDialog);
        _saveDialog = new FileDialog
        {
            Title = "Save MagicQ show as",
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
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _list.ItemSelected += index => Select(_shown[(int)index]);
        _solo = new CheckBox { Text = "Solo (only the selected fixture lights)" };
        _solo.Toggled += _ => ApplyLit();
        var refresh = new Button { Text = "Refresh list" };
        refresh.Pressed += Rescan;
        var saveMech = new Button { Text = "Save mechanics" };
        saveMech.Pressed += () => _status.Text = SaveMechanics();
        var saveShow = new Button { Text = "Save show as (.shw)..." };
        saveShow.Pressed += ShowSaveDialog;
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.7f, 0.7f, 0.7f) };

        leftBox.AddChild(open);
        leftBox.AddChild(_showLabel);

        _tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var fixturesTab = new VBoxContainer { Name = "Fixtures" };
        fixturesTab.AddChild(_groupFilter);
        fixturesTab.AddChild(_cueFilter);
        fixturesTab.AddChild(_list);
        fixturesTab.AddChild(_solo);
        fixturesTab.AddChild(refresh);
        fixturesTab.AddChild(saveMech);
        _tabs.AddChild(fixturesTab);
        _tabs.AddChild(BuildPlaybackTab());
        _tabs.AddChild(BuildActorsTab());
        _tabs.TabChanged += OnTabChanged;
        leftBox.AddChild(_tabs);

        leftBox.AddChild(StrayLightRow());
        leftBox.AddChild(saveShow);
        leftBox.AddChild(_status);
        root.AddChild(left);

        // Middle: empty, mouse goes to the 3D view / camera.
        root.AddChild(new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        // Right: settings of the selected fixture.
        _right = new PanelContainer { CustomMinimumSize = new Vector2(340, 0), Visible = false };
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _params = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_params);
        _right.AddChild(scroll);
        root.AddChild(_right);

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

    // Slider for the room-wide stray light (StrayLight nodes in the scene), 0 = pitch black around the beams.
    private Control StrayLightRow()
    {
        var row = new HBoxContainer();
        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 2,
            Step = 0.01,
            Value = 0.5,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "Stray light: spill, haze scatter and bounce that keep the room from going fully dark",
        };
        slider.ValueChanged += v =>
        {
            foreach (var stray in GetTree().Root.FindChildren("*", "Node", true, false).OfType<StrayLight>())
                stray.Amount = (float)v;
        };
        row.AddChild(new Label { Text = "Stray light" });
        row.AddChild(slider);
        return row;
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
        string current = CurrentBuilder?.ShowFile ?? "";
        string dir = current.Length > 0 ? System.IO.Path.GetDirectoryName(current) : DefaultShowFolder;
        if (System.IO.Directory.Exists(dir))
            _openDialog.CurrentDir = dir;
        _openDialog.PopupCentered(new Vector2I(900, 600));
    }

    private void ShowSaveDialog()
    {
        var builder = CurrentBuilder;
        if (builder == null)
        {
            _status.Text = "No show loaded";
            return;
        }
        _saveDialog.CurrentDir = System.IO.Path.GetDirectoryName(builder.ShowFile);
        _saveDialog.CurrentFile = System.IO.Path.GetFileNameWithoutExtension(builder.ShowFile) + "_kruimels.shw";
        _saveDialog.PopupCentered(new Vector2I(900, 600));
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

    // Loads a show into the first ShowBuilder of the scene, or adds one (e.g. in main.tscn).
    private async void OpenShow(string path)
    {
        Deselect();
        try
        {
            var builder = Builders().FirstOrDefault();
            if (builder == null)
            {
                builder = new ShowBuilder { Name = "Show", FixtureConfig = "res://shows/risquons-tout.fixtures.json" };
                builder.ShowFile = path;
                builder.MechanicsFile = $"res://shows/{System.IO.Path.GetFileNameWithoutExtension(path)}.mechanics.json";
                GetTree().CurrentScene.AddChild(builder);
            }
            else
                builder.OpenShow(path);
        }
        catch (Exception e)
        {
            _status.Text = $"Could not open {path}: {e.Message}";
            return;
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);   // old fixtures are freed at frame end
        Rescan();
        _status.Text = "";
    }

    // Saves the mechanics of every show in the scene (intensity is not part of the mechanics).
    private string SaveMechanics()
    {
        var builders = Builders();
        return builders.Count == 0 ? "No show in this scene" : string.Join("\n", builders.Select(b => b.SaveMechanics()));
    }

    private void UpdateShowLabel()
    {
        var builder = CurrentBuilder;
        var model = builder?.Model;
        _showLabel.Text = model == null
            ? "No show loaded"
            : $"{System.IO.Path.GetFileName(builder.ShowFile)}{(model.MagicQ.Xhw != null ? " + .xhw" : "")}\n" +
              $"{_all.Count} fixtures, {model.MagicQ.Groups.Count} groups, {model.MagicQ.CueStacks.Count} cue stacks";
    }

    // --- Fixture list ---

    private void Rescan()
    {
        _all.Clear();
        Collect(GetTree().Root);
        _all.Sort((a, b) => a.DmxAddress != b.DmxAddress
            ? a.DmxAddress.CompareTo(b.DmxAddress)
            : string.CompareOrdinal(a.Name, b.Name));
        FillGroups();
        FillCues();
        FillList();
        UpdateShowLabel();
        RefreshPlayback();
        RefreshActors();
    }

    private void Collect(Node node)
    {
        if (node is ProfileFixture fixture)
            _all.Add(fixture);
        foreach (Node child in node.GetChildren())
            Collect(child);
    }

    private void Deselect()
    {
        _solo.ButtonPressed = false;
        _selected = null;
        _right.Visible = false;
        _marker.Visible = false;
        _offSaved.Clear();
    }

    private void FillList()
    {
        string group = _groupFilter.Selected > 0 ? _groupFilter.GetItemMetadata(_groupFilter.Selected).AsString() : null;
        Dictionary<ProfileFixture, int> inCue = _cueFilter.Selected > 0
            ? CurrentBuilder?.FixturesInCue(_cueFilter.GetItemMetadata(_cueFilter.Selected).AsInt32())
            : null;

        _list.Clear();
        _shown.Clear();
        foreach (var fixture in _all)
        {
            if (group != null && !fixture.IsInGroup(group))
                continue;
            int level = -1;
            if (inCue != null && !inCue.TryGetValue(fixture, out level))
                continue;

            string text = fixture.Name;
            if (inCue != null && level >= 0)
                text += $"  @ {Mathf.RoundToInt(level * 100f / 255f)}%";
            if (fixture.HasMeta("kind"))
                text += $"  [{fixture.GetMeta("kind")}]";
            string gel = fixture.HasMeta("gel") ? fixture.GetMeta("gel").AsString() : "";
            if (gel.Length > 0)
                text += "  " + gel.Split(' ')[0];
            _list.AddItem(text);
            _shown.Add(fixture);
            if (fixture == _selected)
                _list.Select(_list.ItemCount - 1);
        }
        _filterActive = group != null || inCue != null;
        ApplyLit();
    }

    // "All fixtures" + the MagicQ groups in MagicQ's order (node groups "mq:<name>", made by ShowBuilder).
    // Keeps the current choice when the group still exists after a reload.
    private void FillGroups()
    {
        string current = _groupFilter.Selected > 0 ? _groupFilter.GetItemMetadata(_groupFilter.Selected).AsString() : null;
        _groupFilter.Clear();
        _groupFilter.AddItem($"All groups ({_all.Count} fixtures)");
        _groupFilter.SetItemMetadata(0, "");

        var names = Builders().SelectMany(b => b.Model?.MagicQ.Groups.Select(g => g.Name) ?? Enumerable.Empty<string>())
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

    // "All cues" + per cue stack a separator with its name and its steps (cue number + text) in cue order.
    private void FillCues()
    {
        int current = _cueFilter.Selected > 0 ? _cueFilter.GetItemMetadata(_cueFilter.Selected).AsInt32() : -1;
        _cueFilter.Clear();
        _cueFilter.AddItem("All cues");
        _cueFilter.SetItemMetadata(0, -1);

        var show = CurrentBuilder?.Model?.MagicQ;
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

    private void Select(ProfileFixture fixture)
    {
        _selectedActor = null;
        _selected = fixture;
        _marker.GlobalPosition = fixture.GlobalPosition;
        _marker.Visible = true;
        ApplyLit();
        BuildParams();
    }

    // Only the "lit set" may light up: the selected fixture when Solo is on, else the filtered list (group or cue),
    // else everything. The others go to intensity 0; their own intensity comes back when they are lit again.
    private void ApplyLit()
    {
        foreach (var (fixture, intensity) in _offSaved)
            if (IsInstanceValid(fixture))
                fixture.Intensity = intensity;
        _offSaved.Clear();

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

    // --- Settings panel ---

    private void BuildParams()
    {
        foreach (Node child in _params.GetChildren())
            child.QueueFree();
        var f = _selected;
        _right.Visible = true;

        _params.AddChild(new Label { Text = f.Name, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (f.DmxAddress > 0)
            _params.AddChild(new Label
            {
                Text = $"Head {f.HeadNumber}, DMX {(f.DmxAddress - 1) / 512 + 1}.{(f.DmxAddress - 1) % 512 + 1}",
                Modulate = new Color(0.7f, 0.7f, 0.7f),
            });

        if (f.HasMeta("gel") && f.GetMeta("gel").AsString() != "")
        {
            var gelRow = new HBoxContainer();
            gelRow.AddChild(new ColorRect { Color = f.GelColor, CustomMinimumSize = new Vector2(18, 18) });
            gelRow.AddChild(new Label { Text = "Gel: " + f.GetMeta("gel").AsString() });
            _params.AddChild(gelRow);
        }

        var builder = BuilderOf(f);
        if (builder != null)
            Rigging(f, builder);

        Header("Lamp");
        Slider("Intensity", 0, 1, 0.01, () => f.Intensity, v =>
        {
            f.Intensity = v;
            _offSaved.Remove(f);
        });
        if (builder != null && f.DmxAddress > 0)
            Slider(f.HasMeta("beamFromMagicQ") ? "Beam ° (MagicQ)" : "Beam °", 1, ProfileFixture.MaxFieldAngle, 1,
                () => f.FieldAngle, v => builder.SetBeamAngle(f, v));
        else
            Slider("Field angle °", 5, ProfileFixture.MaxFieldAngle, 0.5, () => f.FieldAngle, v => f.FieldAngle = v);
        Slider("Softness", 0, 0.3, 0.001, () => f.Softness, v => f.Softness = v);
        Slider("Field falloff", 0, 1, 0.01, () => f.FieldFalloff, v => f.FieldFalloff = v);

        Header("Shutters (depth 0-2, angle °)");
        Slider("Top", 0, 2, 0.01, () => f.TopDepth, v => f.TopDepth = v);
        Slider("Top angle", -45, 45, 0.5, () => f.TopAngle, v => f.TopAngle = v);
        Slider("Right", 0, 2, 0.01, () => f.RightDepth, v => f.RightDepth = v);
        Slider("Right angle", -45, 45, 0.5, () => f.RightAngle, v => f.RightAngle = v);
        Slider("Bottom", 0, 2, 0.01, () => f.BottomDepth, v => f.BottomDepth = v);
        Slider("Bottom angle", -45, 45, 0.5, () => f.BottomAngle, v => f.BottomAngle = v);
        Slider("Left", 0, 2, 0.01, () => f.LeftDepth, v => f.LeftDepth = v);
        Slider("Left angle", -45, 45, 0.5, () => f.LeftAngle, v => f.LeftAngle = v);
        Slider("Rotation", -45, 45, 0.5, () => f.ShutterRotation, v => f.ShutterRotation = v);

        Header("Iris / gobo");
        Slider("Iris", 0, 1, 0.01, () => f.Iris, v => f.Iris = v);
        GoboPicker(f);
        Slider("Gobo rotation", -180, 180, 1, () => f.GoboRotation, v => f.GoboRotation = v);
        Slider("Gobo size", 0.2, 2, 0.01, () => f.GoboSize, v => f.GoboSize = v);

        Header("Focus point (m)");
        Slider("X", -12, 12, 0.05, () => f.FocusPoint.X, v => f.FocusPoint = f.FocusPoint with { X = v });
        Slider("Y", -1, 8, 0.05, () => f.FocusPoint.Y, v => f.FocusPoint = f.FocusPoint with { Y = v });
        Slider("Z", -15, 15, 0.05, () => f.FocusPoint.Z, v => f.FocusPoint = f.FocusPoint with { Z = v });
    }

    // Object the head hangs on (MagicVis parent) and its position in stage coordinates (origin = downstage centre).
    // Choosing a truss moves the head onto it; the head stays focused on its focus point.
    private void Rigging(ProfileFixture f, ShowBuilder builder)
    {
        Header("Rigging (saved with 'Save show as')");

        var objects = builder.RigObjects.ToList();
        var picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        picker.AddItem("(not attached)");
        picker.SetItemMetadata(0, 0);
        int parent = builder.GetParent(f);
        foreach (var (id, name) in objects)
        {
            picker.AddItem(name);
            picker.SetItemMetadata(picker.ItemCount - 1, id);
            if (id == parent)
                picker.Select(picker.ItemCount - 1);
        }
        picker.ItemSelected += i =>
        {
            builder.SetParent(f, picker.GetItemMetadata((int)i).AsInt32());
            _marker.GlobalPosition = f.GlobalPosition;
            CallDeferred(MethodName.BuildParams);   // position fields show the snapped position
        };
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Hangs on", CustomMinimumSize = new Vector2(110, 0) });
        row.AddChild(picker);
        _params.AddChild(row);

        Number("X (right +)", () => f.Position.X, v => Move(f, builder, f.Position with { X = v }));
        Number("Y (height)", () => f.Position.Y, v => Move(f, builder, f.Position with { Y = v }));
        Number("Z (audience +)", () => f.Position.Z, v => Move(f, builder, f.Position with { Z = v }));
    }

    private void Move(ProfileFixture f, ShowBuilder builder, Vector3 position)
    {
        builder.MoveFixture(f, position);
        _marker.GlobalPosition = f.GlobalPosition;
    }

    private void Header(string text)
    {
        _params.AddChild(new HSeparator());
        _params.AddChild(new Label { Text = text, Modulate = new Color(1f, 0.75f, 0.4f) });
    }

    private void Number(string label, Func<float> get, Action<float> set)
    {
        var spin = new SpinBox
        {
            MinValue = -50,
            MaxValue = 50,
            Step = 0.01,
            Value = get(),
            Suffix = "m",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        spin.ValueChanged += v => set((float)v);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
        row.AddChild(spin);
        _params.AddChild(row);
    }

    private void Slider(string label, double min, double max, double step, Func<float> get, Action<float> set)
    {
        var row = new HBoxContainer();
        var value = new Label { CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = get(),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        value.Text = slider.Value.ToString("0.###");
        slider.ValueChanged += v =>
        {
            set((float)v);
            value.Text = v.ToString("0.###");
        };
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
        row.AddChild(slider);
        row.AddChild(value);
        _params.AddChild(row);
    }

    private void GoboPicker(ProfileFixture f)
    {
        var paths = DirAccess.GetFilesAt("res://gobos")
            .Where(p => p.EndsWith(".png") || p.EndsWith(".jpg"))
            .Select(p => "res://gobos/" + p)
            .ToList();
        var picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        picker.AddItem("(no gobo)");
        foreach (var p in paths)
            picker.AddItem(p.GetFile().GetBaseName());
        int current = f.Gobo == null ? 0 : paths.IndexOf(f.Gobo.ResourcePath) + 1;
        picker.Selected = Math.Max(current, 0);
        picker.ItemSelected += i => f.Gobo = i == 0 ? null : GD.Load<Texture2D>(paths[(int)i - 1]);

        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Gobo", CustomMinimumSize = new Vector2(110, 0) });
        row.AddChild(picker);
        _params.AddChild(row);
    }
}
