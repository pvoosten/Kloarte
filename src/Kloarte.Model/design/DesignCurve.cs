namespace Kloarte.Model;

/// <summary>
/// A dimmer curve of the native show (<c>rig.yaml</c>): the output (16 bit) for each input 0..254 (input 255 = full),
/// as the console's user curves (MagicQ: U record).
/// </summary>
public sealed class DesignCurve
{
    /// <summary>Curve id (heads refer to it).</summary>
    [FileField("Curve id (heads refer to it)", Min = 1)]
    public int Id { get; set; }

    /// <summary>Curve name.</summary>
    [FileField("Curve name")]
    public string Name { get; set; } = "";

    /// <summary>Output 0..65535 for each input 0..254.</summary>
    [FileField("Output (0..65535) for each input 0..254; input 255 = full")]
    public int[] Table { get; set; } = System.Array.Empty<int>();

    /// <summary>The output (0..255) of an input level (0..255).</summary>
    public int Apply(int level) =>
        level >= Table.Length ? 255 : Table[System.Math.Clamp(level, 0, 255)] >> 8;
}
