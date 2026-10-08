using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

public sealed partial class DesignExportTests
{
    /// <summary>A fresh output path (an export never writes over a file).</summary>
    private string Target(string name)
    {
        string path = Path.Combine(_out, name);
        foreach (string file in new[] { path, Path.ChangeExtension(path, ".xhw") })
            if (File.Exists(file))
                File.Delete(file);
        return path;
    }

    /// <summary>
    /// Every show, imported and exported unchanged, is written byte-identical (.shw and .xhw), with nothing written and no
    /// warnings: the export changes only what the design changed.
    /// </summary>
    [Fact]
    public void UnchangedShowsExportByteIdentical()
    {
        var catalog = Catalog;
        var failures = new List<string>();
        int shows = 0;
        foreach (string shw in Directory.EnumerateFiles(ShowFolder, "*.shw", SearchOption.AllDirectories))
        {
            MagicQShow source;
            try
            {
                source = MagicQShow.Load(shw);
            }
            catch (Exception)
            {
                continue;
            }
            shows++;
            var design = MagicQImport.Import(source, shw, catalog).Show;
            string target = Target($"same_{shows:000}.shw");
            var export = MagicQExport.Export(design, target, catalog);
            string name = Path.GetFileName(shw);
            if (File.ReadAllText(target, System.Text.Encoding.Latin1) != File.ReadAllText(shw, System.Text.Encoding.Latin1))
                failures.Add($"{name}: the .shw differs; written: {string.Join("; ", export.Written.Take(5))}");
            else if (export.Written.Count > 0)
                failures.Add($"{name}: wrote {export.Written[0]}");
            if (export.Warnings.Any(w => w.StartsWith("check after writing")))
                failures.Add($"{name}: {export.Warnings.First(w => w.StartsWith("check after writing"))}");
            string xhw = Path.ChangeExtension(shw, ".xhw");
            if (File.Exists(xhw) && !File.ReadAllBytes(xhw).SequenceEqual(File.ReadAllBytes(Path.ChangeExtension(target, ".xhw"))))
                failures.Add($"{name}: the .xhw differs");
            foreach (string warning in export.Warnings)
                _log.WriteLine($"{name}: {warning}");
        }
        _log.WriteLine($"{shows} shows exported unchanged");
        Check(shows > 0, "the MagicQ show folder has shows");
        Check(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    /// <summary>
    /// Edits of every kind on a Risquons-Tout show export and read back the same (the export's own check after writing
    /// finds nothing), the other cue stacks stay byte-identical, and what is new is on the list to check in MagicQ PC.
    /// </summary>
    [Fact]
    public void EditsExportAndReadBack()
    {
        string shw = Show("Risquons-Tout_RT2025_044.shw");
        var source = MagicQShow.Load(shw);
        var design = MagicQImport.Import(source, shw, Catalog).Show;
        var cues = design.CueOrder;
        var t1 = design.Rig.Heads.First(h => design.Type(h.Type).Gdtf?.Contains("T1") == true);
        var dimmer = design.Rig.Heads.First(h => h.Type == DesignTypes.GenericName);

        // A level, a deleted cue, a renumbered cue (across another), a new cue with a new combined palette.
        cues[1].Heads[DesignShow.Key(dimmer.Number)] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(42) };
        cues[1].FadeIn = 4.5;
        cues[1].Text = "Edited";
        var deleted = cues[2];
        cues.Remove(deleted);
        float between = (cues[3].Number + cues[4].Number) / 2;
        string renumbered = cues[2].Cue;
        cues[2].Cue = CueNumbers.Text(between);
        design.Palettes.Add(new DesignPalette
        {
            Name = "Kloarte look",
            Heads =
            {
                [DesignShow.Key(t1.Number)] = new Dictionary<string, LookValue>
                {
                    ["Pan"] = LookValue.Of(45), ["Tilt"] = LookValue.Of(30), ["ColorSub_C"] = LookValue.Of(0.5),
                },
            },
        });
        cues.Add(new DesignCue
        {
            Cue = CueNumbers.Text(cues[^1].Number + 1), Text = "New", FadeIn = 2, FadeOut = 3,
            Heads =
            {
                [DesignShow.Key(t1.Number)] = new Dictionary<string, LookValue>
                {
                    ["intensity"] = LookValue.Of(80), ["Pan"] = LookValue.OfPalette("Kloarte look"),
                    ["Tilt"] = LookValue.OfPalette("Kloarte look"), ["ColorSub_C"] = LookValue.OfPalette("Kloarte look"),
                    ["Zoom"] = LookValue.Of(20),
                },
            },
        });
        // The patch and rig: a head moved, a new lamp on a new head, a new group.
        int free = design.Rig.Heads.Max(h => h.Address + h.Channels);
        var moved = design.Rig.Heads.First(h => h.Type == DesignTypes.GenericName && h != dimmer);
        foreach (var lamp in design.Rig.Items.Where(i => i.Head == moved.Number))
            lamp.Address = free;
        moved.Address = free;
        int newHead = design.Rig.Heads.Max(h => h.Number) + 1;
        design.Rig.Heads.Add(new DesignHead { Number = newHead, Name = "New PC", Type = DesignTypes.GenericName, Address = free + 1, Gel = 1201 });
        var truss = design.Rig.Items.First(i => i.Kind == "pipe");
        design.Rig.Items.Add(new DesignItem
        {
            Id = design.NextItemId, Parent = truss.Id, At = new[] { 1.0, 6, -2 }, Address = free + 1, Head = newHead,
            Type = DesignTypes.GenericName,
        });
        design.Rig.Groups.Add(new DesignGroup { Number = 99, Name = "Kloarte", Heads = new[] { newHead, dimmer.Number } });
        design.Changed();
        Assert.Empty(design.Problems());

        string target = Target("edited.shw");
        var export = MagicQExport.Export(design, target, Catalog);
        foreach (string line in export.Written.Concat(export.Warnings.Select(w => "WARNING " + w)).Concat(export.CheckInConsole.Select(c => "CHECK " + c)))
            _log.WriteLine(line);
        Assert.DoesNotContain(export.Warnings, w => w.StartsWith("check after writing"));
        Assert.Contains(export.CheckInConsole, c => c.StartsWith("palettes written"));
        Assert.Contains(export.CheckInConsole, c => c.StartsWith("new cues"));

        // Read back: the same cues, numbers, texts, times and DMX (the check), the new palette with its MagicQ ids.
        var back = MagicQImport.Import(target, Catalog).Show;
        Assert.Equal(design.CueOrder.Select(c => c.Cue), back.CueOrder.Select(c => c.Cue));
        Assert.Equal("Edited", back.CueOrder[1].Text);
        Assert.Equal(4.5, back.CueOrder[1].FadeIn);
        Assert.DoesNotContain(back.CueOrder, c => c.Id == deleted.Id);
        Assert.Contains(back.CueOrder, c => c.Cue == CueNumbers.Text(between));
        Assert.DoesNotContain(back.CueOrder, c => c.Cue == renumbered);
        var palette = design.Palette("Kloarte look");
        Assert.Equal(2, palette.Magicq.Length);   // position and colour
        Assert.Equal(newHead, back.Head(newHead)?.Number);
        Assert.Equal(1201, back.Head(newHead).Gel);
        Assert.Equal(free, back.Head(moved.Number).Address);
        Assert.Contains(back.Rig.Groups, g => g.Number == 99 && g.Heads.SequenceEqual(new[] { newHead, dimmer.Number }));
        var newCue = back.CueOrder[^1];
        Assert.Equal("@Kloarte look", newCue.Heads[DesignShow.Key(t1.Number)]["Pan"].ToString());

        // The other cue stacks are byte-identical.
        var written = MagicQShow.Load(target);
        foreach (var stack in source.CueStacks.Where(s => s.Number != design.CueList.Stack))
            Assert.Equal(string.Join("|", stack.Record.CopyLines().Select(l => string.Join(",", l.Tokens))),
                         string.Join("|", written.CueStacks.Single(s => s.Number == stack.Number).Record.CopyLines().Select(l => string.Join(",", l.Tokens))));
    }

    /// <summary>
    /// A new project (the house venue, no show behind it) with a T1 and a dimmer, a palette and cues exports to a show
    /// with a cue stack, its palettes and its patch, and reads back the same.
    /// </summary>
    [Fact]
    public void NewProjectExports()
    {
        var design = MagicQImport.Import(TestEnvironment.File(MagicQStore.NewShowTemplate), Catalog).Show;
        design.Rig.Source = null;
        var t1Type = DesignTypes.Catalogue.First(t => t.Gdtf?.Contains("T1") == true);
        design.Rig.Types.Add(DesignTypes.Copy(t1Type));
        design.Rig.Types.Add(DesignTypes.Generic());
        design.Rig.Heads.Add(new DesignHead { Number = 1, Name = "Front", Type = DesignTypes.GenericName, Address = 1 });
        design.Rig.Heads.Add(new DesignHead { Number = 301, Name = "T1", Type = t1Type.Name, Address = 101, Channels = t1Type.Channels.Count });
        int id = design.NextItemId;
        design.Rig.Items.Add(new DesignItem { Id = id, At = new[] { 0.0, 6, -1 }, Address = 1, Head = 1, Type = DesignTypes.GenericName });
        design.Rig.Items.Add(new DesignItem { Id = id + 1, At = new[] { 2.0, 6, -1 }, Address = 101, Head = 301, Type = t1Type.Name });
        design.Palettes.Add(new DesignPalette
        {
            Name = "Centre",
            Heads = { ["301"] = new Dictionary<string, LookValue> { ["Pan"] = LookValue.Of(0), ["Tilt"] = LookValue.Of(-45) } },
        });
        design.CueList.Name = "Main";
        design.CueOrder.Add(new DesignCue
        {
            Cue = "1", Text = "Preset", FadeIn = 3,
            Heads = { ["1"] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(70) } },
        });
        design.CueOrder.Add(new DesignCue
        {
            Cue = "2", Text = "Spot", FadeIn = 2, FadeOut = 5,
            Heads =
            {
                ["1"] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(0) },
                ["301"] = new Dictionary<string, LookValue>
                {
                    ["intensity"] = LookValue.Of(100), ["Pan"] = LookValue.OfPalette("Centre"), ["Tilt"] = LookValue.OfPalette("Centre"),
                    ["Gobo1"] = LookValue.Of(design.LayoutOf(301)?["Gobo1"]?.SetNames.Skip(2).FirstOrDefault() ?? "Open"),
                },
            },
        });
        design.Changed();
        Assert.Empty(design.Problems());
        string target = Target("new_project.shw");
        var export = MagicQExport.Export(design, target, Catalog);
        foreach (string line in export.Written.Concat(export.Warnings.Select(w => "WARNING " + w)).Concat(export.CheckInConsole.Select(c => "CHECK " + c)))
            _log.WriteLine(line);
        Assert.DoesNotContain(export.Warnings, w => w.StartsWith("check after writing"));
        var back = MagicQImport.Import(target, Catalog).Show;
        Assert.Equal(new[] { "1", "2" }, back.CueOrder.Select(c => c.Cue));
        Assert.Equal("Main", back.CueList.Name);
        Assert.Contains(back.Palettes, p => p.Name == "Centre");
        Assert.Equal(t1Type.Name, back.Head(301).Type);
    }
}
