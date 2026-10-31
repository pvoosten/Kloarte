using System.Collections.Generic;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

public sealed partial class DesignExportTests
{
    /// <summary>
    /// Kruimels_012: heads aimed in Kloarte pointed the other way in MagicQ. The LEDBeam and the T1 tilt the other way
    /// round in MagicQ than their GDTF says, so a positive tilt is the upper half of the channel on both. It stays one
    /// direction: the show reads back as the same angle.
    /// </summary>
    [Fact]
    public void MovingHeadTiltExportsTheWayMagicQTilts()
    {
        const double tilt = 52.69;
        const int ledBeamAddress = 101, t1Address = 201;
        var design = MagicQImport.Import(TestEnvironment.File(MagicQStore.NewShowTemplate), Catalog).Show;
        design.Rig.Source = null;
        var ledBeam = DesignTypes.Catalogue.First(t => t.Gdtf?.Contains("LEDBeam") == true);
        var t1 = DesignTypes.Catalogue.First(t => t.Gdtf?.Contains("T1") == true);
        design.Rig.Types.Add(DesignTypes.Copy(ledBeam));
        design.Rig.Types.Add(DesignTypes.Copy(t1));
        design.Rig.Heads.Add(new DesignHead { Number = 7, Name = "LEDBeam", Type = ledBeam.Name, Address = ledBeamAddress, Channels = ledBeam.Channels.Count });
        design.Rig.Heads.Add(new DesignHead { Number = 19, Name = "T1", Type = t1.Name, Address = t1Address, Channels = t1.Channels.Count });
        design.CueOrder.Add(new DesignCue
        {
            Cue = "1",
            Heads =
            {
                ["7"] = new Dictionary<string, LookValue> { ["Tilt"] = LookValue.Of(tilt) },
                ["19"] = new Dictionary<string, LookValue> { ["Tilt"] = LookValue.Of(tilt) },
            },
        });
        design.Changed();
        Assert.Empty(design.Problems());

        string target = Target("tilt.shw");
        MagicQExport.Export(design, target, Catalog);

        var cue = MagicQShow.Load(target).Cues.Last();
        var dmx = cue.Channels.ToDictionary(c => c.Address, c => c.Value & 0xff);
        const int tiltCoarse = 2;   // pan, pan fine, tilt: the same on both heads
        Check(dmx[ledBeamAddress + tiltCoarse] > 128, $"LEDBeam tilt +{tilt}: DMX {dmx[ledBeamAddress + tiltCoarse]} (upper half)");
        Check(dmx[t1Address + tiltCoarse] > 128, $"T1 tilt +{tilt}: DMX {dmx[t1Address + tiltCoarse]} (upper half)");

        var back = MagicQImport.Import(target, Catalog).Show;
        var look = back.CueOrder[^1].Heads;
        Assert.Equal(tilt, look["7"]["Tilt"].AsNumber.Value, 1);
        Assert.Equal(tilt, look["19"]["Tilt"].AsNumber.Value, 1);
    }

    /// <summary>
    /// A palette value that changes also changes MagicQ's cached copy in the cues that refer to the palette (Kruimels:
    /// "Daglicht 19 20" exported with its new tilt, the cues still with the old low byte).
    /// </summary>
    [Fact]
    public void ChangedPaletteRefreshesTheCuesCachedByte()
    {
        const int address = 201, tiltCoarse = 2;
        var design = MagicQImport.Import(TestEnvironment.File(MagicQStore.NewShowTemplate), Catalog).Show;
        design.Rig.Source = null;
        var t1 = DesignTypes.Catalogue.First(t => t.Gdtf?.Contains("T1") == true);
        design.Rig.Types.Add(DesignTypes.Copy(t1));
        design.Rig.Heads.Add(new DesignHead { Number = 19, Name = "T1", Type = t1.Name, Address = address, Channels = t1.Channels.Count });
        design.Palettes.Add(new DesignPalette
        {
            Name = "Daglicht",
            Heads = { ["19"] = new Dictionary<string, LookValue> { ["Tilt"] = LookValue.Of(40) } },
        });
        design.CueOrder.Add(new DesignCue
        {
            Cue = "1",
            Heads = { ["19"] = new Dictionary<string, LookValue> { ["Tilt"] = LookValue.OfPalette("Daglicht") } },
        });
        design.Changed();
        string first = Target("palette_first.shw");
        MagicQExport.Export(design, first, Catalog);

        var again = MagicQImport.Import(first, Catalog).Show;
        again.Palette("Daglicht").Heads["19"]["Tilt"] = LookValue.Of(-40);
        again.Changed();
        string second = Target("palette_second.shw");
        MagicQExport.Export(again, second, Catalog);

        var show = MagicQShow.Load(second);
        int raw = show.Cues.Last().Channels.First(c => c.Address == address + tiltCoarse).Value;
        Assert.NotEqual(0, raw >> 8);
        Assert.True(show.FindPalette(raw >> 8).TryGetValue(address + tiltCoarse, out int value, out _));
        Assert.Equal(value, raw & 0xff);
    }
}
