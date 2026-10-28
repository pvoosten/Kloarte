using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The material of a set piece: its colour times the preset's albedo, with the preset's roughness and metallic
/// (<see cref="SceneryMaterials"/>). Curtains and gauze show both sides; gauze is translucent
/// (<see cref="SceneryMaterials.GauzeAlpha"/>) and glows when lit from behind (backlight).
/// </summary>
internal static class SetPieceMaterial
{
    /// <summary>A new material for a piece.</summary>
    public static StandardMaterial3D For(SceneryPiece piece)
    {
        var (albedo, roughness, metallic) = SceneryMaterials.Of(piece.Material);
        var colour = new Color("#" + piece.ColorOrDefault);
        var tint = new Color(colour.R * albedo, colour.G * albedo, colour.B * albedo);
        var material = new StandardMaterial3D
        {
            AlbedoColor = tint,
            Roughness = roughness,
            Metallic = metallic,
            CullMode = piece.Shape == SceneryShape.Curtain ? BaseMaterial3D.CullModeEnum.Disabled : BaseMaterial3D.CullModeEnum.Back,
        };
        if (piece.Material == SceneryMaterial.Gauze)
        {
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            material.AlbedoColor = new Color(tint, SceneryMaterials.GauzeAlpha);
            material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            material.BacklightEnabled = true;
            material.Backlight = tint;
        }
        return material;
    }
}
