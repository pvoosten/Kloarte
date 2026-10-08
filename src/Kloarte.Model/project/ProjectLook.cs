using System;

namespace Kloarte.Model;

/// <summary>
/// The look of the room, saved with the project (UI review 24): the stray light (spill, haze scatter and bounce that keep
/// the room from going fully dark) and the haze density in the beams.
/// </summary>
public sealed class ProjectLook
{
    /// <summary>Stray light when the project says nothing.</summary>
    public const float DefaultStrayLight = 0.5f;

    /// <summary>Haze density when the project says nothing (the scene's default).</summary>
    public const float DefaultHaze = 0.015f;

    /// <summary>Highest haze density offered.</summary>
    public const float MaxHaze = 0.1f;

    /// <summary>Stray light amount, 0..2 (0 = pitch black around the beams).</summary>
    public float StrayLight { get; set; } = DefaultStrayLight;

    /// <summary>The haze density of an amount in % of <see cref="MaxHaze"/> (0..100, the Haze slider's scale).</summary>
    public static float DensityOf(double percent) => (float)(Math.Clamp(percent, 0.0, 100.0) / 100.0 * MaxHaze);

    /// <summary>The amount in % of <see cref="MaxHaze"/> of a haze density.</summary>
    public static double PercentOf(float density) => Math.Clamp(density / (double)MaxHaze * 100.0, 0.0, 100.0);

    /// <summary>Haze density, 0..<see cref="MaxHaze"/> (0 = clean air).</summary>
    public float Haze { get; set; } = DefaultHaze;

    /// <summary>The values as text, to see whether the look changed since it was saved.</summary>
    public string Fingerprint => FormattableString.Invariant($"{StrayLight:0.###}|{Haze:0.#####}");

    /// <summary>A copy.</summary>
    public ProjectLook Clone() => new() { StrayLight = StrayLight, Haze = Haze };
}
