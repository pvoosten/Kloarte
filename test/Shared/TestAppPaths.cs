using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>App paths for tests without Godot: res:// is the Godot project folder, project data goes to test/output.</summary>
public sealed class TestAppPaths : IAppPaths
{
    /// <inheritdoc />
    public bool Exported => false;

    /// <inheritdoc />
    public string Projects => TestEnvironment.Output("projects").Replace('\\', '/');

    /// <inheritdoc />
    public string File(string path) => TestEnvironment.File(path);
}
