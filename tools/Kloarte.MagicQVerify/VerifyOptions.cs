using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Kloarte.MagicQVerify;

/// <summary>The command line of the tool.</summary>
internal sealed record VerifyOptions
{
    /// <summary>The usage text.</summary>
    public const string Usage =
        "Kloarte.MagicQVerify --show <exported.shw> --stack <name> --playback <1..10>\n" +
        "    [--restart] [--palettes] [--cue-stack] [--universe-offset <n>] [--out <folder>] [--expected <other.shw>]\n" +
        "--restart: kill a running MagicQ PC (unsaved work in it is lost). Without it a running MagicQ is an error.\n" +
        "--palettes: after the stack, capture MagicQ's palette windows (palettes_*.png) to check the palettes by eye.\n" +
        "--cue-stack: capture the stack's cue stack window before and after playing it (cue_stack_*.png) to check cue order and texts by eye.";

    /// <summary>The exported show. Not touched: MagicQ runs on a copy.</summary>
    public required string Show { get; init; }

    /// <summary>The show the output is compared with, or null for <see cref="Show"/> (to prove a difference is found).</summary>
    public string Expected { get; init; }

    /// <summary>The name of the cue stack to play.</summary>
    public required string Stack { get; init; }

    /// <summary>The playback (1..10) that holds the stack.</summary>
    public required int Playback { get; init; }

    /// <summary>True when a running MagicQ may be killed.</summary>
    public bool Restart { get; init; }

    /// <summary>True when the palette windows are captured after the stack.</summary>
    public bool Palettes { get; init; }

    /// <summary>True when the stack's cue stack window is captured before and after the stack.</summary>
    public bool CueStack { get; init; }

    /// <summary>The Art-Net universe that is MagicQ universe 1.</summary>
    public int UniverseOffset { get; init; }

    /// <summary>The folder for the copy of the show, the report and the screenshots.</summary>
    public string Output { get; init; }

    /// <summary>The options of a command line, or null when it is incomplete.</summary>
    public static VerifyOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>();
        bool restart = false, palettes = false, cueStack = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--restart")
                restart = true;
            else if (args[i] == "--palettes")
                palettes = true;
            else if (args[i] == "--cue-stack")
                cueStack = true;
            else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
                values[args[i][2..]] = args[++i];
            else
                return null;
        }
        if (!values.TryGetValue("show", out string show) || !values.TryGetValue("stack", out string stack)
            || !values.TryGetValue("playback", out string playback)
            || !int.TryParse(playback, CultureInfo.InvariantCulture, out int number) || number is < 1 or > 10)
            return null;
        return new VerifyOptions
        {
            Show = Path.GetFullPath(show),
            Expected = values.TryGetValue("expected", out string expected) ? Path.GetFullPath(expected) : null,
            Stack = stack,
            Playback = number,
            Restart = restart,
            Palettes = palettes,
            CueStack = cueStack,
            UniverseOffset = values.TryGetValue("universe-offset", out string offset) ? int.Parse(offset, CultureInfo.InvariantCulture) : 0,
            Output = Path.GetFullPath(values.GetValueOrDefault("out",
                Path.Combine(Path.GetTempPath(), "kloarte-magicq-verify", Path.GetFileNameWithoutExtension(show)))),
        };
    }
}
