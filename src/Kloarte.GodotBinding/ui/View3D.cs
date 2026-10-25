using System;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// A 3D view: a SubViewport rendering the same world as the scene (shared World3D: same lights, actors, haze) through
/// its own camera. The main "3D" view follows a source camera (the scene's FlyCamera, which the user steers through
/// this view: mouse here, keys while it is the main view). As a small card it renders only every few frames.
/// Left click raises <see cref="StageClicked"/> with the ray under the mouse.
/// </summary>
public partial class View3D : ViewBase
{
    /// <summary>Render every n-th frame while compact (cards are previews; 3D is the expensive part).</summary>
    private const int CompactInterval = 3;

    /// <summary>Raised on a left click in the view: ray origin and direction (world), mouse position in the view.</summary>
    public event Action<Vector3, Vector3, Vector2> StageClicked;

    /// <summary>
    /// Gets the view's input (not left clicks) before the camera, e.g. the wheel for the selected head's intensity;
    /// returns true when it used the event.
    /// </summary>
    public Func<InputEvent, bool> BeforeCamera { get; set; }

    /// <summary>The camera this view follows (null: the view steers its own camera, e.g. the audience view).</summary>
    public FlyCamera Source { get; set; }

    /// <summary>The camera rendering this view.</summary>
    public Camera3D Camera { get; private set; }

    /// <summary>The SubViewport the view renders into.</summary>
    public SubViewport Viewport3D { get; private set; }

    private SubViewportContainer _container;
    private int _frame, _forceFrames;
    private readonly string _title;

    /// <summary>Creates a 3D view with a title (and optionally a camera of its own).</summary>
    public View3D(string title = "3D", Camera3D camera = null)
    {
        _title = title;
        Name = title;
        Camera = camera ?? new Camera3D { Fov = 55f };
    }

    /// <summary>Godot needs a parameterless constructor.</summary>
    public View3D() : this("3D") { }

    /// <inheritdoc />
    public override string Title => _title;

    /// <summary>The view steers its own camera (audience seat, actor's eyes) instead of following the scene camera.</summary>
    public virtual bool OwnCamera => false;

    /// <inheritdoc />
    public override string Context => "3d";

    /// <inheritdoc />
    public override string ShowAction => "view_3d";

    /// <summary>Builds the SubViewport on the scene's world.</summary>
    public override void _Ready()
    {
        _container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        _container.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_container);
        var root = GetTree().Root;
        Viewport3D = new SubViewport
        {
            World3D = root.World3D,
            Msaa3D = root.Msaa3D,
            ScreenSpaceAA = root.ScreenSpaceAA,
            UseTaa = root.UseTaa,
            UseHdr2D = root.UseHdr2D,
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        ShowBuilder.ConfigureShadowAtlas(Viewport3D);   // its own atlas: a place for every lamp (finding 7)
        _container.AddChild(Viewport3D);
        Viewport3D.AddChild(Camera);
        Camera.Current = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
    }

    /// <summary>Follows the source camera; throttles while compact.</summary>
    public override void _Process(double delta)
    {
        if (Source != null && IsInstanceValid(Source))
        {
            Camera.GlobalTransform = Source.GlobalTransform;
            Camera.Fov = Source.Fov;
            Camera.Near = Source.Near;
            Camera.Far = Source.Far;
        }
        Viewport3D.UseHdr2D = GetTree().Root.UseHdr2D;
        if (_forceFrames > 0)
        {
            _forceFrames--;
            Viewport3D.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        }
        else if (!IsVisibleInTree())
            Viewport3D.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;   // e.g. the panel is hidden
        else if (Compact)
            Viewport3D.RenderTargetUpdateMode = ++_frame % CompactInterval == 0
                ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
        else
            Viewport3D.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    /// <summary>Mouse in the view: camera control (source camera) and clicks on the stage.</summary>
    public override void _GuiInput(InputEvent e)
    {
        if (!IsMain)
            return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            GrabFocus();   // out of any text field, so held keys (Shift = aim) count
            var (origin, dir) = Ray(click.Position);
            StageClicked?.Invoke(origin, dir, click.Position);
            AcceptEvent();
            return;
        }
        if (BeforeCamera?.Invoke(e) == true || OnCameraInput(e))
            AcceptEvent();
    }

    /// <summary>Camera control for an input event (also while the mouse is captured, via the host); true when used.</summary>
    public bool CameraInput(InputEvent e) => OnCameraInput(e);

    /// <summary>Camera control for an input event; true when used.</summary>
    protected virtual bool OnCameraInput(InputEvent e) => Source != null && Source.HandleInput(e);

    /// <summary>World ray under a position in the view (view coordinates).</summary>
    private (Vector3 Origin, Vector3 Dir) Ray(Vector2 viewPos)
    {
        var p = viewPos * (Vector2)Viewport3D.Size / Size;
        return (Camera.ProjectRayOrigin(p), Camera.ProjectRayNormal(p));
    }

    /// <summary>View coordinates of a world point, or null when it is behind the camera.</summary>
    public Vector2? ToView(Vector3 world)
    {
        if (Camera.IsPositionBehind(world))
            return null;
        return Camera.UnprojectPosition(world) * Size / (Vector2)Viewport3D.Size;
    }

    /// <summary>Renders every frame for the next `frames` frames (e.g. a card being captured).</summary>
    public void ForceRender(int frames) => _forceFrames = Math.Max(_forceFrames, frames);

    /// <summary>The rendered image of the view.</summary>
    public Image Capture() => Viewport3D.GetTexture().GetImage();

    /// <summary>
    /// Renders the view at `size` for `frames` frames and returns the picture, then gives the view back its own size: a
    /// small card or a hidden view is rendered as large as asked (its container would size it back while it stretches).
    /// </summary>
    public async System.Threading.Tasks.Task<Image> CaptureAt(Vector2I size, int frames)
    {
        bool stretch = _container.Stretch;
        var own = Viewport3D.Size;
        _container.Stretch = false;
        Viewport3D.Size = size;
        try
        {
            ForceRender(frames + 1);
            for (int i = 0; i < frames; i++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            return Capture();
        }
        finally
        {
            Viewport3D.Size = own;
            _container.Stretch = stretch;
        }
    }
}
