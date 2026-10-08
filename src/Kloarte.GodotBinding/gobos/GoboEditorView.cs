using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SysVec2 = System.Numerics.Vector2;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Gobo editor: draw simple black / white gobos with straight lines and arcs. The square shows the rasterised gobo
/// (white = light) with the strokes' handles on top. Tools (key map, context "gobo"): select / move (drag handles or a
/// whole stroke; Delete removes it), line (drag from start to end), arc (drag from the centre: radius and start angle;
/// then click to set the end angle). Points snap to a 1/16 grid and angles to 15° (hold the "draw freely" key, default
/// Alt, to switch that off). Stroke width and cut (light) / steel (black) for new or selected strokes; the base is steel
/// or an open disc. Undo / redo. Saved as gobos/&lt;name&gt;.gobo.json + gobos/&lt;name&gt;.png; "Use on lamp" puts it in the
/// selected lamp's gobo holder, "Live" previews every change on that lamp. A tool in its own window (<see cref="ToolWindow"/>).
/// </summary>
public partial class GoboEditorView : ViewBase
{
    private const float Grid = 1f / 16f, AngleStep = 15f, PreviewSize = 256;
    private enum Tool { Select, Line, Arc }
    private enum Handle { None, A, B, Centre, Start, End, Body }

    private GoboDocument _doc = GoboDocument.Example();
    private readonly List<string> _undo = new(), _redo = new();
    private Tool _tool = Tool.Line;
    private int _selected = -1;
    private Handle _handle;
    private SysVec2 _pressAt, _hover;
    private bool _pressed, _arcPhase2;
    private GoboShape _draft;
    private ImageTexture _preview;
    private bool _previewDirty = true;

    private HBoxContainer _bar;
    private readonly Dictionary<Tool, Button> _toolButtons = new();
    private SpinBox _width;
    private OptionButton _paint, _base, _open;
    private LineEdit _name;
    private CheckBox _live;
    /// <summary>Messages (to the window's status bar).</summary>
    private readonly StatusText _status = new();

    /// <inheritdoc />
    public override string Title => "Gobo editor";

    /// <inheritdoc />
    public override string Context => "gobo";

    /// <summary>The drawing being edited.</summary>
    public GoboDocument Document => _doc;

    /// <summary>Builds the toolbar.</summary>
    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        var panel = new PanelContainer { Position = new Vector2(4, 4) };
        var rows = new VBoxContainer();
        panel.AddChild(rows);
        _bar = new HBoxContainer();
        rows.AddChild(_bar);
        var row2 = new HBoxContainer();
        rows.AddChild(row2);
        AddChild(panel);

        foreach (var (tool, text) in new[] { (Tool.Select, "Select"), (Tool.Line, "Line"), (Tool.Arc, "Arc") })
        {
            var b = new Button { Text = text, ToggleMode = true, FocusMode = FocusModeEnum.None };
            b.Pressed += () => SetTool(tool);
            _toolButtons[tool] = b;
            _bar.AddChild(b);
        }
        _bar.AddChild(new Label { Text = "  Width" });
        _width = new SpinBox { MinValue = 0.01, MaxValue = 1, Step = 0.01, Value = 0.06 };
        _width.ValueChanged += v => ChangeSelected(s => s.Width = (float)v);
        _bar.AddChild(_width);
        _paint = new OptionButton();
        _paint.AddItem("Cut (light)");
        _paint.AddItem("Steel (black)");
        _paint.ItemSelected += i => ChangeSelected(s => s.Cut = i == 0);
        _bar.AddChild(_paint);
        _bar.AddChild(new Label { Text = "  Base" });
        _base = new OptionButton();
        _base.AddItem("Steel");
        _base.AddItem("Open disc");
        _base.ItemSelected += i => Edit(() => _doc.OpenBase = i == 1);
        _bar.AddChild(_base);
        var undo = new Button { Text = "Undo" };
        undo.Pressed += Undo;
        var redo = new Button { Text = "Redo" };
        redo.Pressed += Redo;
        _bar.AddChild(undo);
        _bar.AddChild(redo);

