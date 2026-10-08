using System.Collections.Generic;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>MagicQ PC without starting it: <see cref="Launch"/> only records the show file.</summary>
public sealed class TestConsole : IConsoleApp<MagicQShow>
{
    /// <summary>The real console (comparisons).</summary>
    private readonly MagicQConsole _console = new();

    /// <summary>A console whose show folder is `showFolder`.</summary>
    public TestConsole(string showFolder) => ShowFolder = showFolder;

    /// <inheritdoc />
    public string ShowFolder { get; }

    /// <summary>The show files MagicQ was "started" with.</summary>
    public List<string> Launched { get; } = new();

    /// <inheritdoc />
    public string Launch(string showPath, MagicQShow show)
    {
        Launched.Add(showPath);
        return "launched";
    }

    /// <summary>Never: MagicQ PC is not started in tests.</summary>
    public bool Running => false;

    /// <inheritdoc />
    public bool Matches(MagicQShow show, string path) => _console.Matches(show, path);

    /// <inheritdoc />
    public (int Step, int Compared, int Differences, string Text) BestMatch(Dictionary<int, int> received,
        IReadOnlyList<Dictionary<int, int>> expected, MagicQShow show) => _console.BestMatch(received, expected, show);
}
