using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: the project's set pieces (scenery, Kloarte's own data in blocking/scenery.yaml). The Scenery tab lists
/// them, with "+ Piece" (a shape), Duplicate and Remove (also in the Scenery menu, key map <c>scenery_add</c> /
/// <c>scenery_duplicate</c> / <c>scenery_delete</c>). The inspector edits the selected piece: name, shape, place and
/// rotation, the sizes its shape has, colour and material, and in the current cue whether it goes out or comes in and how
/// far a split curtain opens (it tracks from the cues before, like the lights). A click on a piece in the plan or a 3D view
/// selects it; dragging moves it, in 3D a right drag turns it. Every edit is one undo step (<see cref="SceneryEditing"/>);
/// the stage shows a changed piece through <see cref="ShowSceneryEdit"/>.
/// </summary>
public partial class FixturePanel
{
    /// <summary>The Scenery tab's list.</summary>
    private ItemList _sceneryList;

    /// <summary>The piece ids in the order of <see cref="_sceneryList"/>.</summary>
    private readonly List<string> _pieceIds = new();

    /// <summary>The selected set piece (id), or null.</summary>
    private string _selectedPiece;

    /// <summary>Index of the Scenery tab.</summary>
    private const int SceneryTab = 3;

    /// <summary>The cue step the piece's settings were built for.</summary>
    private int _pieceParamsStep = int.MinValue;

    /// <summary>A piece changed: its undo step is recorded when no mouse button is held (a drag is one step).</summary>
    private bool _sceneryCheckpointPending;

    /// <summary>A cue's change of the selected piece was edited: its settings are built again when no mouse button is held.</summary>
    private bool _pieceParamsDirty;

    /// <summary>The piece being dragged over the floor in a 3D view (id), or null.</summary>
    private string _dragPiece3D;

    /// <summary>The stage height of the dragged piece's bottom (the plane it is dragged over).</summary>
    private float _dragFloor3D;

    /// <summary>The dragged piece's centre relative to the floor point under the mouse when it was grabbed (stage).</summary>
    private Vector3 _dragOffset3D;

    /// <summary>The piece being turned with a right drag in a 3D view (id), or null.</summary>
    private string _turnPiece3D;

    /// <summary>Degrees a piece turns per pixel of a right drag in 3D.</summary>
    private const float TurnPerPixel = 0.5f;

