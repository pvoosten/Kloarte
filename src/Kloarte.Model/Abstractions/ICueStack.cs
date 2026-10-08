using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>A cue stack: its steps in playback order.</summary>
public interface ICueStack
{
    /// <summary>Stack (playback) number.</summary>
    int Number { get; }

    /// <summary>Stack name.</summary>
    string Name { get; }

    /// <summary>The steps, in playback order.</summary>
    IReadOnlyList<ICueStep> Steps { get; }
}
