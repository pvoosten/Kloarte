using System;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// A view asks for the lamp menu (right click on a lamp in the plan; the 3D view and the fixture list ask the panel
/// directly). The panel shows it (<see cref="FixturePanel"/>, UI review 9).
/// </summary>
internal static class LampMenuRequests
{
    /// <summary>The lamp right-clicked and where (viewport position of the mouse).</summary>
    internal static event Action<ProfileFixture, Vector2> Requested;

    /// <summary>Asks for the lamp menu of `lamp` at `at`.</summary>
    internal static void Request(ProfileFixture lamp, Vector2 at) => Requested?.Invoke(lamp, at);
}
