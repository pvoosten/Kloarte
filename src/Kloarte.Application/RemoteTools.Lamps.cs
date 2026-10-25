using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Kloarte.Model;
using static Kloarte.Application.ToolSchema;

namespace Kloarte.Application;

/// <summary>
/// RemoteTools: hanging and taking down lamps (user, 25 Oct 2026): the catalogue's fixture types, the supports (trusses,
/// pipes, stands) to hang them on, adding lamps on a support (each on a head of its own) and removing lamps whose head is
/// dark in every cue (a lamp that lights a cue is refused, with the cues). Positions in stage coordinates.
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>The tools of hanging and removing lamps.</summary>
    private IEnumerable<RemoteTool> LampTools()
    {
        yield return Tool("list_fixture_types", "Fixture types",
            "The fixture types add_lamps can hang (the house catalogue): id, name, kind (pc, fresnel, profile, par, " +
            "flood, moving), watts, beam / zoom angles.",
            Object(), readOnly: true, _ => Files.Catalog?.Models.Select(m => new
            {
                id = m.Id, name = m.Name, kind = m.Kind, watts = m.Watts > 0 ? m.Watts : (int?)null,
                zoom = m.Zoom, fieldAngle = m.Zoom == null ? m.FieldAngle : (float?)null,
            }).GroupBy(m => m.id).Select(g => g.First()).ToList());

        yield return Tool("list_supports", "Trusses and pipes",
            "The supports lamps hang on: id, name, stand or not, start and end [x, y, z] (m, stage coordinates), length, " +
            "and the lamps on it.",
            Object(), readOnly: true, _ => Supports());

        yield return Tool("add_lamps", "Hang lamps",
            "Hangs lamps of a fixture type (list_fixture_types) on a support (list_supports: id or name, e.g. \"LT 5\") at " +
            "stage x positions along it ('x'), or 'count' lamps spread evenly over it. Each gets a head of its own: the " +
            "next free head number and DMX address, or from 'firstHead' / 'firstAddress' on. One undo step. The lamps start " +
            "dark (no cue sets them): light them with set_cue_values, aim moving heads with aim_moving_heads.",
            Object(Required("type", String("Fixture type id, e.g. robe-ledbeam150")),
                Required("support", TextOrNumber("Support id or name, e.g. \"LT 5\"")),
                Optional("x", List("Stage x of each lamp (m, + = audience's right)")),
                Optional("count", Integer("This many lamps, spread evenly over the support (instead of 'x')")),
                Optional("firstHead", Integer("Head number of the first lamp (the next ones follow)")),
                Optional("firstAddress", Integer("DMX address of the first lamp (absolute, 1-based; the next ones follow)"))),
            readOnly: false, AddLamps);

        yield return Tool("remove_lamps", "Remove lamps",
            "Takes lamps out of the rig, only when their head's intensity is 0 in every cue (palettes counted; a lamp that " +
            "lights a cue is refused, with those cues). A head no lamp is left on is unpatched and its values leave the " +
            "cues, palettes and groups. One undo step.",
            Object(Optional("lamps", List("Rig item ids")), Optional("heads", List("Heads (numbers or group names): all their lamps"))),
            readOnly: false, RemoveLamps);
    }

    /// <summary>The tools of gels (a conventional lamp's colour: per head, the same in every cue).</summary>
    private IEnumerable<RemoteTool> GelTools()
    {
        yield return Tool("find_gels", "Find gels",
            "Gels of the colour table that match a code or a name (\"L119\", \"132\", \"blue\", \"R80\"): code, name and " +
            "colour (r, g, b 0..1).",
            Object(Required("text", String("A gel code (L / R and the number; a number alone is Lee) or a part of the name"))),
            readOnly: true, args =>
            {
                var gels = Files.Gels ?? throw new InvalidOperationException("No colour table");
                return GelCodes.Search(gels, args.String("text"), 24).Select(id => new
                {
                    code = GelCodes.Code(id), name = gels[id].Name,
                    rgb = new[] { Math.Round(gels[id].R, 2), Math.Round(gels[id].G, 2), Math.Round(gels[id].B, 2) },
                }).ToList();
            });

        yield return Tool("set_gel", "Set gel",
            "The gel of conventional lamps (their head's colour in the patch: the same in EVERY cue, as a gel in the " +
            "lamp). By lamps (rig item ids) or heads / groups; 'gel' is a code (\"L119\"; \"O/W\" = open white). One undo " +
            "step. Moving heads colour per cue (set_cue_values).",
            Object(Required("gel", String("Gel code, e.g. L119 or R80; O/W = no gel")),
                Optional("lamps", List("Rig item ids")), Optional("heads", List("Heads (numbers or group names)"))),
            readOnly: false, args =>
            {
                var show = Show;
                var gels = Files.Gels ?? throw new InvalidOperationException("No colour table");
                string code = args.String("gel");
                int gel = GelCodes.Parse(code, gels)
                          ?? throw new ArgumentException($"No gel '{code}' in the colour table (find_gels searches it)");
                var lamps = args.Strings("lamps").Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                        ? id : throw new ArgumentException($"'{s}' is not a rig item id"))
                    .Concat(HeadsOf(show, args).SelectMany(h => LampsOfHead(show, h)))
                    .Distinct().ToList();
                if (lamps.Count == 0)
                    throw new ArgumentException("Give 'lamps' or 'heads'");
                foreach (int id in lamps)
                    if (show.Rig.Items.FirstOrDefault(i => i.IsLamp && i.Id == id) is not { } item)
                        throw new ArgumentException($"No lamp {id}");
                    else if (item.Head is { } head && show.LayoutOf(head)?["Pan"] != null)
                        throw new ArgumentException($"Lamp {id} (head {head}) is a moving head: it colours per cue (set_cue_values)");
                return Done(_app.SetGel(lamps, gel));
            });
    }

    /// <summary>The answer of list_supports.</summary>
    private object Supports()
    {
        var show = Show;
        var frame = _app.Frame;
        var rig = new Rig(show);
        return rig.Supports.Select(s =>
        {
            var start = frame.ToStage(s.Start.X, s.Start.Y, s.Start.Z);
            var end = start + s.Direction * s.Length;
            return new
            {
                id = s.Id, name = s.Name, stand = s.IsStand ? true : (bool?)null,
                start = Rounded(start), end = Rounded(end), length = Math.Round(s.Length, 2),
                lamps = rig.LampsOn(s.Id).Select(l => l.Id).ToList(),
            };
        }).ToList();
    }

    /// <summary>add_lamps.</summary>
    private object AddLamps(ToolArgs args)
    {
        var show = Show;
        string type = args.String("type");
        var model = Files.Catalog?.Find(type)
                    ?? throw new ArgumentException($"No fixture type '{type}' (see list_fixture_types)");
        var support = SupportOf(show, args.String("support"));
        var frame = _app.Frame;
        var start = frame.ToStage(support.Start.X, support.Start.Y, support.Start.Z);
        var end = start + support.Direction * support.Length;
        float low = Math.Min(start.X, end.X), high = Math.Max(start.X, end.X);
        List<float> xs;
        if (args.Strings("x") is { Count: > 0 } given)
            xs = given.Select(s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                ? x : throw new ArgumentException($"'{s}' is not a stage x")).ToList();
        else if (args.Int("count") is { } count and > 0)
            xs = Enumerable.Range(0, count).Select(i => low + (high - low) * (i + 1) / (count + 1)).ToList();   // evenly, not at the ends
        else
            throw new ArgumentException("Give 'x' (stage x of each lamp) or 'count'");
        var outside = xs.Where(x => x < low - 0.01f || x > high + 0.01f).ToList();
        if (outside.Count > 0 && Math.Abs(high - low) > 0.01f)
            throw new ArgumentException(
                $"x {string.Join(", ", outside.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)))} is off {support.Name} " +
                $"(it runs from x {low.ToString("0.##", CultureInfo.InvariantCulture)} to {high.ToString("0.##", CultureInfo.InvariantCulture)})");
        var ids = _app.AddLamps($"Hang {xs.Count} {model.ShortName} on {support.Name}", model.Id, support.Id, xs,
            args.Int("firstHead"), args.Int("firstAddress"));
        var lamps = ids.Select(id => show.Rig.Items.First(i => i.Id == id)).ToList();
        return new
        {
            done = Done($"Hung {lamps.Count} {model.ShortName} on {support.Name}"),
            lamps = lamps.Select(l => new
            {
                lamp = l.Id, head = l.Head, address = l.Address, name = show.Head(l.Head ?? 0)?.Name,
                position = StagePosition(l),
            }).ToList(),
        };
    }

    /// <summary>remove_lamps.</summary>
    private object RemoveLamps(ToolArgs args)
    {
        var show = Show;
        var lamps = args.Strings("lamps").Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? id : throw new ArgumentException($"'{s}' is not a rig item id"))
            .Concat(HeadsOf(show, args).SelectMany(h => LampsOfHead(show, h)))
            .Distinct().ToList();
        if (lamps.Count == 0)
            throw new ArgumentException("Give 'lamps' or 'heads'");
        var lit = new List<string>();
        foreach (var head in lamps.Select(id => show.Rig.Items.FirstOrDefault(i => i.IsLamp && i.Id == id)
                                                    ?? throw new ArgumentException($"No lamp {id}"))
                     .Select(l => l.Head).Where(h => h != null).Select(h => h.Value).Distinct())
        {
            var cues = DesignEdits.CuesLighting(show, head)
                       ?? throw new InvalidOperationException($"Head {head} has no intensity channel: Kloarte can't tell it is dark");
            if (cues.Count > 0)
                lit.Add($"head {head} lights cue {string.Join(", ", cues.Select(c => c.Cue).Take(10))}{(cues.Count > 10 ? ", ..." : "")}");
        }
        if (lit.Count > 0)
            throw new InvalidOperationException("Not removed: the intensity must be 0 in every cue; " + string.Join("; ", lit));
        string result = _app.RemoveLamps(lamps.Count == 1 ? $"Remove lamp {lamps[0]}" : $"Remove {lamps.Count} lamps", lamps);
        return Done(result);
    }

    /// <summary>The support with this id or name (without case and spaces: "LT5" finds "LT 5").</summary>
    private static RigSupport SupportOf(DesignShow show, string idOrName)
    {
        var supports = new Rig(show).Supports;
        static string Bare(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        return (int.TryParse(idOrName, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                   ? supports.FirstOrDefault(s => s.Id == id) : null)
               ?? supports.FirstOrDefault(s => Bare(s.Name) == Bare(idOrName))
               ?? throw new ArgumentException(
                   $"No support '{idOrName}' (supports: {string.Join(", ", supports.Select(s => s.Name))})");
    }

    /// <summary>A stage position of a rig item ([x, y, z], centimetres).</summary>
    private double[] StagePosition(IRigItem item) => Rounded(_app.Frame.ToStage(item.X, item.Y, item.Z));
}
