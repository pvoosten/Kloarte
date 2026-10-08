namespace Kloarte.Model;

/// <summary>
/// Where the app's files are (see <see cref="AppPaths"/>). The engine binding implements it; the host sets
/// <see cref="AppPaths.Current"/> at start.
/// </summary>
public interface IAppPaths
{
    /// <summary>True in an exported build, false when run from the project.</summary>
    bool Exported { get; }

    /// <summary>Folder of the project data (mechanics, blocking, a show's own venue, the gobo editor's gobos).</summary>
    string Projects { get; }

    /// <summary>
    /// The absolute file system path of an app path (res:// / user:// in the Godot binding; absolute paths are returned
    /// as they are).
    /// </summary>
    string File(string path);
}
