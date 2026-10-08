using Kloarte.MagicQ;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>MagicQ's show files (<see cref="MagicQStore"/>) with a show folder of the test's own.</summary>
public sealed class TestShowStore : IShowStore<MagicQShow>
{
    /// <summary>The real store.</summary>
    private readonly MagicQStore _store = new();

    /// <summary>A store whose show folder is `showFolder`.</summary>
    public TestShowStore(string showFolder) => ShowFolder = showFolder;

    /// <inheritdoc />
    public MagicQShow Load(string path) => _store.Load(path);

    /// <inheritdoc />
    public IGelTable LoadGels(string showPath) => _store.LoadGels(showPath);

    /// <inheritdoc />
    public IShowEditor EditorFor(MagicQShow show) => _store.EditorFor(show);

    /// <inheritdoc />
    public string SourceOf(string path) => _store.SourceOf(path);

    /// <inheritdoc />
    public bool IsWorkingCopy(string path) => _store.IsWorkingCopy(path);

    /// <summary>None: the test opens its shows itself.</summary>
    public string LastShowFile => null;

    /// <inheritdoc />
    public string ShowFolder { get; }

    /// <inheritdoc />
    public void CreateShow(string path) => _store.CreateShow(path);

    /// <inheritdoc />
    public void CopyShow(string from, string path) => _store.CopyShow(from, path);

    /// <summary>The next numbered show in the test's show folder.</summary>
    public string NewShowFile(string projectFolder, string projectName) => ShowVersions.NextFile(ShowFolder, projectName, null);

    /// <inheritdoc />
    public string WriteConsoleShow(MagicQShow show, string path) => _store.WriteConsoleShow(show, path);

    /// <inheritdoc />
    public System.Collections.Generic.IReadOnlyList<FileProblem> LoadNotes => _store.LoadNotes;

    /// <inheritdoc />
    public string CompanionOf(MagicQShow show) => _store.CompanionOf(show);
}
