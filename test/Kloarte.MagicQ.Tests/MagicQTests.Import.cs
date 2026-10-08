using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>MagicQTests: tests from ModelTests.Import.cs (converted from the Godot model tests).</summary>
public partial class MagicQTests
{
    /// <summary>Lamps added to a show without lamps, from the MagicQ templates.</summary>
    [Fact]
    public void ImportLampTest()
    {
        var templates = MagicQShow.Load(TestEnvironment.File(MagicQShowEditor.TemplatesPath));
        Check(templates.Personalities.Select(p => p.Name).SequenceEqual(new[]
              { "generic_dimmer", "Robe_Rob150LEDBeam_Mode 1", "Robe_RobinT1Profile_Mode3" }),
              "templates: dimmer, LEDBeam 150, T1 Profile in Mode 3 (user)");
        var catalog = FixtureCatalog.Load(TestEnvironment.File(FixtureCatalog.DefaultPath));
        Check(catalog.Find("robe-t1-profile")?.Personality == "Robe_RobinT1Profile_Mode3"
              && catalog.Models.Where(m => !string.IsNullOrEmpty(m.MagicQ)).All(m => templates.Personalities.Any(p => p.Name == m.Personality)),
              "catalogue: the T1 is added in Mode3; every personality it names is in the templates");

        // The venue-only show: MagicVis objects, no personality, patch or lamp.
        string file = Show("Theaterzaal_Brouckere.shw");
        var show = MagicQShow.Load(file);
        Check(show.Shw.Matches(file), "unchanged show matches its file (MagicQ gets the original)");
        int objects = show.Vis.Count;
        Check(show.Personalities.Count == 0 && show.Patch.Count == 0 && show.Vis.All(v => v.Kind == MagicQShow.VisKind.Object),
              $"empty show: {objects} objects, no lamps");
        var patch = new PatchEditor(show);
        MagicQShow.VisItem Add(string personality, string name)
        {
            var lamp = templates.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture
                                                && templates.Personalities.First(p => p.Id == v.Personality).Name == personality);
            int count = templates.Patch.Count(p => p.HeadNumber == lamp.HeadNumber && p.Personality == lamp.Personality);
            return show.ImportLamp(templates, lamp, patch.FreeHeadNumber(), patch.FreeAddress(count), name);
        }
        var dimmer = Add("generic_dimmer", "C103");
        var t1 = Add("Robe_RobinT1Profile_Mode3", "T1");
        var dimmer2 = Add("generic_dimmer", "C103");
        Rig.Place(dimmer2, 1.5f, 6f, 4f, 0, 10f, 20f);
        Check(!show.Shw.Matches(file), "with added lamps it no longer matches (MagicQ gets a copy)");

        Check(show.Personalities.Count == 2 && show.Personalities.Select(p => p.Id).SequenceEqual(new[] { 1, 2 }),
              "personalities copied once each, with free ids");
        Check(dimmer.Personality == 1 && t1.Personality == 2 && dimmer2.Personality == 1, "lamps refer to them");
        Check((dimmer.HeadNumber, dimmer.Address, t1.HeadNumber, t1.Address, dimmer2.HeadNumber, dimmer2.Address) == (1, 1, 2, 2, 3, 55),
              $"heads 1-3 at the next free addresses: {dimmer.Address}, {t1.Address}, {dimmer2.Address}");
        Check(patch.Channels(2).Count == 53 && patch.Channels(2).All(c => c.Personality == 2 && c.HeadName == "T1" && c.Gel == 0),
              "T1 patched: 53 channels (Mode 3), its personality, its name, open white");
        Check(show.Vis.Count == objects + 3 && new[] { dimmer, t1, dimmer2 }.Select(v => v.Id).Distinct().Count() == 3
              && show.Vis.Where(v => v.Kind == MagicQShow.VisKind.Object).All(o => o.Id < dimmer.Id),
              "new MagicVis ids after the objects");
        Check(dimmer.ParentId == 0 && Math.Abs(dimmer2.X - 1.5f) < 1e-4f && Math.Abs(dimmer2.RotY - 20f) < 1e-4f, "placed");

        // MagicQ's record order: personalities, then the patch, then MagicVis.
        var types = show.Shw.Records.Select(r => r.Type).Where(t => t is "P" or "L" or "H").ToList();
        int lastP = types.LastIndexOf("P"), firstL = types.IndexOf("L"), lastL = types.LastIndexOf("L"), firstH = types.IndexOf("H");
        Check(lastP < firstL && lastL < firstH, "records in MagicQ's order: P, L, H");

        // Saved and read back.
        var reread = MagicQShow.Load(SaveCopy(show, "imported.shw"));
        int t1Intensity = reread.Personalities.First(p => p.Id == 2).IntensityChannel;
        Check(reread.Personalities.Count == 2 && reread.Patch.Count == 55 && t1Intensity >= 0
              && reread.Vis.Count(v => v.Kind == MagicQShow.VisKind.Fixture) == 3
              && MagicQCheck.IntensityAddresses(reread).SetEquals(new[] { 1, 2 + t1Intensity, 55 }),
              $"read back: 2 personalities, 55 channels, 3 lamps, intensity of each head (T1 dimmer at offset {t1Intensity})");
    }

    /// <summary>Saves a show into the test output folder and returns the path.</summary>
    private string SaveCopy(MagicQShow show, string name)
    {
        string path = System.IO.Path.Combine(_out, name);
        show.Save(path);
        return path;
    }
}
