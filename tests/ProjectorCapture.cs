using System;
using Godot;

// Automated check for the light-projector questions in CLAUDE.md.
// Instances main.tscn, steps the PoC profile through a few looks and saves screenshots to tests/output/.
// Run:  Godot_console.exe --path . res://tests/projector_capture.tscn
public partial class ProjectorCapture : Node
{
    private const int FramesPerShot = 90;   // let fog temporal reprojection settle

    private ProfileFixture _fixture;
    private FlyCamera _camera;
    private int _frame, _step;
    private string _outDir;

    private (string Name, int View, Action<ProfileFixture> Look)[] _steps;

    public override void _Ready()
    {
        _outDir = ProjectSettings.GlobalizePath("res://tests/output");
        DirAccess.MakeDirRecursiveAbsolute(_outDir);

        var main = GD.Load<PackedScene>("res://main.tscn").Instantiate();
        AddChild(main);
        _fixture = main.GetNode<ProfileFixture>("Fixtures/FOH_DS104");
        _camera = main.GetNode<FlyCamera>("Camera");
        var gobo = GD.Load<Texture2D>("res://gobos/window.png");

        _steps = new (string, int, Action<ProfileFixture>)[]
        {
            ("01_open", 0, _ => { }),
            ("02_open_side", 1, _ => { }),
            // Top + left shutter in, top blade tilted: tests dynamic update and orientation.
            ("03_shutters", 0, f => { f.TopDepth = 0.5f; f.TopAngle = 15f; f.LeftDepth = 0.4f; }),
            ("04_shutters_side", 1, _ => { }),
            ("05_shutters_plan", 2, _ => { }),
            ("06_gobo_iris", 0, f => { f.TopDepth = 0; f.TopAngle = 0; f.LeftDepth = 0; f.Gobo = gobo; f.Iris = 0.8f; }),
            ("07_gobo_side", 1, _ => { }),
            ("08_gobo_plan", 2, _ => { }),
            ("09_gobo_soft", 0, f => { f.Softness = 0.08f; }),
            // Godot's own volumetric fog for comparison: ignores the projector (open question 2).
            ("10_godot_fog_side", 1, f => { f.CustomBeam = false; }),
        };
    }

    public override void _Process(double delta)
    {
        if (_frame == 0)
        {
            if (_step >= _steps.Length)
            {
                GetTree().Quit();
                return;
            }
            _camera.GoTo(_steps[_step].View);
            _steps[_step].Look(_fixture);
        }

        if (++_frame < FramesPerShot)
            return;

        string path = $"{_outDir}/{_steps[_step].Name}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        _fixture.MaskTexture?.GetImage().SavePng($"{_outDir}/{_steps[_step].Name}_mask.png");
        GD.Print($"saved {path}");
        _frame = 0;
        _step++;
    }
}
