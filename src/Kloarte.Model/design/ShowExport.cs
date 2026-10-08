using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>A native show exported as a console show: where, what was written, and what to look at.</summary>
/// <param name="Path">The console show written.</param>
/// <param name="Written">What the export changed in the source show (one line each; empty = nothing, a copy).</param>
/// <param name="Warnings">What could not be exported as it is in Kloarte (one line each).</param>
/// <param name="CheckInConsole">
/// What the export wrote of the kinds not yet verified in the console program (new palettes, cue changes, new records):
/// to check there by hand.
/// </param>
public sealed record ShowExport(string Path, IReadOnlyList<string> Written, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> CheckInConsole);
