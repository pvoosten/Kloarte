using Godot;
using Kloarte.GodotBinding;

namespace Kloarte.Tests;

/// <summary>
/// Renders a show scene from the camera presets and saves screenshots to tests/output/.
/// Run:  Godot_console.exe --path . res://tests/show_capture.tscn
/// </summary>
public partial class ShowCapture : Node
{
    /// <summary>Show scene to render.</summary>
    [Export] public string ScenePath = "res://shows/risquons_tout.tscn";
    private const int FramesPerShot = 90;

    private FlyCamera _camera;
    private int _frame, _view;
    private string _outDir;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        _outDir = ProjectSettings.GlobalizePath("res://tests/output");
        DirAccess.MakeDirRecursiveAbsolute(_outDir);
        var scene = GD.Load<PackedScene>(ScenePath).Instantiate();
        AddChild(scene);
        _camera = scene.GetNode<FlyCamera>("Camera");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_frame == 0)
        {
            if (_view > 3)
            {
                GetTree().Quit();
                return;
            }
            if (_view < 3)
                _camera.GoTo(_view);
            else
            {
                // Close-up of the people on stage.
                _camera.GlobalPosition = new Vector3(-0.5f, 1.6f, -0.5f);
                _camera.LookAt(new Vector3(-1.5f, 1.0f, -4f));
            }
        }
        if (++_frame < FramesPerShot)
            return;
        string path = $"{_outDir}/show_view{_view + 1}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"saved {path}  ({Engine.GetFramesPerSecond()} fps)");
        _frame = 0;
        _view++;
    }
}
