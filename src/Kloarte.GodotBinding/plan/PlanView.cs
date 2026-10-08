using System.Collections.Generic;
using System.Linq;
using Godot;
using SysVec2 = System.Numerics.Vector2;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// 2D top view (lighting plan): stage, trusses with their height, floor stands, lamps as symbols in their gel colour
/// with head number, gel code and DMX address, and a legend of the lamp types and gels.
/// Mouse: wheel = zoom, middle drag = pan, click = select (Ctrl: add), drag on empty = box select, drag a lamp = move it
/// (snaps onto the nearest truss / stand; hold the "place freely" key, default Alt, to place it freely). The patch box
/// sets the selected lamp's head number and DMX address (written into the MagicQ patch). Floor stands of the house
/// types can be added and removed. "Save PDF" prints the plan (A3 / A4, fixed scale, legend, title block).
/// </summary>
public partial class PlanView : ViewBase
{
    private PlanData _data;
    private float _ppm = 30f;
    private Vector2 _offset;
    private bool _fitted, _seats = true, _allFocus, _legend = true;
    private int _refresh;

    private ProfileFixture _dragLamp;
    private Vector2 _pressAt;
    private SysVec2 _dragPlan;
    private PlanSupport _dragSnap;
    private bool _dragging, _boxing;
    private Vector2 _boxEnd;
    private string _placingStand;

    /// <summary>Catalogue model id of the lamps being added by clicking (null: not adding).</summary>
    private string _placingLamp;

    /// <summary>"Add fixture..." + the catalogue types (item metadata = model id).</summary>
    private OptionButton _lampPicker;

    private HFlowContainer _toolbar;
    private OptionButton _standPicker, _paper;
    private PanelContainer _patchBox;
    private Label _patchTitle;

    /// <summary>Messages (to the window's status bar).</summary>
    private readonly StatusText _status = new();
    private SpinBox _head;
    private LineEdit _dmx;
    private FileDialog _pdfDialog;

    /// <inheritdoc />
    public override string Title => "Plan";

    /// <inheritdoc />
    public override string Context => "plan";

    /// <inheritdoc />
    public override string ShowAction => "view_plan";

    private const float LegendWidth = 230f;

    /// <summary>The plan currently drawn (for tests).</summary>
    public PlanData Data => _data;

    /// <summary>Pixels per metre.</summary>
    public float Zoom => _ppm;

