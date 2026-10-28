using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// Measures the cost of the beams' occlusion by scenery (<see cref="BeamOcclusion"/>) on a real rig: the show scene with a
/// copy of a MagicQ show (default the newest Kruimels in the user's MagicQ folder, copied to tests/output, never written
/// there), every lamp at full, haze on, borders and legs of its own on <see cref="RenderLayers.Occluders"/>, vsync off,
/// the audience camera. Prints the median frame time with occlusion off and on, in turns of 30 frames so that the drift
/// of a shared GPU cancels out (steady, and with the moving heads panning), and the frames and the worst frame of a full
/// re-render of all maps (staggered as in the app, and all at once). Must run with a GPU (not --headless).
/// Run:  Godot_console.exe --path . --resolution 1600x900 res://tests/occlusion_bench.tscn [-- --show=C:/.../X.shw]
/// </summary>
public partial class OcclusionBench : Node
{
    /// <summary>The show scene's builder.</summary>
    private ShowBuilder _show;

    /// <summary>The measurement steps, run one after the other.</summary>
    private IEnumerator<int> _steps;

    /// <summary>Frames still to wait before the next step.</summary>
    private int _wait;

    /// <summary>Frame times (µs) collected by the current step.</summary>
    private readonly List<double> _times = new();

    /// <summary>Time of the previous frame (µs).</summary>
    private ulong _last;

    /// <summary>The moving heads pan while true.</summary>
    private bool _panning;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");
        ViewHost.Persist = false;
        FixturePanel.PresentFullScreen = false;
        string folder = ProjectSettings.GlobalizePath("res://tests/output/occlusion_bench/show");
        System.IO.Directory.CreateDirectory(folder);
        MagicQPc.ShowFolderOverride = folder;
        string source = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--show="))?["--show=".Length..]
                        ?? NewestKruimels();
        string copy = System.IO.Path.Combine(folder, "Bench_001.shw");
        System.IO.File.Copy(source, copy, true);
        if (System.IO.File.Exists(System.IO.Path.ChangeExtension(source, ".xhw")))
            System.IO.File.Copy(System.IO.Path.ChangeExtension(source, ".xhw"), System.IO.Path.ChangeExtension(copy, ".xhw"), true);
        GD.Print($"bench show: {source}");

        var scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(scene);
        _show = scene.GetNode<ShowBuilder>("Show");
        _show.OpenShow(copy);
        scene.GetNode<FlyCamera>("Camera").GoTo(0);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        RenderingServer.GlobalShaderParameterSet("haze_density", ProjectLook.DensityOf(50));
        AddOccluders(scene);
        _steps = Steps().GetEnumerator();
        _wait = 120;
    }

    /// <summary>The newest Kruimels_NNN.shw in the user's MagicQ show folder (read only).</summary>
    private static string NewestKruimels()
    {
        string magicq = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "MagicQ", "show");
        return System.IO.Directory.GetFiles(magicq, "Kruimels_*.shw").OrderBy(f => f).Last();
    }

    /// <summary>Borders (4) and legs (8) like a black box, on the Occluders layer.</summary>
    private static void AddOccluders(Node scene)
    {
        var black = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 1f };
        void Add(Vector3 size, Vector3 at) => scene.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = at,
            MaterialOverride = black,
            Layers = RenderLayers.Default | RenderLayers.Occluders,
        });
        for (int i = 0; i < 4; i++)
        {
            float z = -1.5f - 2.5f * i;
            Add(new Vector3(14f, 1.5f, 0.05f), new Vector3(0, 6.5f, z));
            Add(new Vector3(1.5f, 6f, 0.05f), new Vector3(-5.5f, 3f, z));
            Add(new Vector3(1.5f, 6f, 0.05f), new Vector3(5.5f, 3f, z));
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_last != 0)
            _times.Add(now - _last);
        _last = now;
        foreach (var f in _show.Fixtures)
            f.Intensity = 1f;
        if (_panning)
            foreach (var mh in _show.Movers.Values)
                mh.Yoke.RotateY(0.01f);
        if (--_wait > 0)
            return;
        if (!_steps.MoveNext())
        {
            GetTree().Quit();
            return;
        }
        _wait = _steps.Current;
    }

    /// <summary>The steps; each yields the frames to run before the next one.</summary>
    private IEnumerable<int> Steps()
    {
        var lamps = _show.Fixtures.ToList();
        GD.Print($"rig: {lamps.Count} lamps, {_show.Movers.Count} moving heads, {lamps.Count(IsBeam)} beams visible");
        BeamOcclusion.Enabled = true;
        while (BeamOcclusion.Pending > 0)
            yield return 10;
        yield return 30;
        foreach (string what in new[] { "steady", "moving heads panning" })
        {
            _panning = what != "steady";
            // Off and on in turns, so the drift of a shared GPU cancels out; the first 10 frames of a block are skipped.
            var ratios = new List<double>();
            var blocks = new Dictionary<bool, List<double>> { [false] = new(), [true] = new() };
            int maps = BeamOcclusion.Rendered;
            for (int block = 0; block < 32; block++)
            {
                bool on = block % 2 == 1;
                BeamOcclusion.Enabled = on;
                yield return 10;
                Clear();
                yield return 30;
                blocks[on].Add(Median(_times) / 1000);
                if (on)
                    ratios.Add(blocks[true][^1] / blocks[false][^1]);
            }
            GD.Print($"BENCH {what}: frame time median off {Median(blocks[false]):0.00} ms, on {Median(blocks[true]):0.00} ms, " +
                     $"on / off per pair: median {Median(ratios):0.000} (min {ratios.Min():0.000}, max {ratios.Max():0.000}); " +
                     $"{BeamOcclusion.Rendered - maps} maps rendered");
        }
        _panning = false;
        BeamOcclusion.Enabled = true;

        foreach (int perFrame in new[] { 4, 4, 1000, 1000 })
        {
            BeamOcclusion.PerFrame = perFrame;
            yield return 30;
            Clear();
            yield return 20;
            double steady = Median(_times) / 1000;
            foreach (var f in lamps)
                f.InvalidateOcclusion();
            Clear();
            int before = BeamOcclusion.Rendered;
            int frames = 0;
            do
            {
                frames++;
                yield return 1;
            } while (BeamOcclusion.Pending > 0 || frames < 3);
            yield return 2;
            GD.Print($"BENCH re-render all ({BeamOcclusion.Rendered - before} maps, {perFrame} per frame): {_times.Count} frames, " +
                     $"worst {_times.Max() / 1000:0.00} ms, mean {_times.Average() / 1000:0.00} ms (steady median {steady:0.00} ms)");
        }
        BeamOcclusion.PerFrame = 4;
        GD.Print("BENCH DONE");
    }

    /// <summary>The median of a list.</summary>
    private static double Median(List<double> values) => values.OrderBy(v => v).ElementAt(values.Count / 2);

    /// <summary>Starts a new measurement.</summary>
    private void Clear()
    {
        _times.Clear();
    }

    /// <summary>A lamp whose haze beam is drawn.</summary>
    private static bool IsBeam(ProfileFixture f) => f.CustomBeam && f.Intensity > 0f && f.Flood == ProfileFixture.FloodMounting.None;
}
