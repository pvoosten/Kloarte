using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>A block of reserved seats.</summary>
public sealed class SeatBlock
{
    /// <summary>Name, e.g. "Regie".</summary>
    public string Name { get; set; } = "";
    /// <summary>Row letters.</summary>
    public string Rows { get; set; } = "";
    /// <summary>Seat numbers.</summary>
    public List<int> Seats { get; set; } = new();
}
