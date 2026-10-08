namespace Kloarte.Model;

/// <summary>
/// <c>&lt;name&gt;.kloarte.yaml</c>: the project document (YAML from format version 2; the JSON document of before is
/// kept and read when there is no YAML one). The project's name is its folder's name too; the show file changes with
/// every numbered save.
/// </summary>
public sealed class ProjectDocumentFile
{
    /// <summary>The format version this file is written with.</summary>
    public const int CurrentVersion = 2;

    /// <summary>The project's name (its folder and document are named after it).</summary>
    [FileField("The project's name: its folder and this document are named after it")]
    public string Name { get; set; }

    /// <summary>The project's current console show file (an absolute path).</summary>
    [FileField("The project's current console show file (absolute path)")]
    public string ShowFile { get; set; }

    /// <summary>When the project was created (UTC, ISO 8601).</summary>
    [FileField("When the project was created (UTC, ISO 8601)")]
    public string Created { get; set; }

    /// <summary>When the project was last saved (UTC, ISO 8601; Kloarte writes it on every save).</summary>
    [FileField("When the project was last saved by Kloarte (UTC, ISO 8601)")]
    public string Saved { get; set; }

    /// <summary>Stray light of the room (null = Kloarte's default).</summary>
    [FileField("Stray light of the room: spill, haze scatter and bounce that keep it from going fully dark (0 = pitch " +
               "black around the beams; Kloarte's default 0.5)", Min = 0, Max = 2)]
    public float? StrayLight { get; set; }

    /// <summary>Haze density in the beams (null = Kloarte's default).</summary>
    [FileField("Haze density in the beams (0 = clean air; Kloarte's default 0.015)", Min = 0, Max = ProjectLook.MaxHaze)]
    public float? Haze { get; set; }

    /// <summary>The format version of this document (2 = YAML).</summary>
    [FileField("Format version of this document (2 = YAML)", Min = 1)]
    public int? Version { get; set; }
}
