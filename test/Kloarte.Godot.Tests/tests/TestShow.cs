using System.IO;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// The show a test opens: a copy of a MagicQ show (with its .xhw) in a fresh folder of the test's own,
/// tests/output/&lt;name&gt;/show, which also becomes MagicQ's show folder (<see cref="MagicQPc.ShowFolderOverride"/>), so
/// exports and new shows go there and nothing is ever written to the user's MagicQ folder. The app's show scene no
/// longer opens a show by itself (it starts with the project chooser): a test that needs one sets it with
/// <see cref="Load"/> (or <see cref="Copy"/> and <see cref="ShowBuilder.ShowFile"/>) before the scene enters the tree.
/// </summary>
internal static class TestShow
{
    /// <summary>
    /// The user's MagicQ show folder (read only: shows are only copied from it; <see cref="MagicQPc.ShowFolder"/> is the
    /// test's own folder once <see cref="Copy"/> ran).
    /// </summary>
    internal const string MagicQShows = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show";

    /// <summary>The Risquons-Tout show most tests use (read only: only copied).</summary>
    internal const string RisquonsTout = MagicQShows + "/Risquons-Tout/RT2025_031.shw";

    /// <summary>The app's show scene.</summary>
    internal const string ShowScene = "res://shows/risquons_tout.tscn";

    /// <summary>
    /// Copies <paramref name="source"/> (default <see cref="RisquonsTout"/>) and its .xhw into a fresh
    /// tests/output/<paramref name="name"/>/show, makes that MagicQ's show folder and returns the copy's path
    /// (forward slashes).
    /// </summary>
    internal static string Copy(string name, string source = RisquonsTout)
    {
        string showFolder = ProjectSettings.GlobalizePath($"res://tests/output/{name}/show");
        if (Directory.Exists(showFolder))
            Directory.Delete(showFolder, true);
        Directory.CreateDirectory(showFolder);
        MagicQPc.ShowFolderOverride = showFolder;
        string copy = Path.Combine(showFolder, Path.GetFileName(source));
        File.Copy(source, copy);
        string xhw = Path.ChangeExtension(source, ".xhw");
        if (File.Exists(xhw))
            File.Copy(xhw, Path.ChangeExtension(copy, ".xhw"));
        return copy.Replace('\\', '/');
    }

    /// <summary>
    /// Instantiates <paramref name="scenePath"/> (default the app's show scene) with its "Show" node on a fresh copy of
    /// <paramref name="source"/> (<see cref="Copy"/>). The caller adds it to the tree.
    /// </summary>
    internal static Node Load(string name, string source = RisquonsTout, string scenePath = ShowScene)
    {
        var scene = GD.Load<PackedScene>(scenePath).Instantiate();
        scene.GetNode<ShowBuilder>("Show").ShowFile = Copy(name, source);
        return scene;
    }
}
