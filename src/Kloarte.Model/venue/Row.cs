using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>A row of seats (laid out).</summary>
public sealed class Row
{
    /// <summary>Row letter.</summary>
    public string Name { get; set; } = "";
    /// <summary>Index, 0 = front row.</summary>
    public int Index { get; set; }
    /// <summary>centre of the row (stage coordinates)</summary>
    public float Z { get; set; }
    /// <summary>Floor height of the row (stage coordinates).</summary>
    public float FloorY { get; set; }
    /// <summary>Seats from house left to house right.</summary>
    public List<Seat> Seats { get; } = new();
}
