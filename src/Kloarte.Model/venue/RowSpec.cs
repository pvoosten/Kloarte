using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>Specification of one or more rows with the same seats.</summary>
public sealed class RowSpec
{
    /// <summary>one letter per row, e.g. "ABCDEFGHIJKLMNOPQRS"</summary>
    public string Names { get; set; } = "";
    /// <summary>Seats per row.</summary>
    public int Seats { get; set; }
    /// <summary>overrides Seats / 2 (e.g. row U: 8 even, 9 odd)</summary>
    public int Even { get; set; }
    /// <summary>Odd-numbered seats (overrides Seats - Seats / 2).</summary>
    public int Odd { get; set; }
    /// <summary>every other row shifted by this many seats (+X)</summary>
    public float Stagger { get; set; }
}