        var newDoc = new Button { Text = "New" };
        newDoc.Pressed += () => Edit(() => _doc = new GoboDocument { Name = "gobo" + DateTime.Now.ToString("HHmmss") });
        _open = new OptionButton { TooltipText = "Open a saved gobo drawing" };
        _open.ItemSelected += i =>
        {
            if (i > 0)
                Edit(() => _doc = GoboLibrary.LoadDrawing(_open.GetItemText((int)i)));
        };
        _name = new LineEdit { CustomMinimumSize = new Vector2(140, 0), TooltipText = "Gobo name (file name)" };
        _name.TextChanged += t => _doc.Name = string.Concat(t.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-'));
        var save = new Button { Text = "Save" };
        save.Pressed += () => _status.Text = $"Saved {Save()}";
        var use = new Button { Text = "Use on lamp", TooltipText = "Save and put this gobo in the selected lamp's gobo holder" };
        use.Pressed += () => _status.Text = UseOnSelectedLamp();
        _live = new CheckBox { Text = "Live", TooltipText = "Show every change on the selected lamp" };
        foreach (var c in new Control[] { newDoc, _open, new Label { Text = "Name" }, _name, save, use, _live })
            row2.AddChild(c);

        SetTool(Tool.Line);
        RefreshControls();
    }

    /// <inheritdoc />
    protected override void OnRoleChanged()
    {
        if (_bar != null)
            _bar.GetParent().GetParent<Control>().Visible = !Compact;
        QueueRedraw();
    }

    private void SetTool(Tool tool)
    {
        _tool = tool;
        _draft = null;
        _arcPhase2 = false;
        foreach (var (t, b) in _toolButtons)
            b.SetPressedNoSignal(t == tool);
        QueueRedraw();
    }

    private void RefreshControls()
    {
        _name.Text = _doc.Name;
        _base.Select(_doc.OpenBase ? 1 : 0);
        _open.Clear();
        _open.AddItem("Open...");
        foreach (var n in GoboLibrary.Drawings())
            _open.AddItem(n);
        if (_selected >= 0 && _selected < _doc.Shapes.Count)
        {
            _width.SetValueNoSignal(_doc.Shapes[_selected].Width);
            _paint.Select(_doc.Shapes[_selected].Cut ? 0 : 1);
        }
    }

    // --- Editing with undo ---

    private void Edit(Action change)
    {
        _undo.Add(_doc.ToJson());
        _redo.Clear();
        change();
        Changed();
        RefreshControls();
    }

    private void ChangeSelected(Action<GoboShape> change)
    {
        if (_selected >= 0 && _selected < _doc.Shapes.Count)
            Edit(() => change(_doc.Shapes[_selected]));
    }

    /// <summary>Undoes the last change.</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        _redo.Add(_doc.ToJson());
        _doc = GoboDocument.FromJson(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        _selected = -1;
        Changed();
        RefreshControls();
    }

