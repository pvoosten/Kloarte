using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// Tests of the model (headless, no Godot; converted from res://tests/model_tests.tscn).
/// Reads the MagicQ show folder, writes only to test/output/model.
/// </summary>
public partial class ModelTests : TestBase
{
    /// <summary>Output to test/output/model.</summary>
    public ModelTests() : base("model") { }

    /// <summary>The fixture catalogue: models, kinds, PAR lamps, capabilities.</summary>
    [Fact]
    public void Catalogue()
    {
        var c = FixtureCatalog.Load(TestEnvironment.File(FixtureCatalog.DefaultPath));
        Check(c.Models.Count >= 14, $"catalogue has {c.Models.Count} models");
        Check(c.Resolve("profile")?.Id == "adb-ds104" && c.Resolve("adb-c203")?.Kind == FixtureKind.Pc, "kinds and ids resolve");
        Check(c.Find("par64").FindLamp(null).Id == "CP62" && c.Find("par64").FindLamp("CP60").Candela > 0, "PAR lamps");
        Check(c.Models.All(m => FixtureKind.All.Contains(m.Kind)), "every model has a known kind");
        Check(c.Find("adb-ds104").Shutters && !c.Find("adb-c103").Shutters && c.Find("adb-c103").Barndoors,
              "capabilities: profile has shutters, PC has barndoors");
    }

    /// <summary>Mechanics files keyed by DMX address (version 1) are migrated to rig item ids.</summary>
    [Fact]
    public void MechanicsMigration()
    {
        // Version 1 files are keyed by DMX address; every lamp on that address gets the entry.
        string shw = Show("Risquons-Tout_RT2025_021.shw");
        string path = Path.Combine(_out, "mechanics_v1.json");
        File.WriteAllText(path, "{ \"show\": \"x\", \"fixtures\": { \"400\": { \"kind\": \"pc\", \"fieldAngle\": 44 } } }");
        var model = ShowModel<MagicQShow>.Load(MagicQShow.Load(shw), shw, path);
        var lamps = model.Show.Vis.Where(v => v.Kind == MagicQShow.VisKind.Fixture && v.Address == 400).ToList();
        Check(lamps.Count == 6 && lamps.All(l => model.Mechanics.TryGetValue(l.Id, out var m) && m.FieldAngle == 44),
              $"v1 mechanics of address 400 go to its {lamps.Count} lamps");
        model.Mechanics[lamps[0].Id].FieldAngle = 12;
        string v2 = Path.Combine(_out, "mechanics_v2.json");
        model.SaveMechanics(v2);
        var again = ShowModel<MagicQShow>.Load(MagicQShow.Load(shw), shw, v2);
        Check(again.Mechanics[lamps[0].Id].FieldAngle == 12 && again.Mechanics[lamps[1].Id].FieldAngle == 44,
              "v2 mechanics are per lamp");
    }

    /// <summary>Light strength from lumens, beam angle and falloff.</summary>
    [Fact]
    public void PhotometricsTest()
    {
        double a = Photometrics.PeakCandela(6500, 15, 0.85), b = Photometrics.PeakCandela(6500, 31, 0.85);
        Check(Math.Abs(a / b - Photometrics.SolidAngle(31) / Photometrics.SolidAngle(15)) < 1e-9, "narrower = brighter by the solid-angle ratio");
        Check(Math.Abs(Photometrics.Lux(1000, 2) - 250) < 1e-9, "inverse square");
        double omega = FloodDistribution.EffectiveSolidAngle();
        Check(omega > 0.05 && omega < 2 * Math.PI, $"flood effective solid angle {omega:0.000} sr");
        Check(FloodDistribution.Relative(0, 60) > FloodDistribution.Relative(0, 0) && FloodDistribution.Relative(0, -40) < 0.01,
              "flood throws towards the far end, not the near edge");
    }

    /// <summary>The venue's auditorium: rows, seats, eye positions.</summary>
    [Fact]
    public void VenueSeats()
    {
        var venue = Venue.Load(TestEnvironment.File("res://venue/ccdebrouckere.venue.yaml"));
        var rows = venue.Auditorium.Layout(venue.Forestage);
        Check(rows.Count == 21 && rows.Sum(r => r.Seats.Count) == 415, $"{rows.Count} rows, {rows.Sum(r => r.Seats.Count)} seats (tech sheet: 415)");
        var a = rows[0];
        Check(a.Name == "A" && Math.Abs(a.FloorY + 1f) < 1e-4 && a.Z > venue.Forestage, "row A 1 m below the stage, behind the stage edge");
        Check(a.Seats.First(s => s.Number == 20).X < 0 && a.Seats.First(s => s.Number == 19).X > 0, "even numbers house left, 20/19 in the middle");
        Check(Math.Abs(rows[1].FloorY - rows[0].FloorY - venue.Auditorium.Rake) < 1e-4, "rake per row");
        Check(rows[1].Seats.Min(s => s.X) > rows[0].Seats.Min(s => s.X), "row B is staggered");
        Check(rows.First(r => r.Name == "U").Seats.Count == 17, "row U has 17 seats");
        var regie = venue.Auditorium.Viewpoints.First(v => v.Name == "Regie");
        var eye = venue.Auditorium.EyeOf(venue.Forestage, regie);
        Check(eye != null && Math.Abs(eye[0]) < 0.3f && eye[1] > 1f, $"Regie eye at {eye?[0]:0.00}, {eye?[1]:0.00}, {eye?[2]:0.00}");
        Check(venue.Auditorium.IsReserved("N", 17) && !venue.Auditorium.IsReserved("M", 17), "control seats reserved");
        Check(venue.MaxHeight("LT 11") == 7.5f && venue.MaxHeight("T 3") == 8f && venue.MaxHeight("Portaalbrug") == 6.1f,
              "truss height limits from the hoist plan");
        string copy = Path.Combine(_out, "venue.json");
        venue.Save(copy);
        Check(Venue.Load(copy).Auditorium.Layout(venue.Forestage).Sum(r => r.Seats.Count) == 415, "venue JSON round trip");
    }
}
