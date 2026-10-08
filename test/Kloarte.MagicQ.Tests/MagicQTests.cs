using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// Tests of the MagicQ binding (headless, no Godot; converted from res://tests/model_tests.tscn).
/// Reads the MagicQ show folder, writes only to test/output/magicq.
/// </summary>
public partial class MagicQTests : TestBase
{
    /// <summary>Output to test/output/magicq.</summary>
    public MagicQTests() : base("magicq") { }

    /// <summary>Palette references in every cue of the show folder resolve to the cached values.</summary>
    [Fact]
    public void PaletteReferences()
    {
        int ok = 0, missingChannel = 0, wrong = 0;
        foreach (string shw in Directory.EnumerateFiles(ShowFolder, "*.shw", SearchOption.AllDirectories))
        {
            var show = MagicQShow.Load(shw);
            foreach (var cue in show.Cues)
                foreach (var (address, raw) in cue.Channels)
                {
                    int id = raw >> 8;
                    if (id == 0)
                        continue;
                    var p = show.FindPalette(id);
                    if (p != null && p.TryGetValue(address, out int v, out int link))
                    {
                        if (link == 0 && v != (raw & 0xff))
                            wrong++;
                        else
                            ok++;
                    }
                    else
                        missingChannel++;
                }
        }
        System.Console.WriteLine($"palette references: {ok} ok, {missingChannel} channel not in palette, {wrong} wrong");
        Check(ok > 60000 && wrong == 0, "cue palette references resolve to the palette value");
    }

    /// <summary>Recording a palette and using it in a cue.</summary>
    [Fact]
    public void PaletteWrite()
    {
        string src = Show("Risquons-Tout_RT2025_044.shw");
        string dst = Path.Combine(_out, "palette.shw");
        var show = MagicQShow.Load(src);
        // T1 head 709 (address 0x201): pan, tilt coarse, a colour (CMY) and a beam channel -> a combined position palette.
        var values = new List<(int, int)> { (0x201, 0x40), (0x203, 0xa0), (0x20c, 0x80), (0x22f, 0xff) };
        int before = show.Palettes.Count;
        var palette = show.AddPalette(MagicQShow.PaletteType.Position, "kruimels look", values);
        var cue = show.Cues.First(c => c.Name == "autostrade");
        foreach (var (address, value) in values)
            cue.SetChannel(address, palette.Id << 8 | value);
        show.Save(dst);

        var re = MagicQShow.Load(dst);
        var p2 = re.Palettes.FirstOrDefault(p => p.Name == "kruimels look");
        Check(re.Palettes.Count == before + 1 && p2 != null && p2.Type == MagicQShow.PaletteType.Position
              && p2.Values.Select(v => (v.Address, v.Value)).SequenceEqual(values), $"new palette {p2?.Id:x4} read back");
        Check(p2?.Contents(re) == "PCB", $"combined palette holds {p2?.Contents(re)} (P, C, B)");
        var c2 = re.Cues.First(c => c.Name == "autostrade");
        Check(values.All(v => re.Resolve(v.Item1, c2.Channels.First(ch => ch.Address == v.Item1).Value) == v.Item2),
              "cue refers to the palette");
        var oldRecords = MagicQShow.Load(src).Shw.Records.Select(Text).ToHashSet();
        var changed = re.Shw.Records.Where(r => !oldRecords.Contains(Text(r))).Select(r => r.Type + r[1]).ToList();
        Check(changed.Count == 2 && changed.Contains("F" + p2?.Record[1]) && changed.Contains("S" + c2.Record[1]),
              $"only the new palette and the cue changed ({string.Join(", ", changed)})");

        // Re-recording the palette updates the cached value in the cue.
        re.RecordPalette(p2, new[] { (0x201, 0x55) });
        Check(c2.Channels.First(ch => ch.Address == 0x201).Value == (p2.Id << 8 | 0x55), "re-record refreshes the cue's cached value");
    }

    private static string Text(ShwRecord r) => string.Join("|", r.Lines.Select(l => string.Join(",", l.Tokens)));
}
