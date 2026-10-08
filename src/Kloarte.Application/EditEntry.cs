namespace Kloarte.Application;

/// <summary>One undo step: its name ("Remove lamp") and the state before and after it.</summary>
internal sealed record EditEntry(string Name, EditSnapshot Before, EditSnapshot After);
