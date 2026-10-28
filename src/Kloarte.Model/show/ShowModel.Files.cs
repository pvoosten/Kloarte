using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kloarte.Model;

/// <summary>
/// The project's YAML files as the model: one lamp file per lamp (<see cref="LampFile"/>, keyed by its rig item id),
/// <c>blocking/actors.yaml</c>, <c>blocking/scenery.yaml</c> and <c>blocking/steps.yaml</c>. Loading applies what parses and reports the rest; saving
/// writes what changed (<see cref="ProjectFolder"/> refuses what changed outside Kloarte or doesn't parse).
/// </summary>
public sealed partial class ShowModel<TShow>
{
    /// <summary>The actors file, relative to the project folder.</summary>
    public const string ActorsPath = "blocking/actors.yaml";

    /// <summary>The scenery file (the set pieces), relative to the project folder.</summary>
    public const string SceneryPath = "blocking/scenery.yaml";

    /// <summary>The steps file, relative to the project folder.</summary>
    public const string StepsPath = "blocking/steps.yaml";

    /// <summary>The file of each lamp as last read or written (rig item id -> full path).</summary>
    private readonly Dictionary<int, string> _lampPaths = new();

    /// <summary>Lamps whose file has an error: the file is not applied, the lamp keeps its last good state.</summary>
    private readonly HashSet<int> _brokenLamps = new();

    /// <summary>The lamps whose files have an error (the fixture list marks them).</summary>
    public IReadOnlyCollection<int> BrokenLamps => _brokenLamps;

