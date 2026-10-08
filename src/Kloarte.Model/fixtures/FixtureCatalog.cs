using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kloarte.Model;

/// <summary>
/// Catalogue of fixture models (the house rig: data/fixture_catalog.json): kind, light output, zoom range or lamps,
/// and which mechanical settings the model has. The fixture panel builds its settings from these capabilities and
/// the mechanics JSON only stores what the model has. Values that don't come from a datasheet are marked Estimated.
/// No Godot dependencies.
/// </summary>
public sealed class FixtureCatalog
{
    /// <summary>Where the house catalogue is (an app path).</summary>
    public const string DefaultPath = "res://data/fixture_catalog.json";

    /// <summary>The fixture models of the house rig.</summary>
    public List<FixtureModel> Models { get; set; } = new();

    /// <summary>Old "kind" names (fixtures.json, mechanics files before the catalogue) -&gt; default model.</summary>
    public Dictionary<string, string> KindDefaults { get; set; } = new();

    /// <summary>The model with this id (case-insensitive), or null.</summary>
    public FixtureModel Find(string id) =>
        id == null ? null : Models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>A model id, or an old kind name mapped to its default model.</summary>
    public FixtureModel Resolve(string modelOrKind) =>
        Find(modelOrKind) ?? Find(KindDefaults.GetValueOrDefault(modelOrKind ?? ""));

    /// <summary>Reads a catalogue JSON file (comments allowed).</summary>
    public static FixtureCatalog Load(string path) =>
        JsonSerializer.Deserialize<FixtureCatalog>(File.ReadAllText(path), Json) ?? new FixtureCatalog();

    /// <summary>JSON options of the catalogue (case-insensitive, comments, trailing commas).</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
