using System.IO;
using System.Linq;
using Godot;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Writes a test show for checking our writer in MagicQ PC itself: RT2025_031 with three marked edits, and console
/// settings with Art-Net output on universes 1 and 2 (load with SHIFT + LOAD SHOW+CON).
/// Output: &lt;MagicQ show folder&gt;/kruimels-test/kruimels_edit.shw (+ .xhw). Originals are not touched.
/// Run:  Godot_console.exe --headless --path . res://tests/make_magicq_test_show.tscn
/// </summary>
public partial class MakeMagicQTestShow : Node
{
    /// <summary>MagicQ show folder.</summary>
    [Export] public string ShowFolder = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show";

    /// <inheritdoc />
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
        // 4. Console settings (T record, loaded with SHIFT + LOAD SHOW+CON): universes 1 and 2 output Art-Net,
        //    enabled, on Art-Net universes 0 and 1. f8 = universe count, then one word per universe:
        //    top byte = 0x80 enabled | out type (0 None, 1 Art-Net), next byte = in type << 4 (1 Art-Net), low byte =
        //    out / in universe nibbles. Default 0110_00nn = disabled, Art-Net out and in (seen in View DMX I/O).
        var settings = show.Shw.Records.First(r => r.Type == "T");
        int universes = settings.GetHex(8);
        for (int u = 0; u < 2 && u < universes; u++)
        {
            GD.Print($"universe {u + 1} was {settings[9 + u]}");
            settings[9 + u] = $"8110{u * 0x11:x4}";
        }

        show.Save(Path.Combine(dir, "kruimels_edit.shw"));
        GD.Print($"written {dir}/kruimels_edit.shw: head 150 x={vis.X} rotX={vis.RotX}");
        GetTree().Quit();
    }
}
