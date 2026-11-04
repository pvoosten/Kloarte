using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// The look of asymmetric cyc floods (ADB ACP1001): a 15 x 7.5 m cyc cloth, eight floods 2 m apart hung 1.5 m in front
/// of its top and one ground-row flood, in light haze. Screenshots tests/output/acp_front.png (from the audience),
/// acp_side.png (from the side: the light in the haze), acp_body.png (a flood up close) and acp_inside.png (its open
/// front from the cloth).
/// Run:  Godot_console.exe --path . --resolution 1280x720 res://tests/acp_look_test.tscn
/// </summary>
public partial class AcpLookTest : Node3D
{
    /// <summary>The cloth: its plane (z) and size.</summary>
    private const float ClothZ = -8f, ClothWidth = 15f, ClothHeight = 7.5f;

    /// <summary>The camera.</summary>
    private Camera3D _camera;

    /// <summary>Frames since the start.</summary>
    private int _frame;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://tests/output"));
        RenderingServer.GlobalShaderParameterSet("haze_density", ProjectLook.DensityOf(15));
        ShowBuilder.ConfigureShadowAtlas(GetViewport());
        // The app's own environment, with the haze at 15 % (HazeControl sets the fog density the same way).
        var environment = (Environment)GD.Load<Environment>("res://environment/theatre_environment.tres").Duplicate();
        environment.VolumetricFogDensity = ProjectLook.DensityOf(15);
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(20, 20) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.12f), Roughness = 1f },
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(ClothWidth, ClothHeight) },
            Position = new Vector3(0, ClothHeight / 2f, ClothZ),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.8f, 0.8f), Roughness = 0.95f },
        });
        for (int i = 0; i < 8; i++)
            AddFlood(new Vector3((i - 3.5f) * 2f, ClothHeight + 0.4f, ClothZ + 1.5f), ProfileFixture.FloodMounting.Top);
        AddFlood(new Vector3(5.5f, 0.25f, ClothZ + 1.5f), ProfileFixture.FloodMounting.Bottom);

        _camera = new Camera3D { Fov = 60f, Current = true };
        AddChild(_camera);
        Look(new Vector3(0, 3.5f, 9), new Vector3(0, 3.5f, ClothZ));
    }

    /// <summary>A flood aimed at the middle of the cloth in front of it.</summary>
    private void AddFlood(Vector3 position, ProfileFixture.FloodMounting mounting)
    {
        var lamp = new ProfileFixture
        {
            Lumens = 12000, FieldAngle = 170, FieldFalloff = 0.5f, Softness = 0.35f,
            Flood = mounting, Intensity = 1f, LampColor = new Color(1f, 0.85f, 0.65f),
        };
        AddChild(lamp);
        lamp.Position = position;
        lamp.FocusPoint = new Vector3(position.X, ClothHeight / 2f, ClothZ);
        lamp.AimAtFocus();
    }

    /// <summary>Puts the camera at `from`, looking at `at`.</summary>
    private void Look(Vector3 from, Vector3 at)
    {
        _camera.Position = from;
        _camera.LookAt(at, Vector3.Up);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        switch (++_frame)
        {
            case 40:
                Shot("acp_front.png");
                Look(new Vector3(11, 4, -1), new Vector3(0, 4, ClothZ + 0.5f));
                break;
            case 50:
                Shot("acp_side.png");
                Look(new Vector3(2.6f, 7.2f, ClothZ + 3.5f), new Vector3(1f, ClothHeight + 0.3f, ClothZ + 1.5f));
                _camera.Fov = 40f;
                break;
            case 60:
                Shot("acp_body.png");
                // From between the flood and the cloth: the open front and its lit reflector.
                Look(new Vector3(1.9f, ClothHeight - 0.2f, ClothZ + 0.3f), new Vector3(1f, ClothHeight + 0.4f, ClothZ + 1.5f));
                break;
            case 70:
                Shot("acp_inside.png");
                GD.Print("ACPLOOK DONE");
                GetTree().Quit();
                break;
        }
    }

    /// <summary>Saves the view.</summary>
    private void Shot(string name) =>
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://tests/output/" + name));
}
