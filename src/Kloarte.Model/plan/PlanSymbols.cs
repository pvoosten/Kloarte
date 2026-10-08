using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Kloarte.Model;

/// <summary>
/// Plan symbols per fixture kind, in the style of lighting plots: a profile is a long body with a lens barrel, a PC a
/// short body with a flat lens, a fresnel the same with a stepped lens, a PAR a round-backed can, a flood a wide box
/// with a slanted (asymmetric) front, an LED bar a long thin bar, a moving head a round base with its head. The symbol
/// points in the beam direction (plan) and is filled with the gel colour (white = open white). Sizes in metres (real
/// fixture size), multiplied by `scale`. No Godot dependencies.
/// </summary>
public static class PlanSymbols
{
    /// <summary>Display name of a kind for the legend.</summary>
    private static string KindName(string kind) => kind switch
    {
        FixtureKind.Profile => "Profile spot",
        FixtureKind.Fresnel => "Fresnel",
        FixtureKind.Pc => "PC",
        FixtureKind.Par => "PAR",
        FixtureKind.Flood => "Flood (asymmetric)",
        FixtureKind.LedBar => "LED bar",
        FixtureKind.Moving => "Moving head",
        _ => kind,
    };

    /// <summary>Draws the symbol of `kind` at `centre`, pointing along `angle` (radians, plan; 0 = +x).</summary>
    public static void Draw(IPlanCanvas c, string kind, bool small, Vector2 centre, float angle, Rgb? fill, float scale = 1f)
    {
        float s = scale * (small ? 0.6f : 1f);
        var rot = Matrix3x2.CreateRotation(angle);
        Vector2 P(float u, float v) => centre + Vector2.Transform(new Vector2(u, v) * s, rot);
        List<Vector2> Rect(float u0, float u1, float v0, float v1) => new() { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) };
        var f = fill ?? Rgb.White;
        var ink = Rgb.Black;
        float w = 1f;

        switch (kind)
        {
            case FixtureKind.Profile:
                c.Polygon(Rect(-0.32f, 0.12f, -0.1f, 0.1f), f, ink, w);
                c.Polygon(Rect(0.12f, 0.3f, -0.07f, 0.07f), f, ink, w);
                c.Polyline(new[] { P(0.04f, -0.14f), P(0.04f, 0.14f) }, ink, 2f * w);   // shutter plane
                break;
            case FixtureKind.Pc:
            case FixtureKind.Fresnel:
                c.Polygon(Rect(-0.2f, 0.12f, -0.15f, 0.15f), f, ink, w);
                if (kind == FixtureKind.Pc)
                    c.Polygon(Rect(0.12f, 0.16f, -0.17f, 0.17f), ink, ink, w);
                else
                {
                    var lens = new List<Vector2>();
                    for (int i = 0; i <= 6; i++)
                        lens.Add(P(0.13f + (i % 2) * 0.04f, -0.16f + i * 0.32f / 6f));
                    c.Polyline(lens, ink, 1.5f * w);
                }
                break;
            case FixtureKind.Par:
            {
                var can = new List<Vector2> { P(0.14f, -0.13f), P(0.14f, 0.13f), P(-0.08f, 0.13f) };
                for (int i = 0; i <= 8; i++)
                {
                    float a = MathF.PI / 2f + i * MathF.PI / 8f;
                    can.Add(P(-0.08f + 0.13f * MathF.Cos(a), 0.13f * MathF.Sin(a)));
                }
                can.Add(P(-0.08f, -0.13f));
                c.Polygon(can, f, ink, w);
                c.Polyline(new[] { P(0.14f, -0.15f), P(0.14f, 0.15f) }, ink, 2.5f * w);
                break;
            }
            case FixtureKind.Flood:
                c.Polygon(new List<Vector2> { P(-0.12f, -0.22f), P(0.1f, -0.22f), P(0.17f, 0.22f), P(-0.12f, 0.22f) }, f, ink, w);
                c.Polyline(new[] { P(0.1f, -0.22f), P(0.17f, 0.22f) }, ink, 2f * w);
                break;
            case FixtureKind.LedBar:
                c.Polygon(Rect(-0.06f, 0.06f, -0.3f, 0.3f), f, ink, w);
                for (int i = -2; i <= 2; i++)
                    c.Circle(P(0.03f, i * 0.11f), 0.025f * s, ink, null);
                break;
            case FixtureKind.Moving:
                c.Circle(centre, 0.2f * s, Rgb.Grey(0.85f), ink, w);
                c.Polygon(Rect(-0.1f, 0.2f, -0.1f, 0.1f), f, ink, w);
                c.Polyline(new[] { P(-0.02f, -0.16f), P(-0.02f, 0.16f) }, ink, 2f * w);   // yoke
                break;
            default:
                c.Circle(centre, 0.15f * s, f, ink, w);
                break;
        }
    }

    /// <summary>Plan angle (radians) from a lamp to its focus point; upstage (-z) when it points straight down.</summary>
    public static float Angle(PlanLamp lamp)
    {
        if (lamp.Focus is { } f && Vector2.Distance(f, lamp.Position) > 0.05f)
            return MathF.Atan2(f.Y - lamp.Position.Y, f.X - lamp.Position.X);
        return -MathF.PI / 2f;
    }

    /// <summary>Distinct lamp types of a plan (for the legend): kind, small, model name, count.</summary>
    public static IEnumerable<(string Kind, bool Small, string Name, int Count)> Types(PlanData data) =>
        data.Lamps.GroupBy(l => (l.Kind, l.Small, l.ModelName))
            .OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.ModelName)
            .Select(g => (g.Key.Kind, g.Key.Small, g.Key.ModelName == "" ? KindName(g.Key.Kind) : g.Key.ModelName, g.Count()));

    /// <summary>Distinct gels of a plan (for the legend): code, name, colour, count; open white last.</summary>
    public static IEnumerable<(string Code, string Name, Rgb? Colour, int Count)> Gels(PlanData data) =>
        data.Lamps.GroupBy(l => l.GelCode)
            .OrderBy(g => g.Key == "" ? "~" : g.Key)
            .Select(g => (g.Key == "" ? "O/W" : g.Key, g.Key == "" ? "open white" : g.First().GelName, g.First().Gel, g.Count()));
}
