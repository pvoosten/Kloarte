using System;
using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The JSON Schemas of the project files (one per file kind, generated from the <see cref="FileFieldAttribute"/>s by
/// <see cref="SchemaWriter"/>). A project folder gets a copy in <c>.schemas/</c>; the modeline of each file points at
/// it, so VS Code offers completion, hover and warnings.
/// </summary>
public static class ProjectSchemas
{
    /// <summary>The schema file of each kind: name, its type, and the modeline path from a file at the project's root.</summary>
    private static readonly (string File, Type Type, string Title)[] Kinds =
    {
        (LampSchema, typeof(LampFile), "Kloarte lamp (mechanics)"),
        (ActorsSchema, typeof(ActorsFile), "Kloarte actors (blocking)"),
        (StepsSchema, typeof(StepsFile), "Kloarte steps (blocking)"),
        (ProjectSchema, typeof(ProjectDocumentFile), "Kloarte project"),
        (VenueSchema, typeof(Venue), "Kloarte venue"),
        (RigSchema, typeof(DesignRig), "Kloarte rig and patch"),
        (PalettesSchema, typeof(DesignPaletteList), "Kloarte palettes"),
        (CueListSchema, typeof(DesignCueList), "Kloarte cue list"),
    };

    /// <summary>The schema file of a lamp file.</summary>
    public const string LampSchema = "lamp.schema.json";

    /// <summary>The schema file of the actors file.</summary>
    public const string ActorsSchema = "actors.schema.json";

    /// <summary>The schema file of the steps file.</summary>
    public const string StepsSchema = "steps.schema.json";

    /// <summary>The schema file of the project document.</summary>
    public const string ProjectSchema = "project.schema.json";

    /// <summary>The schema file of the venue.</summary>
    public const string VenueSchema = "venue.schema.json";

    /// <summary>The schema file of a native show's rig.</summary>
    public const string RigSchema = "rig.schema.json";

    /// <summary>The schema file of a native show's palettes.</summary>
    public const string PalettesSchema = "palettes.schema.json";

    /// <summary>The schema file of a native show's cue list.</summary>
    public const string CueListSchema = "cuelist.schema.json";

    /// <summary>Every schema: its file name and text (generated; the same text every time).</summary>
    public static IEnumerable<(string File, string Text)> All()
    {
        foreach (var (file, type, title) in Kinds)
            yield return (file, SchemaWriter.Write(type, title));
    }

    /// <summary>
    /// The modeline path of a schema from a file: <c>../.schemas/lamp.schema.json</c> for a file in a subfolder
    /// (<c>mechanics/</c>, <c>blocking/</c>), <c>.schemas/project.schema.json</c> for one at the root.
    /// </summary>
    public static string ModelineFor(string schemaFile, string fileFolderRelative)
    {
        if (string.IsNullOrEmpty(fileFolderRelative))
            return $"{ProjectFolder.SchemaDirectory}/{schemaFile}";
        int depth = fileFolderRelative.Split('/', '\\').Length;
        return string.Concat(Enumerable.Repeat("../", depth)) + $"{ProjectFolder.SchemaDirectory}/{schemaFile}";
    }
}
