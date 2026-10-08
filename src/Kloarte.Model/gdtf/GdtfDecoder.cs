using System;
using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// Turns a moving head's DMX values into what it does (<see cref="MovingHeadState"/>), using its GDTF mode; and back:
/// the programmer sets an attribute's physical value (or a wheel slot) and gets the DMX bytes. Missing DMX values are
/// the channels' GDTF defaults. Colour: CMY subtracts, RGB(W, amber, lime...) adds, CTO is a colour temperature
/// (Kelvin, relative to the fixture's 8000 K), a colour wheel / macro slot filters. No Godot dependencies.
/// </summary>
public static class GdtfDecoder
{
    /// <summary>Decodes the state of a head patched at `start` (absolute address of its first channel).</summary>
    public static MovingHeadState Decode(GdtfFixture fixture, GdtfMode mode, IReadOnlyDictionary<int, int> dmx, int start)
    {
        var s = new MovingHeadState();
        float Phys(string attribute, float fallback)
        {
            var ch = mode.Channel(attribute);
            if (ch == null)
                return fallback;
            int v = ch.Read(dmx, start);
            var f = ch.FunctionAt(v);
            return f != null && f.Attribute.StartsWith(attribute[..Math.Min(attribute.Length, 4)]) ? f.Physical(v) : fallback;
        }
        s.Pan = Phys("Pan", 0);
        s.Tilt = Phys("Tilt", 0);
        s.Zoom = mode.Has("Zoom") ? Phys("Zoom", 0) : 0;
        s.Focus = Phys("Focus1", 0.5f);
        s.Iris = Math.Clamp(Phys("Iris", 1f), 0f, 1f);
        s.Frost = mode.Has("Frost1") ? Math.Clamp(Phys("Frost1", 0) , 0f, 1f) : 0;
        s.GoboRotation = Phys("Gobo1Pos", 0);
        s.ShaperRotation = Phys("ShaperRot", 0);
        for (int i = 0; i < 4; i++)
        {
            var a = mode.Channel($"Blade{i + 1}A");
            if (a != null)
            {
                var f = a.FunctionAt(a.Read(dmx, start));
                float max = Math.Max(f.PhysicalFrom, f.PhysicalTo);
                s.BladeIn[i] = max > 0 ? f.Physical(a.Read(dmx, start)) / max : 0;
            }
            s.BladeRotation[i] = Phys($"Blade{i + 1}Rot", 0);
        }

        // Colour.
        float r = 1, g = 1, b = 1;
        if (mode.Has("ColorSub_C") || mode.Has("ColorSub_M") || mode.Has("ColorSub_Y"))
        {
            r = 1 - Math.Clamp(Phys("ColorSub_C", 0), 0, 1);
            g = 1 - Math.Clamp(Phys("ColorSub_M", 0), 0, 1);
            b = 1 - Math.Clamp(Phys("ColorSub_Y", 0), 0, 1);
        }
        else if (mode.Has("ColorAdd_R"))
        {
            float R = Phys("ColorAdd_R", 0), G = Phys("ColorAdd_G", 0), B = Phys("ColorAdd_B", 0);
            float W = Phys("ColorAdd_W", 0), A = Phys("ColorAdd_RY", 0), L = Phys("ColorAdd_GY", 0);
            r = R + W + A + 0.5f * L;
            g = G + W + 0.6f * A + L;
            b = B + W;
            // Full white (all emitters full) = 1.
            float fullR = 1 + (mode.Has("ColorAdd_W") ? 1 : 0) + (mode.Has("ColorAdd_RY") ? 1 : 0) + (mode.Has("ColorAdd_GY") ? 0.5f : 0);
            float fullG = 1 + (mode.Has("ColorAdd_W") ? 1 : 0) + (mode.Has("ColorAdd_RY") ? 0.6f : 0) + (mode.Has("ColorAdd_GY") ? 1 : 0);
            float fullB = 1 + (mode.Has("ColorAdd_W") ? 1 : 0);
            r /= fullR;
            g /= fullG;
            b /= fullB;
        }
        if (mode.Has("CTO"))
        {
            float kelvin = Phys("CTO", 8000);
            var (kr, kg, kb) = Kelvin(kelvin);
            var (nr, ng, nb) = Kelvin(8000);
            float m = Math.Max(kr / nr, Math.Max(kg / ng, kb / nb));
            r *= kr / nr / m;
            g *= kg / ng / m;
            b *= kb / nb / m;
        }
        foreach (var ch in mode.Channels)
        {
            int v = ch.Read(dmx, start);
            var f = ch.FunctionAt(v);
            if (f == null || string.IsNullOrEmpty(f.Wheel) || !fixture.Wheels.TryGetValue(f.Wheel, out var slots))
                continue;
            int slot = f.SetAt(v)?.Slot ?? 0;
            if (slot <= 0 || slot >= slots.Count)
                continue;
            if (f.Attribute.StartsWith("Gobo"))
            {
                if (f.Attribute == "Gobo1" || f.Attribute.StartsWith("Gobo1"))
                {
                    s.GoboSlot = slot;
                    s.GoboMedia = slots[slot].Media;
                }
            }
            else if (f.Attribute.StartsWith("Color") && slot > 1)
            {
                var (sr, sg, sb) = slots[slot].Rgb;
                r *= sr;
                g *= sg;
                b *= sb;
            }
        }
        (s.R, s.G, s.B) = (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));

