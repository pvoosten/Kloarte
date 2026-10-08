using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>
/// The theatre the show plays in: what the MagicQ show doesn't describe. Stage edge (forestage), cyc, hoist plan
/// (truss height limits), house stands and the auditorium with its seats. Loaded from venue/&lt;venue&gt;.venue.yaml
/// (a project can have its own venue.yaml; an old project's venue.json is still read). Stage coordinates: origin = the show's proscenium line
/// (ShowBuilder), +X audience right, +Y up, +Z towards the audience. No Godot dependencies.
/// </summary>
public sealed class Venue
{
    /// <summary>Venue name.</summary>
    public string Name { get; set; } = "";
    /// <summary>stage edge, metres downstage of the origin</summary>
    public float Forestage { get; set; }
    /// <summary>Stage width (m).</summary>
    public float StageWidth { get; set; } = 15f;
    /// <summary>The cyc, or null.</summary>
    public Cyc Cyc { get; set; }
    /// <summary>stage z of the hoist plan's zero line</summary>
    public float HoistDatum { get; set; }
    /// <summary>Bars of the hoist plan.</summary>
    public List<Hoist> Hoists { get; set; } = new();
    /// <summary>e.g. "LT": 7.5, "T": 8</summary>
    public Dictionary<string, float> MaxHeightByPrefix { get; set; } = new();
    /// <summary>Highest a bar without a limit may go (m).</summary>
    public float DefaultMaxHeight { get; set; } = 8f;
    /// <summary>The house's floor stands.</summary>
    public List<StandType> Stands { get; set; } = new();
    /// <summary>The auditorium.</summary>
    public Auditorium Auditorium { get; set; } = new();

    /// <summary>
    /// Reads a venue: YAML (<c>.yaml</c>, the codec of <see cref="FileCodec"/>) or JSON. Throws
    /// <see cref="InvalidDataException"/> when a YAML venue has an error.
    /// </summary>
    public static Venue Load(string path)
    {
        if (!IsYaml(path))
            return JsonSerializer.Deserialize<Venue>(File.ReadAllText(path), Json) ?? new Venue();
        var read = new ProjectFolder(Path.GetDirectoryName(Path.GetFullPath(path)) ?? "").Read<Venue>(path);
        if (read.HasError)
            throw new InvalidDataException($"{path} is not a venue: {read.Problems[0]}");
        return read.Value;
    }

    /// <summary>Writes the venue: YAML for a <c>.yaml</c> path (the schema copy of the project), else JSON.</summary>
    public void Save(string path)
    {
        if (!IsYaml(path))
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
            return;
        }
        string modeline = ProjectSchemas.ModelineFor(ProjectSchemas.VenueSchema, "");
        File.WriteAllText(Path.GetFullPath(path),
            FileCodec.Current.Write(this, modeline, new[] { FileText.HeaderLine($"the venue {Name}") }));
    }

    /// <summary>True for a YAML file name.</summary>
    private static bool IsYaml(string path) => path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Highest a truss of this name may be flown (hoist plan names like "LT11" / "T 3", case and spaces ignored).
    /// </summary>
    public float MaxHeight(string trussName)
    {
        string n = Normalize(trussName);
        var hoist = Hoists.FirstOrDefault(h => Normalize(h.Name) == n);
        if (hoist is { MaxHeight: > 0 })
            return hoist.MaxHeight;
        foreach (var (prefix, height) in MaxHeightByPrefix.OrderByDescending(kv => kv.Key.Length))
            if (n.StartsWith(Normalize(prefix)) && (n.Length == prefix.Length || char.IsDigit(n[Normalize(prefix).Length])))
                return height;
        return DefaultMaxHeight;
    }

    private static string Normalize(string s) => (s ?? "").Replace(" ", "").ToUpperInvariant();

    /// <summary>JSON options of venue files.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
