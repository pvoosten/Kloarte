using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;
using static Kloarte.Application.ToolSchema;

namespace Kloarte.Application;

/// <summary>
/// RemoteTools: the palettes (combined: one palette can hold intensity, colour, position and beam values per head; cues
/// refer to them as "@name", so a change of a palette changes every cue that uses it).
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>The tools of the palettes.</summary>
    private IEnumerable<RemoteTool> PaletteTools()
    {
        yield return Tool("list_palettes", "Palettes",
            "Every palette: name, its heads, the channel keys it holds, and the cues that refer to it.",
            Object(), readOnly: true, _ => Palettes());

        yield return Tool("get_palette", "Palette values",
            "A palette's values per head number and channel key.",
            Object(Required("name", String("Palette name"))), readOnly: true, args =>
            {
                var palette = PaletteOf(Show, args.String("name"));
                return new { name = palette.Name, values = Texts(palette.Heads), usedBy = UsersOf(Show, palette.Name) };
            });

        yield return Tool("set_palette_values", "Set palette values",
            "Sets values in a palette (made when it doesn't exist); every cue that refers to it follows. " + ValuesHelp +
            " Or 'heads' with 'set'. Refer to it from a cue with the value \"@name\".",
            Object(Required("name", String("Palette name")),
                Optional("values", Map(ValuesHelp)),
                Optional("heads", List("Head numbers or group names, for 'set'")),
                Optional("set", Map("Channel values for every head in 'heads'"))),
            readOnly: false, SetPaletteValues);

        yield return Tool("delete_palette", "Delete palette",
            "Deletes a palette that no cue refers to (a used palette is refused, with the cues that use it).",
            Object(Required("name", String("Palette name"))), readOnly: false, args =>
            {
                var show = Show;
                var palette = PaletteOf(show, args.String("name"));
                var users = UsersOf(show, palette.Name);
                if (users.Count > 0)
                    throw new InvalidOperationException($"Palette {palette.Name} is used by cue {string.Join(", ", users)}");
                _app.EditShow($"Delete palette {palette.Name}", d => d.Palettes.Remove(palette));
                return Done($"Deleted palette {palette.Name}");
            });
    }

    /// <summary>The answer of list_palettes.</summary>
    private object Palettes()
    {
        var show = Show;
        return show.Palettes.Select(p => new
        {
            name = p.Name,
            heads = p.Heads.Keys.ToList(),
            keys = p.Heads.Values.SelectMany(v => v.Keys).Distinct().ToList(),
            usedBy = UsersOf(show, p.Name),
        }).ToList();
    }

    /// <summary>The palette with this name (without case), or an error listing the palettes.</summary>
    private static DesignPalette PaletteOf(DesignShow show, string name) =>
        show.Palette(name) ?? show.Palettes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"No palette '{name}' (palettes: {string.Join(", ", show.Palettes.Select(p => p.Name))})");

    /// <summary>The numbers of the cues that refer to a palette.</summary>
    private static List<string> UsersOf(DesignShow show, string palette) =>
        show.CueOrder.Where(c => c.Heads.Values.Any(v => v.Values.Any(l => l.Palette == palette))).Select(c => c.Cue).ToList();

    /// <summary>set_palette_values.</summary>
    private object SetPaletteValues(ToolArgs args)
    {
        var show = Show;
        string name = args.String("name").Trim();
        if (name.Length == 0)
            throw new ArgumentException("A palette needs a name");
        var existing = show.Palette(name) ?? show.Palettes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var values = ValuesOf(show, args, palettes: false);
        _app.EditShow(existing == null ? $"New palette {name}" : $"Palette {existing.Name}", d =>
        {
            var palette = existing;
            if (palette == null)
                d.Palettes.Add(palette = new DesignPalette { Name = name });
            Apply(palette.Heads, values);
        });
        return Done($"{(existing == null ? "New palette" : "Palette")} {existing?.Name ?? name}: {Summary(values)}");
    }
}
