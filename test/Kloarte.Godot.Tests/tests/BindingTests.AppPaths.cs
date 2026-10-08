using System.IO;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>BindingTests: where app files and project data are (<see cref="GodotAppPaths"/> as <see cref="AppPaths"/>).</summary>
public partial class BindingTests
{
    private void AppPathsTest()
    {
        Check(!AppPaths.Exported && AppPaths.Projects == "res://Projects", $"from the project: data in {AppPaths.Projects}");
        Check(AppPaths.File(FixtureCatalog.DefaultPath) == ProjectSettings.GlobalizePath(FixtureCatalog.DefaultPath)
              && File.Exists(AppPaths.File(FixtureCatalog.DefaultPath)), "res:// app files in the project folder");
        string absolute = Path.Combine(_out, "abs_gobo.png").Replace('\\', '/');
        Check(AppPaths.File(absolute) == absolute, "absolute paths stay as they are (exported projects folder)");

        // An exported build keeps gobos in Documents\Kloarte\gobos: a texture from an absolute path keeps that path.
        File.Copy(ProjectSettings.GlobalizePath("res://gobos/window.png"), absolute, true);
        var gobo = GoboLibrary.Load(absolute);
        Check(gobo != null && gobo.ResourcePath == absolute, $"gobo from an absolute path: {gobo?.ResourcePath}");
    }
}
