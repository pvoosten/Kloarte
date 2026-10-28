using System.IO;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>
/// Set pieces in the scene: runs the app's show scene on a copy of the Risquons-Tout show in tests/output/scenery
/// (never the user's files), adds one piece of each shape (block, pyramid, table, a split curtain, a gauze), checks
/// their nodes (meta, render layers, shadows, gauze material, mesh sizes), plays a cue with the app's player that
/// takes the block out and opens the curtain (eased over its time, <see cref="ShowBuilder.OccludersChanged"/> while it
/// moves and not when nothing moves), Back, and a rebuild of one piece that keeps the playhead's state. Screenshots
/// under some light and haze in tests/output/scenery/*.png. Nothing is saved. Prints "SCENERY OK" or "SCENERY FAILURES".
/// Run: <c>Godot_console.exe --path test/Kloarte.Godot.Tests --resolution 1600x900 res://tests/scenery_test.tscn</c>
/// </summary>
public partial class SceneryTest : Node
{
    /// <summary>The curtain's width (m) and the seconds it takes to open.</summary>
    private const float CurtainWidth = 8f, OpenTime = 2f;

    /// <summary>The output folder (absolute).</summary>
    private static string Out => ProjectSettings.GlobalizePath("res://tests/output/scenery");

    /// <summary>The show scene.</summary>
    private Node _scene;

    /// <summary>The show.</summary>
    private ShowBuilder _builder;

    /// <summary>The app's player (the panel's).</summary>
    private CuePlayer _player;

    /// <summary>Frames so far, failures, occluder events, the frame the GO was taken, frames since the cue settled.</summary>
    private int _frame, _failures, _occluderEvents, _goFrame = -1, _settledFrames;

    /// <summary>The step of cue "1" (lit stage) and of the cue after it (the change).</summary>
    private int _litStep, _changeStep;

    /// <summary>The curtain's openness seen while it moved (between closed and open), and whether it ever went back.</summary>
    private float _lastOpen;

    /// <summary>The curtain was seen between closed and open, and it went back while opening.</summary>
    private bool _sawMidway, _wentBack;

    /// <summary>The test's phase.</summary>
    private int _phase;

