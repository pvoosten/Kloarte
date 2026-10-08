using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>A group of heads of the native show (<c>rig.yaml</c>).</summary>
[FileFlow]
public sealed class DesignGroup
{
    /// <summary>Group number.</summary>
    [FileField("Group number", Min = 0)]
    public int Number { get; set; }

    /// <summary>Group name.</summary>
    [FileField("Group name")]
    public string Name { get; set; } = "";

    /// <summary>Head numbers in the group.</summary>
    [FileField("Head numbers in the group")]
    public int[] Heads { get; set; } = System.Array.Empty<int>();
}
