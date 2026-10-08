using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kloarte.Model;

/// <summary>
/// The fixture types a native show can get lamps of: the catalogue's (<see cref="TypesPath"/>, the same channels as the
/// console's personalities, so an export finds them), else one made from a GDTF mode, else a generic dimmer.
/// </summary>
public static class DesignTypes
{
    /// <summary>
    /// The fixture types of the catalogue's lamps (generic_dimmer and the moving heads), made from the console's
    /// templates (a MagicQ test keeps it in step with data/magicq_templates.shw).
    /// </summary>
    public const string TypesPath = "res://data/fixture_types.yaml";

    /// <summary>The name of the generic dimmer type (every conventional lamp).</summary>
    public const string GenericName = "generic_dimmer";

    /// <summary>The catalogue's fixture types (read once; empty when the file is missing or broken).</summary>
    private static List<DesignPersonality> _catalogue;

    /// <summary>The catalogue's fixture types.</summary>
    public static IReadOnlyList<DesignPersonality> Catalogue
    {
        get
        {
            if (_catalogue != null)
                return _catalogue;
            string path = AppPaths.File(TypesPath);
            var read = File.Exists(path) ? FileCodec.Current.Read<DesignRig>(path, File.ReadAllText(path)).Value : null;
            return _catalogue = read?.Types ?? new List<DesignPersonality>();
        }
    }

    /// <summary>A generic dimmer: one intensity channel.</summary>
    public static DesignPersonality Generic() => new()
    {
        Name = GenericName, Mode = "Dimmer", Channels = new List<string> { "Dimmer" }, Classes = "I",
    };

    /// <summary>
    /// The fixture type of a catalogue model: the catalogue's type of its personality name, else one made from its GDTF
    /// (the mode of the personality's number, else the first), else a generic dimmer. A copy (the caller adds it).
    /// </summary>
    public static DesignPersonality For(FixtureModel model)
    {
        string name = model?.Personality ?? GenericName;
        if (Catalogue.FirstOrDefault(t => t.Name == name) is { } known)
            return Copy(known);
        if (model?.Gdtf is { Length: > 0 } prefix && GdtfLibrary.Get(prefix) is { } fixture && fixture.Modes.Count > 0)
            return FromGdtf(name, prefix, fixture, fixture.Modes[0]);
        return Generic();
    }

    /// <summary>
    /// A fixture type made from a GDTF mode: a channel per DMX offset named after its attribute ("Pan", "Pan fine"),
    /// classes from the attributes (Dimmer = I, Pan / Tilt = P, colour = C, the rest B).
    /// </summary>
    public static DesignPersonality FromGdtf(string name, string prefix, GdtfFixture fixture, GdtfMode mode)
    {
        int count = mode.ChannelCount;
        var names = Enumerable.Range(1, count).Select(o => $"Channel {o}").ToList();
        var classes = Enumerable.Repeat('B', count).ToArray();
        foreach (var ch in mode.Channels)
            for (int i = 0; i < ch.Offsets.Length; i++)
            {
                int offset = ch.Offsets[i] - 1;
                if (offset < 0 || offset >= count)
                    continue;
                names[offset] = i == 0 ? ch.Attribute : ch.Attribute + " fine";
                classes[offset] = ch.Attribute == "Dimmer" ? 'I' : DesignPersonality.ClassOf(ColourName(ch.Attribute), false);
            }
        return new DesignPersonality
        {
            Name = name, Mode = mode.Name, Channels = names, Classes = new string(classes), Gdtf = prefix,
            GdtfMode = mode.Name,
        };
    }

    /// <summary>A GDTF attribute as a name the class rule knows ("ColorSub_C" = colour).</summary>
    private static string ColourName(string attribute) =>
        attribute.StartsWith("Color") || attribute == "CTO" || attribute == "CTB" || attribute == "CTC" ? "col" : attribute;

    /// <summary>A copy of a fixture type.</summary>
    public static DesignPersonality Copy(DesignPersonality type) => new()
    {
        Name = type.Name, Mode = type.Mode, Channels = type.Channels.ToList(), Classes = type.Classes, Gdtf = type.Gdtf,
        GdtfMode = type.GdtfMode,
    };
}
