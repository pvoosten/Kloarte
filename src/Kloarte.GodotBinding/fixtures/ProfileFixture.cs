using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// A fixture's light: field angle (zoom), 4 framing shutters, iris, gobo, focus, barndoors, PAR oval, asymmetric flood.
/// Used for every kind (the catalogue model decides which settings exist). Hand-focus parameters come from the
/// Inspector / mechanics; Intensity (and for moving heads the DMX attributes) is driven by playback.
///
/// Light strength: with Lumens set, the peak intensity follows from the field angle (Photometrics: narrower = brighter),
/// with PeakCandela set (PAR lamps) it is fixed; otherwise Energy is used as is. The light falls off with the square of
/// the distance (SpotAttenuation 2 = inverse square in Godot 4).
///
/// Beam shaping: beam_mask.gdshader renders a mask into a SubViewport. Godot refuses a ViewportTexture
/// as light_projector, so after the viewport has rendered the image is read back into a *new* ImageTexture
/// (ImageTexture.Update() does not refresh Godot's projector atlas). This only happens when a parameter
/// changes: the readback stalls the GPU and the atlas is rebuilt.
///
/// Beam in haze: Godot's volumetric fog ignores light_projector, so the light's fog contribution is off and
/// beam_volume.gdshader raymarches the same mask instead (CustomBeam = false shows Godot's fog for comparison).
/// Scenery on <see cref="RenderLayers.Occluders"/> cuts that beam: see ProfileFixture.Occlusion.cs and
/// <see cref="BeamOcclusion"/>.
///
/// The node's position is the lens; it is aimed at FocusPoint (world space), like focusing a lamp.
/// Generated children are not saved in the scene.
/// </summary>
[Tool]
public partial class ProfileFixture : Node3D
{
    private const string MaskShaderPath = "res://fixtures/beam_mask.gdshader";
    private const string BeamShaderPath = "res://fixtures/beam_volume.gdshader";

    /// <summary>
    /// Spot cone is a bit wider than the field so the gate edge comes from the mask, not the cone cut-off.
    /// </summary>
    private const float ConeMargin = 1.1f;

    /// <summary>
    /// Wider beams are clamped: the projector maps a square onto tan(cone), which degenerates towards 90° half angle.
    /// </summary>
    private const float MaxFieldAngle = 120f;
    /// <summary>asymmetric floods: no cone margin, distribution in the mask</summary>
    private const float MaxFloodAngle = 170f;
    /// <summary>Half cone of a flood light (degrees), see Apply.</summary>
    private const float MaxFloodHalfCone = 89f;

    /// <summary>Godot light energy per candela (calibration: a 1 kW profile at 26° ~ 3300 energy).</summary>
    private const float CandelaToEnergy = 0.075f;

    private float _intensity = 1f, _energy = 400f, _fogEnergy = 1f;
    /// <summary>~3200 K tungsten</summary>
    private Color _lampColor = new(1.0f, 0.82f, 0.62f);
    /// <summary>gel in the colour frame, multiplied with the lamp</summary>
    private Color _gelColor = Colors.White;
    private float _fieldAngle = 26f, _softness = 0.01f, _fieldFalloff = 0.85f;
    private float _topDepth, _topAngle, _rightDepth, _rightAngle;
    private float _bottomDepth, _bottomAngle, _leftDepth, _leftAngle, _shutterRotation;
    private float _iris = 1f;
    private Texture2D _gobo;
    private float _goboRotation, _goboSize = 1f;
    private Vector3 _focusPoint = new(0, 0, -3);
    private bool _castShadows = true, _customBeam = true;
    private int _maskResolution = 512;
    private float _lumens, _peakCandela, _output = 1f;
    private float _barnTop, _barnRight, _barnBottom, _barnLeft, _barnRotation;
    private float _ovalRatio = 1f, _ovalRotation;
    private FloodMounting _flood = FloodMounting.None;
    private bool _autoAim = true;

