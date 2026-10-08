using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary><c>blocking/actors.yaml</c>: the actors of the blocking (their movement is in <see cref="StepsFile"/>).</summary>
public sealed class ActorsFile
{
    /// <summary>The actors.</summary>
    [FileField("The actors on stage")]
    public List<ActorEntry> Actors { get; set; } = new();
}
