namespace Kloarte.Model;

/// <summary>An actor in <c>blocking/actors.yaml</c> (<see cref="ActorDef"/>).</summary>
public sealed class ActorEntry
{
    /// <summary>The actor's id.</summary>
    [FileField("The actor's id (stable across renames; blocking/steps.yaml refers to it)")]
    public string Id { get; set; }

    /// <summary>Name.</summary>
    [FileField("Name, as shown in Kloarte and printed on the shirt")]
    public string Name { get; set; }

    /// <summary>A woman mannequin (else a man).</summary>
    [FileField("true = a woman mannequin (missing = a man)")]
    public bool? Female { get; set; }

    /// <summary>Shirt colour "#rrggbb".</summary>
    [FileField("Shirt colour \"#rrggbb\"; missing = a colour from the palette", Quoted = true)]
    public string ShirtColor { get; set; }

    /// <summary>Height (m).</summary>
    [FileField("Height; missing = the default for a man / woman", Unit = "m", Min = 0.8, Max = 2.3)]
    public double? Height { get; set; }

    /// <summary>State before the first cue.</summary>
    [FileField("Where and how the actor is before the first cue")]
    public ActorStateEntry Initial { get; set; }
}
