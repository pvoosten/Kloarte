using System;
using System.Threading.Tasks;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Takes the pictures of an export (stills, contact sheet; UI review 32): an off-screen SubViewport on the scene's world
/// at the chosen size, with its own camera at the chosen viewpoint and the export's caption, so the views and their
/// sizes don't matter. Dispose removes it.
/// </summary>
internal sealed class ExportShooter : IDisposable
{
    /// <summary>Frames rendered before a picture is taken (masks and shadows settle).</summary>
    private const int SettleFrames = 4;

    /// <summary>The node that owns the viewport (for its signals and tree).</summary>
    private readonly Node _owner;

    /// <summary>The off-screen viewport and its caption.</summary>
    private readonly SubViewport _viewport;
    private readonly ExportCaption _caption;

    /// <summary>Makes the viewport: `camera` in the show's coordinates (position, target, field of view).</summary>
    public ExportShooter(FixturePanel owner, Vector2I size, (Vector3 Position, Vector3 Target, float Fov) camera,
                         PlaybackTimeline timeline, bool captions)
    {
        _owner = owner;
        var root = owner.GetTree().Root;
        _viewport = new SubViewport
        {
            Name = "ExportViewport",
            Size = size,
            World3D = root.World3D,
            Msaa3D = root.Msaa3D,
            ScreenSpaceAA = root.ScreenSpaceAA,
            UseTaa = root.UseTaa,
            UseHdr2D = root.UseHdr2D,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        ShowBuilder.ConfigureShadowAtlas(_viewport);   // its own atlas: a place for every lamp (finding 7)
        var cam = new Camera3D { Fov = camera.Fov, Current = true };
        _viewport.AddChild(cam);
        _caption = new ExportCaption { Timeline = timeline, ShowCue = captions, Size = size };
        _viewport.AddChild(_caption);
        owner.AddChild(_viewport);
        var builder = owner.ExportBuilder;
        cam.GlobalPosition = builder?.ToGlobal(camera.Position) ?? camera.Position;
        cam.LookAt(builder?.ToGlobal(camera.Target) ?? camera.Target);
    }

    /// <summary>An export for one person (their name on their shirt and in the caption), or for everybody (null).</summary>
    public void ForActor(ShowBuilder builder, ActorDef actor)
    {
        if (builder == null)
            return;
        if (actor == null)
            ExportCaption.ShowAllActors(builder);
        else
            ExportCaption.ShowOnlyActor(builder, actor.Id);
        _caption.ActorName = actor?.Name;
    }

    /// <summary>The picture of the stage as it is now (the playhead at `t`, for the caption).</summary>
    public async Task<Image> Shoot(double t)
    {
        _caption.ShowAt(t);
        for (int f = 0; f < SettleFrames; f++)
            await _owner.ToSignal(_owner.GetTree(), SceneTree.SignalName.ProcessFrame);
        await _owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return _viewport.GetTexture().GetImage();
    }

    /// <summary>The caption's text now (tests).</summary>
    public string Caption => _caption.Text;

    /// <inheritdoc />
    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_viewport))
            _viewport.QueueFree();
    }
}
