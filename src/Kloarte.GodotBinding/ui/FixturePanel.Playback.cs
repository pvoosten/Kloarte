using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, tabs "Playback" and "Actors".
///  - Playback: choose a MagicQ cue stack, its cues with In/Out times; GO (also Space), Back, Reset, Auto with hold
///    time. Clicking a cue jumps there without fades. Lights fade with the cue times, actors follow their blocking.
///  - Actors: the actors (initially the MagicVis people); add / remove; the right panel edits the selected actor's
///    height and, in the current cue, its movement steps (run one after the other; each: wait first, move time,
///    visible, pose, position, facing) - before the first cue the initial state. Clicking on the stage places the
///    selected actor (for the edited step). Saved with the project (Save).
/// </summary>
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

        _stackPicker = new OptionButton { TooltipText = "The cue list", CustomMinimumSize = new Vector2(150, 0) };
        _stackPicker.ItemSelected += i =>
        {
            var stack = CurrentBuilder?.Document?.CueStacks.FirstOrDefault(s => s.Number == _stackPicker.GetItemMetadata((int)i).AsInt32());
            _player.SetStack(stack);
        };
        _cueList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _cueList.ItemClicked += (index, _, button) =>
        {
            if (button == (long)MouseButton.Right)
                ShowCueMenu((int)index, GetViewport().GetMousePosition());
        };
        _cueList.ItemSelected += i =>
        {
            if (!_syncing)
                _player.Jump((int)i);
        };

        var buttons = new HBoxContainer();
        var reset = new Button { Text = "Reset" };
        KeyMap.Tooltip(reset, "Before the first cue", "playback_reset");
        reset.Pressed += () => _player.Jump(-1);
        var back = new Button { Text = "Back" };
        KeyMap.Tooltip(back, "The previous cue", "playback_back");
        back.Pressed += _player.Back;
        var go = new Button { Text = "GO", CustomMinimumSize = new Vector2(110, 0) };
        KeyMap.Tooltip(go, "Next cue with its fade times", "playback_go");
        go.Pressed += _player.Go;
        buttons.AddChild(reset);
        buttons.AddChild(back);
        buttons.AddChild(go);

        var autoRow = new HBoxContainer();
        _auto = new CheckBox { Text = "Auto, hold" };
        KeyMap.Tooltip(_auto, "Play the cues one after the other, with the hold after each", "playback_auto");
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

        _playStatus = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The transport bar under the menu bar, always visible (UI review 26; user: top): stack, Reset, Back, GO,
        // current -> next cue, Auto + hold. The tab keeps the cue list, inserting cues, live Art-Net and MagicQ PC.
        var transport = new HBoxContainer { Name = "Transport" };
        transport.AddChild(_stackPicker);
        transport.AddChild(new VSeparator());
        transport.AddChild(buttons);
        transport.AddChild(_playStatus);
        transport.AddChild(autoRow);
        _transport = new PanelContainer { Name = "TransportBar" };
        _transport.AddChild(transport);
        tab.AddChild(_cueList);
        tab.AddChild(InsertCueRows());
        tab.AddChild(SmokeRows());
        tab.AddChild(LiveInputRow());
        tab.AddChild(MagicQRow());
        return tab;
    }

    /// <summary>New cue number (suggested after the current cue) and text for "+ BO" / "+ No change".</summary>
    private SpinBox _newCueNumber;
    private LineEdit _newCueText;

    /// <summary>
    /// Inserting cues into the stack after the current one: a blackout (all intensities 0) or a cue without changes
    /// (the look tracks; e.g. a GO for actor movement only). Saved with the show (Save); times and more in MagicQ.
    /// </summary>
    private Control InsertCueRows()
    {
        var box = new VBoxContainer();
        var row = new HBoxContainer();
        _newCueNumber = new SpinBox
        {
            MinValue = 0.001, MaxValue = 9999, Step = 0.001, Prefix = "cue", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Number of the new cue (suggested: right after the current cue)",
        };
        _newCueText = new LineEdit
        {
            PlaceholderText = "text", MaxLength = 15, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Cue text (MagicQ keeps 15 characters)",
        };
        row.AddChild(_newCueNumber);
        row.AddChild(_newCueText);
        var buttons = new HBoxContainer();
        var table = new Button { Text = "Cue table...", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        KeyMap.Tooltip(table, "Every cue with the heads' values: edit them there", "view_cues");
        table.Pressed += () => KeyMap.Run("view_cues");
        var bo = new Button { Text = "+ BO cue", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Insert a blackout: every intensity to 0" };
        bo.Pressed += () => InsertCue(true);
        var empty = new Button { Text = "+ No change", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Insert a cue without changes: the look stays (tracking)" };
        empty.Pressed += () => InsertCue(false);
        buttons.AddChild(bo);
        buttons.AddChild(empty);
        buttons.AddChild(table);
        box.AddChild(row);
        box.AddChild(buttons);
        return box;
    }

    /// <summary>Suggests the number for a new cue right after the current one (not while it is being typed).</summary>
    private void SuggestCueNumber()
    {
        var stack = _player.Stack;
        if (_newCueNumber == null || stack == null || _newCueNumber.GetLineEdit().HasFocus())
            return;
        var steps = stack.Steps;
        float? current = _player.Step >= 0 ? steps[_player.Step].CueNumber : null;
        float? next = _player.Step + 1 < steps.Count ? steps[_player.Step + 1].CueNumber : null;
        _newCueNumber.SetValueNoSignal(CueNumbers.Between(current, next) ?? (current ?? 0) + 0.001f);
    }

    /// <summary>Inserts the new cue into the current stack and jumps to it.</summary>
    private void InsertCue(bool blackout)
    {
        var builder = CurrentBuilder;
        var stack = _player.Stack;
        if (builder?.Model == null || stack == null)
        {
            _status.Text = "No cue stack chosen";
            return;
        }
        float number = (float)Math.Round(_newCueNumber.Value, 3);
        string text = _newCueText.Text.Trim();
        if (text.Length == 0 && blackout)
            text = "BO";
        try
        {
            var step = builder.Playback.InsertCue(stack, number, text, blackout);
            _newCueText.Text = "";
            _player.Invalidate();
            FillCues();
            int index = stack.Steps.ToList().FindIndex(s => s.CueId == step.CueId);
            _stackPicker.SetItemText(_stackPicker.Selected, $"{(string.IsNullOrEmpty(stack.Name) ? $"Stack {stack.Number}" : stack.Name)} ({stack.Steps.Count})");
            _player.Jump(index);
            _status.Text = $"Inserted cue {step.CueNumberText} {(blackout ? "(blackout)" : "(no change)")} in {stack.Name} {KeyMap.Hint("Save", "file_save")}";
        }
        catch (ArgumentException e)
        {
            _status.Text = e.Message;
        }
    }

    /// <summary>A view asks to go to a cue (index in the cue list): it plays there without fades.</summary>
    private void OnJumpRequested(int index)
    {
        if (_player?.Stack != null && index >= -1 && index < _player.Stack.Steps.Count && index != _player.Step)
            _player.Jump(index);
    }

    /// <summary>Deletes the current cue (one undo step); the playback goes to the cue before it.</summary>
    private void DeleteCurrentCue()
    {
        var builder = CurrentBuilder;
        var cue = CueEditing.Design(builder)?.CueOrder.ElementAtOrDefault(_player.Step);
        if (cue == null)
        {
            _status.Text = "No cue to delete: choose one first";
            return;
        }
        int step = _player.Step;
        CueEditing.Edit(builder, $"Delete cue {cue.Cue}", d => d.CueOrder.Remove(cue));
        _player.Invalidate();
        FillCues();
        _player.Jump(step - 1);
        _status.Text = $"Deleted cue {cue.Cue} {KeyMap.Hint("Undo", "edit_undo")}";
    }

    private Control BuildActorsTab()
    {
        var tab = new VBoxContainer { Name = "Actors" };
        _actorList = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _actorList.ItemClicked += (index, _, button) =>
        {
            if (button == (long)MouseButton.Right && index < _actorIds.Count)
                ShowActorMenu(_actorIds[(int)index], GetViewport().GetMousePosition());
        };
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

        tab.AddChild(_actorList);
        tab.AddChild(add);
        tab.AddChild(new Label
        {
            Text = "Edits go into the current cue (Playback tab). Click on the stage to place the selected actor.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.7f, 0.7f, 0.7f),
        });
        return tab;
    }

    /// <summary>
    /// Playback and actors work on all fixtures: leaving the Fixtures tab clears the group / cue filter and Solo.
    /// </summary>
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
        var stacks = builder?.Document?.CueStacks.Where(s => s.Steps.Count > 0).ToList() ?? new();
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
        var cues = CurrentBuilder?.Document?.Cues.ToDictionary(c => c.Id) ?? new();
        if (stack != null)
            foreach (var step in stack.Steps)
            {
                cues.TryGetValue(step.CueId, out var cue);
                string text = step.Text.Length > 0 ? step.Text : cue?.Name ?? "";
                string times = cue == null ? "" : $"   {cue.FadeIn:0.#}/{cue.FadeOut:0.#}s";
                string smoke = CurrentBuilder?.Model?.Blocking.Smoke.TryGetValue(step.CueId, out var cueSmoke) == true ? $"   smoke {cueSmoke.Amount:0.#}%" : "";
                _cueList.AddItem($"{step.CueNumberText}  {text}{times}{smoke}");
            }
        if (_player.Step >= 0)
        {
            _cueList.Select(_player.Step);
            _cueList.EnsureCurrentIsVisible();
        }
        _auto.SetPressedNoSignal(_player.Auto);
        _syncing = false;
        CueEditing.SetCue(_player.Step);
        SuggestCueNumber();
        SyncSmoke();
        // Only a new cue changes what the actor panel shows; edits in it (which invalidate the timeline and so come
        // here too) must not rebuild it under the user's hands.
        if (_selectedActor != null && _player.Step != _actorParamsStep)
            BuildActorParams();
        // A lamp's settings show the cue's level and "Record into cue N": a new cue (or the end of its fade) builds
        // them again (explored 12 Oct 2026: after GO they still named the cue before).
        else if (_selectedActor == null && _lampParamsCue != null && _lampParamsCue != PlaybackPosition)
            CallDeferred(MethodName.BuildParams);
    }

    /// <summary>The cue stack, step and whether it runs, as the lamp settings were built for (null: not built).</summary>
    private (ICueStack Stack, int Step, bool Running)? _lampParamsCue;

    /// <summary>Where the playback is now (stack, step, running).</summary>
    private (ICueStack Stack, int Step, bool Running) PlaybackPosition => (_player.Stack, _player.Step, _player.Running);

    /// <summary>Cue step the actor settings were built for.</summary>
    private int _actorParamsStep = int.MinValue;

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        PollMovie(delta);
        PollUnsaved(delta);
        PollPresent(delta);
        RefreshStepTiming();
        ApplyLiveInput();
        CheckMagicQ(delta);
        RefreshChips();
        if (_playStatus == null || _player?.Stack == null)
            return;
        var steps = _player.Stack.Steps;
        string cue = _player.Step < 0 ? "before the first cue" : $"cue {steps[_player.Step].CueNumberText} {steps[_player.Step].Text}";
        string next = _player.Step + 1 < steps.Count ? $"next: {steps[_player.Step + 1].CueNumberText} {steps[_player.Step + 1].Text}" : "end of stack";
        _playStatus.Text = $"{cue}{(_player.Running ? "  \u25b6" : "")}   \u2192   {next}";
        if (_selectedActor != null && CurrentBuilder?.Actors.TryGetValue(_selectedActor, out var actor) == true)
            _marker.GlobalPosition = actor.GlobalPosition + Vector3.Up * 2f;
    }

    /// <summary>Global key map actions (playback, fixtures, file) and placing the selected actor with a click.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        var vp = GetViewport();
        if (PresentKey(e, vp))   // FixturePanel.Present.cs
            return;
        if (KeyMap.Pressed(e, "playback_go", vp))
            _player.Go();
        else if (KeyMap.Pressed(e, "playback_back", vp))
            _player.Back();
        else if (KeyMap.Pressed(e, "playback_reset", vp))
            _player.Jump(-1);
        else if (KeyMap.Pressed(e, "playback_auto", vp))
        {
            _player.Auto = !_player.Auto;
            if (_player.Auto && !_player.Running)
                _player.Go();
            SyncPlayback();
        }
        else if (KeyMap.Pressed(e, "cue_insert", vp))
            InsertCue(false);
        else if (KeyMap.Pressed(e, "cue_delete", vp))
            DeleteCurrentCue();
        else if (KeyMap.Pressed(e, "fixture_solo", vp))
            _solo.ButtonPressed = !_solo.ButtonPressed;
        else if (KeyMap.Pressed(e, "deselect", vp) && _aimOnce)
        {
            _aimOnce = false;
            _status.Text = "Aim cancelled";
        }
        else if (KeyMap.Pressed(e, "deselect", vp))
            Deselect();
        else if (KeyMap.Pressed(e, "project_open_folder", vp))
            AskToSave("opening another project", ShowProjectFolderDialog);
        else if (KeyMap.Pressed(e, "file_open", vp))
            AskToSave("opening another show", ShowOpenDialog);
        else if (KeyMap.Pressed(e, "edit_undo", vp))
            UndoEdit(redo: false);   // in the gobo editor's window its own HandleKey undoes the drawing
        else if (KeyMap.Pressed(e, "edit_redo", vp))
            UndoEdit(redo: true);
        else if (ToolKey(e, vp))   // FixturePanel.Workspaces.cs
        { }
        else if (KeyMap.Pressed(e, "command_palette", vp))
            ShowPalette();
        else if (KeyMap.Pressed(e, "preferences", vp))
            ShowPreferences();
        else if (KeyMap.Pressed(e, "file_save", vp))
            _status.Text = SaveProject();
        else if (KeyMap.Pressed(e, "file_save_as", vp))
            ShowSaveDialog();
        else if (KeyMap.Pressed(e, "export_keyframes", vp))
            ShowExportDialog(ExportKind.Stills);   // FixturePanel.ExportDialog.cs
        else if (KeyMap.Pressed(e, "export_movie", vp))
            ShowExportDialog(ExportKind.Movie);
        else if (KeyMap.Pressed(e, "panels_toggle", vp))
            SetSidePanels(!_left.Visible);
        else if (KeyMap.Pressed(e, "inspector_toggle", vp))
            SetInspectorOpen(!InspectorOpen);
        else
            return;
        vp.SetInputAsHandled();
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
            ShowNothingSelected();
            _marker.Visible = false;
        }
    }

    /// <summary>Selects an actor (first movement step of the current cue) through the shared selection.</summary>
    private void SelectActor(string id) => Selection.SelectActor(id, 0);

    /// <summary>Shows the selected actor / step (from the list, the timeline or a 3D click) in the Actors tab.</summary>
    private void ShowActor(string id, int step)
    {
        _selected = null;
        _selectedActor = id;
        _actorStep = step;
        _marker.Visible = true;
        if (_tabs.CurrentTab != ActorsTab)
            _tabs.CurrentTab = ActorsTab;
        int index = _actorIds.IndexOf(id);
        if (index >= 0 && !_actorList.IsSelected(index))
            _actorList.Select(index);
        BuildActorParams();
    }

    private void AddActor(bool female)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            return;
        int n = builder.Model.Blocking.Actors.Count + 1;
        builder.Files.NameNextEdit("Add actor");
        var actor = builder.AddActor($"{(female ? "Woman" : "Man")} {n}", female, new Vector3(0, 0, -3));
        _player.Invalidate();
        actor.Apply(builder.ActorStateAt(actor.ActorId, _player.Stack, _player.Step));
        RefreshActors();
        SelectActor(actor.ActorId);
        _actorList.Select(_actorIds.IndexOf(actor.ActorId));
    }

    private void RemoveActor()
    {
        if (_selectedActor is not { } id || CurrentBuilder?.Model?.Blocking.Find(id) is not { } actor)
            return;
        CurrentBuilder.Files.NameNextEdit($"Remove actor {actor.Name}");
        CurrentBuilder.RemoveActor(id);
        _player.Invalidate();
        _selectedActor = null;
        ShowNothingSelected();
        _marker.Visible = false;
        RefreshActors();
        _status.Text = $"Removed {actor.Name} {KeyMap.Hint("Undo", "edit_undo")}";
    }

    /// <summary>Movement step of the selected actor being edited in the current cue.</summary>
    private int _actorStep;

    /// <summary>
    /// State of the selected actor after movement steps 0.._actorStep of the current cue (before the first cue: the
    /// initial state).
    /// </summary>
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

    /// <summary>
    /// Records a change of the selected actor in the edited movement step of the current cue (the initial state
    /// before the first cue) and shows the actor as it is after that step.
    /// </summary>
    private void RecordActor(Action<ActorState> set)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null || _selectedActor == null)
            return;
        var change = new ActorState();
        set(change);
        var blocking = builder.Model.Blocking;
        builder.Files.NameNextEdit($"Move {blocking.Find(_selectedActor)?.Name}");
        int cue = _player.CueId;
        if (cue < 0)
            blocking.Find(_selectedActor)?.Initial.Apply(change);
        else
            blocking.Record(cue, _selectedActor, _actorStep, change);
        _player.Invalidate();
        builder.Actors[_selectedActor].Apply(StateThroughStep(builder));
    }

    /// <summary>
    /// Changes one coordinate of the selected actor's position in the edited step; the other two are read from the
    /// current state at that moment (not from when the panel was built), so earlier edits are kept.
    /// </summary>
    private void SetActorAxis(int axis, float value)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null || _selectedActor == null)
            return;
        var p = StateThroughStep(builder).Position is { Length: 3 } cur ? (float[])cur.Clone() : new float[3];
        p[axis] = value;
        RecordActor(s => s.Position = p);
    }

    /// <summary>Places the selected actor where a ray (from a click in a 3D view) hits its floor height.</summary>
    private void PlaceActorAt(Vector3 from, Vector3 dir)
    {
        var builder = CurrentBuilder;
        if (builder == null || !builder.Actors.TryGetValue(_selectedActor, out var actor))
            return;
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
        ClearParams($"actor:{_selectedActor}:{_player.Step}:{_actorStep}");
        _actorParamsStep = _player.Step;

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
            builder.Files.NameNextEdit($"Rename {def.Name}");
            def.Name = t;
            if (builder.Actors.TryGetValue(def.Id, out var node))
                node.SetLabel(t);
            RefreshActors();
        };
        Row("Name", name);
        // The shirt (its colour; the name is printed on it): a colour per actor, from a palette until chosen here.
        var shirt = new ColorPickerButton
        {
            Color = ShirtColors.Parse(ShirtColors.Of(builder.Model.Blocking, def)) is var (r, g, b) ? new Color(r, g, b) : Colors.Gray,
            EditAlpha = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Shirt colour; the actor's name is printed on chest and back",
        };
        shirt.PopupClosed += () =>
        {
            string hex = shirt.Color.ToHtml(false);
            if (hex == ShirtColors.Of(builder.Model.Blocking, def))
                return;
            builder.Files.NameNextEdit($"Shirt of {def.Name}");
            def.ShirtColor = hex;
            if (builder.Actors.TryGetValue(def.Id, out var node))
                node.SetShirtColor(hex);
        };
        Row("Shirt", shirt);
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
                Selection.SelectActor(_selectedActor, _actorStep);
                CallDeferred(MethodName.BuildActorParams);
            };
            Row("Movement", picker);
            _stepTiming = new Label
            {
                Modulate = new Color(0.7f, 0.7f, 0.7f),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(180, 0),
            };
            _params.AddChild(_stepTiming);
            RefreshStepTiming();
            var stepButtons = new HBoxContainer();
            var add = new Button { Text = "Add step", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            add.Pressed += () =>
            {
                var list = builder.Model.Blocking.Steps(cueId, _selectedActor, create: true);
                _player.Invalidate();
                list.Add(new ActorState());
                _actorStep = list.Count - 1;
                Selection.SelectActor(_selectedActor, _actorStep);
                CallDeferred(MethodName.BuildActorParams);
            };
            var remove = new Button { Text = "Delete step", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Disabled = steps.Count == 0 };
            remove.Pressed += () =>
            {
                builder.Model.Blocking.RemoveStep(cueId, _selectedActor, _actorStep);
                _player.Invalidate();
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
            var clear = new Button { Text = "Clear movement", TooltipText = "Clear this actor's movement in this cue" };
            clear.Pressed += () =>
            {
                builder.Files.NameNextEdit($"Clear {builder.Model.Blocking.Find(_selectedActor)?.Name}'s movement");
                builder.Model.Blocking.ClearCue(cueId, _selectedActor);
                _player.Invalidate();
                _actorStep = 0;
                CallDeferred(MethodName.BuildActorParams);
            };
            _params.AddChild(clear);
        }
    }

    /// <summary>The selected step's place on the timeline (actor settings; null while not shown).</summary>
    private Label _stepTiming;

    /// <summary>
    /// Says when the selected movement step waits, walks and arrives (UI review 28: the inspector of the selected step);
    /// called every frame, so it follows drags in the timeline without rebuilding the settings.
    /// </summary>
    private void RefreshStepTiming()
    {
        if (_stepTiming == null || !IsInstanceValid(_stepTiming))
            return;
        var timeline = _player.Timeline;
        var step = timeline?.Steps.FirstOrDefault(s => s.ActorId == _selectedActor && s.StepIndex == _actorStep
                                                         && timeline.Segments[s.Segment].Index == _player.Step);
        static string Clock(double t) => $"{(int)(t / 60)}:{(t % 60).ToString("00.0", CultureInfo.InvariantCulture)}";
        string text = step == null ? "Not on the timeline yet (no movement in this cue)"
            : $"On the timeline: waits from {Clock(step.WaitStart)}, walks from {Clock(step.Start)}, arrives at {Clock(step.End)}";
        if (_stepTiming.Text != text)
            _stepTiming.Text = text;
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
