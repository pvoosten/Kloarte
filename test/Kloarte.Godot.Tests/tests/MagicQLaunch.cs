using System.Linq;
using Godot;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Diagnostic: starts MagicQ PC with a show (as "Open in MagicQ PC" does: kills a running MagicQ, writes status.dat,
/// Art-Net out on the show's patched universes to 127.0.0.1) and quits.
/// Run:  Godot_console.exe --headless --path . res://tests/magicq_launch.tscn -- --show=&lt;path of a .shw&gt;
/// </summary>
public partial class MagicQLaunch : Node
{
    /// <inheritdoc />
    public override void _Ready()
    {
        string show = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--show="))?["--show=".Length..];
        if (string.IsNullOrEmpty(show) || !System.IO.File.Exists(show))
        {
            GD.PrintErr($"magicq launch: no show file '{show}' (-- --show=<path>)");
            GetTree().Quit(1);
            return;
        }
        var universes = MagicQShow.Load(show).Patch.Select(p => (p.Address - 1) / 512 + 1).Distinct().ToList();
        GD.Print("magicq launch: " + MagicQPc.Launch(System.IO.Path.GetFullPath(show), universes.Count > 0 ? universes : new() { 1 }));
        GetTree().Quit(0);
    }
}