    /// <summary>Dimmer level 0..1 (from cues, Art-Net or the panel).</summary>
    [ExportGroup("Lamp")]
    [Export(PropertyHint.Range, "0,1,0.001")] public float Intensity { get => _intensity; set => ChangeLight(ref _intensity, value); }
    /// <summary>
    /// Godot light energy at full intensity; derived from Lumens / PeakCandela when one of them is set.
    /// </summary>
    [Export] public float Energy { get => PhotometricEnergy() ?? _energy; set => ChangeLight(ref _energy, value, mechanics: true); }
    /// <summary>Flux through the lens (lm); 0 = not photometric.</summary>
    [Export] public float Lumens { get => _lumens; set => ChangeLight(ref _lumens, value); }
    /// <summary>Fixed peak intensity (cd) of a fixed-beam lamp (PAR); 0 = from Lumens.</summary>
    [Export] public float PeakCandela { get => _peakCandela; set => ChangeLight(ref _peakCandela, value); }
    /// <summary>Output trim (dirty lens, old lamp...): 1 = as the catalogue says.</summary>
    [Export(PropertyHint.Range, "0,1.5,0.01")] public float Output { get => _output; set => ChangeLight(ref _output, value, mechanics: true); }
    /// <summary>Brightness of the beam in haze relative to the physically based value.</summary>
    [Export] public float FogEnergy { get => _fogEnergy; set => ChangeLight(ref _fogEnergy, value); }
    /// <summary>Draw the beam in haze with the raymarched beam volume (else Godot's volumetric fog).</summary>
    [Export] public bool CustomBeam { get => _customBeam; set => Change(ref _customBeam, value); }
    /// <summary>Colour of the lamp (e.g. 3200 K tungsten).</summary>
    [Export] public Color LampColor { get => _lampColor; set => ChangeLight(ref _lampColor, value, mechanics: true); }
    /// <summary>Gel colour incl. its brightness (from the MagicQ patch); white = no gel.</summary>
    [Export] public Color GelColor { get => _gelColor; set => ChangeLight(ref _gelColor, value); }

    /// <summary>Field angle (degrees): zoom / spot-flood, or the PAR oval's width.</summary>
    [ExportGroup("Optics")]
    [Export(PropertyHint.Range, "5,60,0.1,suffix:°")] public float FieldAngle { get => _fieldAngle; set => Change(ref _fieldAngle, value); }
    /// <summary>Edge blur in gate radii: 0.005 = razor sharp, 0.1 = very soft.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.001")] public float Softness { get => _softness; set => Change(ref _softness, value); }
    /// <summary>Intensity at the field edge relative to the centre.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float FieldFalloff { get => _fieldFalloff; set => Change(ref _fieldFalloff, value); }

    /// <summary>
    /// Shutters are named after the beam edge they cut, seen from behind the fixture.
    /// Depth: 0 = out, 1 = to the beam axis, 2 = fully across. Angle: blade tilt, counter-clockwise.
    /// </summary>
    [ExportGroup("Shutters")]
    [Export(PropertyHint.Range, "0,2,0.01")] public float TopDepth { get => _topDepth; set => Change(ref _topDepth, value); }
    /// <summary>Tilt of the top shutter blade (degrees).</summary>
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float TopAngle { get => _topAngle; set => Change(ref _topAngle, value); }
    /// <summary>Right shutter: 0 = out, 1 = to the axis, 2 = fully across.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")] public float RightDepth { get => _rightDepth; set => Change(ref _rightDepth, value); }
    /// <summary>Tilt of the right shutter blade (degrees).</summary>
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float RightAngle { get => _rightAngle; set => Change(ref _rightAngle, value); }
    /// <summary>Bottom shutter: 0 = out, 1 = to the axis, 2 = fully across.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")] public float BottomDepth { get => _bottomDepth; set => Change(ref _bottomDepth, value); }
    /// <summary>Tilt of the bottom shutter blade (degrees).</summary>
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float BottomAngle { get => _bottomAngle; set => Change(ref _bottomAngle, value); }
    /// <summary>Left shutter: 0 = out, 1 = to the axis, 2 = fully across.</summary>
    [Export(PropertyHint.Range, "0,2,0.01")] public float LeftDepth { get => _leftDepth; set => Change(ref _leftDepth, value); }
    /// <summary>Tilt of the left shutter blade (degrees).</summary>
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float LeftAngle { get => _leftAngle; set => Change(ref _leftAngle, value); }
    /// <summary>Rotation of the whole shutter assembly (degrees).</summary>
    [Export(PropertyHint.Range, "-45,45,0.1,suffix:°")] public float ShutterRotation { get => _shutterRotation; set => Change(ref _shutterRotation, value); }

