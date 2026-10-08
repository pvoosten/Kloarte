using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>A named seat (or the middle between seats) to look from.</summary>
public sealed class Viewpoint
{
    /// <summary>Name, e.g. "Regie".</summary>
    public string Name { get; set; } = "";
    /// <summary>Row letter.</summary>
    public string Row { get; set; } = "";
    /// <summary>Seat numbers (their eye positions are averaged).</summary>
    public List<int> Seats { get; set; } = new();
}
