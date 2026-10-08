namespace Kloarte.Model;

/// <summary>The gobo in a <see cref="LampFile"/>.</summary>
public sealed class LampFileGobo
{
    /// <summary>The gobo image.</summary>
    [FileField("The gobo image, e.g. res://gobos/window.png or a gobo of the gobo editor")]
    public string Path { get; set; }

    /// <summary>Rotation (degrees).</summary>
    [FileField("Rotation of the gobo", Unit = "°", Min = -360, Max = 360)]
    public double? Rotation { get; set; }

    /// <summary>Image width relative to the gate.</summary>
    [FileField("Image width relative to the gate: 1 = fills it", Min = 0.05, Max = 4)]
    public double? Size { get; set; }
}
