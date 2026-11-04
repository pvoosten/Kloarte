using Godot;

namespace Kloarte.GodotBinding;

public partial class ProfileFixture
{
    /// <summary>Outside of an ACP-style cyc flood: width, height, depth (m), the box behind its open front.</summary>
    private static readonly Vector3 FloodBox = new(0.40f, 0.46f, 0.28f);

    /// <summary>Wall thickness of the flood's box (m).</summary>
    private const float FloodWall = 0.012f;

    /// <summary>Light of the lit reflector per unit of intensity (its emission energy).</summary>
    private const float ReflectorGlow = 0.8f;

    /// <summary>The housing of a lens lamp: a cylinder behind the lens.</summary>
    private MeshInstance3D _lensBody;

    /// <summary>The housing of a flood: an open box with its reflector, the front towards the cloth.</summary>
    private Node3D _floodBody;

    /// <summary>The flood's reflector, which glows when the lamp is on.</summary>
    private StandardMaterial3D _reflector;

    /// <summary>The matt black of the housings.</summary>
    private static StandardMaterial3D Housing() => new() { AlbedoColor = new Color(0.03f, 0.03f, 0.03f), Roughness = 0.6f };

    /// <summary>Adds both housings; <see cref="ShowBody"/> shows the one that fits. They do not cast shadows.</summary>
    private void CreateBodies()
    {
        _lensBody = new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.6f },
            Position = new Vector3(0, 0, 0.3f),
            RotationDegrees = new Vector3(90, 0, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = Housing(),
        };
        AddChild(_lensBody);

        // The light sits in the open front (z = 0); the box reaches back (+z).
        _floodBody = new Node3D { Name = "FloodBody" };
        AddChild(_floodBody);
        var (w, h, d) = (FloodBox.X, FloodBox.Y, FloodBox.Z);
        var housing = Housing();
        AddPart(new Vector3(w, h, FloodWall), new Vector3(0, 0, d), housing);                     // back
        AddPart(new Vector3(FloodWall, h, d), new Vector3(-w / 2f, 0, d / 2f), housing);         // sides
        AddPart(new Vector3(FloodWall, h, d), new Vector3(w / 2f, 0, d / 2f), housing);
        AddPart(new Vector3(w, FloodWall, d), new Vector3(0, h / 2f, d / 2f), housing);          // top, bottom
        AddPart(new Vector3(w, FloodWall, d), new Vector3(0, -h / 2f, d / 2f), housing);
        // The hanging bracket: a strap down each side and a bar over the top.
        const float strap = 0.03f, above = 0.14f;
        AddPart(new Vector3(strap, h / 2f + above, strap), new Vector3(-w / 2f - strap, (h / 2f + above) / 2f, d / 2f), housing);
        AddPart(new Vector3(strap, h / 2f + above, strap), new Vector3(w / 2f + strap, (h / 2f + above) / 2f, d / 2f), housing);
        AddPart(new Vector3(w + 3f * strap, strap, strap), new Vector3(0, h / 2f + above, d / 2f), housing);
        // Hammered aluminium reflector: curved over the inside of the box, nearest the front at the top.
        _reflector = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.75f, 0.75f, 0.72f),
            Metallic = 0.7f,
            Roughness = 0.45f,
            EmissionEnabled = true,
        };
        AddPart(new Vector3(w - 2f * FloodWall, h - 2f * FloodWall, 0.004f), new Vector3(0, 0, d * 0.6f), _reflector,
            new Vector3(-28f, 0, 0));
        ShowBody();
    }

    /// <summary>A part of the flood's box.</summary>
    private void AddPart(Vector3 size, Vector3 position, Material material, Vector3 rotation = default) =>
        _floodBody.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = position,
            RotationDegrees = rotation,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = material,
        });

    /// <summary>Shows the housing of the lamp's kind and lights the flood's reflector with the lamp.</summary>
    private void ShowBody()
    {
        if (_floodBody == null)
            return;
        _lensBody.Visible = !IsFlood;
        _floodBody.Visible = IsFlood;
        // A ground row stands with its reflector the other way up.
        _floodBody.RotationDegrees = new Vector3(0, 0, _flood == FloodMounting.Bottom ? 180f : 0f);
        var c = _lampColor * _gelColor;
        _reflector.Emission = c;
        _reflector.EmissionEnergyMultiplier = _light != null && _light.Visible ? ReflectorGlow * _intensity * _output : 0f;
    }
}
