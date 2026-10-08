namespace Kloarte.Model;

/// <summary>One DMX channel a cue or palette sets: its address, its DMX value and the palette the value comes from (or null).</summary>
/// <param name="Address">Absolute DMX address (1-based).</param>
/// <param name="Value">DMX value 0..255.</param>
/// <param name="Palette">The palette the value comes from, or null.</param>
public readonly record struct DesignChannel(int Address, int Value, string Palette);
