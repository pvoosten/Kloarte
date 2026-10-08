namespace Kloarte.GodotBinding;

/// <summary>A row of the fixture table (<see cref="FixtureTable"/>): a lamp as a technician names it.</summary>
/// <param name="Fixture">The lamp.</param>
/// <param name="Head">MagicQ head number (0 = not patched).</param>
/// <param name="Name">The head's name from the patch (no node name or id).</param>
/// <param name="Type">Short catalogue type ("C103").</param>
/// <param name="Gel">Gel code ("L201"), "" for open white.</param>
/// <param name="Broken">The lamp's mechanics file has an error.</param>
/// <param name="Tooltip">The details: DMX address, full type and gel names.</param>
internal sealed record FixtureRow(ProfileFixture Fixture, int Head, string Name, string Type, string Gel, bool Broken, string Tooltip);
