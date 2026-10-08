using System.Linq;
using Godot;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Diagnostic: listens for Art-Net for a few seconds and prints the packet count and, per MagicQ universe, the
/// non-zero channels. Used to check that MagicQ PC's Art-Net output reaches the app.
/// Run:  Godot_console.exe --headless --path . res://tests/artnet_probe.tscn [-- --seconds=10]
/// </summary>
public partial class ArtNetProbe : Node
{
    /// <summary>How long to listen, in seconds (command line: <c>--seconds=N</c> after <c>--</c>).</summary>
    [Export] public double Seconds = 3;

    /// <summary>The receiver under test.</summary>
    private readonly ArtNetReceiver _receiver = new();

    /// <summary>Time listened so far.</summary>
    private double _elapsed;

    /// <inheritdoc />
    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--seconds=") && double.TryParse(arg["--seconds=".Length..],
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out double s))
                Seconds = s;
        _receiver.Start();
        GD.Print($"listening on UDP {ArtNetReceiver.Port} for {Seconds} s");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < Seconds)
            return;
        var snapshot = _receiver.Snapshot();
        _receiver.Stop();
        GD.Print($"packets: {_receiver.Packets}");
        foreach (var universe in snapshot.Where(kv => kv.Value != 0).GroupBy(kv => (kv.Key - 1) / 512 + 1)
                     .OrderBy(g => g.Key))
        {
            var channels = universe.OrderBy(kv => kv.Key).Select(kv => $"{(kv.Key - 1) % 512 + 1}={kv.Value}");
            GD.Print($"universe {universe.Key}: {universe.Count()} non-zero: {string.Join(" ", channels)}");
        }
        foreach (int u in snapshot.Keys.Select(a => (a - 1) / 512 + 1).Distinct().Order())
            if (!snapshot.Any(kv => kv.Value != 0 && (kv.Key - 1) / 512 + 1 == u))
                GD.Print($"universe {u}: all zero");
        GD.Print(_receiver.Packets > 0 ? "ARTNET RECEIVED" : "NO ARTNET");
        GetTree().Quit();
    }
}
