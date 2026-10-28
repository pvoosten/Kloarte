using Godot;
using Kloarte.GodotBinding;

namespace Kloarte.Tests;

/// <summary>
/// Renders a show scene from the camera presets and saves screenshots to tests/output/ (the 3D without the panel, on a
/// copy of the Risquons-Tout show); "SHOW CAPTURE OK" when all are taken.
/// Run:  Godot_console.exe --path . res://tests/show_capture.tscn
/// </summary>
public partial class ShowCapture : Node
{
    /// <summary>Show scene to render.</summary>
    [Export] public string ScenePath = "res://shows/risquons_tout.tscn";

    /// <summary>Frames rendered before each screenshot.</summary>
    private const int FramesPerShot = 90;

    /// <summary>The scene's camera.</summary>
    private FlyCamera _camera;

    /// <summary>Frames since the current view was set.</summary>
    private int _frame;

    /// <summary>The current view (0..2 camera presets, 3 the close-up).</summary>
    private int _view;

    /// <summary>The output folder (absolute).</summary>
    private string _outDir;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        _outDir = ProjectSettings.GlobalizePath("res://tests/output");
        DirAccess.MakeDirRecursiveAbsolute(_outDir);
        var scene = TestShow.Load("show_capture", scenePath: ScenePath);   // a copy of the show
        AddChild(scene);
        if (scene.GetNodeOrNull<CanvasLayer>("FixturePanel") is { } panel)
            panel.Visible = false;   // the window renders the scene's camera
        _camera = scene.GetNode<FlyCamera>("Camera");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_frame == 0)
        {
            if (_view > 3)
            {
                GD.Print("SHOW CAPTURE OK");
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