    /// <summary>The Scenery tab: the pieces, + Piece (shape menu), Duplicate, Remove and a hint.</summary>
    private Control BuildSceneryTab()
    {
        var tab = new VBoxContainer { Name = "Scenery" };
        _sceneryList = new ItemList { Name = "SceneryList", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _sceneryList.ItemSelected += i => Selection.SelectPiece(_pieceIds.ElementAtOrDefault((int)i));

        var buttons = new HBoxContainer();
        var add = new MenuButton { Text = "+ Piece", Flat = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        KeyMap.Tooltip(add, "Add a set piece: choose its shape (the key adds a block)", "scenery_add");
        foreach (var shape in Enum.GetValues<SceneryShape>())
            add.GetPopup().AddItem(SceneryEditing.ShapeName(shape), (int)shape);
        add.GetPopup().IdPressed += id => AddPiece((SceneryShape)(int)id);
        var duplicate = new Button { Text = "Duplicate", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        KeyMap.Tooltip(duplicate, "Duplicate the selected set piece", "scenery_duplicate");
        duplicate.Pressed += DuplicatePiece;
        var remove = new Button { Text = "Remove" };
        KeyMap.Tooltip(remove, "Remove the selected set piece", "scenery_delete");
        remove.Pressed += RemovePiece;
        buttons.AddChild(add);
        buttons.AddChild(duplicate);
        buttons.AddChild(remove);

        var hint = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.7f, 0.7f, 0.7f),
            CustomMinimumSize = new Vector2(180, 0),
        };
        void UpdateHint()
        {
            if (!IsInstanceValid(hint))
                return;
            hint.Text = "Click a piece in the plan or the 3D view to select it, drag it to move it; in the plan hold " +
                        $"{KeyMap.KeysText("plan_rotate")} while dragging to turn it, in 3D drag with the right mouse button. " +
                        "In / out per cue and a curtain's opening: in the inspector, for the current cue (Playback tab).";
        }
        UpdateHint();
        KeyMap.Changed += UpdateHint;
        TreeExiting += () => KeyMap.Changed -= UpdateHint;

        tab.AddChild(_sceneryList);
        tab.AddChild(buttons);
        tab.AddChild(hint);

        SceneryEditing.Edited += OnSceneryEdited;
        SceneryEditing.Now = PieceNow;
        TreeExiting += () =>
        {
            SceneryEditing.Edited -= OnSceneryEdited;
            SceneryEditing.Now = null;
        };
        return tab;
    }

    /// <summary>The Scenery menu: add, duplicate, delete (key map actions).</summary>
    private void SceneryMenu(PopupMenu menu)
    {
        Item(menu, "Add a set piece", "scenery_add");
        Item(menu, "Duplicate the selected set piece", "scenery_duplicate");
        Item(menu, "Delete the selected set piece", "scenery_delete");
    }

    /// <summary>The scenery keys (key map); true when used.</summary>
    private bool SceneryKey(InputEvent e, Viewport vp)
    {
        if (KeyMap.Pressed(e, "scenery_add", vp))
            AddPiece(SceneryShape.Block);
        else if (KeyMap.Pressed(e, "scenery_duplicate", vp))
            DuplicatePiece();
        else if (KeyMap.Pressed(e, "scenery_delete", vp))
            RemovePiece();
        else
            return false;
        return true;
    }

    /// <summary>The state of a piece at the playhead, or null when the timeline doesn't know it (yet).</summary>
    private SceneryFrame? PieceNow(string pieceId)
    {
        var timeline = _player?.Builder?.Model != null ? _player.Timeline : null;
        if (timeline == null || !timeline.Scenery.Contains(pieceId))
            return null;
        return timeline.SceneryAt(pieceId, _player.Time);
    }

    /// <summary>Fills the Scenery tab's list (keeps the selected piece selected).</summary>
    private void RefreshScenery()
    {
        if (_sceneryList == null)
            return;
        _sceneryList.Clear();
        _pieceIds.Clear();
        var pieces = CurrentBuilder?.Model?.Blocking.Scenery ?? new List<SceneryPiece>();
        foreach (var piece in pieces)
        {
            _sceneryList.AddItem($"{piece.Name}  ({SceneryEditing.ShapeName(piece.Shape).ToLowerInvariant()})");
            _pieceIds.Add(piece.Id);
            if (piece.Id == _selectedPiece)
                _sceneryList.Select(_sceneryList.ItemCount - 1);
        }
    }

    /// <summary>Adds a piece of a shape and selects it.</summary>
    private void AddPiece(SceneryShape shape)
    {
        var piece = SceneryEditing.Add(CurrentBuilder, shape);
        if (piece == null)
        {
            _status.Text = "No show loaded";
            return;
        }
        Selection.SelectPiece(piece.Id);
        _status.Text = $"Added {piece.Name} {KeyMap.Hint("Undo", "edit_undo")}";
    }

    /// <summary>Duplicates the selected piece and selects the copy.</summary>
    private void DuplicatePiece()
    {
        if (_selectedPiece == null || SceneryEditing.Duplicate(CurrentBuilder, _selectedPiece) is not { } copy)
        {
            _status.Text = "Select a set piece first";
            return;
        }
        Selection.SelectPiece(copy.Id);
        _status.Text = $"Added {copy.Name} {KeyMap.Hint("Undo", "edit_undo")}";
    }

    /// <summary>Removes the selected piece.</summary>
    private void RemovePiece()
    {
        if (_selectedPiece == null || SceneryEditing.Remove(CurrentBuilder, _selectedPiece) is not { } name)
        {
            _status.Text = "Select a set piece first";
            return;
        }
        _status.Text = $"Removed {name} {KeyMap.Hint("Undo", "edit_undo")}";
    }

    /// <summary>A piece or a cue's change of it was edited (here, in the plan or in 3D): the list, playback and stage follow.</summary>
    private void OnSceneryEdited(string pieceId, bool scene)
    {
        if (!IsInstanceValid(this) || _player == null)
            return;
        RefreshScenery();
        _player.Invalidate();   // the timeline knows the pieces and their changes per cue
        if (scene)
        {
            ShowSceneryEdit(pieceId, scene: true);
            _sceneryCheckpointPending = true;
        }
        else
        {
            ShowSceneryEdit(pieceId, scene: false);
            _pieceParamsDirty |= pieceId == _selectedPiece;
        }
    }

    /// <summary>
    /// Shows an edit of the scenery on stage: a piece edit (`scene` true) rebuilds that piece's node (cheap, so a drag
    /// follows live; a removed piece's node goes, no id = all pieces); a cue's change (false) only plays the current cue again.
    /// </summary>
    private void ShowSceneryEdit(string pieceId, bool scene)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            return;
        if (!scene)
        {
            _player.Invalidate();
            if (_player.Stack != null && _player.Step >= 0)
                _player.Seek(_player.Time);
            return;
        }
        if (pieceId == null)
            builder.RebuildScenery();
        else
            builder.RebuildPiece(pieceId);
    }

