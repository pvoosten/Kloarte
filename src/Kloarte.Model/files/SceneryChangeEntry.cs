namespace Kloarte.Model;

/// <summary>What a cue in <c>blocking/steps.yaml</c> changes of a set piece (<see cref="SceneryChange"/>); missing = unchanged.</summary>
[FileFlow]
public sealed class SceneryChangeEntry
{
    /// <summary>On stage.</summary>
    [FileField("true = the piece comes in at this cue's GO, false = it goes out (no fade); missing = as it is")]
    public bool? Visible { get; set; }

    /// <summary>Curtain openness (%).</summary>
    [FileField("How far a split curtain opens in this cue (0 = closed, 100 = open); missing = as it is", Unit = "%", Min = 0, Max = 100)]
    public double? Open { get; set; }

    /// <summary>Seconds to open / close.</summary>
    [FileField("Seconds the curtain takes to open or close; missing = 5 s", Unit = "s", Min = 0, Max = 600)]
    public double? Time { get; set; }
}
