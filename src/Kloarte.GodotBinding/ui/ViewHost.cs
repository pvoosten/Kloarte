using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Shows the views (3D, audience, plan, truss heights, timeline) between the side panels. One main view; the others
/// are small cards in a column on the right, or hidden. Workspaces (UI review 31, decided with the user: Rig, Focus,
/// Blocking, see <see cref="Workspace"/>) pick the main view and the cards; each workspace remembers how the user
/// changed it (main view, small / hidden, "Small views") until View > Reset workspace. The view bar has the workspaces,
/// the Present button (Present mode), a tab per view (click = main view; right click: small / hidden) and
/// "Small views". Click a card to make it the main view (animated). The timeline can be docked under the main view
/// while that is a 3D view (3D, audience, actor): a splitter between them sets its height. Keys (key map): a workspace
/// each, next view, a key per view, hide the small views, dock the timeline. Workspaces (incl. the docked timeline), the
/// dock height and the inspector are remembered in user://views.json. While the host is
/// visible the window itself doesn't render 3D (the 3D views do); when it is hidden (e.g. capture tests hide the panel)
/// the window renders the scene camera again.
/// </summary>
public partial class ViewHost : VBoxContainer
{
    /// <summary>Where the workspaces are remembered.</summary>
    private const string SettingsFile = "user://views.json";

    /// <summary>False: the workspaces are neither loaded nor saved (tests must not change the user's layout).</summary>
    public static bool Persist { get; set; } = true;

    /// <summary>Duration of the card / main view animation (s).</summary>
    private const float AnimationTime = 0.25f;

    /// <summary>The docked timeline's share of the main view's height: default, least, most.</summary>
    private const float DefaultDockShare = 0.3f, MinDockShare = 0.12f, MaxDockShare = 0.75f;

    /// <summary>Height of the splitter between the main view and the docked timeline (px).</summary>
    private const float SplitterHeight = 6f;

    /// <summary>The workspaces' defaults: the main view and the cards, by the views' key map actions.</summary>
    private static readonly Dictionary<Workspace, (string Main, string[] Cards)> Defaults = new()
    {
        [Workspace.Rig] = ("view_plan", new[] { "view_3d", "view_heights" }),
        [Workspace.Focus] = ("view_3d", new[] { "view_audience", "view_plan" }),
        [Workspace.Blocking] = ("view_timeline", new[] { "view_3d", "view_audience", "view_actor" }),
    };

    /// <summary>Key map action of each workspace.</summary>
    private static readonly Dictionary<Workspace, string> WorkspaceActions = new()
    {
        [Workspace.Rig] = "workspace_rig",
        [Workspace.Focus] = "workspace_focus",
        [Workspace.Blocking] = "workspace_blocking",
    };

    private readonly List<ViewBase> _views = new();
    private readonly Dictionary<ViewBase, ViewState> _state = new();
    private readonly Dictionary<ViewBase, Button> _buttons = new();
    private readonly Dictionary<ViewBase, Button> _cardOverlays = new();
    private readonly Dictionary<ViewBase, Label> _cardTitles = new();
    private readonly Dictionary<Workspace, Button> _workspaceButtons = new();

    /// <summary>The other workspaces as the user left them (the current one lives in the fields below).</summary>
    private readonly Dictionary<Workspace, WorkspaceState> _saved = new();
    private HFlowContainer _bar;
    private Control _area;
    private CheckBox _smallToggle;
    private PopupMenu _menu;
    private ViewBase _menuView;
    private ViewBase _main;
    private ViewBase _exclusive;
    private Workspace _workspace = Workspace.Focus;
    private bool _smallHidden;
    private Tween _tween;
    private bool _loadedSettings;
    private View3D _capturing;

    /// <summary>The current workspace docks the timeline under the main view (while that is a 3D view).</summary>
    private bool _timelineDocked;

    /// <summary>The docked timeline's share of the main view's height, as the user dragged the splitter.</summary>
    private float _dockShare = DefaultDockShare;

    /// <summary>The splitter between the main view and the docked timeline (drag = the timeline's height).</summary>
    private Control _splitter;

    /// <summary>The main view and the docked timeline together (where the splitter is dragged).</summary>
    private Rect2 _dockArea;

