using System.Linq;
using Kloarte.Model;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: the inspector on the right (UI review 30, decided with the user). It is always there, so the main view
/// keeps its width when the selection changes: the settings of the selected lamp(s) or actor, else "Nothing selected"
/// with the show's info and the Look (stray light). Collapsible to a thin strip (» / «, key map
/// <c>inspector_toggle</c>, View menu) that names the selection; its width is dragged at its left edge. Open / closed
/// and the width are remembered with the view layout (<see cref="ViewHost.InspectorOpen"/>,
/// <see cref="ViewHost.InspectorWidth"/>).
/// </summary>
public partial class FixturePanel
{
    /// <summary>The whole inspector: grip, panel and the collapsed strip.</summary>
    private HBoxContainer _inspector;

    /// <summary>The drag handle at the inspector's left edge.</summary>
    private Control _grip;

    /// <summary>The collapsed inspector: a « button and the selection's name, rotated.</summary>
    private PanelContainer _strip;
    private Label _stripLabel;

    /// <summary>The show info of "Nothing selected" (refreshed with the unsaved marker); null while something is selected.</summary>
    private Label _showInfo;

    /// <summary>Subject of the "Nothing selected" settings (see <see cref="ClearParams"/>).</summary>
    private const string NothingSelected = "nothing selected";

    /// <summary>Narrowest inspector a drag can make (px); the widest is half the window.</summary>
    private const float InspectorMinWidth = 220f;

    /// <summary>Width of the grip and of the collapsed strip (px).</summary>
    private const float GripWidth = 6f, StripWidth = 30f;

    /// <summary>The grip is being dragged.</summary>
    private bool _resizing;

    /// <summary>The inspector is open (not collapsed to its strip).</summary>
    public bool InspectorOpen => _right.Visible;