    /// <summary>
    /// Barndoor flaps: 0 = folded open, 1 = closed across the lens; very soft edge (the flaps are at the lens).
    /// </summary>
    [ExportGroup("Barndoors")]
    [Export(PropertyHint.Range, "0,1,0.01")] public float BarndoorTop { get => _barnTop; set => Change(ref _barnTop, value); }
    /// <summary>Right barndoor flap: 0 = open, 1 = closed.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BarndoorRight { get => _barnRight; set => Change(ref _barnRight, value); }
    /// <summary>Bottom barndoor flap: 0 = open, 1 = closed.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BarndoorBottom { get => _barnBottom; set => Change(ref _barnBottom, value); }
    /// <summary>Left barndoor flap: 0 = open, 1 = closed.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BarndoorLeft { get => _barnLeft; set => Change(ref _barnLeft, value); }
    /// <summary>Rotation of the barndoor ring (degrees).</summary>
    [Export(PropertyHint.Range, "-90,90,0.5,suffix:°")] public float BarndoorRotation { get => _barnRotation; set => Change(ref _barnRotation, value); }

    /// <summary>
    /// PAR: oval beam. Ratio = height / width of the field (1 = round); FieldAngle is the width. Rotation of the bottle.
    /// </summary>
    [ExportGroup("Oval")]
    [Export(PropertyHint.Range, "0.1,1,0.01")] public float OvalRatio { get => _ovalRatio; set => Change(ref _ovalRatio, value); }
    /// <summary>Rotation of the PAR oval (degrees).</summary>
    [Export(PropertyHint.Range, "-90,90,0.5,suffix:°")] public float OvalRotation { get => _ovalRotation; set => Change(ref _ovalRotation, value); }

    /// <summary>
    /// Asymmetric cyc flood: the distribution is in the mask; Top = hung above the cloth (throws down), Bottom = ground row.
    /// </summary>
    [ExportGroup("Flood")]
    [Export] public FloodMounting Flood { get => _flood; set => Change(ref _flood, value); }

    /// <summary>Iris diameter relative to the gate (1 = open).</summary>
    [ExportGroup("Iris")]
    [Export(PropertyHint.Range, "0,1,0.01")] public float Iris { get => _iris; set => Change(ref _iris, value); }

    /// <summary>Gobo image (white = light passes), null = none.</summary>
    [ExportGroup("Gobo")]
    [Export] public Texture2D Gobo { get => _gobo; set => Change(ref _gobo, value); }
    /// <summary>Gobo rotation (degrees).</summary>
    [Export(PropertyHint.Range, "-180,180,0.1,suffix:°")] public float GoboRotation { get => _goboRotation; set => Change(ref _goboRotation, value); }
    /// <summary>Gobo image width relative to the gate.</summary>
    [Export(PropertyHint.Range, "0.2,2,0.01")] public float GoboSize { get => _goboSize; set => Change(ref _goboSize, value); }

    /// <summary>Where the lamp is aimed (world coordinates); hand focus.</summary>
    [ExportGroup("Rigging")]
    [Export] public Vector3 FocusPoint { get => _focusPoint; set => Change(ref _focusPoint, value); }
    /// <summary>
    /// false: the parent (a moving head's tilt node) aims the light; FocusPoint is not used to turn it.
    /// </summary>
    [Export] public bool AutoAim { get => _autoAim; set => Change(ref _autoAim, value); }
    /// <summary>The light casts shadows (else nothing is a shadow caster for it).</summary>
    [Export] public bool CastShadows { get => _castShadows; set => Change(ref _castShadows, value); }
    /// <summary>Size (pixels) of the beam mask texture.</summary>
    [Export] public int MaskResolution { get => _maskResolution; set => Change(ref _maskResolution, value); }

    /// <summary>MagicQ head number.</summary>
    [ExportGroup("Patch")]
    [Export] public int HeadNumber;
    /// <summary>absolute, 1-based (513 = universe 2, channel 1); 0 = not patched</summary>
    [Export] public int DmxAddress;

