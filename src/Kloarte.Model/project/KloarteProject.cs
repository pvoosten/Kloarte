using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kloarte.Model;

/// <summary>
/// A Kloarte project: one production. It owns a folder (&lt;projects&gt;/&lt;name&gt;/) with the project document
/// <c>&lt;name&gt;.kloarte.yaml</c> (<see cref="ProjectDocumentFile"/>; the JSON document <c>&lt;name&gt;.kloarte.json</c>
/// of before is read when there is no YAML one and is kept), the lamp files (<c>mechanics/</c>), the blocking
/// (<c>blocking/</c>) and an optional <c>venue.yaml</c>, and points at its console show file. The show file's name
/// changes with every numbered save (<see cref="ShowVersions"/>), the project's name and folder don't: project data never
/// drifts with the show file.
/// </summary>
public sealed class KloarteProject
{
    /// <summary>File name suffix of a project document (YAML, format version 2).</summary>
    public const string Extension = ".kloarte.yaml";

    /// <summary>File name suffix of the JSON project document of before (kept, read when there is no YAML one).</summary>
    public const string LegacyExtension = ".kloarte.json";

    /// <summary>The project's name (its folder and document are named after it).</summary>
    public string Name { get; set; } = "";

    /// <summary>The project's current console show file (absolute path).</summary>
    public string ShowFile { get; set; } = "";

    /// <summary>When the project was created (UTC).</summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>When the project document was last written (UTC).</summary>
    public DateTime Saved { get; set; }

    /// <summary>The look of the room (stray light, haze), saved with the project.</summary>
    public ProjectLook Look { get; set; } = new();

    /// <summary>The look as the document has it (read or written last).</summary>
    private string _savedLook = new ProjectLook().Fingerprint;

    /// <summary>True when the look changed since the document was read or written.</summary>
    public bool LookUnsaved => Look.Fingerprint != _savedLook;

    /// <summary>Format version of the project document (1 = JSON, 2 = YAML).</summary>
    public int Version { get; set; } = ProjectDocumentFile.CurrentVersion;

    /// <summary>The project folder (absolute; not stored, it is where the document is).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Folder { get; set; } = "";

    /// <summary>Path of the project document: &lt;folder&gt;/&lt;name&gt;.kloarte.yaml (where Save writes it).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DocumentPath => Path.Combine(Folder, Name + Extension);

    /// <summary>Path of the JSON document of before: &lt;folder&gt;/&lt;name&gt;.kloarte.json.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string LegacyDocumentPath => Path.Combine(Folder, Name + LegacyExtension);

    /// <summary>True when the project's document exists (YAML, or the JSON one of before).</summary>
    public bool DocumentExists => File.Exists(DocumentPath) || File.Exists(LegacyDocumentPath);

    /// <summary>A file of the project folder, e.g. "venue.yaml" or "mechanics.json" (absolute).</summary>
    public string FileOf(string name) => Path.Combine(Folder, name);

    /// <summary>A new project named `name` in &lt;projectsRoot&gt;/&lt;name&gt; (nothing is written yet).</summary>
    public static KloarteProject Create(string projectsRoot, string name, string showFile) => new()
    {
        Name = name,
        ShowFile = showFile,
        Folder = Path.Combine(projectsRoot, name),
    };

    /// <summary>
    /// Reads a project document (YAML, or the JSON one of before); its folder is the document's folder. Throws
    /// <see cref="InvalidDataException"/> when it doesn't read.
    /// </summary>
    public static KloarteProject Load(string documentPath)
    {
        string full = Path.GetFullPath(documentPath);
        var project = full.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? LoadYaml(full) : LoadJson(full);
        project.Folder = Path.GetDirectoryName(full) ?? "";
        // A native project's show is its own folder's rig.yaml (also when the folder was copied or moved: the document
        // still names the old place). A project of before (its show a MagicQ show) keeps it until its first full Save.
        string rig = Path.Combine(project.Folder, DesignShow.RigFile);
        if (File.Exists(rig) && string.Equals(Path.GetFileName(project.ShowFile), DesignShow.RigFile, StringComparison.OrdinalIgnoreCase))
            project.ShowFile = rig;
        project.Look ??= new ProjectLook();
        project._savedLook = project.Look.Fingerprint;
        if (string.IsNullOrEmpty(project.Name))
            project.Name = Path.GetFileName(full)[..^Extension.Length];
        return project;
    }

    /// <summary>Reads the YAML project document.</summary>
    private static KloarteProject LoadYaml(string path)
    {
        var read = new ProjectFolder(Path.GetDirectoryName(path) ?? "").Read<ProjectDocumentFile>(path);
        if (read.HasError)
            throw new InvalidDataException($"{path} is not a Kloarte project: {read.Problems[0]}");
        var file = read.Value;
        return new KloarteProject
        {
            Name = file.Name ?? "",
            ShowFile = file.ShowFile ?? "",
            Created = ParseTime(file.Created, DateTime.UtcNow),
            Saved = ParseTime(file.Saved, default),
            Version = file.Version ?? ProjectDocumentFile.CurrentVersion,
            Look = new ProjectLook
            {
                StrayLight = Math.Clamp(file.StrayLight ?? ProjectLook.DefaultStrayLight, 0f, 2f),
                Haze = Math.Clamp(file.Haze ?? ProjectLook.DefaultHaze, 0f, ProjectLook.MaxHaze),
            },
        };
    }

    /// <summary>Reads the JSON project document of before.</summary>
    private static KloarteProject LoadJson(string path) =>
        JsonSerializer.Deserialize<KloarteProject>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"{path} is not a Kloarte project");

    /// <summary>A time from the file (ISO 8601, UTC), or `fallback`.</summary>
    private static DateTime ParseTime(string text, DateTime fallback) =>
        DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
            ? t : fallback;

    /// <summary>Writes the project document as YAML (creates the folder). The JSON document of before is kept.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        Saved = DateTime.UtcNow;
        var file = new ProjectDocumentFile
        {
            Name = Name,
            ShowFile = ShowFile,
            Created = Created.ToUniversalTime().ToString("o"),
            Saved = Saved.ToString("o"),
            StrayLight = Look.StrayLight == ProjectLook.DefaultStrayLight ? null : MathF.Round(Look.StrayLight, 3),
            Haze = Look.Haze == ProjectLook.DefaultHaze ? null : MathF.Round(Look.Haze, 5),
            Version = ProjectDocumentFile.CurrentVersion,
        };
        string text = FileCodec.Current.Write(file, ProjectSchemas.ModelineFor(ProjectSchemas.ProjectSchema, ""),
            new[] { FileText.HeaderLine($"the Kloarte project {Name}") });
        new ProjectFolder(Folder).WriteSchemas();
        File.WriteAllText(DocumentPath, text);
        Version = ProjectDocumentFile.CurrentVersion;
        _savedLook = Look.Fingerprint;
    }

    /// <summary>JSON options of the JSON project document of before (read only).</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>True when a name can be a project (folder) name: not empty, no path characters.</summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim() == name && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name is not ("." or "..");
}
