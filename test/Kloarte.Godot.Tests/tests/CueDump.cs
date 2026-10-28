using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Diagnostic: prints per step of a cue stack the cue's flags and timing fields and the non-zero intensity channels
/// after it (tracking, palettes resolved), as the app's model sees them.
/// Without --show it dumps a copy of the Risquons-Tout show (<see cref="TestShow"/>), without --stack the stack "show".
/// Run:  Godot_console.exe --headless --path . res://tests/cue_dump.tscn [-- --show=&lt;path&gt; --stack=&lt;name&gt;]
/// </summary>
public partial class CueDump : Node
{
    /// <inheritdoc />
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs().Where(a => a.Contains('=')).ToDictionary(a => a[2..a.IndexOf('=')], a => a[(a.IndexOf('=') + 1)..]);
        var show = MagicQShow.Load(args.TryGetValue("show", out string file) ? file : TestShow.Copy("cue_dump"));
        var stack = show.CueStacks.First(s => s.Name == args.GetValueOrDefault("stack", "show"));
        var cues = show.Cues.ToDictionary(c => c.Id);
        var intensities = MagicQCheck.IntensityAddresses(show);
        var state = new Dictionary<int, int>();
        foreach (var step in stack.Steps)
        {
            string fields = "";
            if (cues.TryGetValue(step.CueId, out var cue))
            {
                foreach (var (address, value) in cue.Channels)
                    state[address] = show.Resolve(address, value);
                fields = string.Join(",", Enumerable.Range(3, 13).Select(i => cue.Record[i]));
            }
            var on = state.Where(kv => intensities.Contains(kv.Key) && kv.Value > 0).OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}={kv.Value}");
            GD.Print($"{step.CueNumberText,-8} {step.Text,-16} cue {step.CueId} [{fields}] on: {string.Join(" ", on)}");
        }
        GetTree().Quit();
    }
}