    private SpotLight3D _light;
    private SubViewport _maskViewport;
    private ShaderMaterial _maskMaterial, _beamMaterial;
    private MeshInstance3D _beam;
    private ImageTexture _maskTexture;
    private Transform3D _beamTransform;
    private bool _dirty = true;
    private bool _lightDirty;
    private int _maskVersion;

    /// <summary>The current beam mask (projector image).</summary>
    public Texture2D MaskTexture => _maskTexture;

    /// <summary>True for an asymmetric flood.</summary>
    private bool IsFlood => _flood != FloodMounting.None;
    /// <summary>Largest field angle for this lamp (degrees).</summary>
    public float MaxAngle => IsFlood ? MaxFloodAngle : MaxFieldAngle;

    /// <summary>Luminous flux leaving the fixture at the current intensity (lm), for the stray light.</summary>
    public float Flux => (_lumens > 0f ? _lumens
                          : _peakCandela > 0f ? _peakCandela * (float)Photometrics.SolidAngle(_fieldAngle) * Mathf.Max(_ovalRatio, 0.1f) * 0.6f
                          : _energy * 2f) * _output * _intensity;

    private float? PhotometricEnergy()
    {
        float field = Mathf.Min(_fieldAngle, MaxAngle);
        double candela = _peakCandela > 0f ? _peakCandela
            : _lumens <= 0f ? -1
            : IsFlood ? _lumens / FloodSolidAngle.Value
            : Photometrics.PeakCandela(_lumens, field, _fieldFalloff) / Mathf.Max(_ovalRatio, 0.1f);
        return candela < 0 ? null : (float)candela * CandelaToEnergy * _output;
    }

    private static readonly System.Lazy<double> FloodSolidAngle = new(FloodDistribution.EffectiveSolidAngle);