    /// <summary>Loads the show scene on a copy of the show.</summary>
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        ViewHost.Persist = false;
        GoboLibrary.Folder = "res://tests/output/gobos";
        FixturePanel.PresentFullScreen = false;
        if (Directory.Exists(Out))
            Directory.Delete(Out, true);
        _scene = TestShow.Load("scenery");
        _builder = _scene.GetNode<ShowBuilder>("Show");
        AddChild(_scene);
        _scene.GetNode<CanvasLayer>("FixturePanel").Visible = false;
    }

    /// <summary>The phases of the test.</summary>
    public override void _Process(double delta)
    {
        _frame++;
        if (_frame == 10)
            Setup();
        else if (_frame == 40)
        {
            Shot("scenery_0_closed");
            _occluderEvents = 0;
            _player.Go();
            _goFrame = _frame;
            _phase = 1;
            GD.Print($"GO {_player.Stack.Steps[_player.Step].CueNumberText}: running={_player.Running}");
        }
        else if (_phase == 1)
            WatchCurtain();
        else if (_phase == 2 && ++_settledFrames == 10)
            Finish();
    }

    /// <summary>Adds the pieces and their cue changes, checks the nodes, lights the stage.</summary>
    private void Setup()
    {
        _player = _scene.FindChild("CuePlayer", true, false) as CuePlayer;
        Check(_player != null, "the panel's cue player");
        _builder.OccludersChanged += () => _occluderEvents++;
        var stack = _builder.Document.CueStacks.First(s => s.Name == "show");
        var steps = stack.Steps.ToList();
        _litStep = steps.FindIndex(s => s.CueNumberText == "1");
        _changeStep = _litStep + 1;

        var blocking = _builder.Model.Blocking;
        var block = SceneryPiece.New("block", "Block", SceneryShape.Block);
        (block.Position, block.Color, block.Material) = (new[] { -3f, 0f, -2f }, "a07040", SceneryMaterial.Wood);
        var pyramid = SceneryPiece.New("pyramid", "Pyramid", SceneryShape.Pyramid);
        (pyramid.Position, pyramid.Width, pyramid.Depth, pyramid.Height, pyramid.Rotation, pyramid.Color) =
            (new[] { -1f, 0f, -2f }, 1.2f, 1.2f, 1.5f, 20f, "b03030");
        var table = SceneryPiece.New("table", "Table", SceneryShape.Table);
        (table.Position, table.Color, table.Material) = (new[] { 1.2f, 0f, -2f }, "c0c0c8", SceneryMaterial.Metal);
        var curtain = SceneryPiece.New("curtain", "Curtain", SceneryShape.Curtain);
        (curtain.Position, curtain.Width, curtain.Height, curtain.Fullness, curtain.Split, curtain.Color, curtain.Material) =
            (new[] { 0f, 0f, -4.5f }, CurtainWidth, 4.5f, 100f, true, "802030", SceneryMaterial.Painted);
        var gauze = SceneryPiece.New("gauze", "Gauze", SceneryShape.Curtain);
        (gauze.Position, gauze.Width, gauze.Height, gauze.Fullness, gauze.Color, gauze.Material) =
            (new[] { 3.6f, 0f, -1f }, 2.5f, 3f, 0f, "e0e0e0", SceneryMaterial.Gauze);
        blocking.Scenery.AddRange(new[] { block, pyramid, table, curtain, gauze });
        int changeCue = steps[_changeStep].CueId;
        blocking.SceneryChangeOf(changeCue, "block", true).Visible = false;
        var open = blocking.SceneryChangeOf(changeCue, "curtain", true);
        (open.Open, open.Time) = (100f, OpenTime);
        _builder.RebuildScenery();
        Check(_occluderEvents >= 1, "RebuildScenery raised OccludersChanged");

        CheckNodes(curtain);
        var cyc = _builder.GetNode("Scenery").GetNodeOrNull<MeshInstance3D>("Cyc");
        Check(cyc == null || (cyc.Layers & RenderLayers.Occluders) != 0, "the cyc is an occluder");

        _player.SetStack(stack);
        _player.Jump(_litStep);
        Check(_builder.SetPieces.Values.All(p => p.Visible), "every piece in at cue 1");
        Check(_builder.SetPieceOf("curtain").Open == 0f, "the curtain closed at cue 1");
        HazeControl.Set(GetTree(), 0.04f);
        _close = new SubViewport { Size = new Vector2I(1280, 720), World3D = _builder.GetWorld3D(), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        ShowBuilder.ConfigureShadowAtlas(_close);
        AddChild(_close);
        var cam = new Camera3D { Fov = 50f, Current = true };
        _close.AddChild(cam);
        cam.GlobalPosition = _builder.ToGlobal(new Vector3(1.5f, 2.0f, 4.5f));
        cam.LookAt(_builder.ToGlobal(new Vector3(0, 1.2f, -2.5f)));
    }

    /// <summary>A view of its own, closer to the pieces than the app's.</summary>
    private SubViewport _close;

    /// <summary>The nodes: one per piece with its id, layers, shadows, materials and sizes.</summary>
    private void CheckNodes(SceneryPiece curtain)
    {
        Check(_builder.SetPieces.Count == 5, $"5 set pieces ({_builder.SetPieces.Count})");
        var parent = _builder.GetNode("Scenery");
        foreach (var (id, node) in _builder.SetPieces)
        {
            Check(node.GetParent() == parent, $"{id} under Scenery");
            Check(node.GetMeta(SetPiece.PieceMeta).AsString() == id, $"{id} has its id as meta");
            var meshes = node.GetChildren().OfType<MeshInstance3D>().ToList();
            Check(meshes.Count > 0, $"{id} has meshes");
            bool gauze = id == "gauze";
            foreach (var m in meshes)
            {
                uint want = gauze ? RenderLayers.Default : RenderLayers.Default | RenderLayers.Occluders;
                Check(m.Layers == want, $"{id}/{m.Name} layers {m.Layers} (want {want})");
                Check(m.CastShadow == (gauze ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.DoubleSided),
                    $"{id}/{m.Name} shadows {m.CastShadow}");
            }
        }
        var g = (StandardMaterial3D)_builder.SetPieceOf("gauze").GetChild<MeshInstance3D>(0).MaterialOverride;
        Check(g.Transparency == BaseMaterial3D.TransparencyEnum.Alpha && Mathf.IsEqualApprox(g.AlbedoColor.A, SceneryMaterials.GauzeAlpha),
            "gauze is translucent");
        Check(g.CullMode == BaseMaterial3D.CullModeEnum.Disabled && g.BacklightEnabled, "gauze is double sided with backlight");
        var metal = (StandardMaterial3D)_builder.SetPieceOf("table").GetChild<MeshInstance3D>(0).MaterialOverride;
        Check(metal.Metallic == 1f, "metal table is metallic");
        Check(_builder.SetPieceOf("table").GetChildCount() == 5, "table = top + 4 legs");

        var pyramid = _builder.SetPieceOf("pyramid").GetChild<MeshInstance3D>(0).GetAabb();
        Check(pyramid.Size.IsEqualApprox(new Vector3(1.2f, 1.5f, 1.2f)) && Mathf.IsZeroApprox(pyramid.Position.Y), $"pyramid size {pyramid.Size}");
        Check(FacesOutward(_builder.SetPieceOf("pyramid").GetChild<MeshInstance3D>(0).Mesh, new Vector3(0, 1.5f / 4f, 0), false),
            "pyramid faces wound clockwise from outside, normals outward");
        Check(FacesOutward(_builder.SetPieceOf("curtain").GetChild<MeshInstance3D>(0).Mesh, Vector3.Zero, true),
            "curtain front (+Z) wound clockwise, normals to the front");
        var block = _builder.SetPieceOf("block");
        Check(block.Position.IsEqualApprox(new Vector3(-3f, 0f, -2f)), "block at its position");
        var cloth = _builder.SetPieceOf("curtain").GetChild<MeshInstance3D>(0).GetAabb();
        GD.Print($"curtain closed: aabb {cloth}, model pleat depth {curtain.PleatDepth:0.000}");
        Check(Mathf.Abs(cloth.Size.X - CurtainWidth) < 0.01f && Mathf.Abs(cloth.Size.Y - 4.5f) < 0.01f, "closed curtain is 8 x 4.5 m");
        Check(Mathf.Abs(cloth.Size.Z - curtain.PleatDepth) < 0.25f * curtain.PleatDepth, "closed curtain's pleats as deep as the model's");
        var flat = _builder.SetPieceOf("gauze").GetChild<MeshInstance3D>(0).GetAabb();
        Check(flat.Size.Z < 0.001f, "a curtain without fullness is flat");
    }

    /// <summary>
    /// True when every triangle of the mesh is wound clockwise seen from the side its vertex normals point to (Godot's
    /// front faces), and the normals point away from `inside` (or, `towardsFront`, to +Z).
    /// </summary>
    private static bool FacesOutward(Mesh mesh, Vector3 inside, bool towardsFront)
    {
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            var arrays = mesh.SurfaceGetArrays(surface);
            var vertices = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
            var normals = (Vector3[])arrays[(int)Mesh.ArrayType.Normal];
            var indices = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil
                ? Enumerable.Range(0, vertices.Length).ToArray()
                : (int[])arrays[(int)Mesh.ArrayType.Index];
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 p = vertices[indices[i]], q = vertices[indices[i + 1]], r = vertices[indices[i + 2]];
                var n = normals[indices[i]];
                var cross = (q - p).Cross(r - p);
                if (cross.LengthSquared() < 1e-12f)
                    continue;
                if (cross.Dot(n) >= 0f)
                    return false;   // counter-clockwise seen from the normal's side
                var outward = towardsFront ? Vector3.Back : (p + q + r) / 3f - inside;
                if (n.Dot(outward) <= 0f)
                    return false;
            }
        }
        return true;
    }

    /// <summary>While the cue runs: the curtain eases open (never back), events come; then the settled checks.</summary>
    private void WatchCurtain()
    {
        var curtain = _builder.SetPieceOf("curtain");
        if (curtain.Open > 5f && curtain.Open < 95f)
        {
            if (!_sawMidway)
                Shot("scenery_1_opening");
            _sawMidway = true;
        }
        if (curtain.Open < _lastOpen - 1e-3f)
            _wentBack = true;
        _lastOpen = curtain.Open;
        if (_frame == _goFrame + 1)
            Check(!_builder.SetPieceOf("block").Visible, "the block goes out at the GO");
        if (_player.Running)
            return;
        GD.Print($"cue settled after {_frame - _goFrame} frames: curtain {curtain.Open:0.0} %, {_occluderEvents} occluder events");
        Check(curtain.Open == 100f, $"curtain fully open ({curtain.Open})");
        Check(_sawMidway && !_wentBack, "curtain eased open");
        Check(_occluderEvents >= 3, $"OccludersChanged while the curtain moved ({_occluderEvents})");
        var mesh = (ArrayMesh)curtain.GetChild<MeshInstance3D>(0).Mesh;
        float visible = CurtainWidth / 2f * 0.15f;
        bool gap = Enumerable.Range(0, mesh.GetSurfaceCount())
            .SelectMany(i => (Vector3[])mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex])
            .All(v => Mathf.Abs(v.X) >= CurtainWidth / 2f - visible - 1e-3f);
        Check(gap && mesh.GetSurfaceCount() == 2, "open curtain: two halves gathered at the sides");
        var aabb = mesh.GetAabb();
        GD.Print($"curtain open: aabb {aabb}");
        Check(aabb.Size.Z > _builder.Model.Blocking.FindPiece("curtain").PleatDepth, "gathered pleats are deeper");
        Shot("scenery_2_open");
        _occluderEvents = 0;
        _phase = 2;
    }

    /// <summary>Nothing moves = no events; Back; a rebuilt piece takes the playhead's state.</summary>
    private void Finish()
    {
        Check(_occluderEvents == 0, $"no OccludersChanged while nothing moves ({_occluderEvents})");
        _player.Back();
        Check(_builder.SetPieceOf("block").Visible && _builder.SetPieceOf("curtain").Open == 0f, "Back: block in, curtain closed");
        Check(_occluderEvents > 0, "Back raised OccludersChanged");
        _player.Jump(_changeStep);
        _builder.Model.Blocking.FindPiece("curtain").Color = "203080";
        _builder.RebuildPiece("curtain");
        var rebuilt = _builder.SetPieceOf("curtain");
        Check(rebuilt.Open == 100f, $"rebuilt curtain keeps the cue's openness ({rebuilt.Open})");
        Check(!_builder.SetPieceOf("block").Visible, "block still out after the rebuild of another piece");
        _builder.Model.Blocking.RemovePiece("table");
        _builder.RebuildPiece("table");
        Check(_builder.SetPieceOf("table") == null && _builder.GetNode("Scenery").GetChildren().OfType<SetPiece>().Count() == 4,
            "a removed piece's node goes");
        GD.Print(_failures == 0 ? "SCENERY OK" : $"SCENERY FAILURES: {_failures}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>Counts and prints a failed check.</summary>
    private void Check(bool ok, string what)
    {
        if (ok)
            return;
        _failures++;
        GD.PrintErr($"FAIL: {what}");
    }

    /// <summary>Saves the window as tests/output/scenery/&lt;name&gt;.png and the close view as &lt;name&gt;_close.png.</summary>
    private void Shot(string name)
    {
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(Out, name + ".png"));
        _close.GetTexture().GetImage().SavePng(Path.Combine(Out, name + "_close.png"));
    }
}
