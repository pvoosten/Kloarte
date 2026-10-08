using System;
using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>What the console's patch says about a lamp (its head), as the scene needs it.</summary>
public sealed record LampPatch
{
    /// <summary>Head name.</summary>
    public string HeadName { get; init; } = "";

    /// <summary>Personality (fixture type) name, e.g. "Robe_RobinT1Profile_Mode1".</summary>
    public string Personality { get; init; } = "";

    /// <summary>Number of DMX channels of the personality.</summary>
    public int ChannelCount { get; init; }

    /// <summary>Channel names of the personality, in DMX order.</summary>
    public IReadOnlyList<string> ChannelNames { get; init; } = Array.Empty<string>();

    /// <summary>Gel id in the colour table (<see cref="IGelTable"/>; 0 = none).</summary>
    public int GelId { get; init; }

    /// <summary>Beam angle set in the patch (whole degrees; 0 = the fixture type's).</summary>
    public int BeamAngle { get; init; }

    /// <summary>Beam shape id set in the patch (0 = none).</summary>
    public int Shape { get; init; }
}
