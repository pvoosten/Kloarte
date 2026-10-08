using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The stage seen from a seat in the auditorium (venue JSON: rows, seats, rake, eye height). Choose a named
/// viewpoint (e.g. "Regie") or a row and seat; keys (key map): previous / next seat, row closer / further.
/// From a seat you can only look around (right mouse button) and zoom (wheel), not walk.
/// </summary>
public partial class AudienceView : View3D
{
    private HBoxContainer _bar;
    private OptionButton _viewpoints, _rows;
    private SpinBox _seat;
    private Label _where;
    private float _yaw, _pitch;
    private bool _looking;

    /// <summary>Row and seat number of the current seat.</summary>
    public string Row { get; private set; } = "N";

    /// <summary>Seat number in the row.</summary>
    public int SeatNumber { get; private set; } = 20;

    /// <summary>Creates the audience view with its own camera.</summary>
    public AudienceView() : base("Audience", new Camera3D { Fov = SeatFov }) { }

    /// <inheritdoc />
    public override string ShowAction => "view_audience";

    /// <inheritdoc />
    public override string Context => KeyMap.Global;

    /// <inheritdoc />
    public override bool OwnCamera => true;

    /// <summary>Adds the seat picker on top of the view.</summary>
    public override void _Ready()
    {
        base._Ready();
        var bar = _bar = new HBoxContainer { Name = "SeatPicker" };
        Toolbar = bar;   // in the host's tool strip (UI review 29); Present mode hides the strip
        _viewpoints = new OptionButton { TooltipText = "Named seats of the venue" };
        _viewpoints.ItemSelected += i =>
        {
            var vp = Builder?.Venue?.Auditorium.Viewpoints.ElementAtOrDefault((int)i - 1);
            if (vp != null)
                SitAt(vp.Row, vp.Seats[0], vp);
        };
        _rows = new OptionButton { TooltipText = "Row" };
        _rows.ItemSelected += i => SitAt(_rows.GetItemText((int)i), SeatNumber);
        _seat = new SpinBox { MinValue = 1, MaxValue = 40, Step = 1, TooltipText = "Seat number (even = house left, odd = house right)" };
        _seat.ValueChanged += v => SitAt(Row, (int)v);
        _where = new Label { Modulate = new Color(1, 1, 1, 0.8f) };
        bar.AddChild(_viewpoints);
        bar.AddChild(new Label { Text = "Row" });
        bar.AddChild(_rows);
        bar.AddChild(new Label { Text = "Seat" });
        bar.AddChild(_seat);
        bar.AddChild(_where);
        OnShowChanged();
    }

    /// <summary>The seat picker only shows on the main view, not on the card, and not while presenting.</summary>
    protected override void OnRoleChanged()
    {
        if (_bar != null)
            _bar.Visible = IsMain && !_presenting;
    }

    /// <summary>Backing field of <see cref="Presenting"/>.</summary>
    private bool _presenting;

    /// <summary>Present mode: the seat picker is hidden (the panel shows where the camera sits).</summary>
    public bool Presenting
    {
        get => _presenting;
        set
        {
            _presenting = value;
            OnRoleChanged();
        }
    }

    /// <summary>The named viewpoint the camera sits at, or null for another seat.</summary>
    public Viewpoint CurrentViewpoint { get; private set; }

    /// <summary>The point on the stage a seat looks at (show coordinates).</summary>
    internal static readonly Vector3 LookAtPoint = new(0, 1.2f, -3.5f);

    /// <summary>Field of view from a seat (degrees).</summary>
    internal const float SeatFov = 50f;

    /// <summary>Where the camera sits: the viewpoint's name, else "Row K, seat 10".</summary>
    public string Where => CurrentViewpoint?.Name ?? $"Row {Row}, seat {SeatNumber}";

    /// <summary>Raised after the camera moved to another seat.</summary>
    public event System.Action Moved;

    /// <summary>Sits at the venue's named viewpoint number `index` (0 = the first); false when there is none.</summary>
    public bool SitAtViewpoint(int index)
    {
        var vp = Builder?.Venue?.Auditorium.Viewpoints.ElementAtOrDefault(index);
        if (vp == null)
            return false;
        _viewpoints.Select(index + 1);
        SitAt(vp.Row, vp.Seats[0], vp);
        return true;
    }

