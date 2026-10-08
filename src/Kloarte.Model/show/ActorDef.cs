namespace Kloarte.Model;

/// <summary>An actor of the blocking: who it is, how tall, and where it starts (before the first cue).</summary>
public sealed class ActorDef
{
    /// <summary>Default height of a man (m); the mannequins are modelled at this height.</summary>
    public const float DefaultMaleHeight = 1.76f;

    /// <summary>Default height of a woman (m).</summary>
    public const float DefaultFemaleHeight = 1.66f;

    /// <summary>Identity (stable across renames): a slug of the name when the actor was made (<see cref="ActorIds"/>).</summary>
    public string Id { get; set; } = "";

    /// <summary>Name shown in the lists and the timeline.</summary>
    public string Name { get; set; } = "";

    /// <summary>Woman (else man) mannequin.</summary>
    public bool Female { get; set; }

    /// <summary>Shirt colour "rrggbb" (the name is printed on it); null = a palette colour (<see cref="ShirtColors"/>).</summary>
    public string ShirtColor { get; set; }

    /// <summary>Height in metres; 0 = the default for a man / woman.</summary>
    public float Height { get; set; }

    /// <summary>State before the first cue.</summary>
    public ActorState Initial { get; set; } = new() { Visible = true, Pose = ActorPose.Standing, Position = new float[3], Facing = 0f };

    /// <summary>The height used (Height, or the default).</summary>
    public float EffectiveHeight => Height > 0f ? Height : Female ? DefaultFemaleHeight : DefaultMaleHeight;
}
