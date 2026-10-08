using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// <c>rig.yaml</c>: the native show's rig and patch: the fixture types, the heads (patch), groups, dimmer curves and
/// every rig item (lamps and objects). Part of <see cref="DesignShow"/>.
/// </summary>
public sealed class DesignRig
{
    /// <summary>The console show this project was imported from, if any.</summary>
    [FileField("The MagicQ show this project was imported from (exports are written into a copy of it)")]
    public string Source { get; set; }

    /// <summary>The fixture types.</summary>
    [FileField("The fixture types (personalities) of the rig")]
    public List<DesignPersonality> Types { get; set; } = new();

    /// <summary>The heads of the patch.</summary>
    [FileField("The heads of the patch")]
    public List<DesignHead> Heads { get; set; } = new();

    /// <summary>The groups of heads.</summary>
    [FileField("The groups of heads")]
    public List<DesignGroup> Groups { get; set; } = new();

    /// <summary>The dimmer curves.</summary>
    [FileField("The dimmer curves heads can use")]
    public List<DesignCurve> Curves { get; set; } = new();

    /// <summary>The rig items: lamps and objects.</summary>
    [FileField("The rig: lamps and objects (trusses, pipes, boxes, people, models)")]
    public List<DesignItem> Items { get; set; } = new();
}
