using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// Light strength test (rendered in HDR with a linear tonemap, no fog, no ambient), prints "PHOTOMETRIC OK":
/// 1. inverse square: the same profile at 4 m and 8 m above a white floor -&gt; brightness ratio 4 (+-5 %);
/// 2. zoom: a DS104 (6500 lm) at 15° vs 31° at 6 m -&gt; ratio = solid-angle ratio (~4.25, +-5 %);
/// 3. a lamp without shadows still lights (Godot drops projector lights without shadows; see ProfileFixture);
/// 4. ACP floods (Risquons-Tout "Horizon" heads on T 22, in front of the cyc): coverage of the cyc between 0.5 and
///    6 m (share of samples above 25 % of the median) and p5/p95 (evenness): as positioned in MagicVis (3.98 m high,
///    0.5 m from the cyc: physically they can't reach the top and bottom of the cloth), then hung at the top of the
///    cyc (7 m, 1 m in front: must cover &gt; 85 %), compared with the old round 120° spot at the same place.
///    Screenshots tests/output/flood_magicvis.png, flood_top.png, flood_old.png.
/// Run:  Godot_console.exe --path . --resolution 1280x720 res://tests/photometric_test.tscn
/// </summary>
public partial class PhotometricTest : Node3D
{
    /// <summary>Frames since start; the steps are scheduled on it.</summary>
    private int _frame;
    private int _failures;
    private ProfileFixture _probe;
    private readonly List<float> _readings = new();
    private Node _show;
    private ShowBuilder _builder;
    private readonly List<string> _results = new();

