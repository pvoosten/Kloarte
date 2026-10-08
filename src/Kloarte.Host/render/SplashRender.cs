using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Host;

/// <summary>
/// Renders the boot splash (<c>splash.png</c>) with Kloarte itself: five actors in a row, the title on their shirts, each lit
/// by a lamp with a random gel colour, in haze. Run by <c>tools/make_splash.ps1</c>:
/// <c>--path src/Kloarte.Host --resolution 1280x720 res://render/splash_render.tscn -- --out=&lt;file.png&gt; --seed=11</c>.
/// </summary>
public partial class SplashRender : Node3D
{
    /// <summary>The words on the shirts, in reading order.</summary>
    private static readonly string[] Names = { "Kloarte", "up", "de", "Plankn", "ant loaden..." };

    /// <summary>Frames to wait before the picture is taken (beam masks are rendered and read back, the shirts rendered).</summary>
    private const int SettleFrames = 90;

    private string _out = "res://splash.png";
    private int _frame;

    /// <summary>Builds the stage: venue, five actors and their lamps, the camera.</summary>
    public override void _Ready()
    {
        try
        {
            Build();
        }
        catch (System.Exception e)
        {
            GD.PushError($"Splash failed: {e}");
            GetTree().Quit(1);
        }
    }

    /// <summary>Builds the scene.</summary>
    private void Build()
    {
        int seed = 11;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--out="))
                _out = arg[6..];
            else if (arg.StartsWith("--seed=") && int.TryParse(arg[7..], out int s))
                seed = s;
        }
        var random = new RandomNumberGenerator { Seed = (ulong)seed };

        AddChild(new WorldEnvironment { Environment = GD.Load<Environment>("res://environment/theatre_environment.tres") });
        AddChild(new HostVenueBuilder { Name = "Venue" });
        RenderingServer.GlobalShaderParameterSet("haze_density", ProjectLook.DensityOf(45));

        var skin = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.45f, 0.37f), Roughness = 0.6f };
        var clothes = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.3f), Roughness = 0.9f };
        var furniture = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), Roughness = 1f };

        // Evenly spread hues, shuffled and jittered: random colours that don't repeat.
        var hues = Enumerable.Range(0, Names.Length).Select(i => (i + 0.5f) / Names.Length).OrderBy(_ => random.Randf()).ToArray();
        _labelColours = new Color[Names.Length];
        const float spacing = 1.9f, z = -2.5f;
        for (int i = 0; i < Names.Length; i++)
        {
            float x = (i - (Names.Length - 1) / 2f) * spacing;
            var colour = Color.FromHsv((hues[i] + random.RandfRange(-0.04f, 0.04f) + 1f) % 1f, 0.85f, 1f);
            _labelColours[i] = colour.Lerp(Colors.White, 0.45f);
            var def = new ActorDef { Id = $"splash{i}", Name = Names[i], Female = i % 2 == 1 };
            var actor = Actor.Create(def, skin, clothes, furniture, "303030");
            actor.Position = new Vector3(x, 0, z);
            AddChild(actor);
            AddChild(new HostProfileFixture
            {
                Name = $"Lamp{i}",
                Position = new Vector3(x, 7.2f, z + 1.2f),
                FieldAngle = 16f,
                Softness = 0.06f,
                Lumens = 5000f,
                GelColor = colour,
                FocusPoint = new Vector3(x, 1.0f, z),
            });
        }

        var camera = new Camera3D { Fov = 34f, Current = true };
        AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0, 1.45f, 6.5f), new Vector3(0, 1.25f, z));
        _camera = camera;
        _z = z;
        _spacing = spacing;
    }

    private Camera3D _camera;
    private Color[] _labelColours;
    private float _z, _spacing;

    /// <summary>The words again under each actor in large letters (the shirts are small in a picture of this size).</summary>
    private void AddNames()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        for (int i = 0; i < Names.Length; i++)
        {
            float x = (i - (Names.Length - 1) / 2f) * _spacing;
            var at = _camera.UnprojectPosition(new Vector3(x, 0, _z));
            var label = new Label
            {
                Text = Names[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                Size = new Vector2(250, 60),
                Position = new Vector2(at.X - 125, at.Y + 62),
                LabelSettings = new LabelSettings { FontSize = i == Names.Length - 1 ? 34 : 46, FontColor = _labelColours[i] },
            };
            layer.AddChild(label);
        }
    }

    /// <summary>Waits for the scene to settle, saves the picture and quits.</summary>
    public override void _Process(double delta)
    {
        if (_frame == 1)
            AddNames();
        if (++_frame < SettleFrames && _frame < 1000)
            return;
        var image = GetViewport().GetTexture().GetImage();
        if (image.GetSize() != new Vector2I(1280, 720))
            image.Resize(1280, 720, Image.Interpolation.Lanczos);
        string path = _out.StartsWith("res://") ? ProjectSettings.GlobalizePath(_out) : _out;
        GD.Print($"SPLASH {(image.SavePng(path) == Error.Ok ? "saved" : "NOT saved")} {path}");
        GetTree().Quit();
    }
}