    /// <summary>The row in the tool strip with the main view's and the docked timeline's toolbars.</summary>
    private HBoxContainer _toolRow;

    /// <summary>Raised when the main view changes.</summary>
    public event Action MainChanged;

    /// <summary>Raised when the user switches to another workspace (the panel shows the workspace's list).</summary>
    public event Action<Workspace> WorkspaceChanged;

    /// <summary>Raised by the Present button of the view bar.</summary>
    public event Action PresentRequested;

    /// <summary>The views in bar order.</summary>
    public IReadOnlyList<ViewBase> Views => _views;

    /// <summary>The main view (the exclusive view while there is one).</summary>
    public ViewBase Main => _exclusive ?? _main;

    /// <summary>The current workspace.</summary>
    public Workspace Workspace => _workspace;

    /// <summary>
    /// A view shown alone, as the main view, without changing the workspace (Present mode); null = the workspace as
    /// usual. Nothing of it is remembered.
    /// </summary>
    public ViewBase Exclusive
    {
        get => _exclusive;
        set
        {
            if (_exclusive == value)
                return;
            _exclusive = value;
            RefreshBar();
            Arrange(false);
            MainChanged?.Invoke();
        }
    }

    /// <summary>The view bar (workspaces, views, small views) is shown; Present mode hides it.</summary>
    public bool ShowBar
    {
        get => _bar?.Visible ?? true;
        set
        {
            if (_bar != null)
                _bar.Visible = value;
            RefreshToolStrip();
        }
    }

    /// <summary>
    /// The current workspace docks the timeline under the main view while that is a 3D view (3D, audience, actor);
    /// remembered per workspace. Docking while the timeline is the main view makes a 3D view the main view; undocking
    /// leaves the timeline a small card.
    /// </summary>
    public bool TimelineDocked
    {
        get => _timelineDocked;
        set
        {
            if (value == _timelineDocked && !(value && _main is TimelineView))
                return;
            _timelineDocked = value;
            if (value && _main is TimelineView)
                _main = _views.OfType<View3D>().FirstOrDefault(v => _state[v] == ViewState.Small) ?? _views.OfType<View3D>().FirstOrDefault() ?? _main;
            if (!value && Find<TimelineView>() is { } timeline && _state[timeline] == ViewState.Hidden)
                _state[timeline] = ViewState.Small;
            RefreshBar();
            Arrange(true);
            SaveSettings();
            MainChanged?.Invoke();
        }
    }

    /// <summary>The timeline while it is docked under the main view (null: not docked, or the main view isn't 3D).</summary>
    public TimelineView DockedTimeline =>
        _timelineDocked && _exclusive == null && Main is View3D ? Find<TimelineView>() : null;

    /// <summary>
    /// The inspector (the panel's right side) is open, not collapsed to its strip; remembered with the workspaces.
    /// </summary>
    public bool InspectorOpen
    {
        get => _inspectorOpen;
        set
        {
            _inspectorOpen = value;
            SaveSettings();
        }
    }

    /// <summary>The inspector's width as the user dragged it (px), 0 = follow the window; remembered with the workspaces.</summary>
    public float InspectorWidth
    {
        get => _inspectorWidth;
        set
        {
            _inspectorWidth = value;
            SaveSettings();
        }
    }

    /// <summary>See <see cref="InspectorOpen"/>.</summary>
    private bool _inspectorOpen = true;

    /// <summary>See <see cref="InspectorWidth"/>.</summary>
    private float _inspectorWidth;

    /// <summary>The scene camera steered by the main 3D view.</summary>
    public FlyCamera SceneCamera { get; private set; }

