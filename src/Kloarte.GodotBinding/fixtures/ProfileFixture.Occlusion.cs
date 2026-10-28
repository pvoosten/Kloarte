using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The lamp's occlusion map (<see cref="BeamOcclusion"/>): a small SubViewport sharing the scene's World3D with a camera
/// at the lens, looking along the beam with the beam's full cone as field of view and only
/// <see cref="RenderLayers.Occluders"/> in its cull mask. A full-screen pass (<c>occlusion_depth.gdshader</c>) writes
/// the linear depth along the beam axis divided by the beam length into an HDR (half float, linear) target, so the
/// ViewportTexture is used by <c>beam_volume.gdshader</c> as is (no read-back, unlike the projector mask). The view is
/// unshaded, without shadows and with an environment of its own (no fog, no GI): only depth matters.
/// </summary>
public partial class ProfileFixture
{
    /// <summary>The shader of the depth pass that writes the occlusion map.</summary>
    private const string OcclusionShaderPath = "res://fixtures/occlusion_depth.gdshader";

    /// <summary>Node name of the depth pass quad (it is on the Occluders layer itself, but cuts nothing).</summary>
    private const string OcclusionPassName = "DepthPass";

    /// <summary>Near plane of the occlusion camera (m): scenery closer to the lens than this doesn't cut the beam.</summary>
    private const float OcclusionNear = 0.05f;

    /// <summary>The environment of every occlusion camera: plain background, no fog, glow, GI or tonemapping curve.</summary>
    private static Environment _occlusionEnvironment;

    /// <summary>The occlusion view; null in the editor.</summary>
    private SubViewport _occlusionViewport;

    /// <summary>The camera at the lens in <see cref="_occlusionViewport"/>.</summary>
    private Camera3D _occlusionCamera;

    /// <summary>The full-screen depth pass in front of <see cref="_occlusionCamera"/>, visible only while it renders.</summary>
    private MeshInstance3D _occlusionPass;

    /// <summary>The material of <see cref="_occlusionPass"/>.</summary>
    private ShaderMaterial _occlusionMaterial;

    /// <summary>The map is out of date (aim, cone, length, scenery).</summary>
    private bool _occlusionDirty = true;

    /// <summary>Scenery was in the beam at the last check (else there is no map and the beam isn't tested).</summary>
    private bool _occluded;

    /// <summary>A map has been rendered (until then the beam isn't cut).</summary>
    private bool _occlusionReady;

    /// <summary>What the beam shader was last told: use the map.</summary>
    private bool _occlusionUsed;

    /// <summary>Process frame of the last render (for <see cref="BeamOcclusion.MovingInterval"/>).</summary>
    private ulong _occlusionFrame;

    /// <summary>Counts renders, so a late "after draw" of an older one does nothing.</summary>
    private int _occlusionVersion;

    /// <summary>Tangent of the beam's half cone and its length (m), as the beam volume uses them.</summary>
    private float _beamTanHalf = -1f, _beamLength = -1f;

    /// <summary>Cone and length of the last rendered map.</summary>
    private float _occlusionTanHalf = -1f, _occlusionLength = -1f;

    /// <summary>The occlusion map (linear depth / range in red), null in the editor.</summary>
    internal Texture2D OcclusionTexture => _occlusionViewport?.GetTexture();

    /// <summary>
    /// The scenery that cuts the beam has changed (moved, added, removed): the lamp's occlusion map is rendered again
    /// when it's the lamp's turn (<see cref="BeamOcclusion"/>). Aim, zoom and beam length are followed by the lamp itself.
    /// </summary>
    public void InvalidateOcclusion() => _occlusionDirty = true;

