using Godot;

// Conventional profile spot: field angle (zoom), 4 framing shutters, iris, gobo, focus.
// Hand-focus parameters come from the Inspector / config; only Intensity will be driven by DMX.
//
// Beam shaping: beam_mask.gdshader renders a mask into a SubViewport. Godot refuses a ViewportTexture
// as light_projector, so after the viewport has rendered the image is read back into a *new* ImageTexture
// (ImageTexture.Update() does not refresh Godot's projector atlas). This only happens when a parameter
// changes: the readback stalls the GPU and the atlas is rebuilt.
//
// Beam in haze: Godot's volumetric fog ignores light_projector, so the light's fog contribution is off and
// beam_volume.gdshader raymarches the same mask instead (CustomBeam = false shows Godot's fog for comparison).
//
// The node's position is the lens; it is aimed at FocusPoint (world space), like focusing a lamp.
// Generated children are not saved in the scene.
[Tool]
public partial class ProfileFixture : Node3D
{
    private const string MaskShaderPath = "res://fixtures/beam_mask.gdshader";
    private const string BeamShaderPath = "res://fixtures/beam_volume.gdshader";

    // Spot cone is a bit wider than the field so the gate edge comes from the mask, not the cone cut-off.
    private const float ConeMargin = 1.1f;

    // Wider beams are clamped: the projector maps a square onto tan(cone), which degenerates towards 90° half angle.
    public const float MaxFieldAngle = 120f;

    private float _intensity = 1f, _energy = 400f, _fogEnergy = 1f;
    private Color _lampColor = new(1.0f, 0.82f, 0.62f);   // ~3200 K tungsten
    private Color _gelColor = Colors.White;                // gel in the colour frame, multiplied with the lamp
    private float _fieldAngle = 26f, _softness = 0.01f, _fieldFalloff = 0.85f;
    private float _topDepth, _topAngle, _rightDepth, _rightAngle;
    private float _bottomDepth, _bottomAngle, _leftDepth, _leftAngle, _shutterRotation;
    private float _iris = 1f;
    private Texture2D _gobo;
    private float _goboRotation, _goboSize = 1f;
    private Vector3 _focusPoint = new(0, 0, -3);
    private bool _castShadows = true, _customBeam = true;
    private int _maskResolution = 512;

    [ExportGroup("Lamp")]
    [Export(PropertyHint.Range, "0,1,0.001")] public float Intensity { get => _intensity; set => Change(ref _intensity, value); }
    [Export] public float Energy { get => _energy; set => Change(ref _energy, value); }
    // Brightness of the beam in haze relative to the physically based value.
    [Export] public float FogEnergy { get => _fogEnergy; set => Change(ref _fogEnergy, value); }
    [Export] public bool CustomBeam { get => _customBeam; set => Change(ref _customBeam, value); }
    [Export] public Color LampColor { get => _lampColor; set => Change(ref _lampColor, value); }
    // Gel colour incl. its brightness (from the MagicQ patch); white = no gel.
    [Export] public Color GelColor { get => _gelColor; set => Change(ref _gelColor, value); }

    [ExportGroup("Optics")]
    [Export(PropertyHint.Range, "5,60,0.1,suffix:°")] public float FieldAngle { get => _fieldAngle; set => Change(ref _fieldAngle, value); }
    // Edge blur in gate radii: 0.005 = razor sharp, 0.1 = very soft.
    [Export(PropertyHint.Range, "0,0.3,0.001")] public float Softness { get => _softness; set => Change(ref _softness, value); }
    [Export(PropertyHint.Range, "0,1,0.01")] public float FieldFalloff { get => _fieldFalloff; set => Change(ref _fieldFalloff, value); }

    // Shutters are named after the beam edge they cut, seen from behind the fixture.
    // Depth: 0 = out, 1 = to the beam axis, 2 = fully across. Angle: blade tilt, counter-clockwise.
    [ExportGroup("Shutters")]
    [Export(PropertyHint.Range, "0,2,0.01")] public float TopDepth { get => _topDepth; set => Change(ref _topDepth, value); }
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float TopAngle { get => _topAngle; set => Change(ref _topAngle, value); }
    [Export(PropertyHint.Range, "0,2,0.01")] public float RightDepth { get => _rightDepth; set => Change(ref _rightDepth, value); }
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float RightAngle { get => _rightAngle; set => Change(ref _rightAngle, value); }
    [Export(PropertyHint.Range, "0,2,0.01")] public float BottomDepth { get => _bottomDepth; set => Change(ref _bottomDepth, value); }
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float BottomAngle { get => _bottomAngle; set => Change(ref _bottomAngle, value); }
    [Export(PropertyHint.Range, "0,2,0.01")] public float LeftDepth { get => _leftDepth; set => Change(ref _leftDepth, value); }
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float LeftAngle { get => _leftAngle; set => Change(ref _leftAngle, value); }
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float ShutterRotation { get => _shutterRotation; set => Change(ref _shutterRotation, value); }

    [ExportGroup("Iris")]
    [Export(PropertyHint.Range, "0,1,0.01")] public float Iris { get => _iris; set => Change(ref _iris, value); }

    [ExportGroup("Gobo")]
    [Export] public Texture2D Gobo { get => _gobo; set => Change(ref _gobo, value); }
    [Export(PropertyHint.Range, "-180,180,0.1,suffix:°")] public float GoboRotation { get => _goboRotation; set => Change(ref _goboRotation, value); }
    [Export(PropertyHint.Range, "0.2,2,0.01")] public float GoboSize { get => _goboSize; set => Change(ref _goboSize, value); }