    /// <summary>The rig item id a lamp file names (its "rigItem:" line), read even when the file doesn't parse; or null.</summary>
    public static int? LampIdIn(string text)
    {
        var match = Regex.Match(text ?? "", @"(?m)^\s*-?\s*rigItem:\s*(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out int id) ? id : null;
    }

    /// <summary>
    /// Loads the project's YAML files: each lamp file into its lamp (keyed by rig item id), the actors and steps into the
    /// blocking. A file with an error is not applied (its lamp keeps the defaults, its part of the blocking is empty) and
    /// is reported in `problems`; warnings are applied and reported. The files that parse are remembered by `folder`.
    /// </summary>
    public static ShowModel<TShow> LoadFiles(TShow show, string shwPath, ProjectFolder folder, FixtureCatalog catalog,
        List<FileProblem> problems)
    {
        var model = new ShowModel<TShow> { Show = show, ShwPath = shwPath };
        var lamps = show.RigItems.Where(v => v.IsFixture).ToDictionary(v => v.Id);
        var read = folder.LampFiles().Select(folder.Read<LampFile>).ToList();
        if (show is DesignShow design)
            design.ReservedItemIds.UnionWith(read.Select(r => LampIdIn(r.Text)).OfType<int>());   // new lamps don't take their ids
        foreach (var broken in read.Where(r => r.HasError && LampIdIn(r.Text) is not null))
            model._brokenLamps.Add(LampIdIn(broken.Text).Value);
        foreach (var file in read)
            problems.AddRange(file.Problems);
        foreach (var group in read.Where(r => !r.HasError).GroupBy(r => r.Value.RigItem))
        {
            if (group.Count() > 1)
            {
                foreach (var duplicate in group)
                    problems.Add(new FileProblem(duplicate.Path, 0, 0,
                        $"two files have lamp {group.Key} (neither is applied)", true));
                model._brokenLamps.Add(group.Key);
                continue;
            }
            var file = group.Single();
            if (!lamps.ContainsKey(file.Value.RigItem))
            {
                problems.Add(new FileProblem(file.Path, 0, 0,
                    $"no lamp {file.Value.RigItem} in the show (not applied)", false));
                continue;
            }
            model.Mechanics[file.Value.RigItem] = MechanicsDiff.FromFile(file.Value, catalog);
            model._lampPaths[file.Value.RigItem] = file.Path;
            folder.Remember(file.Path, file.Text);
        }
        var actors = ReadBlockingFile<ActorsFile>(folder, ActorsPath, problems);
        var steps = ReadBlockingFile<StepsFile>(folder, StepsPath, problems);
        var scenery = ReadBlockingFile<SceneryFile>(folder, SceneryPath, problems);
        model.Blocking = BlockingFiles.FromFiles(actors, steps, scenery, CueIndex.Of(show), folder.PathOf(StepsPath),
            folder.PathOf(SceneryPath), problems);
        return model;
    }

    /// <summary>Reads one blocking file: its value when it parses (remembered), else null (its problems reported).</summary>
    private static T ReadBlockingFile<T>(ProjectFolder folder, string relative, List<FileProblem> problems) where T : class
    {
        string path = folder.PathOf(relative);
        if (!File.Exists(path))
            return null;
        var read = folder.Read<T>(path);
        problems.AddRange(read.Problems);
        if (read.HasError)
            return null;
        folder.Remember(path, read.Text);
        return read.Value;
    }

    /// <summary>Applies a lamp file that was read (an external change): the lamp's mechanics become the file's.</summary>
    public void ApplyLampFile(LampFile file, string path, FixtureCatalog catalog)
    {
        Mechanics[file.RigItem] = MechanicsDiff.FromFile(file, catalog);
        _lampPaths[file.RigItem] = path;
        _brokenLamps.Remove(file.RigItem);
    }

    /// <summary>Marks a lamp whose file has an error (or clears the mark).</summary>
    public void SetBrokenLamp(int rigItem, bool broken)
    {
        if (broken)
            _brokenLamps.Add(rigItem);
        else
            _brokenLamps.Remove(rigItem);
    }

    /// <summary>Applies the actors file that was read (an external change); the rest of the blocking stays as it is.</summary>
    public void ApplyActors(ActorsFile actors, List<FileProblem> problems) =>
        Blocking = BlockingFiles.FromFiles(actors, BlockingFiles.StepsOf(Blocking, CueIndex.Of(Show)),
            BlockingFiles.SceneryOf(Blocking), CueIndex.Of(Show), "", "", problems);

    /// <summary>Applies the steps file that was read (an external change); the rest of the blocking stays as it is.</summary>
    public void ApplySteps(StepsFile steps, List<FileProblem> problems) =>
        Blocking = BlockingFiles.FromFiles(BlockingFiles.ActorsOf(Blocking), steps, BlockingFiles.SceneryOf(Blocking),
            CueIndex.Of(Show), "", "", problems);

    /// <summary>Applies the scenery file that was read (an external change); the rest of the blocking stays as it is.</summary>
    public void ApplyScenery(SceneryFile scenery, List<FileProblem> problems) =>
        Blocking = BlockingFiles.FromFiles(BlockingFiles.ActorsOf(Blocking), BlockingFiles.StepsOf(Blocking, CueIndex.Of(Show)),
            scenery, CueIndex.Of(Show), "", "", problems);

    /// <summary>The lamp with this rig item id (a fixture of the show), or null.</summary>
    private IRigItem LampOf(int rigItem) => Show.RigItems.FirstOrDefault(v => v.Id == rigItem && v.IsFixture);

    /// <summary>True when the show has a fixture with this rig item id.</summary>
    public bool IsLampInShow(int rigItem) => LampOf(rigItem) != null;

    /// <summary>
    /// True when a lamp's mechanics are not in its file yet (or in another content), or a lamp's file is still to be
    /// deleted: the mechanics have unsaved changes.
    /// </summary>
    public bool HasUnsavedLamps(ProjectFolder folder, FixtureCatalog catalog)
    {
        foreach (int id in Mechanics.Keys)
        {
            if (TextOfLamp(id, catalog) is not { } text)
                continue;
            if (LampPathOf(id) is not { } path || !folder.IsKnown(path, text))
                return true;
        }
        return _lampPaths.Keys.Any(id => !Mechanics.ContainsKey(id) || !IsLampInShow(id));
    }

    /// <summary>
    /// The text the lamp's file would have now (what Kloarte would write); null when the lamp has no mechanics or isn't in
    /// the show. Compared with the file's content to tell unsaved changes from an external edit.
    /// </summary>
    public string TextOfLamp(int rigItem, FixtureCatalog catalog) =>
        Mechanics.TryGetValue(rigItem, out var m) && LampOf(rigItem) is { } lamp
            ? LampText(lamp, m, catalog, new Rig(Show)) : null;

    /// <summary>The text the actors file would have now.</summary>
    public string TextOfActors() => FileCodec.Current.Write(BlockingFiles.ActorsOf(Blocking),
        ProjectSchemas.ModelineFor(ProjectSchemas.ActorsSchema, "blocking"),
        new[] { FileText.HeaderLine("the actors on stage (their movement is in steps.yaml)") });

    /// <summary>The text the scenery file would have now.</summary>
    public string TextOfScenery() => FileCodec.Current.Write(BlockingFiles.SceneryOf(Blocking),
        ProjectSchemas.ModelineFor(ProjectSchemas.ScenerySchema, "blocking"),
        new[] { FileText.HeaderLine("the set pieces on stage (the cues in steps.yaml take them out / in, open curtains)") });

    /// <summary>The text the steps file would have now.</summary>
    public string TextOfSteps() => FileCodec.Current.Write(BlockingFiles.StepsOf(Blocking, CueIndex.Of(Show)),
        ProjectSchemas.ModelineFor(ProjectSchemas.StepsSchema, "blocking"),
        new[] { FileText.HeaderLine("the cues with a hold or actor movement, in cue order") });

    /// <summary>The text of a lamp's file (its header says what the lamp is).</summary>
    private string LampText(IRigItem lamp, FixtureMechanics mechanics, FixtureCatalog catalog, Rig rig)
    {
        var file = MechanicsDiff.ToFile(lamp.Id, mechanics, catalog);
        var fixtureModel = catalog?.Resolve(mechanics.Model ?? mechanics.Kind);
        return FileCodec.Current.Write(file,
            ProjectSchemas.ModelineFor(ProjectSchemas.LampSchema, ProjectFolder.MechanicsDirectory),
            new[] { FileText.HeaderLine(LampFileNames.Header(lamp, Show, fixtureModel, rig)) });
    }

    /// <summary>
    /// Writes the files that changed: the lamp files (one per lamp; a renamed lamp's file is renamed; the files of lamps
    /// removed from the show are deleted when Kloarte wrote them) and/or the blocking files. Returns what happened to each.
    /// </summary>
    public List<FileWrite> SaveFiles(ProjectFolder folder, FixtureCatalog catalog, bool mechanics, bool blocking)
    {
        var writes = new List<FileWrite>();
        folder.WriteSchemas();
        if (mechanics)
            SaveLampFiles(folder, catalog, writes);
        if (blocking)
        {
            writes.Add(folder.Write(folder.PathOf(ActorsPath), TextOfActors()));
            writes.Add(folder.Write(folder.PathOf(StepsPath), TextOfSteps()));
            if (Blocking.Scenery.Count > 0 || File.Exists(folder.PathOf(SceneryPath)))
                writes.Add(folder.Write(folder.PathOf(SceneryPath), TextOfScenery()));
        }
        return writes;
    }

    /// <summary>
    /// Writes every lamp file that changed. The lamp files on disk are looked at (not only the paths remembered): a file
    /// of a lamp Kloarte writes under another name (renamed, by hand or by Kloarte) is deleted when it is Kloarte's
    /// content, so a lamp never has two files; the files of lamps that are gone are deleted when Kloarte wrote them.
    /// </summary>
    private void SaveLampFiles(ProjectFolder folder, FixtureCatalog catalog, List<FileWrite> writes)
    {
        var rig = new Rig(Show);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onDisk = folder.LampFiles()
            .Select(p => (Path: p, Id: LampIdIn(File.ReadAllText(p))))
            .Where(f => f.Id != null)
            .ToList();
        foreach (int id in Mechanics.Keys.OrderBy(i => i))
        {
            if (LampOf(id) is not { } lamp)
                continue;
            var mechanics = Mechanics[id];
            string name = LampFileNames.Unique(LampFileNames.For(lamp, mechanics.Model ?? mechanics.Kind, rig), used);
            string path = folder.PathOf(Path.Combine(ProjectFolder.MechanicsDirectory, name));
            var write = folder.Write(path, LampText(lamp, mechanics, catalog, rig));
            writes.Add(write);
            if (write.Kind is not (FileWriteKind.Written or FileWriteKind.Unchanged))
                continue;
            foreach (var other in onDisk.Where(f => f.Id == id && !string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                if (folder.IsUnchanged(other.Path))
                    writes.Add(folder.Delete(other.Path));   // renamed: the old file goes
            _lampPaths[id] = path;
        }
        foreach (var file in onDisk.Where(f => !Mechanics.ContainsKey(f.Id.Value) || LampOf(f.Id.Value) == null))
            if (FileText.IsKloarte(File.ReadAllText(file.Path)) && folder.IsUnchanged(file.Path))
                writes.Add(folder.Delete(file.Path));   // the lamp is gone from the show or the mechanics
        foreach (int id in _lampPaths.Keys.Where(id => !Mechanics.ContainsKey(id) || LampOf(id) == null).ToList())
            _lampPaths.Remove(id);
    }

    /// <summary>The file a lamp's mechanics are saved in (null when the lamp has none yet).</summary>
    public string LampPathOf(int rigItem) => _lampPaths.GetValueOrDefault(rigItem);
}