    /// <summary>Fills the pickers from the venue and sits at the first viewpoint.</summary>
    protected override void OnShowChanged()
    {
        if (_viewpoints == null)
            return;
        _viewpoints.Clear();
        _rows.Clear();
        var aud = Builder?.Venue?.Auditorium;
        if (aud == null)
            return;
        _viewpoints.AddItem("(seat)");
        foreach (var vp in aud.Viewpoints)
            _viewpoints.AddItem(vp.Name);
        foreach (var row in aud.Layout(Builder.StageEdge))
            _rows.AddItem(row.Name);
        var first = aud.Viewpoints.FirstOrDefault();
        if (first != null)
        {
            _viewpoints.Select(1);
            SitAt(first.Row, first.Seats[0], first);
        }
    }

    /// <summary>Moves the camera to a seat (or a viewpoint between its seats) looking at the stage.</summary>
    private void SitAt(string row, int seat, Viewpoint viewpoint = null)
    {
        var aud = Builder?.Venue?.Auditorium;
        if (aud == null || !IsInstanceValid(Builder))
            return;
        float[] eye = viewpoint != null ? aud.EyeOf(Builder.StageEdge, viewpoint) : aud.EyeOf(Builder.StageEdge, row, seat);
        if (eye == null)
            return;
        Row = row;
        SeatNumber = seat;
        var world = Builder.ToGlobal(new Vector3(eye[0], eye[1], eye[2]));
        Camera.GlobalPosition = world;
        Camera.LookAt(Builder.ToGlobal(LookAtPoint));
        _yaw = Camera.Rotation.Y;
        _pitch = Camera.Rotation.X;
        for (int i = 0; i < _rows.ItemCount; i++)
            if (_rows.GetItemText(i) == row)
                _rows.Select(i);
        _seat.SetValueNoSignal(seat);
        if (viewpoint == null)
            _viewpoints.Select(0);
        CurrentViewpoint = viewpoint;
        _where.Text = aud.IsReserved(row, seat) ? "  (control position)" : "";
        Moved?.Invoke();
    }

    /// <summary>Next / previous seat in the row (in seat order across the row), row closer / further.</summary>
    public override bool HandleKey(InputEvent e)
    {
        var aud = Builder?.Venue?.Auditorium;
        if (aud == null)
            return false;
        var rows = aud.Layout(Builder.StageEdge);
        int r = rows.FindIndex(x => x.Name == Row);
        if (r < 0)
            return false;
        var seats = rows[r].Seats;
        int s = seats.FindIndex(x => x.Number == SeatNumber);
        var vp = GetTree().Root;
        if (KeyMap.Pressed(e, "seat_next", vp) && s + 1 < seats.Count)
            SitAt(Row, seats[s + 1].Number);
        else if (KeyMap.Pressed(e, "seat_prev", vp) && s > 0)
            SitAt(Row, seats[s - 1].Number);
        else if (KeyMap.Pressed(e, "row_closer", vp) && r > 0)
            SitAt(rows[r - 1].Name, NearestSeat(rows[r - 1], seats[s].X));
        else if (KeyMap.Pressed(e, "row_further", vp) && r + 1 < rows.Count)
            SitAt(rows[r + 1].Name, NearestSeat(rows[r + 1], seats[s].X));
        else
            return false;
        return true;
    }

    private static int NearestSeat(Row row, float x) => row.Seats.OrderBy(s => Mathf.Abs(s.X - x)).First().Number;

    /// <summary>From a seat: look around with the right mouse button, zoom with the wheel.</summary>
    protected override bool OnCameraInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                _looking = mb.Pressed;
                Input.MouseMode = mb.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                Camera.Fov = Mathf.Clamp(Camera.Fov + (wheel.ButtonIndex == MouseButton.WheelUp ? -3f : 3f), 10f, 90f);
                return true;
            case InputEventMouseMotion mm when _looking:
                _yaw -= mm.Relative.X * 0.003f;
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.003f, -1.2f, 1.2f);
                Camera.Rotation = new Vector3(_pitch, _yaw, 0);
                return true;
        }
        return false;
    }
}