    [ExportGroup("Rigging")]
    [Export] public Vector3 FocusPoint { get => _focusPoint; set => Change(ref _focusPoint, value); }
    [Export] public bool CastShadows { get => _castShadows; set => Change(ref _castShadows, value); }
    [Export] public int MaskResolution { get => _maskResolution; set => Change(ref _maskResolution, value); }

    [ExportGroup("Patch")]
    [Export] public int HeadNumber;
    [Export] public int DmxAddress;   // absolute, 1-based (513 = universe 2, channel 1); 0 = not patched

    private SpotLight3D _light;
    private SubViewport _maskViewport;
    private ShaderMaterial _maskMaterial, _beamMaterial;
    private MeshInstance3D _beam;
    private ImageTexture _maskTexture;
    private Transform3D _beamTransform;
    private bool _dirty = true;
    private int _maskVersion;

    public Texture2D MaskTexture => _maskTexture;

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _maskMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(MaskShaderPath) };
        _maskViewport = new SubViewport
        {
            Name = "Mask",
            Disable3D = true,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        var rect = new ColorRect { Material = _maskMaterial };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _maskViewport.AddChild(rect);
        AddChild(_maskViewport);

        _light = new SpotLight3D
        {
            Name = "Light",
            SpotRange = 40f,
            SpotAttenuation = 1f,           // inverse square
            SpotAngleAttenuation = 0.1f,    // flat field; the gate edge is in the mask
        };
        AddChild(_light);

        _beamMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(BeamShaderPath) };
        _beam = new MeshInstance3D
        {
            Name = "Beam",
            Mesh = new BoxMesh(),
            MaterialOverride = _beamMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_beam);

        // Lamp housing behind the lens; does not cast shadows (the light sits at its front).
        AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.6f },
            Position = new Vector3(0, 0, 0.3f),
            RotationDegrees = new Vector3(90, 0, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.03f, 0.03f), Roughness = 0.6f },
        });

        _dirty = true;
        Apply();
    }

    public override void _Process(double delta)
    {
        if (_dirty)
            Apply();
        if (_beam.Visible && _light.GlobalTransform != _beamTransform)
        {
            _beamTransform = _light.GlobalTransform;
            _beamMaterial.SetShaderParameter("world_to_light", new Projection(_beamTransform.AffineInverse()));
        }
    }

    private void Change<T>(ref T field, T value)
    {
        field = value;
        _dirty = true;
    }

    private void Apply()
    {
        if (_light == null)
            return;
        _dirty = false;

        if (IsInsideTree() && !GlobalPosition.IsEqualApprox(_focusPoint))
            LookAt(_focusPoint, Mathf.Abs(GlobalPosition.DirectionTo(_focusPoint).Y) > 0.99f ? Vector3.Forward : Vector3.Up);

        float halfField = Mathf.DegToRad(Mathf.Min(_fieldAngle, MaxFieldAngle) / 2f);
        float halfCone = Mathf.Min(halfField * ConeMargin, Mathf.DegToRad(89f));
        _light.SpotAngle = Mathf.RadToDeg(halfCone);
        _light.LightEnergy = _energy * _intensity;
        _light.LightVolumetricFogEnergy = _customBeam ? 0f : _fogEnergy;
        _light.LightColor = _lampColor * _gelColor;
        _light.ShadowEnabled = _castShadows;
        _light.Visible = _intensity > 0f;

        _maskViewport.Size = new Vector2I(_maskResolution, _maskResolution);
        var m = _maskMaterial;
        m.SetShaderParameter("gate_radius", Mathf.Tan(halfField) / Mathf.Tan(halfCone));
        m.SetShaderParameter("softness", _softness);
        m.SetShaderParameter("field_falloff", _fieldFalloff);
        m.SetShaderParameter("shutter_depth", new Vector4(_topDepth, _rightDepth, _bottomDepth, _leftDepth));
        m.SetShaderParameter("shutter_angle", new Vector4(_topAngle, _rightAngle, _bottomAngle, _leftAngle));
        m.SetShaderParameter("shutter_rotation", _shutterRotation);
        m.SetShaderParameter("iris", _iris);
        m.SetShaderParameter("gobo_enabled", _gobo != null);
        m.SetShaderParameter("gobo", _gobo);
        m.SetShaderParameter("gobo_rotation", _goboRotation);
        m.SetShaderParameter("gobo_size", _goboSize);
        _maskViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        ReadMaskAfterDraw(++_maskVersion);

        // Beam volume: a box around the frustum, from the lens to a bit past the focus point.
        float tanHalf = Mathf.Tan(halfCone);
        float length = Mathf.Max(5f, GlobalPosition.DistanceTo(_focusPoint) * 1.5f);
        _beam.Visible = _customBeam && _light.Visible;
        ((BoxMesh)_beam.Mesh).Size = new Vector3(2f * tanHalf * length, 2f * tanHalf * length, length);
        _beam.Position = new Vector3(0, 0, -length / 2f);
        _beamTransform = default;   // force world_to_light update
        var b = _beamMaterial;
        b.SetShaderParameter("tan_half", tanHalf);
        b.SetShaderParameter("beam_length", length);
        var c = _lampColor * _gelColor;
        b.SetShaderParameter("light_color", new Vector3(c.R, c.G, c.B) * _light.LightEnergy);
        b.SetShaderParameter("gain", _fogEnergy);
    }

    private async void ReadMaskAfterDraw(int version)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (version != _maskVersion || !IsInstanceValid(_maskViewport))
            return;   // a newer change is pending

        _maskTexture = ImageTexture.CreateFromImage(_maskViewport.GetTexture().GetImage());
        _light.LightProjector = _maskTexture;
        _beamMaterial.SetShaderParameter("mask", _maskTexture);
    }
}
