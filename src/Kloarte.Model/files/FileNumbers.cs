using System;

namespace Kloarte.Model;

/// <summary>
/// Rounding of the numbers in the project files (user: metres 3 decimals, degrees 0.1). A value that rounds to nothing
/// new is written as the rounded value, so a file never holds float noise.
/// </summary>
public static class FileNumbers
{
    /// <summary>The value rounded to `decimals` (away from zero at the half); null stays null.</summary>
    public static double? Round(double? value, int decimals) =>
        value is { } v ? Math.Round(v, decimals, MidpointRounding.AwayFromZero) : null;
}
