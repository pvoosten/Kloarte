using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Schematic side section of the rig: depth (upstage left, audience right) against height (0..9 m). Every truss / pipe
/// and floor stand has a vertical slider on the same height scale, so its knob is the truss: drag it (or type the
/// height below) to fly the truss. Limits from the venue (light bars 7.5 m, scenery bars 8 m, portal bridge 6.1 m;
/// stands their type's range). The lamps on it hang below it and move along, re-aimed at their unchanged focus points
/// (same beam angle, so pools grow when a truss goes up). Trusses at the same depth are placed side by side.
/// </summary>
public partial class TrussHeightView : ViewBase
{
    private const float MaxShown = 9f, MinTruss = 1f, MaxColumn = 44f, MinColumn = 22f, SpinColumn = 40f;
    private const float Top = 70f, Bottom = 64f;

    private readonly Dictionary<int, (VSlider Slider, SpinBox Spin, float X)> _columns = new();
    private List<RigSupport> _supports = new();
    private string _signature = "";
    private bool _syncing;
    private int _frame;
    private float _column = MaxColumn;
    private int _selected;
    private Label _selectedName;
    private SpinBox _selectedSpin;

    /// <inheritdoc />
    public override string Title => "Truss heights";

    /// <inheritdoc />
    public override string ShowAction => "view_heights";

