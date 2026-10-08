namespace Kloarte.Model;

/// <summary>Fixture kinds of the catalogue; the kind decides the plan symbol and which settings a lamp has.</summary>
public static class FixtureKind
{
    /// <summary>Profile spot (zoom, focus, shutters, iris, gobo).</summary>
    public const string Profile = "profile";

    /// <summary>Fresnel (spot-flood, barndoors).</summary>
    public const string Fresnel = "fresnel";

    /// <summary>Pebble convex (spot-flood, barndoors).</summary>
    public const string Pc = "pc";

    /// <summary>PAR can (lamp choice: oval beam).</summary>
    public const string Par = "par";

    /// <summary>Flood, e.g. an asymmetric cyc light.</summary>
    public const string Flood = "flood";

    /// <summary>LED bar with a fixed lens.</summary>
    public const string LedBar = "ledbar";

    /// <summary>Moving head (GDTF).</summary>
    public const string Moving = "moving";

    /// <summary>All kinds.</summary>
    public static readonly string[] All = { Profile, Fresnel, Pc, Par, Flood, LedBar, Moving };
}