    /// <summary>Builds the bar (workspaces first) and the view area.</summary>
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        _bar = new HFlowContainer();   // wraps onto a second line in a narrow window
        AddChild(_bar);
        var group = new ButtonGroup { AllowUnpress = false };
        foreach (var ws in Enum.GetValues<Workspace>())
        {
            var button = new Button { Text = ws.ToString(), ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None };
            KeyMap.Tooltip(button, KeyMap.Find(WorkspaceActions[ws]).Name, WorkspaceActions[ws]);
            button.Pressed += () => SetWorkspace(ws);
            _bar.AddChild(button);
            _workspaceButtons[ws] = button;
        }
        var present = new Button { Text = "Present", FocusMode = FocusModeEnum.None };
        KeyMap.Tooltip(present, "Present mode: only the audience view, full screen, with the cue caption", "present_toggle");
        present.Pressed += () => PresentRequested?.Invoke();
        _bar.AddChild(present);
        _bar.AddChild(new VSeparator());
        // The main view's toolbar, under the view bar (UI review 29).
        _toolStrip = new PanelContainer { Name = "ToolStrip", Visible = false };
        AddChild(_toolStrip);
        _toolRow = new HBoxContainer();
        _toolStrip.AddChild(_toolRow);
        _area = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _area.Resized += () => Arrange(false);
        AddChild(_area);
        _splitter = new Control
        {
            Name = "TimelineSplitter",
            Visible = false,
            MouseDefaultCursorShape = CursorShape.Vsplit,
            TooltipText = "Drag: the docked timeline's height",
        };
        _splitter.Draw += () => _splitter.DrawRect(new Rect2(Vector2.Zero, _splitter.Size), new Color(0.3f, 0.3f, 0.33f));
        _splitter.GuiInput += SplitterInput;
        _area.AddChild(_splitter);
        _menu = new PopupMenu();
        _menu.IdPressed += id =>
        {
            if (_menuView == null)
                return;
            if (id == 0)
                SetMain(_menuView);
            else if (id == 3)
                TimelineDocked = !TimelineDocked;
            else
                SetState(_menuView, id == 1 ? ViewState.Small : ViewState.Hidden);
        };
        AddChild(_menu);
    }

    /// <summary>Adds a view (its tab in the bar); the workspace decides whether it is shown.</summary>
    public void AddView(ViewBase view)
    {
        _views.Add(view);
        _state[view] = ViewState.Hidden;
        _area.AddChild(view);
        if (view.Toolbar != null)
        {
            view.Toolbar.Visible = false;
            _toolRow.AddChild(view.Toolbar);
        }
        var overlay = new Button { Flat = true, TooltipText = $"Show {view.Title} as the main view", MouseDefaultCursorShape = CursorShape.PointingHand };
        overlay.Pressed += () => SetMain(view);
        _area.AddChild(overlay);
        _cardOverlays[view] = overlay;
        var title = new Label { Text = view.Title, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.5f));
        title.AddThemeConstantOverride("outline_size", 4);
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        _area.AddChild(title);
        _cardTitles[view] = title;

        var button = new Button { Text = view.Title, ToggleMode = true, FocusMode = FocusModeEnum.None };
        if (view.ShowAction != "")
            KeyMap.Tooltip(button, $"{view.Title} as the main view (right click: small / hidden)", view.ShowAction);
        button.Pressed += () => SetMain(view);
        button.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                ShowViewMenu(view);
        };
        _bar.AddChild(button);
        _buttons[view] = button;
        _main ??= view;
        if (view is View3D v3)
            v3.StageClicked += (o, d, p) => StageClicked?.Invoke(v3, o, d, p);
        if (view is TimelineView timeline)
            timeline.DockRequested += dock => TimelineDocked = dock;
    }

    /// <summary>A view tab's right-click menu: main view, small, hidden; the timeline's also docked under the 3D view.</summary>
    private void ShowViewMenu(ViewBase view)
    {
        _menuView = view;
        _menu.Clear();
        _menu.AddItem("Main view", 0);
        _menu.AddItem("Small", 1);
        _menu.AddItem("Hidden", 2);
        if (view is TimelineView)
        {
            _menu.AddSeparator();
            _menu.AddCheckItem("Docked under the 3D / audience / actor view", 3);
            _menu.SetItemChecked(_menu.GetItemIndex(3), _timelineDocked);
        }
        _menu.Position = (Vector2I)GetGlobalMousePosition();
        _menu.Popup();
    }

    /// <summary>Dragging the splitter sets the docked timeline's height (its share of the main view's height).</summary>
    private void SplitterInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
            SaveSettings();
        if (e is not InputEventMouseMotion motion || (motion.ButtonMask & MouseButtonMask.Left) == 0 || _dockArea.Size.Y < 10)
            return;
        float y = _splitter.Position.Y + motion.Position.Y - _dockArea.Position.Y;
        _dockShare = Mathf.Clamp((_dockArea.Size.Y - y) / _dockArea.Size.Y, MinDockShare, MaxDockShare);
        Arrange(false);
        _splitter.AcceptEvent();
    }

    /// <summary>Raised on a left click in a 3D view: view, ray origin, ray direction, position in the view.</summary>
    public event Action<View3D, Vector3, Vector3, Vector2> StageClicked;

    /// <summary>
    /// A right click without dragging in the main 3D view (the lamp menu; a right drag looks around): the view and the
    /// position in it where the button went down.
    /// </summary>
    public event Action<View3D, Vector2> ContextClicked;

    /// <summary>Where (in the view) and when the right button went down, and how far the mouse moved since.</summary>
    private Vector2 _rightDownAt;
    private ulong _rightDownMs;
    private float _rightMoved;

    /// <summary>A right click counts as a click (not a look-around drag) within this distance (px) and time (ms).</summary>
    private const float ContextClickDistance = 5f;
    private const ulong ContextClickMs = 400;

    /// <summary>Adds the "small views" toggle after the view tabs; loads the remembered workspaces.</summary>
    public void FinishBar()
    {
        _bar.AddChild(new VSeparator());
        _smallToggle = new CheckBox { Text = "Small views", ButtonPressed = true, FocusMode = FocusModeEnum.None };
        KeyMap.Tooltip(_smallToggle, "Show the workspace's other views as small cards on the right", "small_views_toggle");
        _smallToggle.Toggled += on =>
        {
            _smallHidden = !on;
            Arrange(true);
            SaveSettings();
        };
        _bar.AddChild(_smallToggle);
        LoadSettings();
        RefreshBar();
        Arrange(false);
    }

    /// <summary>Connects the views to a (re)built show and the scene camera.</summary>
    public void SetShow(ShowBuilder builder, FlyCamera sceneCamera)
    {
        SceneCamera = sceneCamera;
        foreach (var v in _views)
        {
            if (v is View3D { OwnCamera: false } v3)
                v3.Source = sceneCamera;
            v.SetBuilder(builder);
        }
        UpdateRoles();
    }

    /// <summary>Makes a view the main view (of the current workspace).</summary>
    public void SetMain(ViewBase view)
    {
        if (view == null || view == _main)
            return;
        _main = view;
        if (_state[view] == ViewState.Hidden)
            _state[view] = ViewState.Small;
        RefreshBar();
        Arrange(true);
        SaveSettings();
        MainChanged?.Invoke();
    }

    /// <summary>Small or hidden (for a view that is not the main view).</summary>
    private void SetState(ViewBase view, ViewState state)
    {
        _state[view] = state;
        if (view == _main && state == ViewState.Hidden)
            _main = _views.FirstOrDefault(v => v != view && _state[v] != ViewState.Hidden) ?? view;
        RefreshBar();
        Arrange(true);
        SaveSettings();
    }

    /// <summary>Switches to a workspace as the user left it (its default the first time).</summary>
    public void SetWorkspace(Workspace workspace)
    {
        if (workspace == _workspace)
        {
            RefreshBar();
            return;
        }
        _saved[_workspace] = Capture();
        _workspace = workspace;
        Apply(_saved.TryGetValue(workspace, out var state) ? state : DefaultState(workspace));
        RefreshBar();
        Arrange(true);
        SaveSettings();
        WorkspaceChanged?.Invoke(workspace);
        MainChanged?.Invoke();
    }

    /// <summary>Puts the current workspace back to its default views.</summary>
    public void ResetWorkspace()
    {
        Apply(DefaultState(_workspace));
        RefreshBar();
        Arrange(true);
        SaveSettings();
        MainChanged?.Invoke();
    }

    /// <summary>The current workspace's arrangement.</summary>
    private WorkspaceState Capture() => new()
    {
        Main = _main?.Title,
        Small = _views.Where(v => v != _main && _state[v] == ViewState.Small).Select(v => v.Title).ToList(),
        SmallHidden = _smallHidden,
        TimelineDocked = _timelineDocked,
    };

    /// <summary>Shows an arrangement (views it doesn't name are hidden).</summary>
    private void Apply(WorkspaceState state)
    {
        _main = _views.FirstOrDefault(v => v.Title == state.Main) ?? _main ?? _views.FirstOrDefault();
        foreach (var v in _views)
            _state[v] = state.Small.Contains(v.Title) ? ViewState.Small : ViewState.Hidden;
        _smallHidden = state.SmallHidden;
        _timelineDocked = state.TimelineDocked;
    }

    /// <summary>A workspace's default arrangement (by the views' key map actions).</summary>
    private WorkspaceState DefaultState(Workspace workspace)
    {
        var (main, cards) = Defaults[workspace];
        string Title(string action) => _views.FirstOrDefault(v => v.ShowAction == action)?.Title;
        return new WorkspaceState
        {
            Main = Title(main),
            Small = cards.Select(Title).Where(t => t != null).ToList(),
        };
    }

    /// <summary>Finds a view by type.</summary>
    public T Find<T>() where T : ViewBase => _views.OfType<T>().FirstOrDefault();

    private void RefreshBar()
    {
        foreach (var (v, b) in _buttons)
        {
            b.SetPressedNoSignal(v == Main);
            b.Modulate = _state[v] == ViewState.Hidden && v != Main && v != DockedTimeline ? new Color(1, 1, 1, 0.5f) : Colors.White;
        }
        foreach (var (ws, b) in _workspaceButtons)
            b.SetPressedNoSignal(ws == _workspace);
        _smallToggle?.SetPressedNoSignal(!_smallHidden);
        RefreshToolStrip();
    }

    /// <summary>Puts a control over the whole view area (the panel's start screen), above the views.</summary>
    internal void ShowOverlay(Control overlay)
    {
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        _area.AddChild(overlay);
    }

    /// <summary>
    /// The tool strip shows the main view's toolbar and the docked timeline's (hidden when there is none or the bar is
    /// hidden).
    /// </summary>
    private void RefreshToolStrip()
    {
        if (_toolStrip == null)
            return;
        var docked = DockedTimeline;
        foreach (var view in _views.Where(v => v.Toolbar != null))
            view.Toolbar.Visible = view == Main || view == docked;
        _toolStrip.Visible = ShowBar && (Main?.Toolbar != null || docked?.Toolbar != null);
    }

    /// <summary>The strip with the main view's toolbar (under the view bar).</summary>
    private PanelContainer _toolStrip;

    /// <summary>Views shown as cards, in bar order (not the docked timeline).</summary>
    private List<ViewBase> Cards =>
        _exclusive != null || _smallHidden ? new List<ViewBase>()
            : _views.Where(v => v != _main && v != DockedTimeline && _state[v] == ViewState.Small).ToList();

    /// <summary>
    /// Positions the main view, the docked timeline under it and the cards (a column on the right), animated or
    /// immediately.
    /// </summary>
    private void Arrange(bool animate)
    {
        if (_area == null || _views.Count == 0)
            return;
        var size = _area.Size;
        if (size.X < 10 || size.Y < 10)
            return;
        var targets = new Dictionary<ViewBase, Rect2>();
        var cards = Cards;
        var main = Main;
        Rect2 mainRect = new(Vector2.Zero, size);
        if (cards.Count > 0)
        {
            float w = Mathf.Max(180f, size.X * 0.22f);
            mainRect = new Rect2(0, 0, size.X - w - 6, size.Y);
            float h = Mathf.Min((size.Y - 6 * (cards.Count - 1)) / cards.Count, w * 0.75f);
            for (int i = 0; i < cards.Count; i++)
                targets[cards[i]] = new Rect2(size.X - w, i * (h + 6), w, h);
        }
        var docked = DockedTimeline;
        _dockArea = mainRect;
        if (docked != null)
        {
            float dh = Mathf.Round(mainRect.Size.Y * _dockShare);
            targets[docked] = new Rect2(mainRect.Position.X, mainRect.End.Y - dh, mainRect.Size.X, dh);
            mainRect.Size = new Vector2(mainRect.Size.X, mainRect.Size.Y - dh - SplitterHeight);
            _splitter.Position = new Vector2(mainRect.Position.X, mainRect.End.Y);
            _splitter.Size = new Vector2(mainRect.Size.X, SplitterHeight);
        }
        _splitter.Visible = docked != null;
        targets[main] = mainRect;

        _tween?.Kill();
        _tween = animate ? CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out) : null;
        foreach (var v in _views)
        {
            bool shown = targets.TryGetValue(v, out var target);
            v.Visible = shown;
            _cardOverlays[v].Visible = shown && v != main && v != docked;
            _cardTitles[v].Visible = shown && v != main && v != docked;
            if (!shown)
                continue;
            v.Rotation = 0f;
            if (_tween != null && v.Size.X > 1)
            {
                _tween.TweenProperty(v, "position", target.Position, AnimationTime);
                _tween.TweenProperty(v, "size", target.Size, AnimationTime);
            }
            else
            {
                v.Position = target.Position;
                v.Size = target.Size;
            }
            var overlay = _cardOverlays[v];
            overlay.Position = target.Position;
            overlay.Size = target.Size;
            _cardTitles[v].Position = target.Position + new Vector2(6, 2);
        }
        // Drawing order: cards first, main view (and the docked timeline) on top of them; overlays and titles above
        // everything.
        foreach (var c in cards)
            _area.MoveChild(c, 0);
        _area.MoveChild(main, cards.Count);
        if (docked != null)
        {
            _area.MoveChild(docked, cards.Count + 1);
            _area.MoveChild(_splitter, cards.Count + 2);
        }
        foreach (var v in _views)
        {
            _area.MoveChild(_cardOverlays[v], -1);
            _area.MoveChild(_cardTitles[v], -1);
        }
        RefreshToolStrip();
        UpdateRoles();
    }

    private void UpdateRoles()
    {
        var main = Main;
        var docked = DockedTimeline;
        foreach (var v in _views)
            v.SetRole(v == main, v != main && v != docked, v == docked);
        if (SceneCamera != null && IsInstanceValid(SceneCamera))
        {
            SceneCamera.InputEnabled = main is View3D { OwnCamera: false } && IsVisibleInTree();
            SceneCamera.ForwardedByView = IsVisibleInTree();
        }
    }

    /// <summary>The window renders no 3D while the host shows the 3D views itself.</summary>
    public override void _Process(double delta)
    {
        var root = GetTree().Root;
        bool hostShows3D = IsVisibleInTree() && _views.Any(v => v is View3D);
        if (root.Disable3D != hostShows3D)
        {
            root.Disable3D = hostShows3D;
            UpdateRoles();
        }
    }

    /// <summary>While the mouse is captured (looking around in a 3D view), all mouse motion goes to that view.</summary>
    public override void _Input(InputEvent e)
    {
        if (!IsVisibleInTree())
            return;
        // A click outside the docked timeline gives its keys back to the main view.
        if (e is InputEventMouseButton { Pressed: true } click && DockedTimeline is { } docked && docked.HasFocus()
            && !docked.GetGlobalRect().HasPoint(click.Position))
            docked.ReleaseFocus();
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } press && Main is View3D v3
            && v3.GetGlobalRect().HasPoint(press.Position))
        {
            _capturing = v3;
            _rightDownAt = press.Position - v3.GetGlobalRect().Position;   // the main view is not rotated
            _rightDownMs = Time.GetTicksMsec();
            _rightMoved = 0f;
        }
        if (_capturing == null)
            return;
        if (e is InputEventMouseMotion motion)
            _rightMoved += motion.Relative.Length();
        if (e is InputEventMouseMotion || e is InputEventMouseButton { ButtonIndex: MouseButton.Right })
        {
            var view = _capturing;
            _capturing.CameraInput(e);
            GetViewport().SetInputAsHandled();
            if (e is InputEventMouseButton { Pressed: false })
            {
                _capturing = null;
                if (_rightMoved <= ContextClickDistance && Time.GetTicksMsec() - _rightDownMs <= ContextClickMs)
                    ContextClicked?.Invoke(view, _rightDownAt);
            }
        }
    }

    /// <summary>View and workspace keys (key map), then the main view's own keys.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        // Menus and the command palette send the action itself (InputEventAction).
        if (!IsVisibleInTree() || e is not (InputEventKey or InputEventMouseButton or InputEventAction))
            return;
        var vp = GetViewport();
        if (_exclusive == null && WorkspaceActions.FirstOrDefault(w => KeyMap.Pressed(e, w.Value, vp)) is { Value: not null } ws)
            SetWorkspace(ws.Key);
        else if (_exclusive == null && KeyMap.Pressed(e, "workspace_reset", vp))
            ResetWorkspace();
        else if (_exclusive == null && KeyMap.Pressed(e, "view_cycle", vp))
        {
            var shown = _views.Where(v => v == _main || _state[v] == ViewState.Small).ToList();
            SetMain(shown[(shown.IndexOf(_main) + 1) % shown.Count]);
        }
        else if (_exclusive == null && _views.FirstOrDefault(v => v.ShowAction != "" && KeyMap.Pressed(e, v.ShowAction, vp)) is { } target)
            SetMain(target);
        else if (_exclusive == null && KeyMap.Pressed(e, "small_views_toggle", vp))
        {
            _smallHidden = !_smallHidden;
            RefreshBar();
            Arrange(true);
            SaveSettings();
        }
        else if (_exclusive == null && KeyMap.Pressed(e, "timeline_dock_toggle", vp))
            TimelineDocked = !TimelineDocked;
        else if (DockedTimeline is { } docked && docked.HasFocus() && docked.HandleKey(e))
        { }
        else if (Main != null && Main.HandleKey(e))
        { }
        else if (Main is View3D { Source: not null } main3D && e is InputEventKey && main3D.Source.HandleInput(e))
        { }
        else
            return;
        vp.SetInputAsHandled();
    }

    /// <summary>
    /// One workspace as remembered: the main view, the views shown small, "Small views" off, the timeline docked.
    /// </summary>
    private sealed class WorkspaceState
    {
        public string Main { get; set; }
        public List<string> Small { get; set; } = new();
        public bool SmallHidden { get; set; }
        public bool TimelineDocked { get; set; }
    }

    /// <summary>The file's content (views by title; a view missing from it is hidden).</summary>
    private sealed class Settings
    {
        public string Workspace { get; set; }
        public Dictionary<string, WorkspaceState> Workspaces { get; set; } = new();
        public bool InspectorOpen { get; set; } = true;
        public float InspectorWidth { get; set; }
        public float TimelineDockShare { get; set; }
    }

    private void SaveSettings()
    {
        if (!_loadedSettings || !Persist)
            return;
        var all = _saved.ToDictionary(p => p.Key.ToString(), p => p.Value);
        all[_workspace.ToString()] = Capture();
        var s = new Settings
        {
            Workspace = _workspace.ToString(),
            Workspaces = all,
            InspectorOpen = _inspectorOpen,
            InspectorWidth = _inspectorWidth,
            TimelineDockShare = _dockShare,
        };
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(SettingsFile), JsonSerializer.Serialize(s));
        }
        catch (IOException e)
        {
            GD.PushWarning($"ViewHost: could not save {SettingsFile}: {e.Message}");
        }
    }

    /// <summary>
    /// Loads the remembered workspaces (a file of before the workspaces only gives the inspector); the current
    /// workspace's arrangement, or its default.
    /// </summary>
    private void LoadSettings()
    {
        _loadedSettings = true;
        string file = ProjectSettings.GlobalizePath(SettingsFile);
        try
        {
            if (Persist && File.Exists(file))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file));
                if (Enum.TryParse(s.Workspace, out Workspace current))
                    _workspace = current;
                foreach (var (name, state) in s.Workspaces ?? new())
                    if (Enum.TryParse(name, out Workspace ws) && state != null)
                        _saved[ws] = new WorkspaceState
                        {
                            Main = state.Main, Small = state.Small ?? new(), SmallHidden = state.SmallHidden,
                            TimelineDocked = state.TimelineDocked,
                        };
                _inspectorOpen = s.InspectorOpen;
                _inspectorWidth = s.InspectorWidth;
                if (s.TimelineDockShare > 0)
                    _dockShare = Mathf.Clamp(s.TimelineDockShare, MinDockShare, MaxDockShare);
            }
        }
        catch (Exception e) when (e is JsonException or IOException or NullReferenceException)
        {
            GD.PushWarning($"ViewHost: ignoring {SettingsFile}: {e.Message}");
        }
        Apply(_saved.Remove(_workspace, out var saved) ? saved : DefaultState(_workspace));
    }
}
