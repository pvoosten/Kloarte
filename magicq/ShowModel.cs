using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// The application's show model: everything from MagicQ (MagicQShow: patch, personalities, groups, MagicVis,
// cue stacks... lossless) plus what MagicQ cannot store: the mechanical set-up of each fixture (lamp type,
// optics, shutters, iris, gobo, barndoors, hand focus) and the blocking (actors and their moves per cue).
// The MagicQ part is saved back to .shw/.xhw, mechanics and blocking to separate JSON files. No Godot dependencies.
public sealed class ShowModel
{
    public MagicQShow MagicQ { get; private set; }
    public string ShwPath { get; private set; } = "";

    // Mechanics per fixture, keyed by absolute DMX address (head numbers are not unique in MagicVis).
    public Dictionary<int, FixtureMechanics> Mechanics { get; private set; } = new();

    // Actors and their changes per cue.
    public Blocking Blocking { get; private set; } = new();

    public static ShowModel Load(string shwPath, string mechanicsPath = null, string blockingPath = null)
    {
        var model = new ShowModel { MagicQ = MagicQShow.Load(shwPath), ShwPath = shwPath };
        if (mechanicsPath != null && File.Exists(mechanicsPath))
            model.Mechanics = JsonSerializer.Deserialize<MechanicsFile>(File.ReadAllText(mechanicsPath), Json)?.Fixtures
                              ?? new Dictionary<int, FixtureMechanics>();
        if (blockingPath != null && File.Exists(blockingPath))
            model.Blocking = JsonSerializer.Deserialize<Blocking>(File.ReadAllText(blockingPath), Json) ?? new Blocking();
        return model;
    }

    public void SaveBlocking(string blockingPath) =>
        File.WriteAllText(blockingPath, JsonSerializer.Serialize(Blocking, Json));

    public void SaveMagicQ(string shwPath) => MagicQ.Save(shwPath);

    public void SaveMechanics(string mechanicsPath)
    {
        var file = new MechanicsFile { Show = Path.GetFileName(ShwPath), Fixtures = Mechanics };
        File.WriteAllText(mechanicsPath, JsonSerializer.Serialize(file, Json));
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class MechanicsFile
    {
        public string Show { get; set; } = "";
        public Dictionary<int, FixtureMechanics> Fixtures { get; set; } = new();
    }
}

// Mechanical and optical set-up of one fixture, as rigged and focused by hand (not in MagicQ).
public sealed class FixtureMechanics
{
    public string Kind { get; set; } = "pc";          // profile, fresnel, pc, par64, flood, ledbeam...
    public float FieldAngle { get; set; } = 30f;      // degrees (zoom / spot-flood)
    public float Softness { get; set; } = 0.1f;       // edge blur (focus), in gate radii
    public float FieldFalloff { get; set; } = 0.6f;   // intensity at the beam edge relative to the centre
    public float Energy { get; set; } = 400f;
    public string Color { get; set; } = "#ffd19e";    // lamp colour incl. gel, HTML notation
    public Shutters Shutters { get; set; }            // profiles; null = none
    public float Iris { get; set; } = 1f;             // 1 = open
    public Gobo Gobo { get; set; }                    // null = none
    public Barndoors Barndoors { get; set; }          // PC / fresnel; null = none
    public float[] FocusPoint { get; set; }           // x, y, z in stage coordinates; null = from MagicVis rotation
}

// Depth: 0 = out, 1 = to the beam axis, 2 = fully across. Angle: blade tilt in degrees, counter-clockwise.
// Named after the beam edge they cut, seen from behind the fixture.
public sealed class Shutters
{
    public Blade Top { get; set; } = new();
    public Blade Right { get; set; } = new();
    public Blade Bottom { get; set; } = new();
    public Blade Left { get; set; } = new();
    public float Rotation { get; set; }               // whole assembly, degrees

    public sealed class Blade
    {
        public float Depth { get; set; }
        public float Angle { get; set; }
    }
}

public sealed class Gobo
{
    public string Path { get; set; } = "";            // e.g. res://gobos/window.png
    public float Rotation { get; set; }
    public float Size { get; set; } = 1f;
}

// Each flap: 0 = fully open (folded back), 1 = closed across the lens. Rotation of the whole barndoor ring.
public sealed class Barndoors
{
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }
    public float Left { get; set; }
    public float Rotation { get; set; }
}
