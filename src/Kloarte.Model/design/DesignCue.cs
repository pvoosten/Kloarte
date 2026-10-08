using System.Collections.Generic;
using System.Globalization;

namespace Kloarte.Model;

/// <summary>
/// A cue of the native show (<c>cues.yaml</c>): its number, text and times, and what it changes (tracking: only what a
/// cue sets is in it; everything else stays as the cues before left it). Per head (by number) the values of its channels
/// (<see cref="LookValue"/>, keyed as <see cref="HeadLayout"/> says: <c>intensity</c>, GDTF attributes such as
/// <c>Pan</c>, channel names, <c>#12</c> for channel 12 of the head).
/// </summary>
public sealed class DesignCue
{
    /// <summary>Cue number, e.g. "12.5".</summary>
    [FileField("Cue number, e.g. 12.5 (the cues are played in number order)")]
    public string Cue { get; set; } = "";

    /// <summary>The cue's id (stable when renumbered; MagicQ's cue id when imported).</summary>
    [FileField("The cue's id: stays when the cue is renumbered (MagicQ's cue id when imported); missing = a new cue", Min = 1)]
    public int? Id { get; set; }

    /// <summary>The cue's text.</summary>
    [FileField("The cue's text (MagicQ keeps 15 characters)")]
    public string Text { get; set; }

    /// <summary>The console's cue name when it differs from the text (MagicQ: the S record's name).</summary>
    [FileField("The console's cue name, when it differs from the text")]
    public string Name { get; set; }

    /// <summary>In time (s): levels that go up.</summary>
    [FileField("Seconds levels that go up (and all other values) take; missing = 0", Unit = "s", Min = 0, Max = 3600)]
    public double? FadeIn { get; set; }

    /// <summary>Out time (s): levels that go down.</summary>
    [FileField("Seconds levels that go down take; missing = 0", Unit = "s", Min = 0, Max = 3600)]
    public double? FadeOut { get; set; }

    /// <summary>Delay (s) before the cue's fades start (Kloarte's playback; not written to the console).</summary>
    [FileField("Seconds after the GO before the fades start (Kloarte's playback only, not exported to MagicQ); missing = 0", Unit = "s", Min = 0, Max = 3600)]
    public double? Delay { get; set; }

    /// <summary>Per head number: the channels this cue sets and their values.</summary>
    [FileField("Per head number: the values this cue sets (intensity in %, GDTF attributes in their units, slot names, \"@palette\")")]
    public Dictionary<string, Dictionary<string, LookValue>> Heads { get; set; } = new();

    /// <summary>Channels of no head (address -> DMX value), as an imported show may have them.</summary>
    [FileField("DMX values of addresses that belong to no head (address: value), as an imported show may have them")]
    public Dictionary<string, int> Dmx { get; set; }

    /// <summary>The cue number as a number (0 when it isn't one).</summary>
    public float Number => CueNumbers.Parse(Cue) ?? 0f;

    /// <summary>A deep copy.</summary>
    public DesignCue Copy() => new()
    {
        Cue = Cue, Id = Id, Text = Text, Name = Name, FadeIn = FadeIn, FadeOut = FadeOut, Delay = Delay,
        Heads = Looks.Copy(Heads),
        Dmx = Dmx == null ? null : new Dictionary<string, int>(Dmx),
    };

    /// <summary>The values a cue sets for a head (null when it sets none).</summary>
    public Dictionary<string, LookValue> Of(int head) =>
        Heads.TryGetValue(head.ToString(CultureInfo.InvariantCulture), out var values) ? values : null;
}
