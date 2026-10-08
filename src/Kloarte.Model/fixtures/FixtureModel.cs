using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kloarte.Model;

/// <summary>A fixture type of the catalogue: light output, optics and which mechanical settings it has.</summary>
public sealed class FixtureModel
{
    /// <summary>Catalogue id, e.g. "adb-ds104".</summary>
    public string Id { get; set; } = "";
    /// <summary>Display name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Short name for lists and labels ("C103", "T1"); the id when the catalogue has none.</summary>
    public string Short { get; set; } = "";
    /// <summary>The short name, else the id.</summary>
    public string ShortName => string.IsNullOrEmpty(Short) ? Id : Short;
    /// <summary>Fixture kind (FixtureKind).</summary>
    public string Kind { get; set; } = FixtureKind.Pc;
    /// <summary>Lamp power (W).</summary>
    public int Watts { get; set; }
    /// <summary>how many the house has</summary>
    public int Count { get; set; }

    /// <summary>Light output through the lens (lm); fixed-beam lamps (PAR) give candela per lamp instead.</summary>
    public float Lumens { get; set; }
    /// <summary>field angle range in degrees; null = fixed</summary>
    public float[] Zoom { get; set; }
    /// <summary>default / fixed field angle</summary>
    public float FieldAngle { get; set; } = 30f;
    /// <summary>field edge relative to the centre</summary>
    public float Falloff { get; set; } = 0.6f;
    /// <summary>default edge softness (focus)</summary>
    public float Softness { get; set; } = 0.1f;
    /// <summary>edge softness can be set (profiles)</summary>
    public bool Focus { get; set; }
    /// <summary>lamp colour (3200 K tungsten by default)</summary>
    public string Color { get; set; } = "#ffd19e";

    /// <summary>Has framing shutters.</summary>
    public bool Shutters { get; set; }
    /// <summary>Has an iris.</summary>
    public bool Iris { get; set; }
    /// <summary>"A", "B" or null = no gobo holder</summary>
    public string GoboSize { get; set; }
    /// <summary>Takes barndoors.</summary>
    public bool Barndoors { get; set; }
    /// <summary>cyc flood with an asymmetric reflector</summary>
    public bool Asymmetric { get; set; }
    /// <summary>PAR: choice of lamps (beam shape + candela)</summary>
    public List<Lamp> Lamps { get; set; }
    /// <summary>Lamp id used when a lamp has none saved (PAR).</summary>
    public string DefaultLamp { get; set; }
    /// <summary>moving heads: GDTF file name prefix in data/gdtf</summary>
    public string Gdtf { get; set; }
    /// <summary>
    /// moving heads: MagicQ personality name (P record), used to add one to a show that has none (from
    /// data/magicq_templates.shw); conventionals are generic dimmers (<see cref="Personality"/>).
    /// </summary>
    public string MagicQ { get; set; }
    /// <summary>The MagicQ personality of the type: <see cref="MagicQ"/>, else "generic_dimmer".</summary>
    public string Personality => string.IsNullOrEmpty(MagicQ) ? "generic_dimmer" : MagicQ;

    /// <summary>True when the photometric values are estimates, not datasheet values.</summary>
    public bool Estimated { get; set; }
    /// <summary>Where the values come from.</summary>
    public string Source { get; set; } = "";

    /// <summary>True when the field angle can be changed (zoom / spot-flood).</summary>
    public bool HasZoom => Zoom is { Length: 2 } && Zoom[1] > Zoom[0];
    /// <summary>True for a moving head.</summary>
    public bool IsMoving => Kind == FixtureKind.Moving;
    /// <summary>The lamp with this id, else the default lamp, else the first (PAR); null without lamps.</summary>
    public Lamp FindLamp(string id) =>
        Lamps?.FirstOrDefault(l => l.Id == id) ?? Lamps?.FirstOrDefault(l => l.Id == DefaultLamp) ?? Lamps?.FirstOrDefault();
}
