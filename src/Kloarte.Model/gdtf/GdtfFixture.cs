using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Kloarte.Model;

/// <summary>
/// A GDTF fixture type (data/gdtf/*.gdtf, a zip with description.xml, 3D models and wheel images): its DMX modes with
/// channels and channel functions, wheels (colours, gobo images), and the beam (luminous flux, field angle) and geometry
/// (yoke / head offsets). Only what the visualiser needs is read. No Godot dependencies.
/// </summary>
public sealed class GdtfFixture
{
    /// <summary>Folder of the house GDTF files (an app path).</summary>
    public const string DefaultFolder = "res://data/gdtf";

    /// <summary>
    /// Fixture types (GDTF Name) that tilt the other way round in MagicQ than their GDTF says: read with the Tilt
    /// function's physical range swapped, so an angle means the same in Kloarte and on the console, in both directions.
    /// </summary>
    private static readonly HashSet<string> TiltReversed = new() { "Robin LEDBeam 150 FW RGBW", "Robin T1 Profile" };

    /// <summary>
    /// Channels (GDTF Name, attribute) whose value in MagicQ when no cue sets them isn't their GDTF default: the
    /// personality's home value is what the console outputs, so it is the default here.
    /// </summary>
    private static readonly Dictionary<(string Fixture, string Attribute), int> MagicQHome = new()
    {
        [("Robin LEDBeam 150 FW RGBW", "ColorMixMode")] = 255,   // colour mix has priority
        [("Robin T1 Profile", "ColorMixMode")] = 45,             // addition
    };

    /// <summary>Fixture type name.</summary>
    public string Name { get; private set; } = "";

    /// <summary>The DMX modes.</summary>
    public List<GdtfMode> Modes { get; } = new();

    /// <summary>Wheels by name: slots, 1-based like GDTF's WheelSlotIndex (index 0 unused).</summary>
    public Dictionary<string, List<GdtfSlot>> Wheels { get; } = new();

    /// <summary>Luminous flux of the beam (lm), 0 if unknown.</summary>
    public float LuminousFlux { get; private set; }

    /// <summary>Field angle of the beam (degrees).</summary>
    public float FieldAngle { get; private set; }

    /// <summary>Distance base -&gt; yoke (pan) axis, metres along the hanging direction.</summary>
    public float YokeOffset { get; private set; } = 0.1f;

    /// <summary>Distance yoke -&gt; head (tilt) axis, metres.</summary>
    public float HeadOffset { get; private set; } = 0.15f;

    /// <summary>Distance tilt axis -&gt; front of the lens (beam origin), metres.</summary>
    public float BeamOffset { get; private set; } = 0.15f;

    /// <summary>File contents of the zip (models/..., wheels/...).</summary>
    public Dictionary<string, byte[]> Files { get; } = new();

