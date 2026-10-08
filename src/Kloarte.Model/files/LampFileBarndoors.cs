namespace Kloarte.Model;

/// <summary>Barndoors in a <see cref="LampFile"/>: each flap 0 = open (folded back), 1 = closed across the lens.</summary>
public sealed class LampFileBarndoors
{
    /// <summary>Top flap.</summary>
    [FileField("Top flap: 0 = open, 1 = closed", Min = 0, Max = 1)]
    public double? Top { get; set; }

    /// <summary>Right flap.</summary>
    [FileField("Right flap: 0 = open, 1 = closed", Min = 0, Max = 1)]
    public double? Right { get; set; }

    /// <summary>Bottom flap.</summary>
    [FileField("Bottom flap: 0 = open, 1 = closed", Min = 0, Max = 1)]
    public double? Bottom { get; set; }

    /// <summary>Left flap.</summary>
    [FileField("Left flap: 0 = open, 1 = closed", Min = 0, Max = 1)]
    public double? Left { get; set; }

    /// <summary>Rotation of the barndoor ring (degrees).</summary>
    [FileField("Rotation of the barndoor ring", Unit = "°", Min = -360, Max = 360)]
    public double? Rotation { get; set; }
}
