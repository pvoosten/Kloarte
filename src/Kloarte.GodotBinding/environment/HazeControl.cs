using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The haze of the room as the scene shows it: the global shader uniform <c>haze_density</c> (the beams' raymarched volume)
/// and the density of the environment's volumetric fog. Set by the Haze slider (the project's Look), and from the cues
/// that set smoke (<see cref="CuePlayer"/>, also in the movie process, which has no panel).
/// </summary>
internal static class HazeControl
{
    /// <summary>The density set last (Godot reads a global shader uniform back only in the editor).</summary>
    internal static float Current { get; private set; } = ProjectLook.DefaultHaze;

    /// <summary>Sets the haze density of the beams and of the environment's volumetric fog.</summary>
    internal static void Set(SceneTree tree, float density)
    {
        Current = density;
        RenderingServer.GlobalShaderParameterSet("haze_density", density);
        foreach (var env in tree.Root.FindChildren("*", nameof(WorldEnvironment), true, false).OfType<WorldEnvironment>())
            if (env.Environment is { VolumetricFogEnabled: true } environment)
                environment.VolumetricFogDensity = density;
    }
}
