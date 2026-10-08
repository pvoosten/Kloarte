using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>A type of floor stand of the house.</summary>
public sealed class StandType
{
    /// <summary>Name, e.g. "Manfrotto 111U".</summary>
    public string Name { get; set; } = "";
    /// <summary>Lowest height (m).</summary>
    public float Min { get; set; }
    /// <summary>Highest height (m).</summary>
    public float Max { get; set; }
    /// <summary>How many the house has.</summary>
    public int Count { get; set; }
}