    /// <summary>Builds the toolbar, the patch box and the PDF dialog.</summary>
    public override void _Ready()
    {
        ClipContents = true;
        FocusMode = FocusModeEnum.Click;
        MouseFilter = MouseFilterEnum.Stop;

        _toolbar = new HFlowContainer { Name = "PlanToolbar" };   // wraps in a narrow view
        Toolbar = _toolbar;   // in the host's tool strip (UI review 29)
        var fit = new Button { Text = "Fit", TooltipText = "Fit the plan in the view" };
        fit.Pressed += Fit;
        var seats = new CheckBox { Text = "Seats", ButtonPressed = _seats };
        seats.Toggled += on => { _seats = on; QueueRedraw(); };
        var focus = new CheckBox { Text = "Focus lines", TooltipText = "Focus lines of all lamps (else only the selected)" };
        var legend = new CheckBox { Text = "Legend", ButtonPressed = _legend };
        legend.Toggled += on => { _legend = on; QueueRedraw(); };
        focus.Toggled += on => { _allFocus = on; QueueRedraw(); };
        _standPicker = new OptionButton { TooltipText = "Add a floor stand: choose the type, then click in the plan" };
        _standPicker.ItemSelected += i =>
        {
            _placingStand = i == 0 ? null : _standPicker.GetItemText((int)i);
            _status.Text = _placingStand == null ? "" : $"Click in the plan to place a {_placingStand}";
        };
        var removeStand = new Button { Text = "Remove stand" };
        removeStand.Pressed += RemoveSelectedStand;
        _lampPicker = new OptionButton();
        KeyMap.Tooltip(_lampPicker, "Add fixtures: choose the type, then click near a truss or stand; right click or", "deselect");
        _lampPicker.ItemSelected += i =>
        {
            _placingLamp = i == 0 ? null : _lampPicker.GetItemMetadata((int)i).AsString();
            _status.Text = _placingLamp == null ? "" : $"Click near a truss or stand to add a {_lampPicker.GetItemText((int)i)}";
        };
        var removeLamp = new Button { Text = "Remove lamp", TooltipText = "Remove the selected lamps from the show" };
        removeLamp.Pressed += RemoveSelectedLamps;
        _paper = new OptionButton();
        _paper.AddItem("A3");
        _paper.AddItem("A4");
        _paper.TooltipText = "Paper of the PDF (default in File > Preferences)";
        _paper.Select(Preferences.Current.PlanPaper == "A4" ? 1 : 0);
        var pdf = new Button { Text = "Save PDF...", TooltipText = "Print the plan with legend and title block" };
        pdf.Pressed += () =>
        {
            _pdfDialog.CurrentFile = System.IO.Path.GetFileNameWithoutExtension(Builder?.ShowFile ?? "plan") + "_plan.pdf";
            NativeFileDialogs.Popup(_pdfDialog, new Vector2I(900, 600));
        };
        foreach (var c in new Control[] { fit, seats, focus, legend, _lampPicker, removeLamp, _standPicker, removeStand, _paper, pdf })
            _toolbar.AddChild(c);

        _patchBox = new PanelContainer();
        var box = new VBoxContainer();
        _patchTitle = new Label { ClipText = true, CustomMinimumSize = new Vector2(360, 0) };
        var row = new HBoxContainer();
        _head = new SpinBox { MinValue = 1, MaxValue = 99999, Step = 1, TooltipText = "MagicQ head number" };
        _dmx = new LineEdit { CustomMinimumSize = new Vector2(80, 0), TooltipText = "DMX address: universe-channel (1-150) or absolute" };
        var apply = new Button { Text = "Patch" };
        apply.Pressed += ApplyPatch;
        _dmx.TextSubmitted += _ => ApplyPatch();
        row.AddChild(new Label { Text = "Head" });
        row.AddChild(_head);
        row.AddChild(new Label { Text = "DMX" });
        row.AddChild(_dmx);
        row.AddChild(apply);
        box.AddChild(_patchTitle);
        box.AddChild(row);
        _patchBox.AddChild(box);
        AddChild(_patchBox);

        _pdfDialog = new FileDialog
        {
            Title = "Save the plan as PDF",
            ModeOverridesTitle = false,   // keep the title (else "Open a File")
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.pdf ; PDF" },
            UseNativeDialog = true,
        };
        _pdfDialog.FileSelected += path => _status.Text = SavePdf(path);
        AddChild(_pdfDialog);

        Selection.Changed += OnSelection;
        TreeExiting += () => Selection.Changed -= OnSelection;
        Resized += () => { PlaceOverlays(); QueueRedraw(); };
        OnRoleChanged();
    }

    /// <summary>Writes the plan PDF; returns a status message.</summary>
    public string SavePdf(string path)
    {
        if (Builder?.Model == null)
            return "No show loaded";
        int scale = PlanPdf.Export(Builder.PlanData(), path, a3: _paper.Selected == 0, seats: _seats);
        return $"Saved {System.IO.Path.GetFileName(path)} (1:{scale})";
    }

    /// <inheritdoc />
    protected override void OnRoleChanged()
    {
        if (_toolbar == null)
            return;
        _patchBox.Visible = !Compact && Selection.Fixture != null;
        if (Compact)
            _fitted = false;
        Refresh();
    }