    /// <summary>Builds the test stage: white floor, camera looking down, one profile.</summary>
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        GetViewport().UseHdr2D = true;
        AddChild(new WorldEnvironment { Environment = LinearEnvironment() });
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40, 40) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.White, Roughness = 1f },
        });
        var camera = new Camera3D { Fov = 30f, Far = 50f };
        AddChild(camera);
        camera.Position = new Vector3(0, 20, 0);
        camera.RotationDegrees = new Vector3(-90, 0, 0);
        _probe = new ProfileFixture { Lumens = 6500, FieldAngle = 26, FieldFalloff = 0.85f, CustomBeam = false, FogEnergy = 0f };
        AddChild(_probe);
        Place(4f, 26f);
    }

    private static Environment LinearEnvironment() => new()
    {
        BackgroundMode = Environment.BGMode.Color,
        BackgroundColor = Colors.Black,
        AmbientLightSource = Environment.AmbientSource.Color,
        AmbientLightColor = Colors.Black,
        AmbientLightEnergy = 0f,
        TonemapMode = Environment.ToneMapper.Linear,
    };

    /// <summary>Hangs the probe at `height` above a floor point 1.5 m beside the camera axis (the body would block
    /// the camera's view of the pool centre), aimed straight down.</summary>
    private void Place(float height, float field)
    {
        _probe.Position = new Vector3(1.5f, height, 0.01f);
        _probe.FieldAngle = field;
        _probe.FocusPoint = new Vector3(1.5f, 0, 0);
    }

    /// <summary>Average linear brightness near the floor point under the probe (just beside the body's shadow-free
    /// view line).</summary>
    private float PoolCentre()
    {
        var camera = GetViewport().GetCamera3D();
        var image = GetViewport().GetTexture().GetImage();
        var c = ToImage(camera.UnprojectPosition(new Vector3(1.5f, 0, 0.3f)), image);
        float sum = 0;
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
                sum += image.GetPixel((int)c.X + dx, (int)c.Y + dy).R;
        return sum / 49f;
    }

    /// <summary>Runs the steps.</summary>
    public override void _Process(double delta)
    {
        _frame++;
        switch (_frame)
        {
            case 20: _readings.Add(PoolCentre()); Place(8f, 26f); break;
            case 40: _readings.Add(PoolCentre()); Place(6f, 15f); break;
            case 60: _readings.Add(PoolCentre()); Place(6f, 31f); break;
            case 80:
                _readings.Add(PoolCentre());
                _probe.CastShadows = false;
                break;
            case 100:
                _readings.Add(PoolCentre());
                float distance = _readings[0] / _readings[1];
                float zoom = _readings[2] / _readings[3];
                float expected = (float)(Photometrics.SolidAngle(31) / Photometrics.SolidAngle(15));
                Check(Mathf.Abs(distance / 4f - 1f) < 0.05f, $"inverse square: 4 m / 8 m = {distance:0.00} (expected 4.00)");
                Check(Mathf.Abs(zoom / expected - 1f) < 0.05f, $"zoom: 15° / 31° = {zoom:0.00} (expected {expected:0.00})");
                Check(Mathf.Abs(_readings[4] / _readings[3] - 1f) < 0.02f, $"no shadows still lights ({_readings[4]:0.000} vs {_readings[3]:0.000})");
                StartFloods();
                break;
            case 180: MeasureFloods("magicvis", 0f); HangAtTop(); break;
            case 240: MeasureFloods("top", 0.85f); SetOldFloods(); break;
            case 300:
                MeasureFloods("old", 0f);
                Check(_coverage["top"] > _coverage["old"] + 0.1f && _evenness["top"] > _evenness["old"],
                      "floods at the top of the cyc light it more evenly than the old round spots");
                foreach (string r in _results)
                    GD.Print(r);
                GD.Print(_failures == 0 ? "PHOTOMETRIC OK" : $"PHOTOMETRIC FAILURES: {_failures}");
                GetTree().Quit(_failures == 0 ? 0 : 1);
                break;
        }
    }

    private List<ProfileFixture> Horizons =>
        _builder.Fixtures.Where(f => f.Name.ToString().Contains("Horizon")).ToList();

    /// <summary>Loads Risquons-Tout with only the horizon floods on, seen from the stage in linear HDR.</summary>
    private void StartFloods()
    {
        foreach (Node child in GetChildren())
            child.QueueFree();
        _show = TestShow.Load("photometric");   // a copy of the show: the scene opens none by itself
        AddChild(_show);
        _show.GetNode<CanvasLayer>("FixturePanel").Visible = false;
        _show.GetNode<WorldEnvironment>("WorldEnvironment").Environment = LinearEnvironment();
        _show.GetNode<Node>("StrayLight").QueueFree();
        _builder = _show.GetNode<ShowBuilder>("Show");
        foreach (var f in _builder.Fixtures)
            f.Intensity = 0f;
        foreach (var f in Horizons)
        {
            f.Intensity = 1f;
            f.GelColor = Colors.White;   // compare light, not gel colours
        }
        foreach (var actor in _builder.Actors.Values)
            actor.Visible = false;
        var cam = _show.GetNode<Camera3D>("Camera");
        cam.Position = new Vector3(0, 3.5f, 2f);
        cam.LookAt(new Vector3(0, 3.5f, _builder.BackStageZ));
        cam.Fov = 80f;
        GD.Print($"floods: {Horizons.Count} horizon lamps, field {Horizons.FirstOrDefault()?.FieldAngle}°, " +
                 $"flood {Horizons.FirstOrDefault()?.Flood}, energy {Horizons.FirstOrDefault()?.Energy:0}");
    }

    /// <summary>The old way: a round symmetric spot, clamped to 120°.</summary>
    private void SetOldFloods()
    {
        foreach (var f in Horizons)
        {
            f.Flood = ProfileFixture.FloodMounting.None;
            f.FieldAngle = 120f;
            f.FieldFalloff = 0.5f;
        }
    }

    /// <summary>Samples the cyc (0.5..6 m high, 13 m wide) in the rendered image.</summary>
    private readonly Dictionary<string, float> _coverage = new(), _evenness = new();

    /// <summary>Hangs the horizon floods where ACPs normally hang: at the top of the cyc (7 m) and 1 m in front of it,
    /// still pointing at the cloth.</summary>
    private void HangAtTop()
    {
        float z = _builder.BackStageZ + 1f;
        foreach (var f in Horizons)
        {
            f.Position = new Vector3(f.Position.X, 7f, z);
            f.FocusPoint = new Vector3(f.Position.X, 7f, _builder.BackStageZ);
        }
    }

    private void MeasureFloods(string label, float minCoverage)
    {
        var cam = _show.GetNode<Camera3D>("Camera");
        var image = GetViewport().GetTexture().GetImage();
        var png = (Image)image.Duplicate();
        png.Convert(Image.Format.Rgb8);
        png.SavePng(ProjectSettings.GlobalizePath($"res://tests/output/flood_{label}.png"));
        var samples = new List<float>();
        float z = _builder.BackStageZ + 0.02f;
        for (float y = 0.5f; y <= 6f; y += 0.25f)
            for (float x = -6.5f; x <= 6.5f; x += 0.25f)
            {
                var p = new Vector3(x, y, z);
                if (cam.IsPositionBehind(p))
                    continue;
                var s = ToImage(cam.UnprojectPosition(p), image);
                if (s.X < 0 || s.Y < 0 || s.X >= image.GetWidth() || s.Y >= image.GetHeight())
                    continue;
                samples.Add(image.GetPixel((int)s.X, (int)s.Y).Luminance);
            }
        samples.Sort();
        float mean = samples.Average();
        // Lit = above a quarter of the median (linear HDR: the cloth right next to the lamps is far brighter than the
        // rest, so the mean says little).
        float median = samples[samples.Count / 2];
        float coverage = samples.Count(v => v > 0.25f * median && v > 1e-3f) / (float)samples.Count;
        float lo = samples[(int)(samples.Count * 0.05f)], hi = samples[(int)(samples.Count * 0.95f)];
        _results.Add($"floods {label}: {samples.Count} samples, mean {mean:0.000}, coverage {coverage * 100:0}%, " +
                     $"p5/p95 {lo / Mathf.Max(hi, 1e-4f):0.00}");
        _coverage[label] = coverage;
        _evenness[label] = lo / Mathf.Max(hi, 1e-4f);
        if (minCoverage > 0f)
            Check(coverage > minCoverage, $"floods '{label}' cover the cyc ({coverage * 100:0}% > {minCoverage * 100:0}%)");
    }

    /// <summary>Viewport (canvas, stretched) coordinates -&gt; pixel coordinates of the rendered image.</summary>
    private Vector2 ToImage(Vector2 viewport, Image image) =>
        viewport * new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;

    private void Check(bool ok, string what)
    {
        _results.Add((ok ? "ok   " : "FAIL ") + what);
        if (!ok)
            _failures++;
    }
}
