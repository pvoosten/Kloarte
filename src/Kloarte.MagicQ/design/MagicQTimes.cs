using System;

namespace Kloarte.MagicQ;

/// <summary>
/// MagicQ's cue times: units of 1/33.3 s, rounded down (1 s = 33, 3 s = 99, 30 s = 999). Seconds from units are the
/// shortest number that gives the same units again (99 = "3", 17 = "0.52"), so an imported time exports unchanged.
/// </summary>
internal static class MagicQTimes
{
    /// <summary>Units of a time in seconds (rounded down; 0..65535).</summary>
    public static int Units(double seconds) => Math.Clamp((int)Math.Floor(seconds * 33.3 + 1e-6), 0, 0xffff);

    /// <summary>The shortest time in seconds (0..4 decimals) that gives these units.</summary>
    public static double Seconds(int units)
    {
        for (int decimals = 0; decimals <= 4; decimals++)
        {
            double scale = Math.Pow(10, decimals);
            double seconds = Math.Ceiling(units / 33.3 * scale - 1e-9) / scale;
            if (Units(seconds) == units)
                return Math.Round(seconds, decimals);
        }
        return Math.Round(units / 33.3, 4);
    }
}
