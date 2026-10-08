using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The gobos: PNG images (white = light), loaded at runtime (also PNGs written by the gobo editor that Godot hasn't
/// imported), and their editable drawings (*.gobo.json). Per project and shared (decided with the user): new gobos go
/// into the open project's folder (<c>&lt;project&gt;/gobos</c>, stored as the relative path <c>gobos/&lt;name&gt;.png</c>,
/// so the project folder can be opened elsewhere); the picker lists the project's, then the shared library
/// (&lt;projects&gt;/gobos, see <see cref="AppPaths.Projects"/>, where they were made before) and the gobos that ship
/// with the app (res://gobos, e.g. window.png). The texture keeps its path, so the mechanics can store which gobo a lamp
/// has.
/// </summary>
public static class GoboLibrary
{
    /// <summary>Gobos that ship with the app (read-only).</summary>
    private const string BuiltInFolder = "res://gobos";

    /// <summary>The shared gobo library (tests point it at their output folder).</summary>
    public static string Folder { get; internal set; } = AppPaths.Projects + "/gobos";

    /// <summary>Folder of a project's own gobos, relative to the project folder (also the start of their paths).</summary>
    private const string ProjectGobos = Kloarte.Model.ProjectFolder.GobosDirectory;

    /// <summary>The open project's folder (absolute; set when a show is built), or null: then new gobos are shared.</summary>
    public static string ProjectFolder { get; set; }

    /// <summary>The folder new gobos go into: the project's gobos, else the shared library.</summary>
    private static string SaveFolder => string.IsNullOrEmpty(ProjectFolder) ? Folder : ProjectGobos;

    private static readonly Dictionary<string, (System.DateTime Time, Texture2D Texture)> Cache = new();

    /// <summary>Paths of all gobo images: the project's own (gobos/...), the shared library, the built-in ones.</summary>
    public static List<string> Images() =>
        new[] { ProjectGobos, Folder, BuiltInFolder }.Distinct().SelectMany(folder => Files(folder, "*")
                .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg"))
                .Select(f => $"{folder}/{Path.GetFileName(f)}").OrderBy(f => f))
            .ToList();

    /// <summary>True for a path of the project's own gobos ("gobos/star.png").</summary>
    public static bool IsProjectGobo(string path) => path != null && path.StartsWith(ProjectGobos + "/");

    /// <summary>The file of a gobo path: a project gobo in the project folder, else as <see cref="AppPaths.File"/>.</summary>
    private static string FileOf(string path) =>
        IsProjectGobo(path) || path == ProjectGobos
            ? string.IsNullOrEmpty(ProjectFolder) ? "" : Path.Combine(ProjectFolder, path)
            : AppPaths.File(path);

    /// <summary>The files of a gobo folder matching a pattern (none when it doesn't exist).</summary>
    private static string[] Files(string folder, string pattern)
    {
        string dir = FileOf(folder);
        return dir.Length > 0 && Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : System.Array.Empty<string>();
    }

    /// <summary>The gobo texture of a path (a project gobo, res://, absolute; reloaded when the file changed), or null.</summary>
    public static Texture2D Load(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        string file = FileOf(path);
        if (!File.Exists(file))
            return !IsProjectGobo(path) && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        var time = File.GetLastWriteTimeUtc(file);
        if (Cache.TryGetValue(file, out var c) && c.Time == time)
            return c.Texture;
        var image = Image.LoadFromFile(file);
        if (image == null || image.IsEmpty())
            return null;
        var texture = ImageTexture.CreateFromImage(image);
        texture.TakeOverPath(path);   // so ReadMechanics stores the path (another project's gobo of the same name gives way)
        Cache[file] = (time, texture);
        return texture;
    }

    /// <summary>Texture of a rasterised drawing (not saved), e.g. for the live preview.</summary>
    public static ImageTexture Texture(GoboDocument doc, int size)
    {
        var image = Image.CreateFromData(size, size, false, Image.Format.L8, doc.Rasterize(size));
        image.Convert(Image.Format.Rgb8);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// Saves the drawing (&lt;name&gt;.gobo.json) and its image (&lt;name&gt;.png) in the project's gobos (else the shared
    /// library); returns the image's path ("gobos/&lt;name&gt;.png" for the project's).
    /// </summary>
    public static string Save(GoboDocument doc, int size = 512)
    {
        string dir = FileOf(SaveFolder);
        Directory.CreateDirectory(dir);
        doc.Save(Path.Combine(dir, doc.Name + ".gobo.json"));
        var image = Image.CreateFromData(size, size, false, Image.Format.L8, doc.Rasterize(size));
        image.Convert(Image.Format.Rgb8);
        string png = Path.Combine(dir, doc.Name + ".png");
        image.SavePng(png);
        Cache.Remove(png);
        return $"{SaveFolder}/{doc.Name}.png";
    }

    /// <summary>Names of the saved drawings: the project's, then the shared ones not named the same.</summary>
    public static List<string> Drawings() =>
        new[] { ProjectGobos, Folder }.SelectMany(folder => Files(folder, "*.gobo.json")
                .Select(f => Path.GetFileName(f)[..^".gobo.json".Length]).OrderBy(n => n))
            .Distinct().ToList();

    /// <summary>Loads a saved drawing by name (the project's first, then the shared library).</summary>
    public static GoboDocument LoadDrawing(string name)
    {
        string own = FileOf(ProjectGobos);
        string file = own.Length > 0 ? Path.Combine(own, name + ".gobo.json") : "";
        return GoboDocument.Load(File.Exists(file) ? file : Path.Combine(AppPaths.File(Folder), name + ".gobo.json"));
    }
}
