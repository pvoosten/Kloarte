using System;

namespace Kloarte.Model;

/// <summary>
/// Where the app's files are, for code without an engine dependency:
/// <list type="bullet">
/// <item>read-only app files given as res:// paths (data/: catalogue, GDTF, colour table; the house venue; built-in
///   gobos; fixture configs): in the project folder, or in an exported build next to the executable;</item>
/// <item>project data: <see cref="Projects"/>.</item>
/// </list>
/// The host sets <see cref="Current"/> at start (Godot: <c>GodotAppPaths</c>).
/// </summary>
public static class AppPaths
{
    /// <summary>The implementation set by the host.</summary>
    private static IAppPaths _current;

    /// <summary>The implementation (set once at start by the host).</summary>
    public static IAppPaths Current
    {
        get => _current ?? throw new InvalidOperationException("AppPaths.Current is not set: the host sets it at start");
        set => _current = value;
    }

    /// <summary>True in an exported build, false when run from the project.</summary>
    public static bool Exported => Current.Exported;

    /// <summary>Folder of the project data.</summary>
    public static string Projects => Current.Projects;

    /// <summary>The absolute file system path of an app path (see <see cref="IAppPaths.File"/>).</summary>
    public static string File(string path) => Current.File(path);
}
