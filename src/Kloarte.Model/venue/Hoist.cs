using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>A bar of the hoist plan.</summary>
public sealed class Hoist
{
    /// <summary>Name, e.g. "LT11".</summary>
    public string Name { get; set; } = "";
    /// <summary>from the hoist plan datum, upstage</summary>
    public float Distance { get; set; }
    /// <summary>0 = from MaxHeightByPrefix</summary>
    public float MaxHeight { get; set; }
}
