using System;

namespace Kloarte.Model;

/// <summary>
/// The values of the material presets (<see cref="SceneryMaterial"/>), from docs/venue-and-rig.md where it has them
/// (molton: almost no reflection, roughness 1). No Godot dependencies: the scene turns them into a material.
/// </summary>
public static class SceneryMaterials
{
    /// <summary>Opacity of gauze (0 = invisible, 1 = opaque).</summary>
    public const float GauzeAlpha = 0.3f;

    /// <summary>The part of the colour reflected (multiplies the albedo), roughness (0..1) and metallic (0..1) of a preset.</summary>
    public static (float Albedo, float Roughness, float Metallic) Of(SceneryMaterial material) => material switch
    {
        SceneryMaterial.Wood => (1f, 0.6f, 0f),
        SceneryMaterial.Molton => (0.08f, 1f, 0f),
        SceneryMaterial.Metal => (1f, 0.35f, 1f),
        SceneryMaterial.Gauze => (1f, 0.95f, 0f),
        _ => (1f, 0.85f, 0f),
    };

    /// <summary>The name of a preset as it is written in the file ("painted", "wood", ...).</summary>
    public static string NameOf(SceneryMaterial material) => material.ToString().ToLowerInvariant();

    /// <summary>The names of all presets, separated by commas (the file field's allowed values).</summary>
    public const string Names = "painted,wood,molton,metal,gauze";

    /// <summary>The preset of a name in the file (case ignored); null when there is none of that name.</summary>
    public static SceneryMaterial? Parse(string name) =>
        Enum.TryParse(name?.Trim(), true, out SceneryMaterial m) && Enum.IsDefined(m) ? m : null;
}
