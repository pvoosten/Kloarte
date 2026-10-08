using System.Collections.Generic;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// A moving head: base (at the MagicVis position, with its mounting rotation; MagicVis heads point down at rotation 0),
/// yoke turning with pan, head turning with tilt, and the lamp (a ProfileFixture, AutoAim off) at the front of the
/// head. Bodies from the GDTF's own 3D models when they load, else simple shapes. <see cref="Apply"/> takes a decoded
/// DMX state: pan / tilt, colour, zoom, focus / frost, iris, gobo (image from the GDTF wheel), framing blades, shutter.
/// </summary>
public partial class MovingHead : Node3D
{
    /// <summary>The lamp in the head.</summary>
    public ProfileFixture Lamp { get; private set; }

    /// <summary>The GDTF fixture type.</summary>
    public GdtfFixture Gdtf { get; private set; }

    /// <summary>The GDTF mode matching the MagicQ personality.</summary>
    public GdtfMode Mode { get; private set; }

    /// <summary>Pan node (turns about its local Y).</summary>
    public Node3D Yoke { get; private set; }

    /// <summary>Tilt node (turns about its local X).</summary>
    public Node3D Head { get; private set; }

    /// <summary>Last applied state.</summary>
    public MovingHeadState State { get; private set; } = new();

    private float _baseOutput = 1f;
    private static readonly Dictionary<(string, string), Texture2D> Gobos = new();

    /// <summary>Builds a moving head around a lamp (the lamp is moved into the head).</summary>
    public static MovingHead Create(ProfileFixture lamp, GdtfFixture gdtf, GdtfMode mode, Transform3D mounting)
    {
        var mh = new MovingHead { Name = lamp.Name + " head", Gdtf = gdtf, Mode = mode, Transform = mounting };
        mh.Yoke = new Node3D { Name = "Yoke", Position = new Vector3(0, -gdtf.YokeOffset, 0) };
        mh.Head = new Node3D { Name = "Head", Position = new Vector3(0, -gdtf.HeadOffset, 0) };
        mh.AddChild(mh.Yoke);
        mh.Yoke.AddChild(mh.Head);
        mh.Lamp = lamp;
        mh._baseOutput = lamp.Output;
        lamp.AutoAim = false;
        lamp.Position = new Vector3(0, -gdtf.BeamOffset, 0);
        lamp.RotationDegrees = new Vector3(-90, 0, 0);   // the lamp's -Z (beam) along the head's -Y
        lamp.SetMeta("movingHead", true);
        mh.Head.AddChild(lamp);
        mh.AddBodies();
        return mh;
    }

    private void AddBodies()
    {
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.04f, 0.04f, 0.045f), Roughness = 0.5f };
        if (!TryModel("base", this) )
            AddChild(Box(new Vector3(0.3f, 0.1f, 0.3f), new Vector3(0, -0.05f, 0), dark));
        if (!TryModel("yoke", Yoke))
        {
            Yoke.AddChild(Box(new Vector3(0.04f, Gdtf.HeadOffset + 0.05f, 0.08f), new Vector3(-0.16f, -Gdtf.HeadOffset / 2f, 0), dark));
            Yoke.AddChild(Box(new Vector3(0.04f, Gdtf.HeadOffset + 0.05f, 0.08f), new Vector3(0.16f, -Gdtf.HeadOffset / 2f, 0), dark));
            Yoke.AddChild(Box(new Vector3(0.36f, 0.04f, 0.08f), new Vector3(0, 0, 0), dark));
        }
        if (!TryModel("head", Head))
            Head.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.13f, Height = Gdtf.BeamOffset * 2f },
                MaterialOverride = dark,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
    }

    /// <summary>Loads models/gltf/&lt;name&gt;.glb from the GDTF (GDTF: Z up, hanging; glTF: Y up), false if it fails.</summary>
    private bool TryModel(string name, Node3D parent)
    {
        if (!Gdtf.Files.TryGetValue($"models/gltf/{name}.glb", out var bytes))
            return false;
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromBuffer(bytes, "", state) != Error.Ok || doc.GenerateScene(state) is not Node3D scene)
            return false;
        foreach (var mesh in scene.FindChildren("*", "MeshInstance3D", true, false))
            ((MeshInstance3D)mesh).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        parent.AddChild(scene);
        return true;
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Material material) => new()
    {
        Mesh = new BoxMesh { Size = size },
        Position = position,
        MaterialOverride = material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    /// <summary>Puts a decoded DMX state on the head and its lamp.</summary>
    public void Apply(MovingHeadState s)
    {
        State = s;
        Yoke.Rotation = new Vector3(0, Mathf.DegToRad(s.Pan), 0);
        Head.Rotation = new Vector3(Mathf.DegToRad(s.Tilt), 0, 0);
        var l = Lamp;
        l.GelColor = new Color(s.R, s.G, s.B);
        if (s.Zoom > 0)
            l.FieldAngle = s.Zoom;
        l.Softness = Mathf.Clamp(0.01f + 0.12f * Mathf.Abs(s.Focus - 0.5f) * 2f + 0.25f * s.Frost, 0.005f, 0.3f);
        l.Iris = Mathf.Max(0.05f, s.Iris);
        l.Gobo = GoboTexture(s);
        l.GoboRotation = s.GoboRotation;
        l.TopDepth = s.BladeIn[0] * 1.4f;
        l.RightDepth = s.BladeIn[1] * 1.4f;
        l.BottomDepth = s.BladeIn[2] * 1.4f;
        l.LeftDepth = s.BladeIn[3] * 1.4f;
        l.TopAngle = s.BladeRotation[0];
        l.RightAngle = s.BladeRotation[1];
        l.BottomAngle = s.BladeRotation[2];
        l.LeftAngle = s.BladeRotation[3];
        l.ShutterRotation = s.ShaperRotation;
        l.Output = s.ShutterOpen ? _baseOutput : 0f;
    }

    private Texture2D GoboTexture(MovingHeadState s)
    {
        if (string.IsNullOrEmpty(s.GoboMedia))
            return null;
        var key = (Gdtf.Name, s.GoboMedia);
        if (Gobos.TryGetValue(key, out var t))
            return t;
        var bytes = Gdtf.Media(s.GoboMedia);
        if (bytes == null)
            return null;
        var image = new Image();
        if (image.LoadPngFromBuffer(bytes) != Error.Ok)
            return null;
        image.Convert(Image.Format.Rgb8);
        return Gobos[key] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>Pan and tilt (degrees) that point the beam at a world point; tilt kept within the GDTF range.</summary>
    public (float Pan, float Tilt) AimAt(Vector3 world)
    {
        var d = (GlobalBasis.Inverse() * (world - Head.GlobalPosition)).Normalized();
        float tilt = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(-d.Y, -1f, 1f)));
        float pan = Mathf.Abs(Mathf.Sin(Mathf.DegToRad(tilt))) < 1e-4f ? State.Pan : Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Z));
        var panRange = Range("Pan");
        if (pan < panRange.Min || pan > panRange.Max)
        {
            // The other way round: pan 180° further and tilt to the other side.
            pan = pan > 0 ? pan - 180f : pan + 180f;
            tilt = -tilt;
        }
        return (pan, tilt);
    }

    /// <summary>Physical range of an attribute in the mode (e.g. Pan -225..225).</summary>
    public (float Min, float Max) Range(string attribute)
    {
        var f = Mode.Channel(attribute)?.Function(attribute);
        return f == null ? (-180f, 180f) : (Mathf.Min(f.PhysicalFrom, f.PhysicalTo), Mathf.Max(f.PhysicalFrom, f.PhysicalTo));
    }
}
