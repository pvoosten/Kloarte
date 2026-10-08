using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>
/// Depth: 0 = out, 1 = to the beam axis, 2 = fully across. Angle: blade tilt in degrees, counter-clockwise.
/// Named after the beam edge they cut, seen from behind the fixture.
/// </summary>
public sealed partial class Shutters
{
    /// <summary>Blade cutting the top of the beam.</summary>
    public Blade Top { get; set; } = new();
    /// <summary>Blade cutting the right of the beam.</summary>
    public Blade Right { get; set; } = new();
    /// <summary>Blade cutting the bottom of the beam.</summary>
    public Blade Bottom { get; set; } = new();
    /// <summary>Blade cutting the left of the beam.</summary>
    public Blade Left { get; set; } = new();
    /// <summary>whole assembly, degrees</summary>
    public float Rotation { get; set; }

}
