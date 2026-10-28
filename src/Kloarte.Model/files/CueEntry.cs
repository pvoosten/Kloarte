using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// One cue in <c>blocking/steps.yaml</c>: its number (as in MagicQ) and id (MagicQ's cue id, S record), the hold after it,
/// the smoke it sets and the actors' movement steps in it. On load the id wins (a renumber in MagicQ keeps the blocking); an entry typed
/// without an id is found by its number in the show's cue stack (user decision of 11 Oct 2026).
/// </summary>
public sealed class CueEntry
{
    /// <summary>Cue number, e.g. "12.5".</summary>
    [FileField("Cue number as in MagicQ, e.g. 12.5 (for reading; the id wins when both are there)")]
    public string Cue { get; set; }

    /// <summary>MagicQ cue id.</summary>
    [FileField("MagicQ's cue id (S record); missing = found by the cue number", Min = 0)]
    public int? Id { get; set; }

    /// <summary>Wait after the cue until the next GO (s).</summary>
    [FileField("Wait after this cue has finished until the next GO (timeline, Auto, movie); missing = the default hold", Unit = "s", Min = 0, Max = 3600)]
    public double? Hold { get; set; }

    /// <summary>Smoke (haze) amount this cue sets, in % of the densest haze.</summary>
    [FileField("Smoke (haze) this cue sets, in % of the densest haze (the Haze slider's scale); it stays in the later cues until one sets another; missing = this cue leaves the smoke as it is", Unit = "%", Min = 0, Max = 100)]
    public double? Smoke { get; set; }

    /// <summary>Seconds the haze takes to reach <see cref="Smoke"/>.</summary>
    [FileField("Seconds the haze takes to build or thin to this cue's smoke amount, like a hazer; missing = 20 s", Unit = "s", Min = 0, Max = 3600)]
    public double? SmokeTime { get; set; }

    /// <summary>Set piece id -> what this cue changes of it.</summary>
    [FileField("Per set piece id (blocking/scenery.yaml): out / in at this cue's GO, how far a curtain opens")]
    public Dictionary<string, SceneryChangeEntry> Scenery { get; set; }

    /// <summary>Actor id -> movement steps in this cue, in order.</summary>
    [FileField("Per actor id: the movement steps in this cue, one after the other")]
    public Dictionary<string, List<ActorStateEntry>> Actors { get; set; }
}