    /// <summary>Sets up the view.</summary>
    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        Resized += Rebuild;
        // Exact height of the selected truss (click a slider to select it).
        var bar = new HBoxContainer { Position = new Vector2(60, 4), Name = "Selected" };
        _selectedName = new Label { Text = "Click a truss" };
        _selectedSpin = new SpinBox { Step = 0.05, Suffix = "m", Editable = false, CustomMinimumSize = new Vector2(110, 0) };
        _selectedSpin.ValueChanged += v =>
        {
            if (Builder?.Rig?.Find(_selected) is { } s)
            {
                var (min, max) = Limits(s);
                SetHeight(_selected, (float)v, min, max);
            }
        };
        bar.AddChild(_selectedName);
        bar.AddChild(_selectedSpin);
        AddChild(bar);
    }

    /// <inheritdoc />
    protected override void OnShowChanged() => Rebuild();

    /// <inheritdoc />
    protected override void OnRoleChanged()
    {
        foreach (var (slider, spin, _) in _columns.Values)
        {
            slider.Visible = !Compact;
            spin.Visible = !Compact && _column >= SpinColumn;
        }
        if (GetNodeOrNull<Control>("Selected") is { } bar)
            bar.Visible = !Compact;
        QueueRedraw();
    }

    private float Y(float height) => Top + (Size.Y - Top - Bottom) * (1f - height / MaxShown);

    /// <summary>Height limits of a support: venue hoist plan for trusses, the stand type's range for stands.</summary>
    public (float Min, float Max) Limits(RigSupport s)
    {
        var venue = Builder?.Venue;
        if (s.IsStand)
        {
            var type = venue?.Stands.FirstOrDefault(t => s.Name.Contains(t.Name));
            return type == null ? (0.3f, 4f) : (Mathf.Max(0.1f, type.Min), Mathf.Max(type.Max, type.Min + 0.1f));
        }
        return (MinTruss, venue?.MaxHeight(s.Name) ?? 8f);
    }

    /// <summary>Recreates the columns when the set of supports changed (or the view was resized).</summary>
    private void Rebuild()
    {
        foreach (var (slider, spin, _) in _columns.Values)
        {
            slider.QueueFree();
            spin.QueueFree();
        }
        _columns.Clear();
        var rig = Builder?.Model == null || !IsInstanceValid(Builder) ? null : Builder.Rig;
        _supports = rig == null ? new() : rig.Trusses.OrderBy(t => t.Start.Z).Concat(rig.Stands.OrderBy(s => s.Start.X)).ToList();
        _signature = string.Join(",", _supports.Select(s => s.Id));
        if (_supports.Count == 0 || Size.X < 50)
        {
            QueueRedraw();
            return;
        }
        // Column x: proportional to the depth, but at least a column width apart (schematic).
        var trusses = _supports.Where(s => !s.IsStand).ToList();
        var stands = _supports.Where(s => s.IsStand).ToList();
        _column = Mathf.Clamp((Size.X - 100f - (stands.Count > 0 ? 30f : 0f)) / Mathf.Max(1, _supports.Count), MinColumn, MaxColumn);
        float standsWidth = stands.Count * _column + (stands.Count > 0 ? 30f : 0f);
        float left = 70f, right = Size.X - 20f - standsWidth;
        float zMin = trusses.Count > 0 ? trusses.Min(t => t.Start.Z) : 0, zMax = trusses.Count > 0 ? trusses.Max(t => t.Start.Z) : 1;
        var xs = new List<float>();
        float x = left - _column;
        foreach (var t in trusses)
        {
            float proportional = left + (right - left - _column) * (zMax > zMin ? (t.Start.Z - zMin) / (zMax - zMin) : 0.5f);
            x = Mathf.Max(proportional, x + _column);
            xs.Add(x);
        }
        // Minimum spacing can push the last columns past the right edge: squeeze everything back in.
        float last = xs.Count > 0 ? xs[^1] : left;
        for (int i = 0; i < trusses.Count; i++)
            AddColumn(trusses[i], last > right ? left + (xs[i] - left) * (right - left) / (last - left) : xs[i]);
        x = Size.X - standsWidth + 10f;
        foreach (var s in stands)
        {
            AddColumn(s, x);
            x += _column;
        }
        OnRoleChanged();
        QueueRedraw();
    }

    private void AddColumn(RigSupport s, float x)
    {
        var (min, max) = Limits(s);
        float h = s.Height - FloorY;
        var slider = new VSlider
        {
            MinValue = 0,
            MaxValue = MaxShown,
            Step = 0.05,
            Value = Mathf.Clamp(h, min, max),
            Position = new Vector2(x - 10f, Y(MaxShown)),
            Size = new Vector2(20f, Y(0) - Y(MaxShown)),
            TooltipText = $"{s.Name}: {min:0.0}-{max:0.0} m",
        };
        var spin = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Step = 0.05,
            Value = Mathf.Clamp(h, min, max),
            Suffix = "m",
            Position = new Vector2(x - _column / 2f, Size.Y - Bottom + 26f),
            CustomMinimumSize = new Vector2(_column - 2f, 0),
            Size = new Vector2(_column - 2f, 24f),
            TooltipText = s.Name,
        };
        spin.GetLineEdit().AddThemeFontSizeOverride("font_size", 11);
        slider.ValueChanged += v => SetHeight(s.Id, (float)v, min, max);
        slider.FocusEntered += () => SelectSupport(s.Id);
        slider.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true })
                SelectSupport(s.Id);
        };
        spin.ValueChanged += v => SetHeight(s.Id, (float)v, min, max);
        AddChild(slider);
        AddChild(spin);
        _columns[s.Id] = (slider, spin, x);
    }

    /// <summary>Selects a truss: its exact height in the field at the top, highlighted in the plan.</summary>
    private void SelectSupport(int id)
    {
        _selected = id;
        if (Builder != null)
            Builder.SelectedSupport = id;
        Sync();
        QueueRedraw();
    }

    /// <summary>MagicVis y of the stage floor (ShowBuilder's origin).</summary>
    private float FloorY => Builder == null ? 0f : Builder.FromStage(Vector3.Zero).Y;

    /// <summary>Flies a support (stage height, clamped to its limits) and updates both controls.</summary>
    public void SetHeight(int supportId, float height, float min, float max)
    {
        if (_syncing || Builder == null)
            return;
        height = Mathf.Clamp(height, min, max);
        Builder.SetSupportHeight(supportId, height);
        Sync();
        Selection.Touch();
        QueueRedraw();
    }

    /// <summary>Controls follow the model (heights changed elsewhere, e.g. a stand added in the plan).</summary>
    private void Sync()
    {
        _syncing = true;
        var rig = Builder?.Rig;
        if (rig?.Find(_selected) is { } sel)
        {
            var (min, max) = Limits(sel);
            _selectedName.Text = sel.Name;
            _selectedSpin.Editable = true;
            _selectedSpin.MinValue = min;
            _selectedSpin.MaxValue = max;
            _selectedSpin.SetValueNoSignal(sel.Height - FloorY);
        }
        foreach (var (id, (slider, spin, _)) in _columns)
            if (rig?.Find(id) is { } s)
            {
                float h = s.Height - FloorY;
                slider.SetValueNoSignal(h);
                spin.SetValueNoSignal(h);
            }
        _syncing = false;
    }

    /// <summary>Picks up rig changes made elsewhere.</summary>
    public override void _Process(double delta)
    {
        if (!Visible || Builder?.Model == null || ++_frame % 20 != 0)
            return;
        var rig = Builder.Rig;
        if (string.Join(",", rig.Trusses.OrderBy(t => t.Start.Z).Concat(rig.Stands.OrderBy(s => s.Start.X)).Select(s => s.Id)) != _signature)
            Rebuild();
        else
            Sync();
        QueueRedraw();
    }

    /// <summary>Grid, axis, truss markers, names and the lamps hanging below them.</summary>
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.12f, 0.12f, 0.13f));
        var font = ThemeDB.FallbackFont;
        int fs = Compact ? 9 : 12;
        for (int m = 0; m <= (int)MaxShown; m++)
        {
            float y = Y(m);
            DrawLine(new Vector2(50, y), new Vector2(Size.X - 10, y), new Color(1, 1, 1, m == 0 ? 0.5f : 0.1f), 1f);
            DrawString(font, new Vector2(8, y + 4), $"{m} m", HorizontalAlignment.Left, -1, fs, new Color(1, 1, 1, 0.6f));
        }
        if (!Compact)
        {
            DrawString(font, new Vector2(60, Size.Y - 8), "upstage", HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.5f));
            DrawString(font, new Vector2(Size.X - 220, Size.Y - 8), "audience  /  floor stands", HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.5f));
        }
        var rig = Builder?.Model == null ? null : Builder.Rig;
        if (rig == null)
            return;
        float floorY = FloorY;
        foreach (var s in _supports)
        {
            if (!_columns.TryGetValue(s.Id, out var col) || rig.Find(s.Id) is not { } live)
                continue;
            float h = live.Height - floorY;
            var lamps = rig.LampsOn(s.Id).ToList();
            var colour = s.Id == _selected ? new Color(1f, 0.45f, 0f) : lamps.Count > 0 ? new Color(1f, 0.75f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
            var (_, max) = Limits(live);
            DrawLine(new Vector2(col.X - 14, Y(max)), new Vector2(col.X + 14, Y(max)), new Color(1, 0.3f, 0.3f, 0.7f), 2f);
            if (live.IsStand)
                DrawLine(new Vector2(col.X, Y(0)), new Vector2(col.X, Y(h)), colour, 3f);
            else
                DrawRect(new Rect2(col.X - 12, Y(h) - 4, 24, 8), colour);
            // Lamps hanging below (or standing on top), at their real drop.
            for (int i = 0; i < lamps.Count && i < 8; i++)
            {
                float ly = lamps[i].Y - floorY;
                var p = new Vector2(col.X - 10 + (i % 4) * 6.5f, Y(ly));
                DrawCircle(p, 3f, new Color(0.9f, 0.9f, 0.9f));
            }
            if (lamps.Count > 8)
                DrawString(font, new Vector2(col.X - 8, Y(h) + 22), $"+{lamps.Count - 8}", HorizontalAlignment.Left, -1, 9, colour);
            if (_column < SpinColumn && !Compact)
                DrawString(font, new Vector2(col.X - 10, Size.Y - Bottom + 40f), h.ToString("0.0"), HorizontalAlignment.Left, -1, 10, colour);
            // Name above the column (rotated when columns are narrow).
            DrawSetTransform(new Vector2(col.X + 4, Top - 6), -Mathf.Pi / 2.6f, Vector2.One);
            DrawString(font, Vector2.Zero, s.Name, HorizontalAlignment.Left, -1, fs, colour);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }
}
