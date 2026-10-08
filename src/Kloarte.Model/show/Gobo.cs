using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>The gobo in a lamp's gobo holder (mechanics).</summary>
public sealed class Gobo
{
    /// <summary>e.g. res://gobos/window.png</summary>
    public string Path { get; set; } = "";
    /// <summary>Rotation (degrees).</summary>
    public float Rotation { get; set; }
    /// <summary>Image width relative to the gate.</summary>
    public float Size { get; set; } = 1f;
}
