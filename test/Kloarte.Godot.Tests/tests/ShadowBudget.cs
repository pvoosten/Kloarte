using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;

namespace Kloarte.Tests;

/// <summary>
/// Shadow atlas budget (Godot finding 7, open since the start): a lamp is a spot light with a projector, and a projector
/// light without a shadow gives no light at all (finding 3), so a lamp that gets no place in the positional shadow atlas
/// may go dark. 182 narrow profiles in a grid over a white floor, seen from above: the lamps whose pool stays dark are
/// counted with Godot's default atlas (4 + 4 + 16 + 64 = 88 places: 94 of them dark, 11 Oct 2026) and with the atlas as
/// the scene has it (<see cref="ShowBuilder.ConfigureShadowAtlas"/>). Prints "SHADOWS OK" when every lamp lights with the scene's atlas.
/// Run:  Godot_console.exe --path . --resolution 1280x720 res://tests/shadow_budget.tscn
/// </summary>
public partial class ShadowBudget : Node3D
{
    /// <summary>Grid of lamps: columns x rows, spacing (m), height (m).</summary>
    private const int Columns = 14, Rows = 13;
    private const float Spacing = 1.5f, Height = 6f;

    private int _frame;
    private readonly List<ProfileFixture> _lamps = new();
    private readonly Dictionary<string, int> _dark = new();

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");
        AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = Colors.Black,
                TonemapMode = Environment.ToneMapper.Linear,
            },
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40, 40) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.White, Roughness = 1f },
        });
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 24f, Far = 50f };
        AddChild(camera);
        camera.Position = new Vector3(0, 20, 0);
        camera.RotationDegrees = new Vector3(-90, 0, 0);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                var at = PoolOf(c, r);
                var lamp = new ProfileFixture { Lumens = 6500, FieldAngle = 12, FieldFalloff = 0.85f, CustomBeam = false, FogEnergy = 0f };
                AddChild(lamp);
                lamp.Position = at + new Vector3(0, Height, 0.01f);
                lamp.FocusPoint = at;
                _lamps.Add(lamp);
            }
        // First Godot's default quadrants (4 + 4 + 16 + 64 places), then the scene's atlas (lamps keep a place once they
        // have one, so the default can't come second).
        var vp = GetViewport();
        vp.SetPositionalShadowAtlasQuadrantSubdiv(0, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4);
        vp.SetPositionalShadowAtlasQuadrantSubdiv(1, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4);
        vp.SetPositionalShadowAtlasQuadrantSubdiv(2, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv16);
        vp.SetPositionalShadowAtlasQuadrantSubdiv(3, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv64);
        GD.Print($"shadow budget: {_lamps.Count} lamps, atlas {GetViewport().PositionalShadowAtlasSize}, " +
                 $"quadrants {string.Join(" ", Enumerable.Range(0, 4).Select(q => GetViewport().GetPositionalShadowAtlasQuadrantSubdiv(q)))}");
    }

    /// <summary>The floor point under lamp (c, r).</summary>
    private static Vector3 PoolOf(int c, int r) =>
        new((c - (Columns - 1) / 2f) * Spacing, 0, (r - (Rows - 1) / 2f) * Spacing);

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frame++;
        if (_frame == 60)
        {
            _dark["default"] = CountDark("shadow_budget_default.png");
            ShowBuilder.ConfigureShadowAtlas(GetViewport());
        }
        if (_frame == 120)
        {
            _dark["scene"] = CountDark("shadow_budget_scene.png");
            GD.Print($"dark lamps: {_dark["scene"]} with the scene's atlas, {_dark["default"]} with Godot's default (of {_lamps.Count})");
            bool ok = _dark["scene"] == 0 && _dark["default"] > 0;   // the default must still show the problem
            GD.Print(ok ? "SHADOWS OK" : "SHADOWS FAILURES: 1");
            GetTree().Quit(ok ? 0 : 1);
        }
    }

    /// <summary>
    /// The lamps whose pool is darker than a fifth of the brightest (not the median: with half of them dark the median is
    /// 0 and nothing counts as dark); a screenshot for the record.
    /// </summary>
    private int CountDark(string shot)
    {
        var camera = GetViewport().GetCamera3D();
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(ProjectSettings.GlobalizePath("res://tests/output/" + shot));
        var levels = new List<float>();
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                // Beside the lamp's body (it hides the pool's centre from the camera).
                var p = camera.UnprojectPosition(PoolOf(c, r) + new Vector3(0.3f, 0, 0.3f))
                        * new Vector2(image.GetWidth(), image.GetHeight()) / GetViewport().GetVisibleRect().Size;
                levels.Add(image.GetPixel((int)p.X, (int)p.Y).R);
            }
        float brightest = levels.Max();
        return levels.Count(v => v < brightest * 0.2f);
    }
}
