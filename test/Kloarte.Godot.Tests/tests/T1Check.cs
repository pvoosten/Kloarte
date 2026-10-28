using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Diagnostic: opens shows with Robe T1s (copies in tests/output/t1_&lt;n&gt;/show, through the panel's Import MagicQ
/// show dialog) and prints what the app builds for them; screenshot per show. "T1 CHECK OK" when every show has T1s.
/// Run:  Godot_console.exe --path . res://tests/t1_check.tscn
/// </summary>
public partial class T1Check : Node
{
    /// <summary>The shows with T1s (read only: only copied).</summary>
    private static readonly string[] Shows =
    {
        TestShow.MagicQShows + "/Risquons-Tout_RT2025_044.shw",
        TestShow.MagicQShows + "/la comedie.shw",
    };

    /// <summary>The app's show scene.</summary>
    private Node _scene;

    /// <summary>Frames since the current show was opened.</summary>
    private int _frame;

    /// <summary>The index of the current show in <see cref="Shows"/>.</summary>
    private int _show;

    /// <summary>Shows in which no T1 was found.</summary>
    private int _withoutT1;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        ViewHost.Persist = false;
        GoboLibrary.Folder = "res://tests/output/gobos";
        _scene = GD.Load<PackedScene>(TestShow.ShowScene).Instantiate();   // no show: it opens copies of its own
        AddChild(_scene);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frame++;
        var builder = _scene.GetNode<ShowBuilder>("Show");
        if (_frame == 5)
        {
            // As the user opens a show (the panel follows); a copy in its own folder, never the user's file.
            string copy = TestShow.Copy($"t1_{_show}", Shows[_show]);
            _scene.FindChildren("*", nameof(FileDialog), true, false).OfType<FileDialog>().First(d => d.Title == "Import MagicQ show")
                .EmitSignal(FileDialog.SignalName.FileSelected, copy);
        }
        if (_frame != 120)
            return;

        int t1s = 0;
        foreach (var f in builder.Fixtures)   // moving heads hang in a MovingHead node, not directly under Fixtures
        {
            var vis = builder.VisOf(f);
            string pers = vis == null ? null : builder.Document.LampPatchOf(vis).Personality;
            if (pers == null || !pers.Contains("T1"))
                continue;
            t1s++;
            GD.Print($"{System.IO.Path.GetFileName(Shows[_show])}: {f.Name} pers='{pers}' pos={f.Position} " +
                     $"focus={f.FocusPoint} I={f.Intensity} field={f.FieldAngle} energy={f.Energy} " +
                     $"kind={f.GetMeta("kind")} gel='{f.GetMeta("gel")}' visible={f.Visible} color={f.LampColor * f.GelColor}");
        }
        var cam = _scene.GetNode<FlyCamera>("Camera");
        cam.GoTo(0);
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://tests/output/t1_{_show}.png"));
        GD.Print($"{System.IO.Path.GetFileName(Shows[_show])}: {t1s} T1 lamps");
        if (t1s == 0)
            _withoutT1++;
        _frame = 0;
        if (++_show >= Shows.Length)
        {
            GD.Print(_withoutT1 == 0 ? "T1 CHECK OK" : $"T1 CHECK FAIL: {_withoutT1} shows without T1s");
            GetTree().Quit(_withoutT1 == 0 ? 0 : 1);
        }
    }
}
