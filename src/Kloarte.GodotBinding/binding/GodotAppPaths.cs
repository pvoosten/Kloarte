using System.IO;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Where the app's files are (Godot), both when it runs from the Godot project (editor, <c>--path</c>) and as an exported
/// build:
/// <list type="bullet">
/// <item>read-only app files given as res:// paths (data/: catalogue, GDTF, colour table; the house venue; built-in
///   gobos; fixture configs): in the project folder, or in an exported build next to the executable (they are not in
///   the .pck: data/ has a .gdignore and JSON is not exported; the distribution script copies them);</item>
/// <item>project data (mechanics, blocking, a show's own venue, the gobo editor's gobos): res://Projects (git-ignored)
///   from the project, Documents\Kloarte in an exported build (res:// can't be written there).</item>
/// </list>
/// Settings (key map, views) stay in user://. Set as <see cref="AppPaths.Current"/> by the host at start.
/// </summary>
public sealed class GodotAppPaths : IAppPaths
{
    /// <summary>True in an exported build (Godot export template), false when run from the project.</summary>
    public bool Exported => OS.HasFeature("template");

    /// <summary>Folder of the project data: res://Projects, or Documents/Kloarte in an exported build.</summary>
    public string Projects => Exported
        ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "Kloarte").Replace('\\', '/')
        : "res://Projects";

    /// <summary>
    /// The absolute file system path of a res:// / user:// path (absolute paths are returned as they are). In an
    /// exported build a res:// path is resolved next to the executable.
    /// </summary>
    public string File(string path)
    {
        const string res = "res://";
        if (Exported && path.StartsWith(res, System.StringComparison.Ordinal))
            return Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", path[res.Length..]).Replace('\\', '/');
        return ProjectSettings.GlobalizePath(path);
    }
}
