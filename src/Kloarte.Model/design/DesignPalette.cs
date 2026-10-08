using System.Collections.Generic;
using System.Globalization;

namespace Kloarte.Model;

/// <summary>
/// A palette of the native show (<c>palettes.yaml</c>): a named set of values per head that cues refer to
/// (<c>"@name"</c>); editing it changes every cue that uses it. Combined: one palette can hold intensity, colour,
/// position and beam values (position as pan / tilt angles). The console's palettes are typed, so on export a palette
/// becomes one console palette per type it holds; their ids are kept here so a later export writes the same palettes.
/// </summary>
public sealed class DesignPalette
{
    /// <summary>The palette's name (unique in the show; cues refer to it).</summary>
    [FileField("The palette's name (unique; cues refer to it as \"@name\")")]
    public string Name { get; set; } = "";

    /// <summary>The console's palette ids this palette is written as (MagicQ: type &lt;&lt; 12 | entry).</summary>
    [FileField("The MagicQ palettes this palette is (MagicQ's palette ids: type * 4096 + entry); kept for the next export")]
    public int[] Magicq { get; set; }

    /// <summary>Per head number: the palette's values.</summary>
    [FileField("Per head number: the palette's values (intensity in %, GDTF attributes in their units, slot names)")]
    public Dictionary<string, Dictionary<string, LookValue>> Heads { get; set; } = new();

    /// <summary>The palette's values for a head (null when it has none).</summary>
    public Dictionary<string, LookValue> Of(int head) =>
        Heads.TryGetValue(head.ToString(CultureInfo.InvariantCulture), out var values) ? values : null;

    /// <summary>A deep copy.</summary>
    public DesignPalette Copy() => new() { Name = Name, Magicq = (int[])Magicq?.Clone(), Heads = Looks.Copy(Heads) };
}
