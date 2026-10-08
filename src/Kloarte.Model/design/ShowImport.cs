using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>A console show imported as a native show, with notes on what didn't come along as it was.</summary>
/// <param name="Show">The native show.</param>
/// <param name="Notes">What the import changed or left out (one line each; empty = all of it as it was).</param>
public sealed record ShowImport(DesignShow Show, IReadOnlyList<string> Notes);
