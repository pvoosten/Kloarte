using System.Linq;
using Godot;

// Diagnostic: opens shows with Robe T1s and prints what the app builds for them; screenshot per show.
// Run:  Godot_console.exe --path . res://tests/t1_check.tscn
public partial class T1Check : Node
{
    private static readonly string[] Shows =
    {
        "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show/Risquons-Tout_RT2025_044.shw",
        "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show/la comedie.shw",
    };
    private Node _scene;
    private int _frame, _show;

    public override void _Ready()
    {
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
    }

    public override void _Process(double delta)
    {
        _frame++;
        var builder = _scene.GetNode<ShowBuilder>("Show");
        if (_frame == 5)
            builder.OpenShow(Shows[_show]);
        if (_frame != 120)
            return;

        var model = builder.Model.MagicQ;
        foreach (var f in builder.GetNode("Fixtures").GetChildren().OfType<ProfileFixture>())
        {
            var ch = model.Patch.FirstOrDefault(p => p.Address == f.DmxAddress);
            var pers = model.Personalities.FirstOrDefault(p => p.Id == ch?.Personality);
            if (pers == null || !pers.Name.Contains("T1"))
                continue;
            GD.Print($"{System.IO.Path.GetFileName(Shows[_show])}: {f.Name} pers='{pers.Name}' pos={f.Position} " +
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
