using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// Edits of the native show: the patch rules of its editor (<see cref="DesignShowEditor"/>), values from DMX, tracking,
/// and the list of differences of a re-import (<see cref="DesignDiff"/>).
/// </summary>
public sealed class DesignEditTests : TestBase
{
    /// <summary>Creates the test class (output folder design_edit).</summary>
    public DesignEditTests() : base("design_edit") { }

    /// <summary>A show with two dimmers on heads 1 and 2 and a cue that sets head 1.</summary>
    private static DesignShow TwoDimmers()
    {
        var show = new DesignShow();
        show.Rig.Types.Add(DesignTypes.Generic());
        show.Rig.Heads.Add(new DesignHead { Number = 1, Name = "One", Type = DesignTypes.GenericName, Address = 1 });
        show.Rig.Heads.Add(new DesignHead { Number = 2, Name = "Two", Type = DesignTypes.GenericName, Address = 2 });
        show.Rig.Items.Add(new DesignItem { Id = 10, Address = 1, Head = 1, Type = DesignTypes.GenericName });
        show.Rig.Items.Add(new DesignItem { Id = 11, Address = 2, Head = 2, Type = DesignTypes.GenericName });
        show.Rig.Groups.Add(new DesignGroup { Number = 1, Name = "All", Heads = new[] { 1, 2 } });
        show.CueOrder.Add(new DesignCue { Cue = "1", Id = 1, Heads = { ["1"] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(50) } } });
        show.Changed();
        return show;
    }

    /// <summary>The plan's patch rules: move and renumber the only lamp's head (values follow), join a head, refuse a conflict.</summary>
    [Fact]
    public void PatchRules()
    {
        var show = TwoDimmers();
        var editor = new DesignShowEditor(show);
        var one = show.Rig.Items[0];
        Assert.StartsWith("Head 1 moved to DMX 1-010 and renumbered to 5", editor.ApplyLampPatch(one, 5, 10));
        Assert.Equal(10, show.Head(5).Address);
        Assert.Equal(new[] { 5, 2 }, show.Rig.Groups[0].Heads);
        Assert.True(show.CueOrder[0].Heads.ContainsKey("5"));
        Assert.Equal(128, CueTracking.LevelsAfter(show, ((IShowDocument)show).CueStacks[0], 0)[10]);
        Assert.Throws<ShowEditException>(() => editor.ApplyLampPatch(one, 7, 2));   // head 2's address, another number
        Assert.StartsWith("Joined head 2", editor.ApplyLampPatch(one, 2, 2));
        Assert.True(editor.RemoveUnusedHead(5) == false);   // a cue still sets it
        show.CueOrder[0].Heads.Remove("5");
        show.Changed();
        Assert.False(editor.RemoveUnusedHead(5));   // a group still has it
        show.Rig.Groups[0].Heads = new[] { 2 };
        Assert.True(editor.RemoveUnusedHead(5));
        Assert.Equal(2, editor.LampCount(2));
    }

    /// <summary>A lamp of a catalogue type gets the type and a head of its own; gel, name and beam per head.</summary>
    [Fact]
    public void NewLampsAndHeadSettings()
    {
        var show = TwoDimmers();
        var editor = new DesignShowEditor(show);
        var t1 = DesignTypes.Catalogue.First(t => t.Gdtf?.Contains("T1") == true);
        var lamp = (DesignItem)editor.ImportLamp(t1.Name, "Spot", "T1");
        Assert.Equal(3, lamp.Head);
        Assert.Equal(3, lamp.Address);
        Assert.Equal(53, show.Head(3).Channels);
        Assert.NotNull(show.LayoutOf(3)["Pan"]);
        Assert.Equal(1, editor.SetGel(3, 1201));
        Assert.Equal(0, editor.SetGel(3, 1201));
        Assert.Equal(1, editor.RenameHead(3, "Spot \"links\""));
        Assert.Equal("Spot links", show.Head(3).Name);   // no double quotes (MagicQ's names)
        Assert.True(editor.SetBeamAngle(4, 20));
        Assert.Equal(20, show.Head(3).Beam);
        Assert.Throws<ShowEditException>(() => editor.ImportLamp("No such type", "x", "thing"));
    }

    /// <summary>Levels by DMX into a cue (tracking-aware), a blackout cue, and the tracked value of a channel.</summary>
    [Fact]
    public void LevelsAndTracking()
    {
        var show = TwoDimmers();
        var editor = new DesignShowEditor(show);
        var stack = ((IShowDocument)show).CueStacks[0];
        var step = editor.InsertCue(stack, 2, "BO", blackout: true);
        Assert.Equal("2", step.CueNumberText);
        Assert.Equal(0, CueTracking.LevelsAfter(show, stack, 1)[1]);
        Assert.Equal(200, editor.NudgeLevel(stack, 1, 2, 200));
        Assert.Equal("intensity", show.CueOrder[1].Of(2).Keys.Single());
        string status = editor.RecordIntoCue(stack, 1, new[] { (1, 128), (2, 10) });
        Assert.Contains("1 as in the cue before", status);
        Assert.False(show.CueOrder[1].Of(1)?.ContainsKey("intensity") ?? false);   // tracks from cue 1 again
        var (value, at) = DesignEdits.Tracked(show, 1, 1, "intensity");
        Assert.Equal(0, at);
        Assert.Equal("50", value.ToString());
        Assert.Throws<System.ArgumentException>(() => editor.InsertCue(stack, 2, "again", false));
    }

    /// <summary>The differences of two shows, one line each; taking a re-import keeps Kloarte's delays.</summary>
    [Fact]
    public void DifferencesOfAReimport()
    {
        var before = TwoDimmers();
        before.CueOrder[0].Delay = 2;
        var after = before.Copy();
        after.CueOrder[0].Text = "Opening";
        after.CueOrder[0].Heads["2"] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(30) };
        after.CueOrder.Add(new DesignCue { Cue = "3", Id = 3, Text = "New" });
        after.Head(2).Gel = 1201;
        after.Palettes.Add(new DesignPalette { Name = "Warm" });
        after.Changed();
        var lines = DesignDiff.Describe(before, after);
        Assert.Contains("cue 3 New added", lines);
        Assert.Contains(lines, l => l.StartsWith("cue 1: text \"Opening\"") && l.Contains("values of head 2"));
        Assert.Contains("head 2: gel L201", lines);
        Assert.Contains("palette Warm added", lines);
        Assert.Empty(DesignDiff.Describe(before, before.Copy()));

        after.CueOrder[0].Delay = null;
        DesignDiff.TakeInto(before, after);
        Assert.Equal("Opening", before.CueOrder[0].Text);
        Assert.Equal(2, before.CueOrder[0].Delay);
    }

    /// <summary>The native show's project files: written, read back, a broken one not applied, a cues file of before left alone.</summary>
    [Fact]
    public void ProjectFiles()
    {
        var show = TwoDimmers();
        var back = new DesignShow();
        var problems = new List<FileProblem>();
        foreach (string file in show.ProjectFiles)
            Assert.True(back.Apply(file, file, show.TextOf(file), problems));
        Assert.Empty(problems);
        Assert.Equal(show.ToText(), back.ToText());
        Assert.StartsWith("# yaml-language-server: $schema=.schemas/rig.schema.json", show.TextOf(DesignShow.RigFile));

        Assert.False(back.Apply(DesignShow.CuesFile, "cues.yaml", "cues:\n  - cue: [broken\n", problems));
        Assert.Single(back.CueOrder);
        problems.Clear();
        Assert.True(back.Apply(DesignShow.CuesFile, "cues.yaml", "stacks:\n  - stack: 1\n", problems));
        Assert.Single(back.CueOrder);
        Assert.Contains(problems, p => p.Message.Contains("MagicQ show of before"));
    }
}
