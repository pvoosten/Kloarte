using System.Collections.Generic;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: tests from ModelTests.MagicQPc.cs (converted from the Godot model tests).</summary>
public partial class MagicQTests
{
    /// <summary>MagicQ PC: status.dat, user dimmer curves, cue matching.</summary>
    [Fact]
    public void MagicQPcTest()
    {
        // The T record of a show has the same layout as MagicQ's status.dat.
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var doc = show.Shw;
        var t = doc.Records.First(r => r.Type == "T");
        int count = t.GetHex(8);
        Check(count == 0x12 && t[9] == "01100000" && t[9 + count] == "8ec80902", "T: 18 universe words, then the IP 2.9.200.142");
        var set = MagicQPc.PrepareStatus(doc, @"C:\x\show.shw", new[] { 2, 1, 1, 99 });
        Check(set.SequenceEqual(new[] { 1, 2 }), "universes in range only, once");
        Check(t.GetString(1) == "C:/x/show.shw", "show path with forward slashes");
        Check(t[9] == "81100000" && t[10] == "81100011" && t[11] == "01100022", $"universe 1, 2 enabled Art-Net out ({t[9]}, {t[10]})");
        Check(t[9 + count] == "0100007f" && t[11 + count] == "000000ff", "IP 127.0.0.1 / 255.0.0.0");
        Check(MagicQPc.IpHex("2.9.200.142") == "8ec80902", "IP notation");

        // status.dat ends its V record with `"MagicQ 1";`.
        var status = ShwDocument.Parse("\\ MagicQ status file\r\n\r\nV,0091,\"MagicQ 1\";\r\n\r\n" +
            "T,\"C:/a.sbk\",0000,0000,0000,0000,\"D:\",\r\n0046,0002,01100000,01100011,\r\n8ec80902,0000,000000ff,\"\",00000000,\r\n;\r\n");
        Check(status.Records.Select(r => r.Type).SequenceEqual(new[] { "V", "T" }), "status.dat: V and T records");
        Check(MagicQPc.PrepareStatus(status, "C:/b.shw", new[] { 1 }).Count == 1
              && status.Records.Last()[9] == "81100000" && status.Records.Last()[11] == "0100007f", "status.dat prepared");

        // lastshow.shw: MagicQ's copy of the loaded show; its header names the show (name, .xhw).
        string original = Show("RT2025_031.shw");
        string savedAs = MagicQShow.SavedAs(original);
        string last = System.IO.Path.Combine(_out, "lastshow.shw");
        System.IO.File.Copy(original, last, true);
        Check(savedAs != null && savedAs.EndsWith("RT2025_031.shw"), $"header: saved as {savedAs}");
        Check(MagicQPc.SourceOf(last) == MagicQPc.Existing(savedAs)
              && System.IO.Path.GetFileNameWithoutExtension(MagicQPc.SourceOf(last)) == System.IO.Path.GetFileNameWithoutExtension(savedAs)
              && MagicQPc.SourceOf(original) == original,
            $"lastshow.shw stands for the show in its header (newest of .shw / .sbk: {System.IO.Path.GetFileName(MagicQPc.SourceOf(last))}), other files for themselves");
        Check(MagicQShow.Load(last).Xhw != null, "lastshow.shw gets the .xhw of that show");

        // Dimmer curves: RT2025_031 channel 159 ("Dimmer", curve 4 = User 1, a square-law-like spline); MagicQ PC
        // output 196 for 222 in cue 30.3.
        var curved = show.WithCurves(new Dictionary<int, int> { [159] = 222, [150] = 222 });
        Check(curved[159] == 196 && curved[150] == 222, $"user curve on 159: 222 -> {curved[159]} (MagicQ: 196), 150 linear");
        var linear = new Dictionary<int, int> { [150] = 10 };
        Check(ReferenceEquals(show.WithCurves(linear), linear), "no curve: the same values");

        // Comparison: the look after a cue matches that cue.
        var fresh = MagicQShow.Load(Show("RT2025_031.shw"));
        var stack = fresh.CueStacks.First(s => s.Steps.Count >= 3);
        var cues = fresh.Cues.ToDictionary(c => c.Id);
        var expected = new List<Dictionary<int, int>> { new() };
        var state = new Dictionary<int, int>();
        foreach (var step in stack.Steps)
        {
            if (cues.TryGetValue(step.CueId, out var cue))
                foreach (var (address, value) in cue.Channels)
                    state[address] = fresh.Resolve(address, value);
            expected.Add(new Dictionary<int, int>(state));
        }
        var intensities = MagicQCheck.IntensityAddresses(fresh);
        Check(intensities.Contains(150) && intensities.Count > 50, $"{intensities.Count} intensity addresses (dimmer 150 among them)");
        var received = new Dictionary<int, int>(expected[3]);
        var (found, compared, diffs) = MagicQCheck.BestMatch(received, expected, intensities);
        Check(found == 2 && diffs.Count == 0 && compared > 0, $"look after step 2 found as step {found} ({diffs.Count} diffs)");
        int a = intensities.First(x => received.GetValueOrDefault(x) == 0);
        received[a] = 200;
        (found, _, diffs) = MagicQCheck.BestMatch(received, expected, intensities);
        Check(diffs.Any(d => d.Address == a && d.Expected == 0 && d.Received == 200), $"a stray channel is reported: {MagicQCheck.FormatDiffs(diffs)}");
    }
}