    /// <summary>Builds the inspector into the panel row; called once from _Ready.</summary>
    private void BuildInspector(Control row)
    {
        _inspector = new HBoxContainer { Name = "Inspector" };
        _inspector.AddThemeConstantOverride("separation", 0);
        row.AddChild(_inspector);

        _grip = new ColorRect
        {
            Color = new Color(1f, 1f, 1f, 0.06f),
            CustomMinimumSize = new Vector2(GripWidth, 0),
            MouseDefaultCursorShape = Control.CursorShape.Hsize,
            TooltipText = "Drag to make the inspector wider or narrower",
        };
        _grip.GuiInput += OnGripInput;
        _inspector.AddChild(_grip);

        _right = new PanelContainer();
        var box = new VBoxContainer();
        var header = new HBoxContainer();
        header.AddChild(new Label
        {
            Text = "Inspector",
            Modulate = new Color(0.7f, 0.7f, 0.7f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        var collapse = new Button { Text = "»", Flat = true, FocusMode = Control.FocusModeEnum.None };
        KeyMap.Tooltip(collapse, "Collapse the inspector", "inspector_toggle");
        collapse.Pressed += () => SetInspectorOpen(false);
        header.AddChild(collapse);
        box.AddChild(header);
        _paramScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _params = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _paramScroll.AddChild(_params);
        box.AddChild(_paramScroll);
        _right.AddChild(box);
        _inspector.AddChild(_right);

        _strip = new PanelContainer { CustomMinimumSize = new Vector2(StripWidth, 0), Visible = false, Name = "InspectorStrip" };
        var stripBox = new VBoxContainer();
        var expand = new Button { Text = "«", Flat = true, FocusMode = Control.FocusModeEnum.None };
        KeyMap.Tooltip(expand, "Open the inspector", "inspector_toggle");
        expand.Pressed += () => SetInspectorOpen(true);
        stripBox.AddChild(expand);
        // The selection's name, read top to bottom (a rotated label in a plain control: containers would undo the rotation).
        var nameArea = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _stripLabel = new Label { RotationDegrees = 90, Modulate = new Color(0.8f, 0.8f, 0.8f), MouseFilter = Control.MouseFilterEnum.Ignore };
        nameArea.AddChild(_stripLabel);
        nameArea.Resized += () => PlaceStripLabel(nameArea);
        _stripLabel.Resized += () => PlaceStripLabel(nameArea);
        stripBox.AddChild(nameArea);
        _strip.AddChild(stripBox);
        _inspector.AddChild(_strip);

        UiFit.Watch(_right);
        SetInspectorOpen(_host.InspectorOpen, remember: false);
        ShowNothingSelected();
    }

    /// <summary>The rotated name starts at the top of the strip, under the « button (turned 90° about its top left).</summary>
    private void PlaceStripLabel(Control area) =>
        _stripLabel.Position = new Vector2((area.Size.X + _stripLabel.Size.Y) / 2f, 8f);

    /// <summary>Opens the inspector or collapses it to its strip.</summary>
    private void SetInspectorOpen(bool open, bool remember = true)
    {
        _right.Visible = open;
        _grip.Visible = open;
        _strip.Visible = !open;
        UpdateInspectorStrip();
        if (remember)
            _host.InspectorOpen = open;
    }

    /// <summary>Drags the inspector's left edge (wider to the left); the width is remembered when the button goes up.</summary>
    private void OnGripInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _resizing = button.Pressed;
                if (!_resizing)
                    _host.InspectorWidth = _right.CustomMinimumSize.X;
                _grip.AcceptEvent();
                break;
            case InputEventMouseMotion motion when _resizing:
                _right.CustomMinimumSize = new Vector2(ClampInspectorWidth(_right.CustomMinimumSize.X - motion.Relative.X), 0);
                _grip.AcceptEvent();
                break;
        }
    }

    /// <summary>A width between <see cref="InspectorMinWidth"/> and half the window.</summary>
    private float ClampInspectorWidth(float width) =>
        Mathf.Clamp(width, InspectorMinWidth, Mathf.Max(InspectorMinWidth, GetViewport().GetVisibleRect().Size.X * 0.5f));

    /// <summary>The inspector's width: as dragged, else about a quarter of the window.</summary>
    private float InspectorWidth(float window) =>
        _host.InspectorWidth > 0 ? ClampInspectorWidth(_host.InspectorWidth) : Mathf.Clamp(window * 0.26f, 250f, 380f);

    /// <summary>The collapsed strip names what the inspector would show.</summary>
    private void UpdateInspectorStrip()
    {
        if (_stripLabel == null)
            return;
        var lamps = Selection.Fixtures.Where(IsInstanceValid).ToList();
        string text = lamps.Count > 1 ? $"{lamps.Count} lamps"
            : _selected != null && IsInstanceValid(_selected) ? LampTitle(_selected)
            : _selectedActor != null ? CurrentBuilder?.Model?.Blocking.Find(_selectedActor)?.Name ?? "Actor"
            : "Nothing selected";
        if (_stripLabel.Text != text)
            _stripLabel.Text = text;
    }

    /// <summary>
    /// The inspector without a selection: "Nothing selected", a hint, the show's info and the Look (stray light and
    /// haze, saved with the project, UI review 24).
    /// </summary>
    private void ShowNothingSelected()
    {
        ClearParams(NothingSelected);
        _params.AddChild(new Label { Text = "Nothing selected" });
        _params.AddChild(new Label
        {
            Text = "Click a lamp in a view or in the Fixtures list, or an actor in the Actors list.",
            Modulate = new Color(0.7f, 0.7f, 0.7f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(180, 0),
        });
        Header("Show");
        _showInfo = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(180, 0) };
        _params.AddChild(_showInfo);
        RefreshShowInfo();
        Header("Look", "The room's stray light and haze, saved with the project");
        LookRows();   // FixturePanel.Look.cs
        UpdateInspectorStrip();
    }

    /// <summary>Fills the show info of "Nothing selected" (no-op while something else is shown).</summary>
    private void RefreshShowInfo()
    {
        if (_showInfo == null || !IsInstanceValid(_showInfo) || !Equals(_paramsSubject, NothingSelected))
            return;
        var builder = CurrentBuilder;
        if (builder?.Model == null)
        {
            _showInfo.Text = "No show loaded";
            return;
        }
        var document = builder.Document;
        var stacks = document.CueStacks;
        bool native = document is IProjectShow;   // its show is the project's own files: no show file to name
        string cue = _player?.Stack == null ? "no cue stack"
            : _player.Step < 0 ? $"{StackName(_player.Stack)}: before the first cue"
            : $"{StackName(_player.Stack)}: cue {_player.Stack.Steps[_player.Step].CueNumberText}";
        string text = $"Project: {builder.ProjectName}{(_unsaved.Count > 0 ? $" • unsaved ({string.Join(", ", _unsaved)})" : "")}\n" +
                      (native ? "" : $"Show: {System.IO.Path.GetFileName(builder.Files.SourceFile)}\n") +
                      $"{Count(_all.Count, "fixture")}, {Count(document.Groups.Count, "group")}\n" +
                      (native ? $"{Count(stacks.Sum(s => s.Steps.Count), "cue")}\n"
                          : $"{Count(stacks.Count, "cue stack")}, {Count(stacks.Sum(s => s.Steps.Count), "cue")}\n") +
                      $"{Count(builder.Model.Blocking.Actors.Count, "actor")}\n" +
                      $"Playback: {cue}";
        if (_showInfo.Text != text)
            _showInfo.Text = text;
    }

    /// <summary>"1 cue", "2 cues".</summary>
    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>A cue stack's name, or "Cue stack n" when it has none.</summary>
    private static string StackName(ICueStack stack) =>
        string.IsNullOrEmpty(stack.Name) ? $"Cue stack {stack.Number}" : stack.Name;
}
