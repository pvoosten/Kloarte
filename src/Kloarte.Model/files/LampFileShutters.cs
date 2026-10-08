namespace Kloarte.Model;

/// <summary>The framing shutters in a <see cref="LampFile"/>; a blade that is out is left out.</summary>
public sealed class LampFileShutters
{
    /// <summary>Blade cutting the top of the beam.</summary>
    [FileField("Blade cutting the top of the beam")]
    public LampFileBlade Top { get; set; }

    /// <summary>Blade cutting the right of the beam.</summary>
    [FileField("Blade cutting the right of the beam (seen from behind the lamp)")]
    public LampFileBlade Right { get; set; }

    /// <summary>Blade cutting the bottom of the beam.</summary>
    [FileField("Blade cutting the bottom of the beam")]
    public LampFileBlade Bottom { get; set; }

    /// <summary>Blade cutting the left of the beam.</summary>
    [FileField("Blade cutting the left of the beam (seen from behind the lamp)")]
    public LampFileBlade Left { get; set; }

    /// <summary>Rotation of the whole shutter assembly (degrees).</summary>
    [FileField("Rotation of the whole shutter assembly", Unit = "°", Min = -360, Max = 360)]
    public double? Rotation { get; set; }
}
