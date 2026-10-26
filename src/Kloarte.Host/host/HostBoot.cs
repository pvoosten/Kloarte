using System.Linq;
using Godot;

namespace Kloarte.Host;

/// <summary>
/// The exported build's main scene (<c>boot.tscn</c>, project setting run/main_scene.dist). An export template refuses a
/// scene on the command line (Godot 4.7: <c>disable_path_overrides</c>), so the movie process the app starts is told by
/// <c>--render-movie</c> after <c>--</c>: it renders the movie (<see cref="RenderScene"/>, added at once so the first
/// movie frame is the render's); else the app's show scene (<see cref="ShowScene"/>) becomes the current scene.
/// </summary>
public partial class HostBoot : Node
{
    /// <summary>The user argument that makes this process the movie render.</summary>
    public const string RenderMovieArg = "--render-movie";

    /// <summary>The app's show scene.</summary>
    [Export(PropertyHint.File, "*.tscn")]
    public string ShowScene { get; set; } = "res://shows/risquons_tout.tscn";

    /// <summary>The movie render scene.</summary>
    [Export(PropertyHint.File, "*.tscn")]
    public string RenderScene { get; set; } = "res://render/show_render.tscn";

    /// <inheritdoc />
    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains(RenderMovieArg))
            AddChild(GD.Load<PackedScene>(RenderScene).Instantiate());
        else
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, ShowScene);
    }
}
