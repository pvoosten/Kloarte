using System;
using System.IO;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>
/// MagicQ's show files for native shows: <see cref="MagicQImport"/>, <see cref="MagicQExport"/>, the colour table
/// (<see cref="MagicQStore.LoadGels"/>) and the show folder (<see cref="MagicQPc.ShowFolder"/>).
/// </summary>
public sealed class MagicQExchange : IShowExchange
{
    /// <summary>The show files of the colour table.</summary>
    private readonly MagicQStore _store = new();

    /// <summary>The show folder; null = MagicQ's (<see cref="MagicQPc.ShowFolder"/>). Tests set their own.</summary>
    public string ShowFolderOverride { get; set; }

    /// <inheritdoc />
    public string ShowFolder => ShowFolderOverride ?? MagicQPc.ShowFolder;

    /// <inheritdoc />
    public string Extension => ".shw";

    /// <inheritdoc />
    public string NewShow => AppPaths.File(MagicQStore.NewShowTemplate);

    /// <summary>True for a MagicQ show and its autosave (.shw, .sbk).</summary>
    public bool IsConsoleShow(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".shw" or ".sbk";

    /// <inheritdoc />
    public ShowImport Import(string path, FixtureCatalog catalog) => MagicQImport.Import(path, catalog);

    /// <inheritdoc />
    public ShowExport Export(DesignShow show, string path, FixtureCatalog catalog) => MagicQExport.Export(show, path, catalog);

    /// <inheritdoc />
    public IGelTable LoadGels(string showPath) => _store.LoadGels(showPath ?? Path.Combine(ShowFolder, "x.shw"));
}
