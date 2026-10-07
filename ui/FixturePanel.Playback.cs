using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

// FixturePanel, tabs "Playback" and "Actors".
//  - Playback: choose a MagicQ cue stack, its cues with In/Out times; GO (also Space), Back, Reset, Auto with hold
//    time. Clicking a cue jumps there without fades. Lights fade with the cue times, actors follow their blocking.
//  - Actors: the actors (initially the MagicVis people); add / remove; the right panel edits the selected actor's
//    height and, in the current cue, its movement steps (run one after the other; each: wait first, move time,
//    visible, pose, position, facing) - before the first cue the initial state. Clicking on the stage places the
//    selected actor (for the edited step). "Save blocking" writes res://shows/<show>.blocking.json.
public partial class FixturePanel
{
    private TabContainer _tabs;
    private CuePlayer _player;
    private OptionButton _stackPicker;
    private ItemList _cueList;
    private CheckBox _auto;
    private Label _playStatus;
    private ItemList _actorList;
    private readonly List<string> _actorIds = new();
    private string _selectedActor;
    private bool _syncing;

    private const int PlaybackTab = 1, ActorsTab = 2;

    private Control BuildPlaybackTab()
    {
        var tab = new VBoxContainer { Name = "Playback" };
        _player = new CuePlayer { Name = "CuePlayer" };
        AddChild(_player);
        _player.Changed += SyncPlayback;

        _stackPicker = new OptionButton { TooltipText = "MagicQ cue stack" };
        _stackPicker.ItemSelected += i =>
        {
            var stack = CurrentBuilder?.Model?.MagicQ.CueStacks.FirstOrDefault(s => s.Number == _stackPicker.GetItemMetadata((int)i).AsInt32());
            _player.SetStack(stack);
        };
        _cueList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _cueList.ItemSelected += i =>
        {
            if (!_syncing)
                _player.Jump((int)i);
        };

        var buttons = new HBoxContainer();
        var reset = new Button { Text = "Reset", TooltipText = "Before the first cue" };
        reset.Pressed += () => _player.Jump(-1);
        var back = new Button { Text = "Back" };
        back.Pressed += _player.Back;
        var go = new Button { Text = "GO", TooltipText = "Next cue with its fade times (Space)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        go.Pressed += _player.Go;
        buttons.AddChild(reset);
        buttons.AddChild(back);
        buttons.AddChild(go);

        var autoRow = new HBoxContainer();
        _auto = new CheckBox { Text = "Auto, hold" };
        _auto.Toggled += on =>
        {
            _player.Auto = on;
            if (on && !_player.Running)
                _player.Go();
        };
        var hold = new SpinBox { MinValue = 0, MaxValue = 60, Step = 0.5, Value = _player.Hold, Suffix = "s" };
        hold.ValueChanged += v => _player.Hold = (float)v;
        autoRow.AddChild(_auto);
        autoRow.AddChild(hold);

        _playStatus = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        tab.AddChild(_stackPicker);
        tab.AddChild(_cueList);
        tab.AddChild(buttons);
        tab.AddChild(autoRow);
        tab.AddChild(_playStatus);
        return tab;
    }

    private Control BuildActorsTab()
    {
        var tab = new VBoxContainer { Name = "Actors" };
        _actorList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _actorList.ItemSelected += i => SelectActor(_actorIds[(int)i]);

        var add = new HBoxContainer();
        var addMan = new Button { Text = "+ Man", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        addMan.Pressed += () => AddActor(false);
        var addWoman = new Button { Text = "+ Woman", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        addWoman.Pressed += () => AddActor(true);
        var remove = new Button { Text = "Remove" };
        remove.Pressed += RemoveActor;
        add.AddChild(addMan);
        add.AddChild(addWoman);
        add.AddChild(remove);

        var save = new Button { Text = "Save blocking" };
        save.Pressed += () => _status.Text = CurrentBuilder?.SaveBlocking() ?? "No show loaded";
        tab.AddChild(_actorList);
        tab.AddChild(add);
        tab.AddChild(new Label
        {
            Text = "Edits go into the current cue (Playback tab). Click on the stage to place the selected actor.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.7f, 0.7f, 0.7f),
        });
        tab.AddChild(save);
        return tab;
    }

    // Playback and actors work on all fixtures: leaving the Fixtures tab clears the group / cue filter and Solo.
    private void OnTabChanged(long tab)
    {
        if (tab == 0)
            return;
        bool filtered = _groupFilter.Selected > 0 || _cueFilter.Selected > 0 || _solo.ButtonPressed;
        _solo.ButtonPressed = false;
        _groupFilter.Select(0);
        _cueFilter.Select(0);
        FillList();
        if (filtered && _player.Step >= 0)
            _player.Jump(_player.Step);
    }

    // --- Playback ---

    private void RefreshPlayback()
    {
        var builder = CurrentBuilder;
        _player.Builder = builder;
        _stackPicker.Clear();
        var stacks = builder?.Model?.MagicQ.CueStacks.Where(s => s.Steps.Count > 0).ToList() ?? new();
        foreach (var stack in stacks)
        {
            _stackPicker.AddItem($"{(string.IsNullOrEmpty(stack.Name) ? $"Stack {stack.Number}" : stack.Name)} ({stack.Steps.Count})");
            _stackPicker.SetItemMetadata(_stackPicker.ItemCount - 1, stack.Number);
        }
        // Prefer the stack called "show", else the one with most cues. Selecting doesn't change the stage yet.
        var preferred = stacks.FirstOrDefault(s => s.Name.Equals("show", StringComparison.OrdinalIgnoreCase))
                        ?? stacks.OrderByDescending(s => s.Steps.Count).FirstOrDefault();
        if (preferred != null)
            _stackPicker.Select(stacks.IndexOf(preferred));
        _player.Select(preferred);
    }

    private void SyncPlayback()
    {
        _syncing = true;
        _cueList.Clear();
        var stack = _player.Stack;
        var cues = CurrentBuilder?.Model?.MagicQ.Cues.ToDictionary(c => c.Id) ?? new();
        if (stack != null)
            foreach (var step in stack.Steps)
            {
                cues.TryGetValue(step.CueId, out var cue);
                string text = step.Text.Length > 0 ? step.Text : cue?.Name ?? "";
                string times = cue == null ? "" : $"   {cue.FadeIn:0.#}/{cue.FadeOut:0.#}s";
                _cueList.AddItem($"{step.CueNumberText}  {text}{times}");
            }
        if (_player.Step >= 0)
        {
            _cueList.Select(_player.Step);
            _cueList.EnsureCurrentIsVisible();
        }
        _auto.SetPressedNoSignal(_player.Auto);
        _syncing = false;
        if (_selectedActor != null)
            BuildActorParams();
    }

    public override void _Process(double delta)
    {
        if (_playStatus == null || _player?.Stack == null)
            return;
        var steps = _player.Stack.Steps;
        string cue = _player.Step < 0 ? "before the first cue" : $"cue {steps[_player.Step].CueNumberText} {steps[_player.Step].Text}";
        string next = _player.Step + 1 < steps.Count ? $"next: {steps[_player.Step + 1].CueNumberText} {steps[_player.Step + 1].Text}" : "end of stack";
        _playStatus.Text = $"{cue}{(_player.Running ? "  (running)" : "")}\n{next}";
        if (_selectedActor != null && CurrentBuilder?.Actors.TryGetValue(_selectedActor, out var actor) == true)
            _marker.GlobalPosition = actor.GlobalPosition + Vector3.Up * 2f;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Space } && _tabs.CurrentTab == PlaybackTab)
        {
            _player.Go();
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb
                 && _tabs.CurrentTab == ActorsTab && _selectedActor != null)
        {
            PlaceActorAt(mb.Position);
            GetViewport().SetInputAsHandled();
        }
    }

    // --- Actors ---

    private void RefreshActors()
    {
        _actorList.Clear();
        _actorIds.Clear();
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            return;
        foreach (var def in builder.Model.Blocking.Actors)
        {
            _actorList.AddItem($"{def.Name}  ({(def.Female ? "woman" : "man")})");
            _actorIds.Add(def.Id);
            if (def.Id == _selectedActor)
                _actorList.Select(_actorList.ItemCount - 1);
        }
        if (_selectedActor != null && !_actorIds.Contains(_selectedActor))
        {
            _selectedActor = null;
            _right.Visible = false;
            _marker.Visible = false;
        }
    }

    private void SelectActor(string id)
    {
        _actorStep = 0;
        _selected = null;
        _selectedActor = id;
        _marker.Visible = true;
        BuildActorParams();
    }

    private void AddActor(bool female)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            return;
        int n = builder.Model.Blocking.Actors.Count + 1;
        var actor = builder.AddActor($"{(female ? "Woman" : "Man")} {n}", female, new Vector3(0, 0, -3));
        actor.Apply(builder.ActorStateAt(actor.ActorId, _player.Stack, _player.Step));
        RefreshActors();
        SelectActor(actor.ActorId);
        _actorList.Select(_actorIds.IndexOf(actor.ActorId));
    }