        var shutter = mode.Channel("Shutter1");
        if (shutter != null)
        {
            int v = shutter.Read(dmx, start);
            var f = shutter.FunctionAt(v);
            string set = f?.SetAt(v)?.Name ?? "";
            s.ShutterOpen = !(set.Contains("Closed", StringComparison.OrdinalIgnoreCase) && !set.Contains("Open", StringComparison.OrdinalIgnoreCase))
                            && !(f?.Name == "Open/Closed" && f.Physical(v) < 0.5f);
        }
        return s;
    }

    /// <summary>Writes an attribute's physical value (e.g. Pan = 45°) as DMX bytes; false if the mode has no such attribute.</summary>
    public static bool Set(GdtfMode mode, IDictionary<int, int> dmx, int start, string attribute, float physical)
    {
        var ch = mode.Channel(attribute);
        var f = ch?.Function(attribute) ?? ch?.Functions.FirstOrDefault();
        if (f == null)
            return false;
        ch.Write(dmx, start, f.Dmx(physical));
        return true;
    }

    /// <summary>Selects a wheel slot (1-based) on the channel of `attribute` (e.g. "Gobo1"); false if impossible.</summary>
    public static bool SetSlot(GdtfMode mode, IDictionary<int, int> dmx, int start, string attribute, int slot)
    {
        var ch = mode.Channel(attribute);
        var f = ch?.Functions.FirstOrDefault(fn => fn.Sets.Any(s => s.Slot == slot) && !string.IsNullOrEmpty(fn.Wheel));
        if (f == null)
            return false;
        int i = f.Sets.FindIndex(s => s.Slot == slot);
        int from = f.Sets[i].DmxFrom, to = i + 1 < f.Sets.Count ? f.Sets[i + 1].DmxFrom - 1 : f.DmxTo;
        ch.Write(dmx, start, (from + to) / 2);
        return true;
    }

    /// <summary>Opens the shutter (an "Open" set of the shutter channel), if there is one.</summary>
    public static void OpenShutter(GdtfMode mode, IDictionary<int, int> dmx, int start)
    {
        var ch = mode.Channel("Shutter1");
        var f = ch?.Functions.FirstOrDefault(fn => fn.Name.StartsWith("Open") && fn.Name != "Open/Closed")
                ?? ch?.Functions.FirstOrDefault();
        if (f == null)
            return;
        var open = f.Sets.FirstOrDefault(s => s.Name.Contains("Open", StringComparison.OrdinalIgnoreCase));
        ch.Write(dmx, start, open.Name != null ? open.DmxFrom : (f.DmxFrom + f.DmxTo) / 2);
    }

    /// <summary>Approximate RGB of a black body (Tanner Helland's fit), 0..1.</summary>
    public static (float R, float G, float B) Kelvin(float kelvin)
    {
        double t = Math.Clamp(kelvin, 1000, 40000) / 100.0;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return ((float)Math.Clamp(r / 255, 0, 1), (float)Math.Clamp(g / 255, 0, 1), (float)Math.Clamp(b / 255, 0, 1));
    }
}