    /// <summary>Reads a .gdtf file.</summary>
    public static GdtfFixture Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var fixture = new GdtfFixture();
        foreach (var entry in zip.Entries)
        {
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            fixture.Files[entry.FullName.Replace('\\', '/')] = ms.ToArray();
        }
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(fixture.Files["description.xml"]));
        var type = xml.Root!.Element("FixtureType")!;
        fixture.Name = (string)type.Attribute("Name") ?? "";

        foreach (var wheel in type.Descendants("Wheel"))
        {
            var slots = new List<GdtfSlot> { null };
            foreach (var slot in wheel.Elements("Slot"))
            {
                var c = ((string)slot.Attribute("Color") ?? "0.3127,0.329,100").Split(',').Select(F).ToArray();
                slots.Add(new GdtfSlot((string)slot.Attribute("Name") ?? "", c[0], c[1], c.Length > 2 ? c[2] : 100f,
                    (string)slot.Attribute("MediaFileName") ?? ""));
            }
            fixture.Wheels[(string)wheel.Attribute("Name") ?? ""] = slots;
        }

        var beam = type.Descendants("Beam").FirstOrDefault();
        if (beam != null)
        {
            fixture.LuminousFlux = F((string)beam.Attribute("LuminousFlux") ?? "0");
            fixture.FieldAngle = F((string)beam.Attribute("FieldAngle") ?? "0");
            fixture.BeamOffset = Math.Abs(OffsetZ((string)beam.Attribute("Position")));
        }
        var axes = type.Descendants("Axis").ToList();
        if (axes.Count >= 2)
        {
            fixture.YokeOffset = Math.Abs(OffsetZ((string)axes[0].Attribute("Position")));
            fixture.HeadOffset = Math.Abs(OffsetZ((string)axes[1].Attribute("Position")));
        }

        foreach (var mode in type.Descendants("DMXMode"))
        {
            var m = new GdtfMode { Name = (string)mode.Attribute("Name") ?? "" };
            foreach (var ch in mode.Descendants("DMXChannel"))
            {
                var offsets = ((string)ch.Attribute("Offset") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => int.Parse(o, CultureInfo.InvariantCulture)).ToArray();
                if (offsets.Length == 0)
                    continue;   // virtual channel
                var logical = ch.Element("LogicalChannel");
                var functions = ch.Descendants("ChannelFunction").ToList();
                string main = (string)logical?.Attribute("Attribute") ?? "";
                var channel = new GdtfChannel
                {
                    Attribute = main,
                    Offsets = offsets,
                    Default = MagicQHome.TryGetValue((fixture.Name, main), out int home) ? home
                        : functions.Count > 0 ? Dmx((string)functions[0].Attribute("Default"), offsets.Length) : 0,
                };
                foreach (var f in functions)
                {
                    string attribute = (string)f.Attribute("Attribute") ?? "";
                    float physicalFrom = F((string)f.Attribute("PhysicalFrom") ?? "0");
                    float physicalTo = F((string)f.Attribute("PhysicalTo") ?? "1");
                    if (attribute == "Tilt" && TiltReversed.Contains(fixture.Name))
                        (physicalFrom, physicalTo) = (physicalTo, physicalFrom);
                    var fn = new GdtfFunction
                    {
                        Attribute = attribute,
                        Name = (string)f.Attribute("Name") ?? "",
                        DmxFrom = Dmx((string)f.Attribute("DMXFrom"), offsets.Length),
                        PhysicalFrom = physicalFrom,
                        PhysicalTo = physicalTo,
                        Wheel = (string)f.Attribute("Wheel") ?? "",
                    };
                    foreach (var set in f.Elements("ChannelSet"))
                        fn.Sets.Add((Dmx((string)set.Attribute("DMXFrom"), offsets.Length), (string)set.Attribute("Name") ?? "",
                                     int.TryParse((string)set.Attribute("WheelSlotIndex"), out int slot) && slot > 0 ? slot : 0));
                    channel.Functions.Add(fn);
                }
                // Functions that start at the same DMX value are alternatives (mode masters): keep the first.
                var distinct = channel.Functions.GroupBy(f => f.DmxFrom).Select(g => g.First()).OrderBy(f => f.DmxFrom).ToList();
                channel.Functions.Clear();
                channel.Functions.AddRange(distinct);
                for (int i = 0; i < channel.Functions.Count; i++)
                    channel.Functions[i].DmxTo = i + 1 < channel.Functions.Count ? channel.Functions[i + 1].DmxFrom - 1 : channel.Max;
                m.Channels.Add(channel);
            }
            fixture.Modes.Add(m);
        }
        return fixture;
    }

    /// <summary>
    /// The mode matching a MagicQ personality: same channel count; when several modes match (e.g. the T1's CMY and RGB
    /// Mode 1), the one whose colour channels match the personality's channel names, else the first.
    /// </summary>
    public GdtfMode ModeFor(int channelCount, IEnumerable<string> channelNames = null)
    {
        var candidates = Modes.Where(m => m.ChannelCount == channelCount).ToList();
        if (candidates.Count <= 1)
            return candidates.FirstOrDefault();
        var names = string.Join(" ", channelNames ?? Enumerable.Empty<string>()).ToLowerInvariant();
        bool cmy = names.Contains("cyan");   // MagicQ names the T1 colour channels "Cyan/Red": CMY is the default
        return candidates.FirstOrDefault(m => m.Has("ColorSub_C") == cmy) ?? candidates[0];
    }

    /// <summary>Image of a wheel slot (wheels/&lt;media&gt;.png), or null.</summary>
    public byte[] Media(string name) =>
        string.IsNullOrEmpty(name) ? null : Files.FirstOrDefault(f => f.Key.StartsWith("wheels/") && Path.GetFileNameWithoutExtension(f.Key) == name).Value;

    /// <summary>Finds the .gdtf in a folder whose file name starts with the prefix (e.g. "Robe_Lighting@Robin_T1_Profile").</summary>
    public static string Find(string folder, string prefix) =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, "*.gdtf").Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f).FirstOrDefault() : null;

    /// <summary>"32768/2" -&gt; value in a channel of `bytes` bytes.</summary>
    private static int Dmx(string text, int bytes)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        var parts = text.Split('/');
        long v = long.Parse(parts[0], CultureInfo.InvariantCulture);
        int n = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
        if (n < bytes)
            v <<= 8 * (bytes - n);
        else if (n > bytes)
            v >>= 8 * (n - bytes);
        return (int)v;
    }

    private static float F(string s) => float.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Z translation of a GDTF matrix "{..}{..}{..}{..}" (the 4th value of the 3rd row).</summary>
    private static float OffsetZ(string matrix)
    {
        if (string.IsNullOrEmpty(matrix))
            return 0;
        var rows = matrix.Split('}', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim('{', ' ').Split(',')).ToList();
        return rows.Count >= 3 && rows[2].Length >= 4 ? F(rows[2][3]) : 0;
    }
}
