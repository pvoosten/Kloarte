using Godot;

// Free-fly camera.
//   Hold right mouse button: look around     WASD: move, Q/E: down/up, Shift: faster
//   Mouse wheel: forward / back (Shift: x4)   Hold middle mouse button: pan
//   1 / 2 / 3: audience / side / plan view
public partial class FlyCamera : Camera3D
{
    [Export] public Vector3 LookAtOnStart = new(0, 1, -4);
    [Export] public float Speed = 3f;
    [Export] public float MouseSensitivity = 0.003f;
    [Export] public float WheelStep = 0.5f;     // metres per wheel notch
    [Export] public float PanSensitivity = 0.01f;

    // Preset views: position, target.
    private static readonly (Vector3 Pos, Vector3 Target)[] Presets =
    {
        (new Vector3(0, 2.5f, 12f), new Vector3(0, 0.5f, -4f)),    // audience, raked seating
        (new Vector3(6.5f, 2.5f, 5f), new Vector3(0, 3.5f, 2.5f)), // house right, side-on to the FOH beams
        (new Vector3(0, 7.5f, -3f), new Vector3(0, 0, -3.3f)),     // plan view, just under the grid
    };

    private float _yaw, _pitch;

    public override void _Ready()
    {
        if (!GlobalPosition.IsEqualApprox(LookAtOnStart))
            LookAt(LookAtOnStart);
        ReadAngles();
    }

    public void GoTo(int preset)
    {
        var (pos, target) = Presets[preset];
        GlobalPosition = pos;
        LookAt(target);
        ReadAngles();
    }

    private void ReadAngles()
    {
        _yaw = Rotation.Y;
        _pitch = Rotation.X;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                Input.MouseMode = mb.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                GetViewport().GuiReleaseFocus();   // so WASD moves the camera instead of typing in the UI
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                float step = WheelStep * (wheel.ShiftPressed ? 4f : 1f) * (wheel.Factor > 0 ? wheel.Factor : 1f);
                Translate(Vector3.Forward * (wheel.ButtonIndex == MouseButton.WheelUp ? step : -step));
                break;
            case InputEventMouseMotion mm when (mm.ButtonMask & MouseButtonMask.Middle) != 0:
                Translate(new Vector3(-mm.Relative.X, mm.Relative.Y, 0) * PanSensitivity);
                break;
            case InputEventMouseMotion mm when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw -= mm.Relative.X * MouseSensitivity;
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSensitivity, -1.55f, 1.55f);
                Rotation = new Vector3(_pitch, _yaw, 0);
                break;
            case InputEventKey { Pressed: true, Echo: false } k when k.PhysicalKeycode is >= Key.Key1 and <= Key.Key3:
                GoTo((int)(k.PhysicalKeycode - Key.Key1));
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit)
            return;   // typing in a text field
        var dir = Vector3.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) dir += Vector3.Forward;
        if (Input.IsPhysicalKeyPressed(Key.S)) dir += Vector3.Back;
        if (Input.IsPhysicalKeyPressed(Key.A)) dir += Vector3.Left;
        if (Input.IsPhysicalKeyPressed(Key.D)) dir += Vector3.Right;
        if (Input.IsPhysicalKeyPressed(Key.E)) dir += Vector3.Up;
        if (Input.IsPhysicalKeyPressed(Key.Q)) dir += Vector3.Down;
        if (dir == Vector3.Zero)
            return;

        float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? Speed * 4f : Speed;
        Translate(dir.Normalized() * speed * (float)delta);
    }
}
