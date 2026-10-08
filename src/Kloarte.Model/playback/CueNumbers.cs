using System;
using System.Globalization;

namespace Kloarte.Model;

/// <summary>Cue numbers as a console gives them out.</summary>
public static class CueNumbers
{
    /// <summary>Two cue numbers closer than this are the same number.</summary>
    public const float Tolerance = 0.0005f;

    /// <summary>A cue number from a text ("12.5"), or null when it isn't one (or is negative).</summary>
    public static float? Parse(string text) =>
        float.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) && n >= 0f ? n : null;

    /// <summary>A cue number as the console shows it: "12", "12.5", "12.125".</summary>
    public static string Text(float number) => number.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>True when two cue numbers are the same.</summary>
    public static bool Same(float a, float b) => MathF.Abs(a - b) < Tolerance;

    /// <summary>
    /// A free cue number right after `current` (null: before the first cue) and before `next` (null: the last cue),
    /// with as few decimals as possible, like inserting on the console: 1 -&gt; 2 when free, 1 and 2 -&gt; 1.1, 1.1 and
    /// 1.2 -&gt; 1.11. Null when there is no room with 3 decimals.
    /// </summary>
    public static float? Between(float? current, float? next)
    {
        float from = current ?? 0f;
        for (int decimals = 0; decimals <= 3; decimals++)
        {
            double unit = Math.Pow(10, -decimals);
            double candidate = (Math.Floor(from / unit + 1e-6) + 1) * unit;
            if (next == null || candidate < next.Value - 1e-4)
                return (float)Math.Round(candidate, decimals);
        }
        return null;
    }
}
