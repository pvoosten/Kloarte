namespace Kloarte.Model;

/// <summary>One framing shutter blade in a <see cref="LampFile"/>, written in flow style: <c>{depth: 0.35, angle: -5}</c>.</summary>
[FileFlow]
public sealed class LampFileBlade
{
    /// <summary>Depth: 0 = out, 1 = to the beam axis, 2 = fully across.</summary>
    [FileField("Depth: 0 = out, 1 = to the beam axis, 2 = fully across", Min = 0, Max = 2)]
    public double? Depth { get; set; }

    /// <summary>Tilt (degrees, counter-clockwise).</summary>
    [FileField("Tilt of the blade, counter-clockwise", Unit = "°", Min = -45, Max = 45)]
    public double? Angle { get; set; }
}