    /// <summary>
    /// Every frame: an edit that has settled (no mouse button held) is shown on stage, the selected piece's settings follow
    /// a cue edit, and the marker stays over the selected piece.
    /// </summary>
    private void PollScenery()
    {
        bool settled = Input.GetMouseButtonMask() == 0;
        if (_sceneryCheckpointPending && settled)
        {
            _sceneryCheckpointPending = false;
            CurrentBuilder?.Checkpoint();
        }
        if (_pieceParamsDirty && settled)
        {
            _pieceParamsDirty = false;
            BuildPieceParams();
        }
        PlaceMarkerOverPiece();
    }

    /// <summary>The marker over the selected piece (above its top).</summary>
    private void PlaceMarkerOverPiece()
    {
        if (_selectedPiece != null && _marker != null && CurrentBuilder?.Model?.Blocking.FindPiece(_selectedPiece) is { } piece)
        {
            var p = piece.Position;
            _marker.GlobalPosition = CurrentBuilder.ToGlobal(new Vector3(p[0], p[1] + piece.Height + 0.3f, p[2]));
        }
    }

    /// <summary>Shows the selected piece: the Scenery tab, its row, the marker and its settings.</summary>
    private void ShowPiece(string id)
    {
        _selected = null;
        _selectedActor = null;
        _selectedPiece = id;
        _lampParamsCue = null;
        _list.ShowSelected(Selection.Fixtures);
        ApplyLit();
        _marker.Visible = true;
        if (_tabs.CurrentTab != SceneryTab)
            _tabs.CurrentTab = SceneryTab;
        int index = _pieceIds.IndexOf(id);
        if (index < 0)
        {
            RefreshScenery();
            index = _pieceIds.IndexOf(id);
        }
        if (index >= 0 && !_sceneryList.IsSelected(index))
            _sceneryList.Select(index);
        BuildPieceParams();
        PlaceMarkerOverPiece();
    }

    // --- 3D views ---

    /// <summary>A click in a 3D view that hits a piece selects it and starts dragging it over the floor; true when it did.</summary>
    private bool PickPiece(Vector3 origin, Vector3 dir)
    {
        var builder = CurrentBuilder;
        if (SceneryEditing.Hit(builder, origin, dir) is not { } hit)
            return false;
        Selection.SelectPiece(hit.Piece.Id);
        var p = hit.Piece.Position;
        _dragPiece3D = hit.Piece.Id;
        _dragFloor3D = p[1];
        var floor = FloorPoint(builder, origin, dir, p[1]) ?? hit.Point;
        _dragOffset3D = new Vector3(p[0] - floor.X, 0f, p[2] - floor.Z);
        return true;
    }

    /// <summary>Where a world ray meets the horizontal plane at stage height `y` (stage coordinates), or null.</summary>
    private static Vector3? FloorPoint(ShowBuilder builder, Vector3 origin, Vector3 dir, float y)
    {
        var o = builder.ToLocal(origin);
        var d = builder.GlobalBasis.Inverse() * dir;
        if (Mathf.Abs(d.Y) < 1e-4f)
            return null;
        float t = (y - o.Y) / d.Y;
        return t <= 0f ? null : o + d * t;
    }

