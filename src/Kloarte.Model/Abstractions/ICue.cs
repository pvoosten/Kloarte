using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>A recorded cue: its channels and fade times.</summary>
public interface ICue
{
    /// <summary>Cue id (referred to by cue stack steps).</summary>
    int Id { get; }

    /// <summary>Cue name.</summary>
    string Name { get; }

    /// <summary>In time (s): rising levels.</summary>
    float FadeIn { get; }

    /// <summary>Out time (s): falling levels.</summary>
    float FadeOut { get; }

    /// <summary>Delay (s) after the GO before the fades start (0 when the show document has none).</summary>
    float Delay => 0f;

    /// <summary>Recorded channels: absolute DMX address and raw value (see <see cref="IShowDocument.Resolve"/>).</summary>
    IReadOnlyList<(int Address, int Value)> Channels { get; }
}
