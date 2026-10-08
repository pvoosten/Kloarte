using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>The cyclorama (horizon cloth).</summary>
public sealed class Cyc
{
    /// <summary>hangs on this MagicVis truss (by name)...</summary>
    public string Truss { get; set; }
    /// <summary>...else at this stage z</summary>
    public float Z { get; set; } = -9f;
    /// <summary>relative to the truss (upstage = negative)</summary>
    public float Offset { get; set; } = -0.1f;
    /// <summary>Width (m).</summary>
    public float Width { get; set; } = 15f;
    /// <summary>Height (m).</summary>
    public float Height { get; set; } = 7.5f;
    /// <summary>Reflectance (0..1).</summary>
    public float Albedo { get; set; } = 0.8f;
}
