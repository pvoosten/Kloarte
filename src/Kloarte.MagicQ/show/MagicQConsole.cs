using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;

namespace Kloarte.MagicQ;

/// <summary>MagicQ PC (<see cref="MagicQPc"/>, <see cref="MagicQCheck"/>).</summary>
public sealed class MagicQConsole : IConsoleApp<IShowDocument>
{
    /// <inheritdoc />
    public string ShowFolder => MagicQPc.ShowFolder;

    /// <summary>
    /// Points status.dat at the show file with Art-Net output on its patched universes (the file's patch: a native show
    /// was exported to it) and restarts MagicQ PC.
    /// </summary>
    public string Launch(string showPath, IShowDocument show) =>
        MagicQPc.Launch(showPath, MagicQShow.Load(showPath).Patch.Select(p => (p.Address - 1) / 512 + 1).Distinct());

    /// <summary>True while mqqt.exe runs.</summary>
    public bool Running => MagicQPc.Running;

    /// <inheritdoc />
    public bool Matches(IShowDocument show, string path) => show is MagicQShow magicQ && magicQ.Shw.Matches(path);

    /// <inheritdoc />
    public (int Step, int Compared, int Differences, string Text) BestMatch(Dictionary<int, int> received,
        IReadOnlyList<Dictionary<int, int>> expected, IShowDocument show)
    {
        var intensities = show is MagicQShow magicQ ? MagicQCheck.IntensityAddresses(magicQ) : show.IntensityAddresses.ToHashSet();
        var (step, compared, diffs) = MagicQCheck.BestMatch(received, expected.ToList(), intensities);
        return (step, compared, diffs.Count, MagicQCheck.FormatDiffs(diffs));
    }
}
