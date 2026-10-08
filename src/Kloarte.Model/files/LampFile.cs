namespace Kloarte.Model;

/// <summary>
/// One lamp's file in the project's <c>mechanics/</c> folder (user: one file per lamp). <see cref="RigItem"/> is the key,
/// the file name is only for reading. Only the values that differ from the lamp model's defaults
/// (<see cref="FixtureMechanics.DefaultFor"/>) are written; a missing value is the default (<see cref="MechanicsDiff"/>).
/// </summary>
public sealed class LampFile
{
    /// <summary>The lamp: its MagicVis fixture id.</summary>
    [FileField("The lamp: the id of its MagicVis fixture (H record) in the show. The key of this file; the file name is only for reading.", Min = 1)]
    public int RigItem { get; set; }

    /// <summary>Catalogue model id.</summary>
    [FileField("Catalogue model (data/fixture_catalog.json), e.g. adb-c103")]
    public string Model { get; set; }

    /// <summary>Kind, for lamps without a catalogue model (old files).</summary>
    [FileField("Kind, only for a lamp without a catalogue model", Values = "profile,fresnel,pc,par,flood,ledbar,moving")]
    public string Kind { get; set; }

    /// <summary>Beam angle (degrees).</summary>
    [FileField("Beam (field) angle: the zoom or spot-flood", Unit = "°", Min = 1, Max = 120)]
    public double? FieldAngle { get; set; }

    /// <summary>Edge softness in gate radii.</summary>
    [FileField("Edge softness (focus) in gate radii: 0 = a hard edge", Min = 0, Max = 1)]
    public double? Softness { get; set; }

    /// <summary>Intensity at the beam edge relative to the centre.</summary>
    [FileField("Intensity at the beam edge relative to the centre", Min = 0, Max = 1)]
    public double? FieldFalloff { get; set; }

    /// <summary>Light energy for lamps without catalogue photometrics.</summary>
    [FileField("Light energy, only for a lamp without catalogue photometrics", Min = 0)]
    public double? Energy { get; set; }

    /// <summary>Output trim.</summary>
    [FileField("Output trim: 1 = the catalogue value", Min = 0, Max = 4)]
    public double? Output { get; set; }

    /// <summary>Lamp colour "#rrggbb".</summary>
    [FileField("Lamp colour \"#rrggbb\" (the gel comes from MagicQ)", Quoted = true)]
    public string Color { get; set; }

    /// <summary>Framing shutters.</summary>
    [FileField("Framing shutters (profiles), named after the beam edge they cut, seen from behind the lamp")]
    public LampFileShutters Shutters { get; set; }

    /// <summary>Iris, 1 = open.</summary>
    [FileField("Iris: 1 = open", Min = 0, Max = 1)]
    public double? Iris { get; set; }

    /// <summary>Gobo in the gobo holder.</summary>
    [FileField("The gobo in the gobo holder")]
    public LampFileGobo Gobo { get; set; }

    /// <summary>Barndoors.</summary>
    [FileField("Barndoors (PC / fresnel)")]
    public LampFileBarndoors Barndoors { get; set; }

    /// <summary>PAR lamp id.</summary>
    [FileField("PAR lamp: CP60, CP61, CP62...")]
    public string Lamp { get; set; }

    /// <summary>PAR oval rotation (degrees).</summary>
    [FileField("PAR: rotation of the oval", Unit = "°", Min = -360, Max = 360)]
    public double? OvalRotation { get; set; }

    /// <summary>Mounting of a flood or moving head.</summary>
    [FileField("Flood: top / bottom; moving head: hanging / standing", Values = "top,bottom,hanging,standing")]
    public string Mounting { get; set; }

    /// <summary>Focus point x, y, z (stage coordinates, m).</summary>
    [FileField("Where the lamp is focused: [x, y, z] in stage coordinates (x to the audience's right, y up, z towards the audience)", Unit = "m")]
    public double[] FocusPoint { get; set; }
}