    /// <inheritdoc />
    protected override void OnShowChanged()
    {
        _fitted = false;
        if (_standPicker != null)
        {
            _standPicker.Clear();
            _standPicker.AddItem("Add stand...");
            foreach (var t in Builder?.Venue?.Stands ?? new())
                _standPicker.AddItem(t.Name);
        }
        if (_lampPicker != null)
        {
            _lampPicker.Clear();
            _lampPicker.AddItem("Add fixture...");
            _lampPicker.SetItemMetadata(0, "");
            foreach (var m in Builder?.Catalog?.Models ?? new())
            {
                _lampPicker.AddItem(m.Name);
                _lampPicker.SetItemMetadata(_lampPicker.ItemCount - 1, m.Id);
            }
            StopAddingLamps();
        }
        Refresh();
    }

    private void OnSelection()
    {
        if (Selection.Fixtures.Count > 0 && Builder != null)
            Builder.SelectedSupport = 0;   // one thing selected at a time: Delete acts on the lamps
        Refresh();
        var f = Selection.Fixture;
        if (_patchBox == null)
            return;
        _patchBox.Visible = !Compact && f != null;
        if (f != null && Builder?.VisOf(f) is { } vis)
        {
            _patchTitle.Text = $"{f.Name}  (head {vis.HeadNumber}, DMX {DmxAddress.Format(vis.Address)}, " +
                               $"{Builder.LampsOnHead(vis.HeadNumber)} lamp(s) on this head)";
            _head.SetValueNoSignal(vis.HeadNumber);
            _dmx.Text = DmxAddress.Format(vis.Address);
        }
        PlaceOverlays();
    }

    private void PlaceOverlays()
    {
        if (_patchBox == null)
            return;
        _patchBox.ResetSize();
        _patchBox.Position = new Vector2(6, Size.Y - _patchBox.Size.Y - 6);
    }

    /// <summary>Takes a new snapshot of the plan and redraws.</summary>
    public void Refresh()
    {
        if (Builder?.Model == null || !IsInstanceValid(Builder))
        {
            _data = null;
            QueueRedraw();
            return;
        }
        _data = Builder.PlanData();
        if (!_fitted && Size.X > 10)
            Fit();
        QueueRedraw();
    }

    /// <summary>Fits the plan (stage, rig, and the seats when shown) into the view.</summary>
    public void Fit()
    {
        if (_data == null || Size.X < 10)
            return;
        var (min, max) = PlanDrawing.Bounds(_data, seats: false);
        float legend = Compact || !_legend ? 0 : LegendWidth + 10f;
        float top = Compact ? 0 : 40f;
        _ppm = Mathf.Min((Size.X - legend - 20) / (max.X - min.X), (Size.Y - top - 20) / (max.Y - min.Y));
        var size = new Vector2(max.X - min.X, max.Y - min.Y) * _ppm;
        _offset = new Vector2((Size.X - legend - size.X) / 2f, top + (Size.Y - top - size.Y) / 2f) - new Vector2(min.X, min.Y) * _ppm;
        _fitted = true;
        QueueRedraw();
    }

    /// <summary>Keeps the snapshot fresh (lamps can be moved elsewhere too).</summary>
    public override void _Process(double delta)
    {
        if (Visible && !_dragging && ++_refresh % 15 == 0)
            Refresh();
    }

    private SysVec2 ToPlan(Vector2 p) => new((p.X - _offset.X) / _ppm, (p.Y - _offset.Y) / _ppm);

    /// <summary>View position (pixels) of a plan point.</summary>
    public Vector2 ToView(SysVec2 p) => _offset + new Vector2(p.X, p.Y) * _ppm;