    private void RemoveActor()
    {
        if (_selectedActor == null)
            return;
        CurrentBuilder?.RemoveActor(_selectedActor);
        _selectedActor = null;
        _right.Visible = false;
        _marker.Visible = false;
        RefreshActors();
    }

    // Movement step of the selected actor being edited in the current cue.
    private int _actorStep;

    // State of the selected actor after movement steps 0.._actorStep of the current cue (before the first cue: the
    // initial state).
    private ActorState StateThroughStep(ShowBuilder builder)
    {
        int cue = _player.CueId;
        if (cue < 0)
            return builder.ActorStateAt(_selectedActor, _player.Stack, -1);
        var state = builder.ActorStateAt(_selectedActor, _player.Stack, _player.Step - 1);
        var steps = builder.Model.Blocking.Steps(cue, _selectedActor);
        for (int i = 0; i <= _actorStep && i < steps.Count; i++)
            state.Apply(steps[i]);
        return state;
    }

    // Records a change of the selected actor in the edited movement step of the current cue (the initial state
    // before the first cue) and shows the actor as it is after that step.
    private void RecordActor(Action<ActorState> set)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null || _selectedActor == null)
            return;
        var change = new ActorState();
        set(change);
        var blocking = builder.Model.Blocking;
        int cue = _player.CueId;
        if (cue < 0)
            blocking.Find(_selectedActor)?.Initial.Apply(change);
        else
            blocking.Record(cue, _selectedActor, _actorStep, change);
        builder.Actors[_selectedActor].Apply(StateThroughStep(builder));
    }

    // Changes one coordinate of the selected actor's position in the edited step; the other two are read from the
    // current state at that moment (not from when the panel was built), so earlier edits are kept.
    private void SetActorAxis(int axis, float value)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null || _selectedActor == null)
            return;
        var p = StateThroughStep(builder).Position is { Length: 3 } cur ? (float[])cur.Clone() : new float[3];
        p[axis] = value;
        RecordActor(s => s.Position = p);
    }

    private void PlaceActorAt(Vector2 screen)
    {
        var builder = CurrentBuilder;
        var camera = GetViewport().GetCamera3D();
        if (builder == null || camera == null || !builder.Actors.TryGetValue(_selectedActor, out var actor))
            return;
        Vector3 from = camera.ProjectRayOrigin(screen), dir = camera.ProjectRayNormal(screen);
        float y = actor.GlobalPosition.Y;
        if (Mathf.Abs(dir.Y) < 1e-4f)
            return;
        float t = (y - from.Y) / dir.Y;
        if (t <= 0f)
            return;
        var p = builder.ToLocal(from + dir * t);
        RecordActor(s => s.Position = new[] { p.X, actor.Position.Y, p.Z });
        CallDeferred(MethodName.BuildActorParams);
    }

    private void BuildActorParams()
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null || _selectedActor == null)
            return;
        var def = builder.Model.Blocking.Find(_selectedActor);
        if (def == null)
            return;
        foreach (Node child in _params.GetChildren())
            child.QueueFree();
        _right.Visible = true;

        int cueId = _player.CueId;
        var steps = cueId >= 0 ? builder.Model.Blocking.Steps(cueId, _selectedActor) : new List<ActorState>();
        _actorStep = Math.Clamp(_actorStep, 0, Math.Max(0, steps.Count - 1));
        var state = StateThroughStep(builder);
        var stepData = _actorStep < steps.Count ? steps[_actorStep] : null;
        builder.Actors[_selectedActor].Apply(state);

        _params.AddChild(new Label { Text = def.Name, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var name = new LineEdit { Text = def.Name };
        name.TextSubmitted += t =>
        {
            def.Name = t;
            RefreshActors();
        };
        Row("Name", name);
        var height = new SpinBox { MinValue = 0.8, MaxValue = 2.3, Step = 0.01, Value = def.EffectiveHeight, Suffix = "m" };
        height.ValueChanged += v =>
        {
            def.Height = (float)v;
            builder.Actors[_selectedActor].SetHeight((float)v);
        };
        Row("Height", height);

        var step = _player.Step >= 0 ? _player.Stack.Steps[_player.Step] : null;
        Header(step == null ? "Initial state (before the first cue)" : $"Cue {step.CueNumberText} {step.Text}");
        if (step != null)
        {
            // Movement steps of this actor in this cue: they run one after the other on GO.
            var picker = new OptionButton();
            for (int i = 0; i < Math.Max(1, steps.Count); i++)
                picker.AddItem(i < steps.Count ? $"Step {i + 1} of {steps.Count}" : "Step 1 (no changes yet)");
            picker.Selected = _actorStep;
            picker.ItemSelected += i =>
            {
                _actorStep = (int)i;
                CallDeferred(MethodName.BuildActorParams);
            };
            Row("Movement", picker);
            var stepButtons = new HBoxContainer();
            var add = new Button { Text = "Add step", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            add.Pressed += () =>
            {
                var list = builder.Model.Blocking.Steps(cueId, _selectedActor, create: true);
                list.Add(new ActorState());
                _actorStep = list.Count - 1;
                CallDeferred(MethodName.BuildActorParams);
            };
            var remove = new Button { Text = "Delete step", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Disabled = steps.Count == 0 };
            remove.Pressed += () =>
            {
                builder.Model.Blocking.RemoveStep(cueId, _selectedActor, _actorStep);
                _actorStep = Math.Max(0, _actorStep - 1);
                CallDeferred(MethodName.BuildActorParams);
            };
            stepButtons.AddChild(add);
            stepButtons.AddChild(remove);
            _params.AddChild(stepButtons);

            var delay = new SpinBox
            {
                MinValue = 0, MaxValue = 300, Step = 0.5, Value = stepData?.Delay ?? 0, Suffix = "s",
                TooltipText = "Wait before this step starts (after GO / the previous step)",
            };
            delay.ValueChanged += v => RecordActor(s => s.Delay = (float)v);
            Row("Wait first", delay);
            var move = new SpinBox
            {
                MinValue = 0, MaxValue = 300, Step = 0.5, Value = stepData?.MoveTime ?? 0, Suffix = "s",
                TooltipText = "Time to walk to this step's position; 0 = walking speed",
            };
            move.ValueChanged += v => RecordActor(s => s.MoveTime = (float)v);
            Row("Move time", move);
        }

        var visible = new CheckBox { Text = "On stage", ButtonPressed = state.Visible ?? true };
        visible.Toggled += on => RecordActor(s => s.Visible = on);
        Row("Visible", visible);

        var pose = new OptionButton();
        foreach (string p in ActorPose.All)
            pose.AddItem(p);
        pose.Selected = Math.Max(0, Array.IndexOf(ActorPose.All, state.Pose ?? ActorPose.Standing));
        pose.ItemSelected += i => RecordActor(s => s.Pose = ActorPose.All[i]);
        Row("Pose", pose);

        Header("Position (m) and facing");
        var pos = state.Position is { Length: 3 } sp ? sp : new float[3];
        Number("X (right +)", () => pos[0], v => SetActorAxis(0, v));
        Number("Y (height)", () => pos[1], v => SetActorAxis(1, v));
        Number("Z (audience +)", () => pos[2], v => SetActorAxis(2, v));
        Slider("Facing °", -180, 180, 1, () => state.Facing ?? 0f, v => RecordActor(s => s.Facing = v));

        if (step != null && steps.Count > 0)
        {
            var clear = new Button { Text = "Clear this actor's movement in this cue" };
            clear.Pressed += () =>
            {
                builder.Model.Blocking.ClearCue(cueId, _selectedActor);
                _actorStep = 0;
                CallDeferred(MethodName.BuildActorParams);
            };
            _params.AddChild(clear);
        }
    }

    private void Row(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
        control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(control);
        _params.AddChild(row);
    }
}
