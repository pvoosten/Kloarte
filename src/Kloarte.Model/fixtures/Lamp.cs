using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kloarte.Model;

/// <summary>A PAR lamp: oval beam (width x height in degrees, field) and peak intensity.</summary>
public sealed class Lamp
{
    /// <summary>Lamp id, e.g. "CP62".</summary>
    public string Id { get; set; } = "";
    /// <summary>Display name, e.g. "CP62 MFL".</summary>
    public string Name { get; set; } = "";
    /// <summary>Field angles of the oval beam: narrow and wide (degrees).</summary>
    public float[] Beam { get; set; } = { 20f, 20f };
    /// <summary>Peak intensity (cd).</summary>
    public float Candela { get; set; }
}
