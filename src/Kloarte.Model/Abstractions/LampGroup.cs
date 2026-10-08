using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>A group of heads, as the console defines it.</summary>
/// <param name="Name">Group name.</param>
/// <param name="Heads">Head numbers in the group.</param>
public sealed record LampGroup(string Name, IReadOnlyList<int> Heads);
