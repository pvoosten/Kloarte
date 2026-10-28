namespace Kloarte.GodotBinding;

/// <summary>
/// The render layers (<c>VisualInstance3D.Layers</c>) Kloarte gives meaning to. Everything is on layer 1 (Godot's
/// default) for the cameras; what stops a haze beam is also on <see cref="Occluders"/>, which the lamps' occlusion
/// cameras render (only that layer).
/// </summary>
public static class RenderLayers
{
    /// <summary>Godot's default layer: every camera renders it.</summary>
    public const uint Default = 1u;

    /// <summary>
    /// Layer 11: the opaque scenery that cuts haze beams (set pieces except gauze, imported MagicVis boxes / borders /
    /// proscenium, the cyc); not actors, not lamps, not the floor.
    /// </summary>
    public const uint Occluders = 1u << 10;
}