    /// <summary>Creates the occlusion view (not in the editor).</summary>
    private void CreateOcclusion()
    {
        if (Engine.IsEditorHint())
            return;
        _occlusionEnvironment ??= new Environment
        {
            BackgroundMode = Environment.BGMode.Color,
            BackgroundColor = Colors.White,
            AmbientLightSource = Environment.AmbientSource.Disabled,
            ReflectedLightSource = Environment.ReflectionSource.Disabled,
            TonemapMode = Environment.ToneMapper.Linear,
        };
        _occlusionViewport = new SubViewport
        {
            Name = "Occlusion",
            Size = new Vector2I(BeamOcclusion.Resolution, BeamOcclusion.Resolution),
            UseHdr2D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            UseTaa = false,
            UseDebanding = false,
            UseOcclusionCulling = false,
            PositionalShadowAtlasSize = 0,
            DebugDraw = Viewport.DebugDrawEnum.Unshaded,
            GuiDisableInput = true,
            TransparentBg = false,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        _occlusionCamera = new Camera3D
        {
            Name = "Camera",
            CullMask = RenderLayers.Occluders,
            Near = OcclusionNear,
            Environment = _occlusionEnvironment,
            Current = true,
        };
        _occlusionMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(OcclusionShaderPath) };
        _occlusionMaterial.SetShaderParameter("occlusion_layers", RenderLayers.Occluders);
        _occlusionPass = new MeshInstance3D
        {
            Name = OcclusionPassName,
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f) },
            MaterialOverride = _occlusionMaterial,
            Layers = RenderLayers.Occluders,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            // The quad lies in the camera's plane, behind the near plane: an AABB in front so it isn't culled.
            CustomAabb = new Aabb(new Vector3(-1f, -1f, -2f), new Vector3(2f, 2f, 2f)),
            Visible = false,
        };
        _occlusionCamera.AddChild(_occlusionPass);
        _occlusionViewport.AddChild(_occlusionCamera);
        AddChild(_occlusionViewport);
        _beamMaterial.SetShaderParameter("occlusion", _occlusionViewport.GetTexture());
        _beamMaterial.SetShaderParameter("occlusion_enabled", false);
    }

    /// <summary>The beam volume's cone or length changed (from <see cref="Apply"/>).</summary>
    private void BeamShapeChanged(float tanHalf, float length)
    {
        _beamTanHalf = tanHalf;
        _beamLength = length;
        if (!Mathf.IsEqualApprox(tanHalf, _occlusionTanHalf) || !Mathf.IsEqualApprox(length, _occlusionLength))
            _occlusionDirty = true;
    }

    /// <summary>Every frame: asks for a new map when needed and tells the beam shader whether to use it.</summary>
    private void ProcessOcclusion()
    {
        if (_occlusionViewport == null)
            return;
        bool use = BeamOcclusion.Enabled && _occlusionReady && _occluded;
        if (use != _occlusionUsed)
        {
            _occlusionUsed = use;
            _beamMaterial.SetShaderParameter("occlusion_enabled", use);
        }
        if (BeamOcclusion.Enabled && _occlusionDirty && _beam.Visible
            && Engine.GetProcessFrames() - _occlusionFrame >= (ulong)BeamOcclusion.MovingInterval)
            BeamOcclusion.Request(this);
        BeamOcclusion.Pump();
    }

    /// <summary>
    /// Renders the map now (this frame), from the lens as it is; false when there is nothing to render (no visible
    /// beam, switched off, no scenery in the beam). Called by <see cref="BeamOcclusion.Pump"/>.
    /// </summary>
    internal bool RenderOcclusion()
    {
        if (_occlusionViewport == null || !BeamOcclusion.Enabled || !_beam.Visible || _beamLength <= 0f)
            return false;
        _occlusionDirty = false;
        _occlusionFrame = Engine.GetProcessFrames();
        _occlusionTanHalf = _beamTanHalf;
        _occlusionLength = _beamLength;
        _occlusionCamera.GlobalTransform = _light.GlobalTransform;
        _occlusionCamera.Fov = Mathf.RadToDeg(2f * Mathf.Atan(_beamTanHalf));
        _occlusionCamera.Far = _beamLength;
        float near = NearestOccluder();
        _occluded = near < _beamLength;
        if (!_occluded)
        {
            _occlusionReady = false;
            return false;   // nothing in the beam: no map, and the beam shader skips the test
        }
        _occlusionMaterial.SetShaderParameter("range", _beamLength);
        _beamMaterial.SetShaderParameter("occlusion_range", _beamLength);
        _beamMaterial.SetShaderParameter("occlusion_near", near);
        _occlusionPass.Visible = true;
        _occlusionViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        OcclusionAfterDraw(++_occlusionVersion);
        return true;
    }

    /// <summary>
    /// The visible instances on <see cref="RenderLayers.Occluders"/> (not a lamp's depth pass) whose AABB touches the
    /// occlusion camera's frustum (the beam), by the rendering server's cull of the scene (no rendering): the smallest
    /// depth along the beam axis of their AABBs' corners (m, conservative: nothing occludes nearer the lens), or
    /// infinity when none is in the beam.
    /// </summary>
    private float NearestOccluder()
    {
        var planes = new Godot.Collections.Array<Plane>(_occlusionCamera.GetFrustum());
        var toLight = _light.GlobalTransform.AffineInverse();
        float near = float.PositiveInfinity;
        foreach (ulong id in RenderingServer.InstancesCullConvex(planes, GetWorld3D().Scenario))
        {
            if (InstanceFromId(id) is not VisualInstance3D v || (v.Layers & RenderLayers.Occluders) == 0
                || !v.IsVisibleInTree() || v.Name == OcclusionPassName)
                continue;
            var box = toLight * v.GlobalTransform * v.GetAabb();
            near = Mathf.Min(near, Mathf.Max(0f, -box.End.Z));
        }
        return near;
    }

    /// <summary>After the frame that rendered the map: hides the depth pass and lets the beam use the map.</summary>
    private async void OcclusionAfterDraw(int version)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (version != _occlusionVersion || !IsInstanceValid(_occlusionPass))
            return;
        _occlusionPass.Visible = false;
        _occlusionReady = true;
    }
}
