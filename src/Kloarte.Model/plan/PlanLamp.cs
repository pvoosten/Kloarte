using System.Numerics;

namespace Kloarte.Model;

/// <summary>A lamp in the plan (snapshot of a MagicVis fixture with its patch, type and gel).</summary>
public sealed class PlanLamp
{
    /// <summary>MagicVis id (the lamp's identity).</summary>
    public int VisId { get; init; }

    /// <summary>Plan position (stage x, stage z).</summary>
    public Vector2 Position { get; init; }

    /// <summary>Height above the stage floor.</summary>
    public float Height { get; init; }

    /// <summary>Where the beam lands, in plan (null: points straight down or unknown).</summary>
    public Vector2? Focus { get; init; }

    /// <summary>Fixture kind (FixtureKind) for the symbol.</summary>
    public string Kind { get; init; } = FixtureKind.Pc;

    /// <summary>Catalogue model id ("" if unknown) and display name.</summary>
    public string Model { get; init; } = "";

    /// <summary>Model name for the legend.</summary>
    public string ModelName { get; init; } = "";

    /// <summary>Small fixture (PAR36 pinspot, LED bar): smaller symbol.</summary>
    public bool Small { get; init; }

    /// <summary>Gel colour (normalised), or null for open white.</summary>
    public Rgb? Gel { get; init; }

    /// <summary>Gel code ("L201", "R132") or "" for open white.</summary>
    public string GelCode { get; init; } = "";

    /// <summary>Gel name from MagicQ's colour table.</summary>
    public string GelName { get; init; } = "";

    /// <summary>MagicQ head number.</summary>
    public int Head { get; init; }

    /// <summary>Absolute DMX address (1-based).</summary>
    public int Address { get; init; }

    /// <summary>Id of the support it hangs on (0 = none).</summary>
    public int Support { get; init; }

    /// <summary>Selected in the shared selection.</summary>
    public bool Selected { get; init; }
}
