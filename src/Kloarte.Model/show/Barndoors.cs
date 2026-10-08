using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>
/// Each flap: 0 = fully open (folded back), 1 = closed across the lens. Rotation of the whole barndoor ring.
/// </summary>
public sealed class Barndoors
{
    /// <summary>Top flap: 0 = open, 1 = closed.</summary>
    public float Top { get; set; }
    /// <summary>Right flap: 0 = open, 1 = closed.</summary>
    public float Right { get; set; }
    /// <summary>Bottom flap: 0 = open, 1 = closed.</summary>
    public float Bottom { get; set; }
    /// <summary>Left flap: 0 = open, 1 = closed.</summary>
    public float Left { get; set; }
    /// <summary>Rotation of the barndoor ring (degrees).</summary>
    public float Rotation { get; set; }
}
