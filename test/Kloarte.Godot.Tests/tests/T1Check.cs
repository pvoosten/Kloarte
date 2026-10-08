using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Model;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Diagnostic: opens shows with Robe T1s and prints what the app builds for them; screenshot per show.
/// Run:  Godot_console.exe --path . res://tests/t1_check.tscn
/// </summary>
public partial class T1Check : Node
{
    private static readonly string[] Shows =
    {
        "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show/Risquons-Tout_RT2025_044.shw",
        "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show/la comedie.shw",
    };
    private Node _scene;
    private int _frame, _show;

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _frame++;
        var builder = _scene.GetNode<ShowBuilder>("Show");
        if (_frame == 5)
            builder.OpenShow(Shows[_show]);
        if (_frame != 120)
            return;

        foreach (var f in builder.GetNode("Fixtures").GetChildren().OfType<ProfileFixture>())
        {
            var vis = builder.VisOf(f);
            string pers = vis == null ? null : builder.Document.LampPatchOf(vis).Personality;
            if (pers == null || !pers.Contains("T1"))
                continue;
            GD.Print($"{System.IO.Path.GetFileName(Shows[_show])}: {f.Name} pers='{pers}' pos={f.Position} " +
                     $"focus={f.FocusPoint} I={f.Intensity} field={f.FieldAngle} energy={f.Energy} " +
                     $"kind={f.GetMeta("kind")} gel='{f.GetMeta("gel")}' visible={f.Visible} color={f.LampColor * f.GelColor}");
        }
        var cam = _scene.GetNode<FlyCamera>("Camera");
        cam.GoTo(0);
        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://tests/output/t1_{_show}.png"));
        _frame = 0;
        if (++_show >= Shows.Length)
            GetTree().Quit();
    }
}
