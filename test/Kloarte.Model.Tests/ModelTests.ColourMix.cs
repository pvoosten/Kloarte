using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

public partial class ModelTests
{
    /// <summary>
    /// Kruimels cue "kom mo binn": a LEDBeam with only its virtual colour wheel set is whitish in MagicQ, whose home value
    /// of the Colour Mix control is 255 (colour mix has priority, RGBW full), and was blue in Kloarte. The wheel counts
    /// once the channel gives it priority.
    /// </summary>
    [Fact]
    public void LedBeamWheelCountsOnlyWhenColourMixControlAllowsIt()
    {
        const int start = 1, wheel = 55, virtualPriority = 0;   // 55 = Filter 132 (Medium Blue)
        var beam = GdtfFixture.Load(GdtfFixture.Find(TestEnvironment.File(GdtfFixture.DefaultFolder), "Robe_Lighting@Robin_LEDBeam_150"));
        var mode = beam.Modes.First(m => m.ChannelCount == 22);
        var dmx = new Dictionary<int, int>();
        mode.Channel("ColorMacro1").Write(dmx, start, wheel);

        var home = GdtfDecoder.Decode(beam, mode, dmx, start);
        Check(home.R > 0.9f && home.G > 0.9f && home.B > 0.9f, $"wheel set, control at MagicQ's home: ({home.R:0.00},{home.G:0.00},{home.B:0.00})");

        mode.Channel("ColorMixMode").Write(dmx, start, virtualPriority);
        var blue = GdtfDecoder.Decode(beam, mode, dmx, start);
        Check(blue.B > blue.R + 0.3f, $"wheel set, virtual has priority: ({blue.R:0.00},{blue.G:0.00},{blue.B:0.00})");
    }

    /// <summary>
    /// Kruimels "Daglicht 19 20": a T1 in RGBAL with only its virtual colour wheel set added the wheel to its full
    /// emitters in MagicQ (home value of the Colour Mix control: 45, addition), and was sky blue in Kloarte.
    /// </summary>
    [Fact]
    public void T1WheelAddsToItsEmittersAtMagicQsHome()
    {
        const int start = 1;
        var t1 = GdtfFixture.Load(GdtfFixture.Find(TestEnvironment.File(GdtfFixture.DefaultFolder), "Robe_Lighting@Robin_T1_Profile"));
        var mode = t1.Modes.First(m => m.Has("ColorAdd_RY"));
        var layout = new HeadLayout(null, mode.ChannelCount, t1, mode);
        var dmx = new Dictionary<int, int>();
        layout["ColorMacro1"].Write(dmx, start, layout["ColorMacro1"].ToDmx(LookValue.Of("Filter 68 (Sky Blue)")).Value);

        var home = GdtfDecoder.Decode(t1, mode, dmx, start);
        Check(home.R > 0.9f && home.G > 0.9f && home.B > 0.9f, $"wheel set, control at MagicQ's home: ({home.R:0.00},{home.G:0.00},{home.B:0.00})");

        foreach (string emitter in new[] { "ColorAdd_R", "ColorAdd_G", "ColorAdd_B", "ColorAdd_RY", "ColorAdd_GY" })
            mode.Channel(emitter).Write(dmx, start, 0);
        var wheel = GdtfDecoder.Decode(t1, mode, dmx, start);
        Check(wheel.B > wheel.R + 0.3f, $"wheel set, emitters off: ({wheel.R:0.00},{wheel.G:0.00},{wheel.B:0.00})");
    }
}
