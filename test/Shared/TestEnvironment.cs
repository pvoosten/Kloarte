using System;
using System.IO;
using System.Linq;

namespace Kloarte.Tests;

/// <summary>
/// Where the tests find things: the repository (app files given as res:// paths resolve to the Godot project folder),
/// the user's MagicQ show folder (read only) and the output folder (test/output, git-ignored).
/// </summary>
public static class TestEnvironment
{
    /// <summary>MagicQ show folder (read only).</summary>
    public const string ShowFolder = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show";

    /// <summary>The repository root (the folder with the solution).</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>The Godot project folder (res://).</summary>
    public static string GodotProject => Path.Combine(RepoRoot, "src", "Kloarte.Host");

    /// <summary>The file system path of an app path (res:// = <see cref="GodotProject"/>; others as they are).</summary>
    public static string File(string path)
    {
        const string res = "res://";
        return path.StartsWith(res, StringComparison.Ordinal)
            ? Path.Combine(GodotProject, path[res.Length..]).Replace('\\', '/')
            : path;
    }

    /// <summary>An output folder of the tests (created): test/output/&lt;name&gt;.</summary>
    public static string Output(string name)
    {
        string dir = Path.Combine(RepoRoot, "test", "output", name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A show file of the MagicQ show folder, by file name (searched in the subfolders).</summary>
    public static string Show(string name) => Directory.EnumerateFiles(ShowFolder, name, SearchOption.AllDirectories).First();

    /// <summary>Walks up from the test assembly to the folder with the solution.</summary>
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
        throw new InvalidOperationException("solution folder not found above " + AppContext.BaseDirectory);
    }
}
