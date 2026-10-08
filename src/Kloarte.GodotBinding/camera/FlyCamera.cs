using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Free-fly camera of a 3D view. Mouse: hold the right button to look around, wheel = forward / back (Shift: x4),
/// hold the middle button to pan. Keys from the key map (context "3d"): move (default WASD, Q/E down/up, Shift fast),
/// presets audience / side / plan (1/2/3). Only an enabled camera (the main 3D view) reacts.
/// </summary>
public partial class FlyCamera : Camera3D
{
    /// <summary>Point the camera looks at when the scene starts.</summary>
    [Export] public Vector3 LookAtOnStart = new(0, 1, -4);
    /// <summary>Movement speed in m/s.</summary>
    [Export] public float Speed = 3f;
    /// <summary>Radians per pixel of mouse movement.</summary>
    [Export] public float MouseSensitivity = 0.003f;
    /// <summary>Metres per wheel notch.</summary>
    [Export] public float WheelStep = 0.5f;
    /// <summary>Metres per pixel when panning.</summary>
    [Export] public float PanSensitivity = 0.01f;

    /// <summary>False: the camera ignores input (e.g. a small view, or another view is the main view).</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>Preset views: position, target.</summary>
    private static readonly (Vector3 Pos, Vector3 Target)[] Presets =
    {
        (new Vector3(0, 2.5f, 12f), new Vector3(0, 0.5f, -4f)),    // audience, raked seating
        (new Vector3(6.5f, 2.5f, 5f), new Vector3(0, 3.5f, 2.5f)), // house right, side-on to the FOH beams
        (new Vector3(0, 7.5f, -3f), new Vector3(0, 0, -3.3f)),     // plan view, just under the grid
    };

    private float _yaw, _pitch;

    /// <summary>Looks at <see cref="LookAtOnStart"/>.</summary>
    public override void _Ready()
    {
        KeyMap.EnsureLoaded();
        if (!GlobalPosition.IsEqualApprox(LookAtOnStart))
            LookAt(LookAtOnStart);
        ReadAngles();
    }

    /// <summary>Jumps to preset 0 (audience), 1 (side) or 2 (plan).</summary>
    public void GoTo(int preset)
    {
        var (pos, target) = Presets[preset];
        LookFrom(pos, target);
    }

    /// <summary>Puts the camera at `position` looking at `target`.</summary>
    private void LookFrom(Vector3 position, Vector3 target)
    {
        GlobalPosition = position;
        LookAt(target);
        ReadAngles();
    }

    private void ReadAngles()
    {
        _yaw = Rotation.Y;
        _pitch = Rotation.X;
    }

    /// <summary>Mouse look / wheel / pan and the preset keys (when the camera is not inside a view that forwards input).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (InputEnabled && !ForwardedByView && HandleInput(e))
            GetViewport().SetInputAsHandled();
    }

    /// <summary>True when a view (View3D) forwards the input of its area to this camera.</summary>
    public bool ForwardedByView { get; set; }

    /// <summary>Handles an input event; true when it was used.</summary>
    public bool HandleInput(InputEvent e)
    {
        var vp = GetTree().Root;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } mb:
                Input.MouseMode = mb.Pressed ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
                vp.GuiReleaseFocus();   // so WASD moves the camera instead of typing in the UI
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true, CtrlPressed: false } wheel:
                float step = WheelStep * (wheel.ShiftPressed ? 4f : 1f) * (wheel.Factor > 0 ? wheel.Factor : 1f);
                Translate(Vector3.Forward * (wheel.ButtonIndex == MouseButton.WheelUp ? step : -step));
                return true;
            case InputEventMouseMotion mm when (mm.ButtonMask & MouseButtonMask.Middle) != 0:
                Translate(new Vector3(-mm.Relative.X, mm.Relative.Y, 0) * PanSensitivity);
                return true;
            case InputEventMouseMotion mm when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw -= mm.Relative.X * MouseSensitivity;
                _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * MouseSensitivity, -1.55f, 1.55f);
                Rotation = new Vector3(_pitch, _yaw, 0);
                return true;
        }
        for (int i = 0; i < Presets.Length; i++)
            if (KeyMap.Pressed(e, $"camera_preset_{i + 1}", vp))
            {
                GoTo(i);
                return true;
            }
        return false;
    }

    /// <summary>Moves with the held movement keys.</summary>
    public override void _Process(double delta)
    {
        if (!InputEnabled)
            return;
        var vp = GetTree().Root;
        var dir = Vector3.Zero;
        if (KeyMap.Held("camera_forward", vp)) dir += Vector3.Forward;
        if (KeyMap.Held("camera_back", vp)) dir += Vector3.Back;
        if (KeyMap.Held("camera_left", vp)) dir += Vector3.Left;
        if (KeyMap.Held("camera_right", vp)) dir += Vector3.Right;
        if (KeyMap.Held("camera_up", vp)) dir += Vector3.Up;
        if (KeyMap.Held("camera_down", vp)) dir += Vector3.Down;
        if (dir == Vector3.Zero)
            return;

        float speed = KeyMap.Held("camera_fast", vp) ? Speed * 4f : Speed;
        Translate(dir.Normalized() * speed * (float)delta);
    }
}