    /// <summary>Redoes the last undone change.</summary>
    public void Redo()
    {
        if (_redo.Count == 0)
            return;
        _undo.Add(_doc.ToJson());
        _doc = GoboDocument.FromJson(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        Changed();
        RefreshControls();
    }

    private void Changed()
    {
        _previewDirty = true;
        QueueRedraw();
        if (_live?.ButtonPressed == true && Selection.Fixture is { } f && Builder?.ModelOf(f) is { GoboSize: not null } or null)
            f.Gobo = GoboLibrary.Texture(_doc, 256);
    }

    /// <summary>Saves drawing + image; returns the image's res:// path.</summary>
    public string Save()
    {
        string path = GoboLibrary.Save(_doc);
        RefreshControls();
        return path;
    }

    /// <summary>Saves and puts the gobo in the selected lamp; returns a status message.</summary>
    public string UseOnSelectedLamp()
    {
        var f = Selection.Fixture;
        if (f == null)
            return "Select a lamp first (fixture list, plan or 3D view)";
        if (Builder?.ModelOf(f) is { } model && model.GoboSize == null)
            return $"{model.Name} has no gobo holder";
        f.Gobo = GoboLibrary.Load(Save());
        return $"{_doc.Name} in {f.Name} (save mechanics to keep it)";
    }

    /// <summary>Adds a stroke (as drawn with the mouse); used by tests too.</summary>
    public void Add(GoboShape shape) => Edit(() =>
    {
        _doc.Shapes.Add(shape);
        _selected = _doc.Shapes.Count - 1;
    });

    // --- Drawing ---

    private Rect2 Square()
    {
        // Room under the square for the hint line.
        float top = Compact ? 4 : 84, side = Mathf.Max(10, Mathf.Min(Size.X - 20, Size.Y - top - (Compact ? 10 : 22)));
        return new Rect2((Size.X - side) / 2f, top, side, side);
    }

    private Vector2 ToView(SysVec2 p)
    {
        var sq = Square();
        return sq.Position + new Vector2((p.X + 1f) / 2f * sq.Size.X, (1f - p.Y) / 2f * sq.Size.Y);
    }

    private SysVec2 ToGobo(Vector2 v)
    {
        var sq = Square();
        return new SysVec2((v.X - sq.Position.X) / sq.Size.X * 2f - 1f, 1f - (v.Y - sq.Position.Y) / sq.Size.Y * 2f);
    }

    private bool SnapOff => KeyMap.Held("gobo_free", GetViewport());

    private SysVec2 SnapPoint(SysVec2 p) =>
        SnapOff ? p : new SysVec2(MathF.Round(p.X / Grid) * Grid, MathF.Round(p.Y / Grid) * Grid);

    private float SnapAngle(float degrees) => SnapOff ? degrees : MathF.Round(degrees / AngleStep) * AngleStep;

    private static float AngleOf(SysVec2 d) => MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;

    /// <summary>Gobo image, grid, disc outline, strokes' handles and the stroke being drawn.</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.13f, 0.13f, 0.14f));
        if (_previewDirty || _preview == null)
        {
            _preview = GoboLibrary.Texture(_doc, (int)PreviewSize);
            _previewDirty = false;
        }
        var sq = Square();
        DrawTextureRect(_preview, sq, false);
        if (!Compact)
            for (int i = -8; i <= 8; i++)
            {
                var c = new Color(1f, 0.5f, 0.1f, i == 0 ? 0.35f : 0.12f);
                DrawLine(ToView(new SysVec2(i / 8f, -1)), ToView(new SysVec2(i / 8f, 1)), c);
                DrawLine(ToView(new SysVec2(-1, i / 8f)), ToView(new SysVec2(1, i / 8f)), c);
            }
        DrawArc(ToView(SysVec2.Zero), sq.Size.X / 2f, 0, Mathf.Tau, 96, new Color(1f, 0.5f, 0.1f, 0.6f), 1.5f);
        if (Compact)
            return;
        for (int i = 0; i < _doc.Shapes.Count; i++)
            DrawHandles(_doc.Shapes[i], i == _selected);
        if (_draft != null)
            DrawStroke(_draft, new Color(0.3f, 0.8f, 1f));
        DrawString(ThemeDB.FallbackFont, sq.Position + new Vector2(4, sq.Size.Y + 14),
            "top of the beam is up; snap 1/16 and 15° (hold Alt: free)", HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1, 0.5f));
    }

    private void DrawStroke(GoboShape s, Color c)
    {
        float w = Mathf.Max(1.5f, s.Width / 2f * Square().Size.X);
        if (s.Kind == "line")
            DrawLine(ToView(s.PointA), ToView(s.PointB), new Color(c, 0.5f), w);
        else
        {
            var pts = new List<Vector2>();
            int n = 48;
            for (int i = 0; i <= n; i++)
                pts.Add(ToView(s.ArcPoint(s.Start + (s.End - s.Start) * i / n)));
            DrawPolyline(pts.ToArray(), new Color(c, 0.5f), w);
        }
    }

    private void DrawHandles(GoboShape s, bool selected)
    {
        var c = selected ? new Color(1f, 0.45f, 0f) : new Color(0.3f, 0.7f, 1f, 0.7f);
        if (s.Kind == "line")
        {
            DrawLine(ToView(s.PointA), ToView(s.PointB), c, 1f);
            if (selected)
                foreach (var p in new[] { s.PointA, s.PointB })
                    DrawRect(new Rect2(ToView(p) - new Vector2(4, 4), new Vector2(8, 8)), c);
            return;
        }
        var pts = new List<Vector2>();
        for (int i = 0; i <= 48; i++)
            pts.Add(ToView(s.ArcPoint(s.Start + (s.End - s.Start) * i / 48f)));
        DrawPolyline(pts.ToArray(), c, 1f);
        if (selected)
        {
            DrawCircle(ToView(s.CentrePoint), 4, c);
            DrawRect(new Rect2(ToView(s.ArcPoint(s.Start)) - new Vector2(4, 4), new Vector2(8, 8)), c);
            DrawRect(new Rect2(ToView(s.ArcPoint(s.End)) - new Vector2(4, 4), new Vector2(8, 8)), c, false, 2f);
        }
    }

    // --- Mouse ---

    private float PixelsPerUnit => Square().Size.X / 2f;

    private (int Index, Handle Handle) HitTest(SysVec2 p)
    {
        float tol = 8f / PixelsPerUnit;
        if (_selected >= 0 && _selected < _doc.Shapes.Count)
        {
            var s = _doc.Shapes[_selected];
            if (s.Kind == "line")
            {
                if (SysVec2.Distance(p, s.PointA) < tol) return (_selected, Handle.A);
                if (SysVec2.Distance(p, s.PointB) < tol) return (_selected, Handle.B);
            }
            else
            {
                if (SysVec2.Distance(p, s.CentrePoint) < tol) return (_selected, Handle.Centre);
                if (SysVec2.Distance(p, s.ArcPoint(s.Start)) < tol) return (_selected, Handle.Start);
                if (SysVec2.Distance(p, s.ArcPoint(s.End)) < tol) return (_selected, Handle.End);
            }
        }
        for (int i = _doc.Shapes.Count - 1; i >= 0; i--)
            if (_doc.Shapes[i].Distance(p) < Mathf.Max(tol, _doc.Shapes[i].Width / 2f))
                return (i, Handle.Body);
        return (-1, Handle.None);
    }

    /// <summary>Drawing and editing strokes with the mouse.</summary>
    public override void _GuiInput(InputEvent e)
    {
        if (!IsMain)
            return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                _draft = null;
                _arcPhase2 = false;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
                GrabFocus();
                Press(ToGobo(down.Position));
                break;
            case InputEventMouseMotion move:
                Move(ToGobo(move.Position), (move.ButtonMask & MouseButtonMask.Left) != 0);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up:
                Release(ToGobo(up.Position));
                break;
            default:
                return;
        }
        AcceptEvent();
        QueueRedraw();
    }

    /// <summary>Mouse press at a gobo point (public for tests).</summary>
    public void Press(SysVec2 p)
    {
        _pressed = true;
        _pressAt = p;
        var sp = SnapPoint(p);
        switch (_tool)
        {
            case Tool.Line:
                _draft = GoboShape.Line(sp, sp, (float)_width.Value, _paint.Selected == 0);
                break;
            case Tool.Arc when _arcPhase2 && _draft != null:
                FinishArc();
                break;
            case Tool.Arc:
                _draft = GoboShape.Arc(sp, 0f, 0f, 360f, (float)_width.Value, _paint.Selected == 0);
                break;
            case Tool.Select:
                (_selected, _handle) = HitTest(p);
                if (_selected >= 0)
                {
                    _undo.Add(_doc.ToJson());
                    _redo.Clear();
                    RefreshControls();
                }
                break;
        }
    }

    /// <summary>Mouse move (dragging or not) at a gobo point.</summary>
    public void Move(SysVec2 p, bool dragging)
    {
        _hover = p;
        var sp = SnapPoint(p);
        if (_tool == Tool.Line && dragging && _draft != null)
            _draft.B = new[] { sp.X, sp.Y };
        else if (_tool == Tool.Arc && _draft != null && !_arcPhase2 && dragging)
        {
            var d = p - _draft.CentrePoint;
            float r = SnapOff ? d.Length() : MathF.Round(d.Length() / Grid) * Grid;
            _draft.Radius = r;
            _draft.Start = SnapAngle(AngleOf(d));
            _draft.End = _draft.Start + 360f;
        }
        else if (_tool == Tool.Arc && _draft != null && _arcPhase2)
        {
            float a = SnapAngle(AngleOf(p - _draft.CentrePoint));
            float sweep = ((a - _draft.Start) % 360f + 360f) % 360f;
            _draft.End = _draft.Start + (sweep < 1f ? 360f : sweep);
        }
        else if (_tool == Tool.Select && dragging && _selected >= 0 && _handle != Handle.None)
        {
            var s = _doc.Shapes[_selected];
            switch (_handle)
            {
                case Handle.A: s.A = new[] { sp.X, sp.Y }; break;
                case Handle.B: s.B = new[] { sp.X, sp.Y }; break;
                case Handle.Centre: s.Centre = new[] { sp.X, sp.Y }; break;
                case Handle.Start:
                {
                    float sweep = s.End - s.Start;
                    s.Start = SnapAngle(AngleOf(p - s.CentrePoint));
                    s.End = s.Start + sweep;
                    s.Radius = SnapOff ? (p - s.CentrePoint).Length() : MathF.Round((p - s.CentrePoint).Length() / Grid) * Grid;
                    break;
                }
                case Handle.End:
                {
                    float a = SnapAngle(AngleOf(p - s.CentrePoint));
                    float sweep = ((a - s.Start) % 360f + 360f) % 360f;
                    s.End = s.Start + (sweep < 1f ? 360f : sweep);
                    break;
                }
                case Handle.Body:
                {
                    var delta = SnapPoint(p) - SnapPoint(_pressAt);
                    _pressAt += delta;
                    s.A = new[] { s.A[0] + delta.X, s.A[1] + delta.Y };
                    s.B = new[] { s.B[0] + delta.X, s.B[1] + delta.Y };
                    s.Centre = new[] { s.Centre[0] + delta.X, s.Centre[1] + delta.Y };
                    break;
                }
            }
            Changed();
        }
        QueueRedraw();
    }

    /// <summary>Mouse release at a gobo point.</summary>
    public void Release(SysVec2 p)
    {
        if (!_pressed)
            return;
        _pressed = false;
        if (_tool == Tool.Line && _draft != null)
        {
            if (SysVec2.Distance(_draft.PointA, _draft.PointB) > 0.02f)
                Add(_draft);
            _draft = null;
        }
        else if (_tool == Tool.Arc && _draft != null && !_arcPhase2)
        {
            if (_draft.Radius > 0.02f)
                _arcPhase2 = true;   // now move to choose the end angle, click to finish
            else
                _draft = null;
        }
        _handle = Handle.None;
        QueueRedraw();
    }

    private void FinishArc()
    {
        var arc = _draft;
        _draft = null;
        _arcPhase2 = false;
        Add(arc);
    }

    /// <summary>Gobo keys: tools, delete, undo / redo.</summary>
    public override bool HandleKey(InputEvent e)
    {
        // Undo / redo of the drawing in the gobo editor's window (the show's undo in the main window).
        var vp = GetViewport();
        if (KeyMap.Pressed(e, "edit_undo", vp))
        {
            Undo();
            return true;
        }
        if (KeyMap.Pressed(e, "edit_redo", vp))
        {
            Redo();
            return true;
        }
        if (KeyMap.Pressed(e, "gobo_select", vp)) SetTool(Tool.Select);
        else if (KeyMap.Pressed(e, "gobo_line", vp)) SetTool(Tool.Line);
        else if (KeyMap.Pressed(e, "gobo_arc", vp)) SetTool(Tool.Arc);
        else if (KeyMap.Pressed(e, "gobo_delete", vp) && _selected >= 0 && _selected < _doc.Shapes.Count)
        {
            int index = _selected;
            Edit(() => _doc.Shapes.RemoveAt(index));
            _selected = -1;
        }
        else
            return false;
        return true;
    }
}
