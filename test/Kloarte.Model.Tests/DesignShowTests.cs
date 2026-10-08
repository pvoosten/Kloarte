using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.Model;
using Xunit;
using Xunit.Abstractions;

namespace Kloarte.Tests;

/// <summary>The native show (<see cref="DesignShow"/>): values, channel layouts, files and the projection to DMX.</summary>
public sealed class DesignShowTests : TestBase
{
    /// <summary>Test output.</summary>
    private readonly ITestOutputHelper _log;

    /// <summary>Creates the test class (output folder design).</summary>
    public DesignShowTests(ITestOutputHelper log) : base("design") => _log = log;

    /// <summary>Values read as written, and are written back the same.</summary>
    [Fact]
    public void LookValuesReadAndWrite()
    {
        foreach (string text in new[] { "12.5", "-40", "Stars", "Stars (dmx 37)", "dmx 37", "@Front wash", "Shutter1Strobe 5" })
            Assert.Equal(text, LookValue.Parse(text).ToString());
        Assert.Equal("Front", LookValue.Parse(" @ Front ").Palette);
        Assert.Equal(37, LookValue.Parse("DMX 37").Dmx);
        Assert.Equal(12.5, LookValue.Parse("12.5").AsNumber);
        Assert.Throws<System.FormatException>(() => LookValue.Parse(" "));
        Assert.Throws<System.FormatException>(() => LookValue.Parse("@"));
    }

    /// <summary>
    /// Every DMX value of every channel of the moving heads' modes reads as a value that gives the same DMX value
    /// (16-bit channels sampled), and most of them without an exact DMX value.
    /// </summary>
    [Fact]
    public void EveryChannelValueRoundTrips()
    {
        int checkedValues = 0, exact = 0;
        foreach (string prefix in new[] { "Robe_Lighting@Robin_T1_Profile", "Robe_Lighting@Robin_LEDBeam_150" })
        {
            var fixture = GdtfLibrary.Get(prefix);
            Assert.NotNull(fixture);
            foreach (var mode in fixture.Modes)
            {
                var type = DesignTypes.FromGdtf("test", prefix, fixture, mode);
                var layout = new HeadLayout(type, mode.ChannelCount, fixture, mode);
                Assert.NotNull(layout.Intensity);
                foreach (var channel in layout.Channels)
                {
                    var values = channel.Max == 255 ? Enumerable.Range(0, 256)
                        : Enumerable.Range(0, 1772).Select(i => i * 37).Append(65535);
                    foreach (int dmx in values)
                    {
                        var value = channel.FromDmx(dmx);
                        Assert.True(channel.ToDmx(LookValue.Parse(value.ToString())) == dmx,
                            $"{prefix} {mode.Name} {channel.Key}: {dmx} -> '{value}' -> {channel.ToDmx(value)}");
                        checkedValues++;
                        if (value.Dmx != null)
                            exact++;
                    }
                }
            }
        }
        _log.WriteLine($"{checkedValues} values round trip, {exact} need an exact DMX value");
        Assert.True(exact < checkedValues / 10, $"{exact} of {checkedValues} values need an exact DMX value");
    }