    /// <summary>Draws the plan, the drag preview, the selection box and the legend.</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(PlanDrawing.Paper.R, PlanDrawing.Paper.G, PlanDrawing.Paper.B));
        if (_data == null)
            return;
        var canvas = new GodotPlanCanvas(this, _offset, _ppm);
        PlanDrawing.Draw(canvas, _data, new PlanStyle
        {
            LabelSize = Compact ? 0 : Mathf.Clamp(_ppm * 0.28f, 9f, 14f),
            AllFocusLines = _allFocus,
            Seats = _seats,
        });
        if (_dragging && _dragLamp != null)
        {
            var p = ToView(_dragPlan);
            DrawCircle(p, 6f, new Color(1, 0.45f, 0, 0.8f));
            if (_dragSnap != null)
                DrawLine(ToView(_dragSnap.Start), ToView(_dragSnap.End), new Color(1, 0.45f, 0), 4f);
        }
        if (_boxing)
        {
            var r = new Rect2(_pressAt, _boxEnd - _pressAt).Abs();
            DrawRect(r, new Color(1, 0.45f, 0, 0.15f));
            DrawRect(r, new Color(1, 0.45f, 0), false, 1.5f);
        }
        if (!Compact && _legend)
        {
            var legendCanvas = new GodotPlanCanvas(this, Vector2.Zero, 1f);
            var origin = new SysVec2(Size.X - LegendWidth, 50f);
            DrawRect(new Rect2(origin.X - 8, origin.Y - 4, LegendWidth + 8, Size.Y - origin.Y), new Color(1, 1, 1, 0.9f));
            PlanDrawing.DrawLegend(legendCanvas, _data, origin, 11f, LegendWidth - 8f);
        }
    }

    private PlanLamp LampAt(Vector2 viewPos)
    {
        if (_data == null)
            return null;
        var p = ToPlan(viewPos);
        return _data.Lamps.Select(l => (l, d: SysVec2.Distance(l.Position, p)))
            .Where(x => x.d < Mathf.Max(0.35f, 10f / _ppm)).OrderBy(x => x.d).Select(x => x.l).FirstOrDefault();
    }

    private PlanSupport StandAt(Vector2 viewPos)
    {
        var p = ToPlan(viewPos);
        return _data?.Supports.Where(s => s.IsStand && SysVec2.Distance(s.Start, p) < Mathf.Max(0.25f, 8f / _ppm)).FirstOrDefault();
    }

    /// <summary>Zoom, pan, select, drag lamps, box select, place stands.</summary>
    public override void _GuiInput(InputEvent e)
    {
        if (!IsMain || _data == null)
            return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
            {
                float k = wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1f / 1.15f;
                _offset = wheel.Position + (_offset - wheel.Position) * k;
                _ppm *= k;
                QueueRedraw();
                AcceptEvent();
                break;
            }
            case InputEventMouseMotion mm when (mm.ButtonMask & MouseButtonMask.Middle) != 0:
                _offset += mm.Relative;
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when _placingLamp != null:
                StopAddingLamps();
                _status.Text = "";
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } right
                when LampAt(right.Position) is { } menuLamp && Builder?.FixtureOf(menuLamp.VisId) is { } fixture:
                LampMenuRequests.Request(fixture, GetViewport().GetMousePosition());   // the lamp menu (FixturePanel)
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                GrabFocus();
                _pressAt = down.Position;
                if (_placingStand != null)
                {
                    PlaceStand(ToPlan(down.Position));
                    break;
                }
                if (_placingLamp != null)
                {
                    PlaceLamp(ToPlan(down.Position));
                    AcceptEvent();
                    break;
                }
                var lamp = LampAt(down.Position);
                if (lamp != null)
                {
                    _dragLamp = Builder.FixtureOf(lamp.VisId);
                    if (down.CtrlPressed)
                        Selection.Select(Selection.Fixtures.Append(_dragLamp));
                    else if (!Selection.Fixtures.Contains(_dragLamp))
                        Selection.Select(_dragLamp);
                }
                else if (StandAt(down.Position) is { } stand)
                {
                    Selection.Clear();   // Delete then removes the stand, not lamps
                    Builder.SelectedSupport = stand.Id;
                    Refresh();
                }
                else
                {
                    _boxing = true;
                    _boxEnd = down.Position;
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion move when (move.ButtonMask & MouseButtonMask.Left) != 0:
                if (_boxing)
                    _boxEnd = move.Position;
                else if (_dragLamp != null && (_dragging || move.Position.DistanceTo(_pressAt) > 4f))
                {
                    _dragging = true;
                    _dragPlan = ToPlan(move.Position);
                    _dragSnap = KeyMap.Held("plan_free", GetTree().Root) ? null : NearestSupport(_dragPlan);
                    if (_dragSnap != null)
                        _dragPlan = SnapPreview(_dragSnap, _dragPlan);
                }
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up:
                if (_boxing)
                {
                    var a = ToPlan(_pressAt);
                    var b = ToPlan(up.Position);
                    var min = SysVec2.Min(a, b);
                    var max = SysVec2.Max(a, b);
                    var inBox = _data.Lamps.Where(l => l.Position.X >= min.X && l.Position.X <= max.X && l.Position.Y >= min.Y && l.Position.Y <= max.Y)
                        .Select(l => Builder.FixtureOf(l.VisId)).Where(f => f != null).ToList();
                    if (up.Position.DistanceTo(_pressAt) < 4f)
                    {
                        Selection.Clear();
                        Builder.SelectedSupport = 0;
                    }
                    else
                        Selection.Select(up.CtrlPressed ? Selection.Fixtures.Concat(inBox) : inBox);
                }
                else if (_dragging && _dragLamp != null)
                {
                    var snapped = Builder.MoveLampInPlan(_dragLamp, ToPlan(up.Position), snap: !KeyMap.Held("plan_free", GetTree().Root));
                    _status.Text = snapped != null ? $"On {snapped.Name}" : "Placed freely (not attached)";
                    Selection.Touch();
                }
                _dragging = _boxing = false;
                _dragLamp = null;
                _dragSnap = null;
                Refresh();
                AcceptEvent();
                break;
        }
    }

    private PlanSupport NearestSupport(SysVec2 p) =>
        _data.Supports.Select(s => (s, d: SysVec2.Distance(SnapPreview(s, p), p))).Where(x => x.d <= ShowBuilder.SnapDistance)
            .OrderBy(x => x.d).Select(x => x.s).FirstOrDefault();

    private static SysVec2 SnapPreview(PlanSupport s, SysVec2 p)
    {
        if (s.IsStand)
            return s.Start;
        var d = s.End - s.Start;
        float t = d.LengthSquared() < 1e-6f ? 0 : System.Math.Clamp(SysVec2.Dot(p - s.Start, d) / d.LengthSquared(), 0f, 1f);
        return s.Start + d * t;
    }

    private void PlaceStand(SysVec2 at)
    {
        var type = Builder.Venue?.Stands.FirstOrDefault(t => t.Name == _placingStand);
        float height = type == null ? 2f : Mathf.Clamp(2.5f, type.Min, type.Max);
        var stand = Builder.AddStand(_placingStand, at, height);
        Builder.SelectedSupport = stand.Id;
        _status.Text = $"Added {stand.Name} ({height:0.00} m); drag lamps onto it";
        _placingStand = null;
        _standPicker.Select(0);
        Refresh();
    }

    /// <summary>Adds a lamp of the chosen type at a plan position (on the nearest truss / stand) and selects it.</summary>
    private void PlaceLamp(SysVec2 at)
    {
        try
        {
            var f = Builder.AddLampInPlan(_placingLamp, at);
            var vis = Builder.VisOf(f);
            var support = Builder.Rig.Find(vis.ParentId);
            _status.Text = $"Added {Builder.ModelOf(f)?.Name} on {support?.Name ?? "no support"}: head {vis.HeadNumber}, " +
                           $"DMX {DmxAddress.Format(vis.Address)} (click to add more; {KeyMap.KeysText("deselect")} stops)";
            Selection.Select(f);
        }
        catch (System.Exception e) when (e is ShowEditException or System.InvalidOperationException or System.ArgumentException)
        {
            _status.Text = e.Message;
        }
        Refresh();
    }

    /// <summary>What the next click in the plan places ("a C103", "a floor stand"), or null (the panel's mode chip).</summary>
    internal string Placing => _placingLamp != null
        ? _lampPicker?.Selected > 0 ? _lampPicker.GetItemText(_lampPicker.Selected) : _placingLamp
        : _placingStand;

    /// <summary>Leaves the add-fixture and place-stand modes (the mode chip's ✕, Esc).</summary>
    internal void StopPlacing()
    {
        StopAddingLamps();
        _placingStand = null;
        _standPicker?.Select(0);
        _status.Text = "";
    }

    /// <summary>Leaves the add-fixture mode.</summary>
    private void StopAddingLamps()
    {
        _placingLamp = null;
        if (_lampPicker?.ItemCount > 0)
            _lampPicker.Select(0);
    }

    /// <summary>Removes the selected lamps from the show.</summary>
    private void RemoveSelectedLamps()
    {
        var lamps = Selection.Fixtures.Where(IsInstanceValid).ToList();
        if (Builder == null || lamps.Count == 0)
        {
            _status.Text = "Select the lamps to remove first";
            return;
        }
        // As a technician names them: type and head (not the node names with their hex ids, UI review 22).
        Selection.Clear();
        _status.Text = string.Join("; ", lamps.Select(Builder.RemoveLamp)) + " " + KeyMap.Hint("Undo", "edit_undo");
        Refresh();
    }

    /// <summary>Starts adding lamps of a catalogue type, as if chosen in the toolbar (each click adds one).</summary>
    public void BeginLamp(string modelId) => _placingLamp = modelId;

    private void RemoveSelectedStand()
    {
        var s = Builder?.Rig?.Find(Builder.SelectedSupport);
        if (s == null || !s.IsStand)
        {
            _status.Text = "Select a floor stand first (click it)";
            return;
        }
        Builder.RemoveSupport(s.Id);
        Builder.SelectedSupport = 0;
        _status.Text = $"Removed {s.Name} {KeyMap.Hint("Undo", "edit_undo")}";
        Refresh();
    }

    /// <summary>Sets head and DMX of the selected lamp as if typed in the patch box; returns the status text.</summary>
    public string Patch(int head, string dmx)
    {
        _head.SetValueNoSignal(head);
        _dmx.Text = dmx;
        ApplyPatch();
        return _status.Text;
    }

    /// <summary>Starts placing a stand of a type, as if chosen in the toolbar (the next click places it).</summary>
    public void BeginStand(string type) => _placingStand = type;

    private void ApplyPatch()
    {
        var f = Selection.Fixture;
        var vis = Builder?.VisOf(f);
        if (vis == null)
            return;
        var address = DmxAddress.Parse(_dmx.Text);
        if (address == null)
        {
            _status.Text = $"'{_dmx.Text}' is not a DMX address (e.g. 1-150 or 150)";
            return;
        }
        string error = Builder.ApplyLampPatch(f, (int)_head.Value, address.Value, out string done);
        _status.Text = error ?? done + " " + KeyMap.Hint("Save", "file_save");
        Selection.Touch();
    }

    /// <summary>Plan keys: fit, stop adding (Esc), delete the selected stand or else the selected lamps.</summary>
    public override bool HandleKey(InputEvent e)
    {
        var vp = GetTree().Root;
        if (KeyMap.Pressed(e, "plan_fit", vp))
            Fit();
        else if ((_placingLamp != null || _placingStand != null) && KeyMap.Pressed(e, "deselect", vp))
            StopPlacing();
        else if (KeyMap.Pressed(e, "plan_delete", vp))
        {
            if (Builder?.Rig?.Find(Builder.SelectedSupport) is { IsStand: true })
                RemoveSelectedStand();
            else
                RemoveSelectedLamps();
        }
        else
            return false;
        return true;
    }
}
