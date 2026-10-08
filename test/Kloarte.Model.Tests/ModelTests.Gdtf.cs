using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>ModelTests: tests from ModelTests.Gdtf.cs (converted from the Godot model tests).</summary>
public partial class ModelTests
{
    /// <summary>GDTF fixtures: modes, channels, DMX decoding.</summary>
    [Fact]
    public void GdtfTest()
    {
        string folder = TestEnvironment.File(GdtfFixture.DefaultFolder);
        var beam = GdtfFixture.Load(GdtfFixture.Find(folder, "Robe_Lighting@Robin_LEDBeam_150"));
        var t1 = GdtfFixture.Load(GdtfFixture.Find(folder, "Robe_Lighting@Robin_T1_Profile"));
        Check(beam.Modes.Any(m => m.ChannelCount == 22) && t1.Modes.Count(m => m.ChannelCount == 49) == 2 && t1.Modes.Any(m => m.ChannelCount == 53),
              "modes: LEDBeam 22 ch, T1 49 (CMY + RGB) and 53 ch");
        // The MagicQ personalities of the shows choose the mode.
        var show = MagicQShow.Load(Show("Risquons-Tout_RT2025_044.shw"));
        var p = show.Personalities.First(x => x.Name.Contains("T1"));
        var mode = t1.ModeFor(p.ChannelCount, p.Channels.Select(c => c.Name));
        Check(mode?.Name == "Mode 1 - CMY" && mode.Channel("Dimmer").Offsets[0] - 1 == p.IntensityChannel,
              $"T1 personality -> {mode?.Name}, dimmer offset = MagicQ intensity channel {p.IntensityChannel}");
        var bp = MagicQShow.Load(Show("RT2025_031.shw")).Personalities.First(x => x.Name.Contains("LEDBeam"));
        var bmode = beam.ModeFor(bp.ChannelCount, bp.Channels.Select(c => c.Name));
        Check(bmode != null && bmode.Channel("Dimmer").Offsets[0] - 1 == bp.IntensityChannel, $"LEDBeam -> {bmode?.Name}");
        Check(beam.LuminousFlux > 1000 && t1.LuminousFlux > 5000, $"flux from GDTF: LEDBeam {beam.LuminousFlux} lm, T1 {t1.LuminousFlux} lm");

        // Defaults: pan / tilt centred, open.
        const int start = 513;
        var empty = new Dictionary<int, int>();
        var s0 = GdtfDecoder.Decode(t1, mode, empty, start);
        Check(Math.Abs(s0.Pan) < 0.5f && Math.Abs(s0.Tilt) < 0.5f && s0.ShutterOpen == (s0.ShutterOpen) && s0.R > 0.9f && s0.GoboSlot <= 1,
              $"defaults: pan {s0.Pan:0.0}, tilt {s0.Tilt:0.0}, colour ({s0.R:0.00},{s0.G:0.00},{s0.B:0.00})");

        // Encoding and decoding round trips.
        var dmx = new Dictionary<int, int>();
        GdtfDecoder.Set(mode, dmx, start, "Pan", 90f);
        GdtfDecoder.Set(mode, dmx, start, "Tilt", -45f);
        GdtfDecoder.Set(mode, dmx, start, "ColorSub_C", 1f);
        GdtfDecoder.Set(mode, dmx, start, "Zoom", 20f);
        GdtfDecoder.SetSlot(mode, dmx, start, "Gobo1", 3);
        GdtfDecoder.OpenShutter(mode, dmx, start);
        var s = GdtfDecoder.Decode(t1, mode, dmx, start);
        Check(Math.Abs(s.Pan - 90f) < 0.1f && Math.Abs(s.Tilt + 45f) < 0.1f, $"pan / tilt round trip: {s.Pan:0.00} / {s.Tilt:0.00}");
        // The T1's CTO defaults to DMX 110 (~5600 K): the default white is a little warm.
        Check(s.R < 0.02f && Math.Abs(s.G - s0.G) < 0.02f && Math.Abs(s.B - s0.B) < 0.02f, $"full cyan removes red: ({s.R:0.00},{s.G:0.00},{s.B:0.00})");
        Check(Math.Abs(s.Zoom - 20f) < 0.2f, $"zoom {s.Zoom:0.0}°");
        Check(s.GoboSlot == 3 && t1.Media(s.GoboMedia) != null, $"gobo slot 3 = {s.GoboMedia} with its image");
        Check(s.ShutterOpen, "shutter open");
        var closed = new Dictionary<int, int>(dmx) { [start + mode.Channel("Shutter1").Offsets[0] - 1] = 0 };
        Check(!GdtfDecoder.Decode(t1, mode, closed, start).ShutterOpen, "shutter closed at DMX 0");
        GdtfDecoder.Set(mode, dmx, start, "CTO", 2700f);
        var warm = GdtfDecoder.Decode(t1, mode, dmx, start);
        Check(warm.B < warm.G, "CTO 2700 K is warm");

        // LEDBeam RGBW: all emitters full = white; red only = red.
        var rgbw = new Dictionary<int, int>();
        foreach (var a in new[] { "ColorAdd_R", "ColorAdd_G", "ColorAdd_B", "ColorAdd_W" })
            GdtfDecoder.Set(bmode, rgbw, 1, a, 1f);
        var white = GdtfDecoder.Decode(beam, bmode, rgbw, 1);
        Check(white.R > 0.95f && white.G > 0.95f && white.B > 0.95f, $"RGBW full = white ({white.R:0.00},{white.G:0.00},{white.B:0.00})");
        var red = new Dictionary<int, int>();
        GdtfDecoder.Set(bmode, red, 1, "ColorAdd_R", 1f);
        foreach (var a in new[] { "ColorAdd_G", "ColorAdd_B", "ColorAdd_W" })
            GdtfDecoder.Set(bmode, red, 1, a, 0f);   // the GDTF defaults are full (white)
        var r = GdtfDecoder.Decode(beam, bmode, red, 1);
        Check(r.R > 0.4f && r.G < 0.05f && r.B < 0.05f, $"red only ({r.R:0.00},{r.G:0.00},{r.B:0.00})");
    }
}