    /// <inheritdoc />
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
            SpotAttenuation = 2f,           // inverse square (Godot 4: 2 = physical)
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
        CreateOcclusion();

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

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_dirty)
            Apply();
        else if (_lightDirty)
            ApplyLight();
        if (_beam.Visible && _light.GlobalTransform != _beamTransform)
        {
            _beamTransform = _light.GlobalTransform;
            _beamMaterial.SetShaderParameter("world_to_light", new Projection(_beamTransform.AffineInverse()));
            _occlusionDirty = true;
        }
        ProcessOcclusion();
    }

    /// <summary>Turns the lamp to its focus point now (normally done on the next frame after a change).</summary>
    public void AimAtFocus()
    {
        if (_autoAim && IsInsideTree() && !GlobalPosition.IsEqualApprox(_focusPoint))
            LookAt(_focusPoint, Mathf.Abs(GlobalPosition.DirectionTo(_focusPoint).Y) > 0.99f ? Vector3.Forward : Vector3.Up);
    }

    /// <summary>A setting that changes the mask, cone or aim: the whole lamp is updated (mask re-rendered).</summary>
    private void Change<T>(ref T field, T value)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        _dirty = true;
        MechanicsChanged?.Invoke(this);
    }

    /// <summary>
    /// A setting that only changes the light's strength or colour (intensity during fades): cheap update. `mechanics`:
    /// it is part of the lamp's mechanical set-up (output, lamp colour), so <see cref="MechanicsChanged"/> is raised;
    /// intensity and gel are not.
    /// </summary>
    private void ChangeLight<T>(ref T field, T value, bool mechanics = false)
    {
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        _lightDirty = true;
        if (mechanics)
            MechanicsChanged?.Invoke(this);
    }

    /// <summary>
    /// Raised when a setting of the mechanical set-up changes (optics, shutters, iris, gobo, barndoors, focus point,
    /// output, lamp colour; not intensity or gel), so the show model can follow.
    /// </summary>
    public event System.Action<ProfileFixture> MechanicsChanged;

    private void Apply()
    {
        if (_light == null)
            return;
        _dirty = false;

        AimAtFocus();

        float halfField = Mathf.DegToRad(Mathf.Min(_fieldAngle, MaxAngle) / 2f);
        // Floods: a 89° cone: Godot's spot shadow map (needed for the projector) cuts the light off ~5° inside the cone
        // edge, unevenly; the distribution in the mask fades out by 85°.
        float halfCone = IsFlood ? Mathf.DegToRad(MaxFloodHalfCone) : Mathf.Min(halfField * ConeMargin, Mathf.DegToRad(89f));
        _light.SpotAngle = Mathf.RadToDeg(halfCone);
        _light.SpotAngleAttenuation = IsFlood ? 0.02f : 0.1f;
        // Godot 4.7: a spot light with a projector and ShadowEnabled = false gives no light at all. So shadows stay
        // enabled; "no shadows" = nothing is a shadow caster for this light.
        _light.ShadowEnabled = true;
        _light.ShadowCasterMask = _castShadows && !IsFlood ? 0xFFFFFFFFu : 0u;
        ApplyLight();

        if (IsFlood)
        {
            _maskVersion++;   // cancels a pending mask read-back
            _maskTexture = FloodMask(_flood, Mathf.RadToDeg(halfCone));
            _light.LightProjector = _maskTexture;
            _beam.Visible = false;
            return;
        }

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
        m.SetShaderParameter("barndoor", new Vector4(_barnTop, _barnRight, _barnBottom, _barnLeft));
        m.SetShaderParameter("barndoor_rotation", _barnRotation);
        m.SetShaderParameter("oval", new Vector2(1f, Mathf.Clamp(_ovalRatio, 0.1f, 1f)));
        m.SetShaderParameter("oval_rotation", _ovalRotation);
        _maskViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        ReadMaskAfterDraw(++_maskVersion);

        // Beam volume: a box around the frustum, from the lens to a bit past the focus point.
        float tanHalf = Mathf.Tan(halfCone);
        float length = Mathf.Max(5f, GlobalPosition.DistanceTo(_focusPoint) * 1.5f);
        ((BoxMesh)_beam.Mesh).Size = new Vector3(2f * tanHalf * length, 2f * tanHalf * length, length);
        _beam.Position = new Vector3(0, 0, -length / 2f);
        _beamTransform = default;   // force world_to_light update
        var b = _beamMaterial;
        b.SetShaderParameter("tan_half", tanHalf);
        b.SetShaderParameter("beam_length", length);
        BeamShapeChanged(tanHalf, length);
        ApplyLight();
    }

    /// <summary>Strength, colour and visibility of the light and its beam in haze (no mask work).</summary>
    private void ApplyLight()
    {
        _lightDirty = false;
        if (_light == null)
            return;
        _light.LightEnergy = Energy * _intensity;
        // Floods: no raymarched beam (a 170° cone is not a beam); a little of Godot's fog glow near the cloth.
        _light.LightVolumetricFogEnergy = IsFlood ? 0.15f * _fogEnergy : _customBeam ? 0f : _fogEnergy;
        _light.LightColor = _lampColor * _gelColor;
        _light.Visible = _intensity > 0f;
        if (IsFlood)
            return;
        _beam.Visible = _customBeam && _light.Visible;
        var c = _lampColor * _gelColor;
        _beamMaterial.SetShaderParameter("light_color", new Vector3(c.R, c.G, c.B) * _light.LightEnergy);
        _beamMaterial.SetShaderParameter("gain", _fogEnergy);
    }

    /// <summary>
    /// Asymmetric flood distribution as projector image (computed once per mounting / angle). Projector space: image top =
    /// light +Y; texel -&gt; direction (x, y, -1) * tan(half cone). Top mounting throws towards -Y (down the cloth).
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<(FloodMounting, int), ImageTexture> FloodMasks = new();

    private static ImageTexture FloodMask(FloodMounting mounting, float halfConeDegrees)
    {
        var key = (mounting, Mathf.RoundToInt(halfConeDegrees * 10f));
        if (FloodMasks.TryGetValue(key, out var cached))
            return cached;
        const int size = 1024;
        float tanHalf = Mathf.Tan(Mathf.DegToRad(halfConeDegrees));
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = ((i + 0.5f) / size * 2f - 1f) * tanHalf;
                float y = (1f - (j + 0.5f) / size * 2f) * tanHalf;
                double h = Mathf.RadToDeg(Mathf.Atan(x));
                double up = Mathf.RadToDeg(Mathf.Atan(y));
                float v = (float)FloodDistribution.Relative(h, mounting == FloodMounting.Top ? -up : up);
                image.SetPixel(i, j, new Color(v, v, v));
            }
        return FloodMasks[key] = ImageTexture.CreateFromImage(image);
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
