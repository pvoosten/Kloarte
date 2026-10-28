using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// Occlusion of a haze beam by scenery (<see cref="BeamOcclusion"/>): a profile at 6 m aimed down at the stage, seen
/// from the side in haze, with a plate on <see cref="RenderLayers.Occluders"/> across the top half of the beam halfway
/// down. Measures the beam's brightness in front of and behind the plate, on its top side (cut) and its bottom side
/// (not cut), with occlusion off and on; reads the occlusion map back (linear depth / range, image top = beam top);
/// then moves the plate out of the beam and checks that <see cref="ProfileFixture.InvalidateOcclusion"/> brings the beam
/// back. Screenshots tests/output/occlusion_off.png, occlusion_on.png, occlusion_moved.png, occlusion_front.png.
/// Prints "OCCLUSION OK".
/// Run:  Godot_console.exe --path . --resolution 1280x720 res://tests/occlusion_test.tscn
/// </summary>
public partial class OcclusionTest : Node3D
{
    /// <summary>The lens and the focus point of the lamp.</summary>
    private static readonly Vector3 Lens = new(0, 6, 4), Focus = new(0, 0, -2);

    /// <summary>The lamp under test.</summary>
    private ProfileFixture _lamp;

    /// <summary>The plate that cuts the top half of the beam.</summary>
    private MeshInstance3D _plate;

    /// <summary>The side camera.</summary>
    private Camera3D _camera;

    /// <summary>Beam axis and the beam's top direction (the lamp's +Y), set once the lamp is aimed.</summary>
    private Vector3 _axis, _up;

    /// <summary>Frames since the start.</summary>
    private int _frame;

    /// <summary>Measurements: [front top, front bottom, behind top, behind bottom] with occlusion off and on.</summary>
    private float[] _off, _on;

    /// <summary>Failures so far.</summary>
    private int _failures;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://tests/output"));
        RenderingServer.GlobalShaderParameterSet("haze_density", ProjectLook.DensityOf(20));
        BeamOcclusion.Enabled = true;   // off by default (cost, see BeamOcclusion.Enabled)
        AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.05f, 0.05f, 0.05f),
                TonemapMode = Environment.ToneMapper.Linear,
            },
        });
        AddChild(new MeshInstance3D   // the stage floor: lit, but not an occluder
        {
            Mesh = new PlaneMesh { Size = new Vector2(16, 16) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.25f, 0.25f), Roughness = 1f },
        });
        _lamp = new ProfileFixture { Lumens = 6500, FieldAngle = 20, FieldFalloff = 0.85f };
        AddChild(_lamp);
        _lamp.Position = Lens;
        _lamp.FocusPoint = Focus;
        _lamp.AimAtFocus();
        _axis = (Focus - Lens).Normalized();
        _up = _lamp.GlobalBasis.Y;

        // 3 m wide (across the view), 0.9 m along the beam's top direction from just above the axis, 10 cm thick.
        var middle = Lens + _axis * Lens.DistanceTo(Focus) / 2f;
        _plate = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(3f, 0.9f, 0.1f) },
            Layers = RenderLayers.Default | RenderLayers.Occluders,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.35f) },
        };
        AddChild(_plate);
        _plate.GlobalTransform = new Transform3D(new Basis(Vector3.Right, _up, -_axis), middle + _up * 0.5f);

        _camera = new Camera3D { Fov = 60f, Current = true };
        AddChild(_camera);
        _camera.Position = new Vector3(11, 3, 1);
        _camera.LookAt(new Vector3(0, 3, 1), Vector3.Up);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frame++;
        switch (_frame)
        {
            case 30:
                BeamOcclusion.Enabled = false;   // after the first map is rendered: off, then on again
                break;
            case 40:
                _off = Measure("occlusion_off.png");
                BeamOcclusion.Enabled = true;
                break;
            case 50:
                _on = Measure("occlusion_on.png");
                CheckMap();
                Check("front top is not cut", _on[0] > 0.8f * _off[0] && _off[0] > 0.02f);
                Check("front bottom is not cut", _on[1] > 0.8f * _off[1] && _off[1] > 0.02f);
                Check("behind top is cut", _on[2] < 0.25f * _off[2] && _off[2] > 0.02f);
                Check("behind bottom is not cut", _on[3] > 0.8f * _off[3] && _off[3] > 0.02f);
                _plate.Position += new Vector3(0, 0, 20);
                _lamp.InvalidateOcclusion();
                break;
            case 60:
                var moved = Measure("occlusion_moved.png");
                Check("plate moved away: behind top lit again", moved[2] > 0.8f * _off[2]);
                // For the record: the cut beam from the front, three quarters.
                _plate.Position -= new Vector3(0, 0, 20);
                _lamp.InvalidateOcclusion();
                _camera.Position = new Vector3(8, 4.5f, 5);
                _camera.LookAt(new Vector3(0, 2f, 0), Vector3.Up);
                break;
            case 70:
                SaveShot("occlusion_front.png");
                GD.Print(_failures == 0 ? "OCCLUSION OK" : $"OCCLUSION FAILURES: {_failures}");
                GetTree().Quit(_failures == 0 ? 0 : 1);
                break;
        }
    }

    /// <summary>A screenshot and the brightness (luminance) at the four sample points of the beam.</summary>
    private float[] Measure(string shot)
    {
        var image = SaveShot(shot);
        var points = new[]
        {
            Lens + _axis * 2f + _up * 0.2f, Lens + _axis * 2f - _up * 0.2f,
            Lens + _axis * 6.5f + _up * 0.55f, Lens + _axis * 6.5f - _up * 0.55f,
        };
        var values = new float[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var p = _camera.UnprojectPosition(points[i]) * new Vector2(image.GetWidth(), image.GetHeight())
                    / GetViewport().GetVisibleRect().Size;
            float sum = 0f;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    sum += image.GetPixel((int)p.X + dx, (int)p.Y + dy).Luminance;
            values[i] = sum / 25f;
        }
        GD.Print($"{shot}: front top {values[0]:0.000} bottom {values[1]:0.000}, behind top {values[2]:0.000} bottom {values[3]:0.000}");
        return values;
    }

    /// <summary>
    /// The occlusion map read back: the plate (top half, image top = beam top) at its depth along the axis divided by
    /// the beam length, nothing (1) below the axis.
    /// </summary>
    private void CheckMap()
    {
        var image = _lamp.OcclusionTexture.GetImage();
        int n = image.GetWidth();
        float top = image.GetPixel(n / 2, n / 4).R, bottom = image.GetPixel(n / 2, 3 * n / 4).R;
        float length = Mathf.Max(5f, Lens.DistanceTo(Focus) * 1.5f);
        float expected = (Lens.DistanceTo(Focus) / 2f - 0.05f) / length;   // the plate's face towards the lens
        GD.Print($"occlusion map {n}x{n} {image.GetFormat()}: top {top:0.0000} (expected {expected:0.0000}), bottom {bottom:0.0000}");
        Check("map: plate depth in the top half", Mathf.Abs(top - expected) < 0.01f);
        Check("map: nothing in the bottom half", bottom > 0.99f);
    }

    /// <summary>Saves the window's picture to tests/output and returns it.</summary>
    private Image SaveShot(string name)
    {
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(ProjectSettings.GlobalizePath("res://tests/output/" + name));
        return image;
    }

    /// <summary>Prints a check's result and counts a failure.</summary>
    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