    /// <summary>
    /// Mouse in a 3D view before its camera: dragging a picked piece over the floor, and a right drag that starts on the
    /// selected piece turns it (instead of looking around). True when the event was used.
    /// </summary>
    private bool SceneryMouse(View3D view, InputEvent e)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            return false;
        switch (e)
        {
            case InputEventMouseMotion move when _dragPiece3D != null && (move.ButtonMask & MouseButtonMask.Left) != 0:
            {
                var (origin, dir) = view.RayAt(move.Position);
                if (FloorPoint(builder, origin, dir, _dragFloor3D) is { } at && builder.Model.Blocking.FindPiece(_dragPiece3D) is { } piece)
                {
                    var offset = _dragOffset3D;
                    SceneryEditing.Edit(builder, $"Move {piece.Name}", piece.Id,
                        p => p.Position = new[] { at.X + offset.X, p.Position[1], at.Z + offset.Z });
                }
                return true;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _dragPiece3D != null:
                _dragPiece3D = null;
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when _selectedPiece != null:
            {
                var (origin, dir) = view.RayAt(view.GetLocalMousePosition());
                if (SceneryEditing.Hit(builder, origin, dir)?.Piece.Id != _selectedPiece)
                    return false;
                _turnPiece3D = _selectedPiece;
                return true;
            }
            case InputEventMouseMotion turn when _turnPiece3D != null:
                if (builder.Model.Blocking.FindPiece(_turnPiece3D) is { } turned)
                    SceneryEditing.Edit(builder, $"Turn {turned.Name}", turned.Id,
                        p => p.Rotation = SceneryGeometry.Wrap(p.Rotation - turn.Relative.X * TurnPerPixel));
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } when _turnPiece3D != null:
                _turnPiece3D = null;
                return true;
        }
        return false;
    }

    // --- Inspector ---

    /// <summary>The selected piece's settings: the piece itself, then what the current cue does with it.</summary>
    private void BuildPieceParams()
    {
        var builder = CurrentBuilder;
        var piece = builder?.Model?.Blocking.FindPiece(_selectedPiece);
        if (piece == null)
            return;
        ClearParams($"piece:{piece.Id}:{_player.Step}");
        _pieceParamsStep = _player.Step;
        _lampParamsCue = null;
        string id = piece.Id;
        // The piece as it is now: a rename or a drag doesn't build these settings again, and an undo replaces the object.
        SceneryPiece Now() => builder.Model?.Blocking.FindPiece(id) ?? piece;
        void Edit(string what, Action<SceneryPiece> change) => SceneryEditing.Edit(builder, $"{what} of {Now().Name}", id, change);

        _params.AddChild(new Label { Text = piece.Name, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        var name = new LineEdit { Text = piece.Name, TooltipText = "The piece's name (its id in the files stays)" };
        name.TextSubmitted += t =>
        {
            if (t.Trim().Length == 0 || t.Trim() == Now().Name)
                return;
            SceneryEditing.Edit(builder, $"Rename {Now().Name}", id, p => p.Name = t.Trim());
            CallDeferred(MethodName.BuildPieceParams);   // the title and the cue rows name it
        };
        Row("Name", name);
        var shape = new OptionButton { TooltipText = "The shape; its sizes go to the new shape's defaults" };
        foreach (var s in Enum.GetValues<SceneryShape>())
            shape.AddItem(SceneryEditing.ShapeName(s), (int)s);
        shape.Selected = shape.GetItemIndex((int)piece.Shape);
        shape.ItemSelected += i =>
        {
            var chosen = (SceneryShape)shape.GetItemId((int)i);
            if (chosen == Now().Shape)
                return;
            Edit("Shape", p => ChangeShape(p, chosen));
            CallDeferred(MethodName.BuildPieceParams);   // the new shape's sizes and fields
        };
        Row("Shape", shape);

        Header("Place (m) and rotation", "Centre of the piece's bottom in stage coordinates; rotation about the vertical, counter-clockwise seen from above");
        PieceNumber("X (right +)", "Stage x of the centre (audience's right +)", -30, 30, 0.01, "m", piece.Position[0],
            v => Edit("Place", p => p.Position = new[] { v, p.Position[1], p.Position[2] }));
        PieceNumber("Y (height)", "Height of the piece's bottom above the stage floor (e.g. on a rostrum)", -5, 20, 0.01, "m", piece.Position[1],
            v => Edit("Place", p => p.Position = new[] { p.Position[0], v, p.Position[2] }));
        PieceNumber("Z (audience +)", "Stage z of the centre (towards the audience +; 0 = proscenium line)", -30, 30, 0.01, "m", piece.Position[2],
            v => Edit("Place", p => p.Position = new[] { p.Position[0], p.Position[1], v }));
        Slider("Rotation °", -180, 180, 1, () => Now().Rotation, v => Edit("Rotation", p => p.Rotation = v),
            "Counter-clockwise seen from above (0 = the piece's width along the stage's width)");

        Header("Size (m)");
        bool curtain = piece.Shape == SceneryShape.Curtain;
        PieceNumber(piece.Shape == SceneryShape.Table ? "Length" : "Width", "Size along the piece's own x axis", 0.01, 40, 0.01, "m",
            piece.Width, v => Edit("Width", p => p.Width = v));
        if (!curtain)
            PieceNumber("Depth", "Size along the piece's own z axis", 0.01, 40, 0.01, "m", piece.Depth, v => Edit("Depth", p => p.Depth = v));
        PieceNumber("Height", piece.Shape == SceneryShape.Table ? "Height of the table top's upper side" : "Height", 0.01, 30, 0.01, "m",
            piece.Height, v => Edit("Height", p => p.Height = v));
        if (piece.Shape == SceneryShape.Table)
        {
            PieceNumber("Top thickness", "Thickness of the table top", 0.005, 0.5, 0.005, "m", piece.TopThickness,
                v => Edit("Top thickness", p => p.TopThickness = v));
            PieceNumber("Legs", "Side of the legs' square section", 0.01, 0.5, 0.005, "m", piece.LegThickness,
                v => Edit("Legs", p => p.LegThickness = v));
        }
        if (curtain)
        {
            PieceNumber("Fullness", "Extra cloth in the pleats: 0 % = a flat cloth, 100 % = twice the width of cloth", 0, 200, 5, "%",
                piece.Fullness, v => Edit("Fullness", p => p.Fullness = v));
            var split = new CheckBox { Text = "In two halves that open", ButtonPressed = piece.Split };
            split.Toggled += on =>
            {
                Edit("Split", p => p.Split = on);
                CallDeferred(MethodName.BuildPieceParams);
            };
            Row("Split", split);
            if (piece.Split)
                PieceNumber("Open at start", "How far the curtain is open before the first cue (0 = closed, 100 = fully open)", 0, 100, 1, "%",
                    piece.Open, v =>
                    {
                        Edit("Opening", p => p.Open = v);
                        _pieceParamsDirty = true;   // the cue rows show what tracks from the start (built again when settled)
                    });
        }

        Header("Look");
        var colour = new ColorPickerButton
        {
            Color = ShirtColors.Parse(piece.ColorOrDefault) is var (r, g, b) ? new Color(r, g, b) : Colors.Gray,
            EditAlpha = false,
            TooltipText = "The piece's colour (the material sets how it reflects light)",
        };
        colour.PopupClosed += () =>
        {
            string hex = colour.Color.ToHtml(false).ToLowerInvariant();
            if (hex != Now().ColorOrDefault)
                Edit("Colour", p => p.Color = hex);
        };
        Row("Colour", colour);
        var material = new OptionButton { TooltipText = "How the colour reflects light: matt paint, varnished wood, molton (absorbs nearly all), metal, gauze (translucent)" };
        foreach (var m in Enum.GetValues<SceneryMaterial>())
            material.AddItem(m.ToString(), (int)m);
        material.Selected = material.GetItemIndex((int)piece.Material);
        material.ItemSelected += i => Edit("Material", p => p.Material = (SceneryMaterial)material.GetItemId((int)i));
        Row("Material", material);

        PieceCueRows(builder, piece);
        UpdateInspectorStrip();
    }

    /// <summary>A labelled number of the piece (a spin box; dragging it is one undo step: the edit is recorded when the mouse is released).</summary>
    private void PieceNumber(string label, string tooltip, double min, double max, double step, string suffix, float value, Action<float> set)
    {
        var spin = new SpinBox
        {
            MinValue = min, MaxValue = max, Step = step, Value = value, Suffix = suffix, TooltipText = tooltip,
            CustomArrowStep = step,
        };
        spin.ValueChanged += v => set((float)v);
        Row(label, spin);
    }

    /// <summary>A piece with the sizes (and, without a colour of its own, the colour and material) of another shape's defaults.</summary>
    private static void ChangeShape(SceneryPiece piece, SceneryShape shape)
    {
        var fresh = SceneryPiece.New(piece.Id, piece.Name, shape);
        piece.Shape = shape;
        (piece.Width, piece.Depth, piece.Height) = (fresh.Width, fresh.Depth, fresh.Height);
        (piece.TopThickness, piece.LegThickness, piece.Fullness, piece.Split, piece.Open) =
            (fresh.TopThickness, fresh.LegThickness, fresh.Fullness, fresh.Split, fresh.Open);
        if (piece.Color == null)
            (piece.Color, piece.Material) = (fresh.Color, fresh.Material);
    }

    /// <summary>
    /// The current cue's rows for the piece: in / out / unchanged, and for a split curtain how far it opens and in how many
    /// seconds; a line says what tracks in from the cues before. Before the first cue: a hint.
    /// </summary>
    private void PieceCueRows(ShowBuilder builder, SceneryPiece piece)
    {
        var stack = _player.Stack;
        int index = _player.Step;
        if (stack == null || index < 0 || index >= stack.Steps.Count)
        {
            Header("Before the first cue");
            _params.AddChild(GreyHint("The piece is on stage before the first cue" + (piece.Opens ? $", {piece.Open:0} % open" : "") +
                                  ". Go to a cue (Playback) to take it out, bring it in" + (piece.Opens ? " or open the curtain" : "") +
                                  " in that cue."));
            return;
        }
        var step = stack.Steps[index];
        int cueId = step.CueId;
        string cue = step.CueNumberText;
        string id = piece.Id;
        var blocking = builder.Model.Blocking;
        var own = blocking.SceneryChangeOf(cueId, id);
        var earlier = stack.Steps.Take(index).ToList();
        var before = blocking.SceneryAfter(id, earlier.Select(s => s.CueId));
        string From(Func<SceneryChange, bool> sets)
        {
            for (int i = earlier.Count - 1; i >= 0; i--)
                if (blocking.SceneryChangeOf(earlier[i].CueId, id) is { } c && sets(c))
                    return $"cue {earlier[i].CueNumberText}";
            return "the start";
        }

        Header($"Cue {cue} {step.Text}".TrimEnd(), "What this cue does with the piece; what it doesn't say tracks from the cues before, like the lights");
        var inOut = new OptionButton { TooltipText = "In / out at this cue's GO (no fade); unchanged = as the cues before left it" };
        inOut.AddItem($"Unchanged ({(before.Visible ? "in" : "out")})");
        inOut.AddItem("In");
        inOut.AddItem("Out");
        inOut.Selected = own?.Visible == null ? 0 : own.Visible.Value ? 1 : 2;
        inOut.ItemSelected += i => SceneryEditing.EditCue(builder,
            $"{piece.Name} {(i == 1 ? "in" : i == 2 ? "out" : "unchanged")} in cue {cue}", cueId, id, c => c.Visible = i == 0 ? null : i == 1);
        Row("In this cue", inOut);

        if (piece.Opens)
        {
            var open = new SpinBox
            {
                MinValue = 0, MaxValue = 100, Step = 1, Suffix = "%", Value = own?.Open ?? before.Open,
                TooltipText = "How far the curtain opens in this cue (0 = closed, 100 = fully open); changing it makes this cue open / close it",
            };
            open.ValueChanged += v => SceneryEditing.EditCue(builder, $"{piece.Name} open in cue {cue}", cueId, id, c => c.Open = (float)v);
            Row("Open", open);
            var time = new SpinBox
            {
                MinValue = 0, MaxValue = 600, Step = 0.5, Suffix = "s", Value = own?.Seconds ?? Blocking.DefaultOpenTime,
                TooltipText = $"Seconds the curtain takes to open or close (default {Blocking.DefaultOpenTime:0} s)",
            };
            time.ValueChanged += v => SceneryEditing.EditCue(builder, $"{piece.Name} open time in cue {cue}", cueId, id, c =>
            {
                c.Time = Math.Abs(v - Blocking.DefaultOpenTime) < 0.05 ? null : (float)v;
                c.Open ??= (float)open.Value;
            });
            Row("Opens in", time);
        }

        string tracked = $"Before this cue: {(before.Visible ? "in" : "out")} (from {From(c => c.Visible != null)})" +
                         (piece.Opens ? $", {before.Open.ToString("0", CultureInfo.InvariantCulture)} % open (from {From(c => c.Open != null)})" : "") + ".";
        string mine = own == null || own.IsEmpty ? " This cue leaves it as it is (it tracks)."
            : " This cue" + (own.Visible == true ? " brings it in" : own.Visible == false ? " takes it out" : "") +
              (own.Visible != null && own.Open != null ? " and" : "") +
              (own.Open != null ? $" opens it to {own.Open.Value.ToString("0", CultureInfo.InvariantCulture)} % in {own.Seconds.ToString("0.#", CultureInfo.InvariantCulture)} s" : "") + ".";
        _params.AddChild(GreyHint(tracked + mine));
        if (own != null)
        {
            var clear = new Button { Text = "Clear this cue's change", TooltipText = "This cue leaves the piece as the cues before left it" };
            clear.Pressed += () => SceneryEditing.ClearCue(builder, $"Clear {piece.Name} in cue {cue}", cueId, id);
            _params.AddChild(clear);
        }
    }

    /// <summary>A grey wrapping explanation for the inspector.</summary>
    private static Label GreyHint(string text) => new()
    {
        Text = text,
        Modulate = new Color(0.7f, 0.7f, 0.7f),
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(180, 0),
    };
}
