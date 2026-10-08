using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>The loaded show as the scene sees it: the show document, the mechanics per lamp and the blocking.</summary>
public interface IShowModel
{
    /// <summary>The console's show document.</summary>
    IShowDocument Show { get; }

    /// <summary>Mechanics per lamp (rig item id).</summary>
    Dictionary<int, FixtureMechanics> Mechanics { get; }

    /// <summary>Actors and their changes per cue.</summary>
    Blocking Blocking { get; }

    /// <summary>The lamps whose project file has an error (the fixture list marks them).</summary>
    IReadOnlyCollection<int> BrokenLamps { get; }
}
