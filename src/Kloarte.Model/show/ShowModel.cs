using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kloarte.Model;

/// <summary>
/// The application's show model: the console's show document (<typeparamref name="TShow"/>: patch, rig, cues,
/// palettes... kept by the console binding, lossless) plus what the console cannot store: the mechanical set-up of
/// each lamp (model, optics, shutters, iris, gobo, barndoors, hand focus) and the blocking (actors and their moves per
/// cue). The show document is saved in its own format, mechanics and blocking to separate JSON files. No Godot or
/// MagicQ dependencies.
/// </summary>
/// <typeparam name="TShow">The console binding's show document (MagicQ: <c>MagicQShow</c>).</typeparam>
public sealed partial class ShowModel<TShow> : IShowModel where TShow : class, IShowDocument
{
    /// <summary>The console's show document.</summary>
    public TShow Show { get; private set; }

    /// <inheritdoc />
    IShowDocument IShowModel.Show => Show;
    /// <summary>Path of the show file it was loaded from.</summary>
    public string ShwPath { get; private set; } = "";

    /// <summary>
    /// Mechanics per lamp, keyed by MagicVis item id (H field 1). Several lamps can share a DMX address / head
    /// (e.g. six lamps on one dimmer channel), so the address is not a key. Files of version 1 were keyed by address
    /// and are migrated on load (every lamp on that address gets the entry).
    /// </summary>
    public Dictionary<int, FixtureMechanics> Mechanics { get; private set; } = new();

    /// <summary>Actors and their changes per cue.</summary>
    public Blocking Blocking { get; private set; } = new();

    /// <summary>Version of the mechanics file format (2 = keyed by MagicVis id).</summary>
    private const int MechanicsVersion = 2;

    /// <summary>A show document loaded from `shwPath`, with its mechanics and blocking (missing files = empty).</summary>
    public static ShowModel<TShow> Load(TShow show, string shwPath, string mechanicsPath = null, string blockingPath = null)
    {
        var model = new ShowModel<TShow> { Show = show, ShwPath = shwPath };
        if (mechanicsPath != null && File.Exists(mechanicsPath))
            model.Mechanics = model.ReadMechanics(File.ReadAllText(mechanicsPath));
        if (blockingPath != null && File.Exists(blockingPath))
            model.Blocking = JsonSerializer.Deserialize<Blocking>(File.ReadAllText(blockingPath), Json) ?? new Blocking();
        return model;
    }

    private Dictionary<int, FixtureMechanics> ReadMechanics(string json)
    {
        var file = JsonSerializer.Deserialize<MechanicsFile>(json, Json) ?? new MechanicsFile();
        var fixtures = file.Fixtures ?? new Dictionary<int, FixtureMechanics>();
        if (file.Version >= MechanicsVersion)
            return fixtures;
        var result = new Dictionary<int, FixtureMechanics>();
        foreach (var vis in Show.RigItems.Where(v => v.IsFixture))
            if (fixtures.TryGetValue(vis.Address, out var m))
                result[vis.Id] = Clone(m);
        return result;
    }

    /// <summary>A fingerprint of the mechanics as they would be saved.</summary>
    public long MechanicsHash() => Hashes.Fnv(MechanicsJson());

    /// <summary>A fingerprint of the blocking as it would be saved.</summary>
    public long BlockingHash() => Hashes.Fnv(BlockingJson());

    /// <summary>The mechanics per rig item id as JSON (undo snapshots; see <see cref="RestoreState"/>).</summary>
    public string MechanicsJson() => JsonSerializer.Serialize(Mechanics, Json);

    /// <summary>The blocking as JSON (undo snapshots; see <see cref="RestoreState"/>).</summary>
    public string BlockingJson() => JsonSerializer.Serialize(Blocking, Json);

    /// <summary>
    /// Puts back an earlier state (undo / redo): the show document (null = keep the current one), and the mechanics and
    /// blocking from <see cref="MechanicsJson"/> / <see cref="BlockingJson"/>.
    /// </summary>
    public void RestoreState(TShow show, string mechanicsJson, string blockingJson)
    {
        if (show != null)
            Show = show;
        Mechanics = JsonSerializer.Deserialize<Dictionary<int, FixtureMechanics>>(mechanicsJson, Json) ?? new();
        Blocking = JsonSerializer.Deserialize<Blocking>(blockingJson, Json) ?? new Blocking();
    }

    /// <summary>Writes the blocking JSON.</summary>
    public void SaveBlocking(string blockingPath)
    {
        CreateFolderOf(blockingPath);
        File.WriteAllText(blockingPath, JsonSerializer.Serialize(Blocking, Json));
    }

    /// <summary>Writes the show document (MagicQ: .shw and .xhw).</summary>
    public void SaveShow(string shwPath) => Show.Save(shwPath);

    /// <summary>Writes the mechanics JSON.</summary>
    public void SaveMechanics(string mechanicsPath)
    {
        var file = new MechanicsFile { Show = Path.GetFileName(ShwPath), Version = MechanicsVersion, Fixtures = Mechanics };
        CreateFolderOf(mechanicsPath);
        File.WriteAllText(mechanicsPath, JsonSerializer.Serialize(file, Json));
    }

    /// <summary>Creates the folder a file goes into (e.g. the git-ignored Projects folder on a fresh clone).</summary>
    private static void CreateFolderOf(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
    }

    /// <summary>A deep copy.</summary>
    public static FixtureMechanics Clone(FixtureMechanics m) =>
        JsonSerializer.Deserialize<FixtureMechanics>(JsonSerializer.Serialize(m, Json), Json);

    /// <summary>JSON options for mechanics and blocking.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class MechanicsFile
    {
        public string Show { get; set; } = "";
        public int Version { get; set; } = 1;
        public Dictionary<int, FixtureMechanics> Fixtures { get; set; } = new();
    }
}
