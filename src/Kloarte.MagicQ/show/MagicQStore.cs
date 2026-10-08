using System;
using System.IO;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>MagicQ's show files: <see cref="MagicQShow"/> (.shw + .xhw), status.dat, lastshow.shw (<see cref="MagicQPc"/>).</summary>
public sealed class MagicQStore : IShowStore<MagicQShow>
{
    /// <summary>Fallback copy of MagicQ's colour table, used when the show folder has no heads/allcols.csv.</summary>
    public const string GelTableFallback = "res://data/allcols.csv";

    /// <inheritdoc />
    public MagicQShow Load(string path) => MagicQShow.Load(path);

    /// <summary>MagicQ's colour table of the show's folder (heads/allcols.csv), else <see cref="GelTableFallback"/>.</summary>
    public IGelTable LoadGels(string showPath)
    {
        string table = MagicQColours.FindFor(showPath) ?? AppPaths.File(GelTableFallback);
        return File.Exists(table) ? MagicQColours.Load(table) : null;
    }

    /// <inheritdoc />
    public IShowEditor EditorFor(MagicQShow show) => new MagicQShowEditor(show);

    /// <inheritdoc />
    public string SourceOf(string path) => MagicQPc.SourceOf(path);

    /// <summary>True for lastshow.shw, MagicQ's copy of the loaded show.</summary>
    public bool IsWorkingCopy(string path) =>
        string.Equals(Path.GetFileName(path), "lastshow.shw", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string LastShowFile => MagicQPc.LastShowFile;

    /// <summary>Documents\MagicQ\show (<see cref="MagicQPc.ShowFolder"/>).</summary>
    public string ShowFolder => MagicQPc.ShowFolder;

    /// <summary>
    /// A new show (<see cref="NewShowTemplate"/>, made in MagicQ: only the MagicVis objects of CC De Brouckere, the
    /// house venue).
    /// </summary>
    public const string NewShowTemplate = "res://data/new_show.shw";

    /// <summary>Copies <see cref="NewShowTemplate"/> to `path`.</summary>
    public void CreateShow(string path) => File.Copy(AppPaths.File(NewShowTemplate), path);

    /// <summary>Copies the .shw and, when there is one, the .xhw next to it (never over an existing file).</summary>
    public void CopyShow(string from, string path)
    {
        File.Copy(from, path);
        string xhw = Path.ChangeExtension(from, ".xhw");
        if (File.Exists(xhw))
            File.Copy(xhw, Path.ChangeExtension(path, ".xhw"));
    }

    /// <summary>The next numbered show of the project's name in MagicQ's show folder.</summary>
    public string NewShowFile(string projectFolder, string projectName) => ShowVersions.NextFile(ShowFolder, projectName, null);

    /// <summary>Saves the show (.shw and .xhw).</summary>
    public string WriteConsoleShow(MagicQShow show, string path)
    {
        show.Save(path);
        return $"Saved MagicQ show {Path.GetFileName(path)} (+ .xhw)";
    }

    /// <summary>None: a MagicQ show loads as it is.</summary>
    public System.Collections.Generic.IReadOnlyList<FileProblem> LoadNotes { get; } = System.Array.Empty<FileProblem>();

    /// <summary>".xhw" when the show has its MagicVis archive.</summary>
    public string CompanionOf(MagicQShow show) => show.Xhw != null ? ".xhw" : null;
}
