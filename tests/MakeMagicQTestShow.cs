using System.IO;
using System.Linq;
using Godot;

// Writes a test show for checking our writer in MagicQ PC itself: RT2025_031 with three marked edits.
// Output: <MagicQ show folder>/kruimels-test/kruimels_edit.shw (+ .xhw). Originals are not touched.
// Run:  Godot_console.exe --headless --path . res://tests/make_magicq_test_show.tscn
public partial class MakeMagicQTestShow : Node
{
    [Export] public string ShowFolder = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show";

    public override void _Ready()
    {
        string src = Path.Combine(ShowFolder, "Risquons-Tout", "RT2025_031.shw");
        string dir = Path.Combine(ShowFolder, "kruimels-test");
        Directory.CreateDirectory(dir);
        var show = MagicQShow.Load(src);

        // 1. Rename head 150 ("Binnen T", address 150) in the patch.
        foreach (var ch in show.Patch.Where(p => p.Address == 150))
            ch.HeadName = "KRUIMELS TEST";
        // 2. Move its MagicVis fixture 2 m to the right and tilt it to 45 degrees.
        var vis = show.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture && v.Address == 150);
        GD.Print($"head 150 was at x={vis.X} rotX={vis.RotX}");
        vis.X += 2f;
        vis.RotX = 45f;
        // 3. Rename a group.
        var group = show.Groups.First(g => g.Name == "Water T");
        group.Name = "KRUIMELS GROEP";

        show.Save(Path.Combine(dir, "kruimels_edit.shw"));
        GD.Print($"written {dir}/kruimels_edit.shw: head 150 x={vis.X} rotX={vis.RotX}");
        GetTree().Quit();
    }
}
