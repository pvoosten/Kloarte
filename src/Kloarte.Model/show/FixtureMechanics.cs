using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>
/// Mechanical and optical set-up of one lamp, as rigged and focused by hand (not in MagicQ). Fixed for the whole show.
/// Only what the lamp's catalogue model has is used (FixtureCatalog); per-cue settings of moving heads are MagicQ
/// palettes, not mechanics.
/// </summary>
public sealed class FixtureMechanics
{
    /// <summary>catalogue model id (data/fixture_catalog.json)</summary>
    public string Model { get; set; }
    /// <summary>kind (old files: the only type information)</summary>
    public string Kind { get; set; } = "pc";
    /// <summary>degrees (zoom / spot-flood)</summary>
    public float FieldAngle { get; set; } = 30f;
    /// <summary>edge blur (focus), in gate radii</summary>
    public float Softness { get; set; } = 0.1f;
    /// <summary>intensity at the beam edge relative to the centre</summary>
    public float FieldFalloff { get; set; } = 0.6f;
    /// <summary>only for lamps without catalogue photometrics</summary>
    public float Energy { get; set; } = 400f;
    /// <summary>output trim (1 = catalogue value)</summary>
    public float Output { get; set; } = 1f;
    /// <summary>lamp colour, HTML notation (the gel comes from MagicQ)</summary>
    public string Color { get; set; } = "#ffd19e";
    /// <summary>profiles; null = none</summary>
    public Shutters Shutters { get; set; }
    /// <summary>1 = open</summary>
    public float Iris { get; set; } = 1f;
    /// <summary>null = none</summary>
    public Gobo Gobo { get; set; }
    /// <summary>PC / fresnel; null = none</summary>
    public Barndoors Barndoors { get; set; }
    /// <summary>PAR: lamp id (CP60, CP61, CP62...)</summary>
    public string Lamp { get; set; }
    /// <summary>PAR: rotation of the oval, degrees</summary>
    public float OvalRotation { get; set; }
    /// <summary>flood: "top" / "bottom"; moving head: "hanging" / "standing"</summary>
    public string Mounting { get; set; }
    /// <summary>x, y, z in stage coordinates; null = from MagicVis rotation</summary>
    public float[] FocusPoint { get; set; }

    /// <summary>The set-up a new lamp of a catalogue model gets (`type`: the model id or kind it was asked for).</summary>
    public static FixtureMechanics DefaultFor(FixtureModel model, string type) => new()
    {
        Model = model?.Id,
        Kind = model?.Kind ?? type,
        FieldAngle = model?.FieldAngle ?? 30f,
        Softness = model?.Softness ?? 0.1f,
        FieldFalloff = model?.Falloff ?? 0.6f,
        Color = model?.Color ?? "#ffd19e",
        Lamp = model?.DefaultLamp,
        Mounting = model?.Kind == FixtureKind.Flood ? "top" : model?.IsMoving == true ? "hanging" : null,
    };
}