    /// <summary>A small show: a conventional lamp, a T1 and a palette.</summary>
    private static DesignShow SmallShow()
    {
        var show = new DesignShow();
        var t1Fixture = GdtfLibrary.Get("Robe_Lighting@Robin_T1_Profile");
        var cmy = t1Fixture.Modes.First(m => m.Name == "Mode 1 - CMY");
        show.Rig.Types.Add(DesignTypes.Generic());
        show.Rig.Types.Add(DesignTypes.FromGdtf("T1 CMY", "Robe_Lighting@Robin_T1_Profile", t1Fixture, cmy));
        show.Rig.Heads.Add(new DesignHead { Number = 1, Name = "Front L", Type = "generic_dimmer", Address = 1, Gel = 1201 });
        show.Rig.Heads.Add(new DesignHead { Number = 201, Name = "T1", Type = "T1 CMY", Address = 101, Channels = cmy.ChannelCount });
        show.Rig.Items.Add(new DesignItem { Id = 1, Kind = "pipe", At = new[] { -5.0, 6, -1 }, Size = new[] { 10.0, 0, 0 }, Name = "LX1" });
        show.Rig.Items.Add(new DesignItem { Id = 2, Parent = 1, At = new[] { -2.0, 6, -1 }, Address = 1, Head = 1, Type = "generic_dimmer" });
        show.Rig.Items.Add(new DesignItem { Id = 3, Parent = 1, At = new[] { 2.0, 6, -1 }, Address = 101, Head = 201, Type = "T1 CMY" });
        show.Palettes.Add(new DesignPalette
        {
            Name = "Front",
            Heads = { ["201"] = new Dictionary<string, LookValue> { ["Pan"] = LookValue.Of(90), ["Tilt"] = LookValue.Of(-30) } },
        });
        show.CueOrder.Add(new DesignCue
        {
            Cue = "1", Id = 1, Text = "Open", FadeIn = 3,
            Heads = { ["1"] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(50) } },
        });
        show.CueOrder.Add(new DesignCue
        {
            Cue = "2", Id = 2, Text = "T1",
            Heads =
            {
                ["201"] = new Dictionary<string, LookValue>
                {
                    ["intensity"] = LookValue.Of(100), ["Pan"] = LookValue.OfPalette("Front"), ["Tilt"] = LookValue.OfPalette("Front"),
                },
            },
        });
        show.Changed();
        return show;
    }

    /// <summary>The show written as one file reads back the same, with the same fingerprint.</summary>
    [Fact]
    public void ShowFileRoundTrips()
    {
        var show = SmallShow();
        string path = Path.Combine(_out, "small.kshow.yaml");
        show.Save(path);
        var back = DesignShow.Load(path);
        Assert.Equal(show.ToText(), back.ToText());
        Assert.Equal(show.ContentHash(), back.ContentHash());
        string text = File.ReadAllText(path);
        _log.WriteLine(text);
        Assert.Contains("\"201\": {intensity: 100, Pan: '@Front', Tilt: '@Front'}", text);
        Assert.Contains("{id: 3, kind: fixture, parent: 1, at: [2, 6, -1], address: 101, head: 201, type: T1 CMY}", text);
        back.CueOrder[0].Text = "Changed";
        Assert.NotEqual(show.ContentHash(), back.ContentHash());
    }

    /// <summary>The cues become DMX: tracking through the cue list, palette values resolved, 16-bit channels in two bytes.</summary>
    [Fact]
    public void CuesProjectToDmx()
    {
        var show = SmallShow();
        IShowDocument document = show;
        var stack = document.CueStacks.Single();
        Assert.Equal(new[] { "1", "2" }, stack.Steps.Select(s => s.CueNumberText));
        var first = CueTracking.LevelsAfter(show, stack, 0);
        Assert.Equal(128, first[1]);
        var second = CueTracking.LevelsAfter(show, stack, 1);
        Assert.Equal(128, second[1]);   // tracked
        var layout = show.LayoutOf(201);
        var pan = layout["Pan"];
        Assert.Equal(pan.ToDmx(LookValue.Of(90)), pan.Read(second, 101));
        Assert.Equal(65535, layout.Intensity.Read(second, 101));
        Assert.Contains(show.ChannelsOf(show.CueOrder[1]), c => c.Palette == "Front" && c.Address == 101 + pan.Offsets[0] - 1);
        Assert.Empty(show.Problems());
        Assert.Equal(new[] { 1 }.Concat(layout.Intensity.Offsets.Select(o => 100 + o)), document.IntensityAddresses);
        Assert.Equal(1201, document.LampPatchOf(show.Rig.Items[1]).GelId);
        Assert.Equal(101 + layout.Intensity.Offsets[0] - 1, document.IntensityAddressOf(101));
        Assert.Equal(1.5f, show.CueOrder.Count == 2 ? 1.5f : 0);
    }

    /// <summary>What a cue or palette sets that means nothing is listed (and left out of the DMX).</summary>
    [Fact]
    public void ProblemsAreListed()
    {
        var show = SmallShow();
        show.CueOrder[0].Heads["1"]["Pan"] = LookValue.Of(3);
        show.CueOrder[0].Heads["999"] = new Dictionary<string, LookValue> { ["intensity"] = LookValue.Of(1) };
        show.CueOrder[1].Heads["201"]["Gobo1"] = LookValue.Of("No such gobo");
        show.CueOrder[1].Heads["201"]["Zoom"] = LookValue.OfPalette("Missing");
        show.Changed();
        var problems = show.Problems();
        _log.WriteLine(string.Join("\n", problems));
        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Contains("no channel 'Pan'"));
        Assert.Contains(problems, p => p.Contains("head 999"));
        Assert.Contains(problems, p => p.Contains("No such gobo"));
        Assert.Contains(problems, p => p.Contains("no palette 'Missing'"));
    }
}
