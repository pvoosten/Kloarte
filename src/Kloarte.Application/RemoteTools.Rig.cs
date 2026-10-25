using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Kloarte.Model;
using static Kloarte.Application.ToolSchema;

namespace Kloarte.Application;

/// <summary>
/// RemoteTools: the rig: heads (patch, channels and the values they have at a cue), groups, lamps and their mechanics
/// (barndoors, shutters, iris, gobo, focus: set by hand, not by cues).
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>The tools of the rig.</summary>
    private IEnumerable<RemoteTool> RigTools()
    {
        yield return Tool("list_heads", "Heads and groups",
            "The patch: every head (number, name, fixture type, DMX address, channel count, gel, its lamps' rig item " +
            "ids) and the groups (name, heads).",
            Object(), readOnly: true, _ => Heads());

        yield return Tool("get_head", "Head channels",
            "A head's channels (the keys cue and palette values use, what values each takes) and the value each has " +
            "after a cue (tracked: with the cue that set it; palette values resolved).",
            Object(Required("head", Integer("Head number")),
                Optional("cue", TextOrNumber("Cue number (default: the current cue)"))),
            readOnly: true, Head);

        yield return Tool("list_lamps", "Lamps",
            "The lamps of the rig: rig item id (mechanics and selection use it), name, head, address, catalogue model, " +
            "position (m), and which of barndoors / shutters / iris / gobo they have.",
            Object(), readOnly: true, _ => Lamps());

        yield return Tool("get_lamp", "Lamp mechanics",
            "A lamp's rig item and its mechanics (JSON as Kloarte keeps them): fieldAngle (deg), softness, iris (1 = " +
            "open), barndoors {top,right,bottom,left: 0 open..1 closed, rotation deg}, shutters {top,right,bottom,left: " +
            "{depth 0 out..1 axis..2 across, angle deg}, rotation}, gobo {path, rotation, size}, focusPoint [x,y,z] m.",
            Object(Optional("lamp", Integer("Rig item id")), Optional("head", Integer("Head number (its first lamp)"))),
            readOnly: true, Lamp);

        yield return AsyncTool("set_lamp_mechanics", "Set lamp mechanics",
            "Changes a lamp's mechanics: 'changes' is merged into them (JSON merge patch: nested objects merge, null " +
            "removes, e.g. {\"barndoors\": {\"top\": 0.4}} or {\"focusPoint\": [0, 0, -2]}). One undo step; the lamp's " +
            "beam is rebuilt.",
            Object(Optional("lamp", Integer("Rig item id")), Optional("head", Integer("Head number (its first lamp)")),
                Required("changes", Map("The mechanics fields to change (see get_lamp)"))),
            readOnly: false, SetMechanics);
    }

    /// <summary>The answer of list_heads.</summary>
    private object Heads()
    {
        var show = Show;
        var lamps = show.Rig.Items.Where(i => i.IsLamp && i.Head != null).ToLookup(i => i.Head.Value, i => i.Id);
        return new
        {
            heads = show.Rig.Heads.OrderBy(h => h.Number).Select(h => new
            {
                head = h.Number, name = h.Name, type = h.Type, address = h.Address, channels = h.Channels, gel = h.Gel,
                lamps = lamps[h.Number].ToList(),
            }).ToList(),
            groups = show.Rig.Groups.Select(g => new { name = g.Name, heads = g.Heads }).ToList(),
        };
    }

    /// <summary>The answer of get_head.</summary>
    private object Head(ToolArgs args)
    {
        var show = Show;
        int number = args.Int("head") ?? throw ToolArgs.Missing("head");
        var head = show.Head(number) ?? throw new ArgumentException($"No head {number}");
        var layout = show.LayoutOf(head);
        int cue = show.CueOrder.Count == 0 ? -1 : args.Has("cue") || CurrentIndex(show) >= 0 ? CueIndex(show, args) : -1;
        return new
        {
            head = head.Number, name = head.Name, type = head.Type, address = head.Address, gel = head.Gel,
            atCue = cue >= 0 ? show.CueOrder[cue].Cue : "before the first cue",
            channels = layout.Channels.Select(c =>
            {
                var (value, from) = cue >= 0 ? DesignEdits.Tracked(show, cue, number, c.Key) : (null, -1);
                var resolved = value is { IsPalette: true } ? DesignEdits.Resolved(show, number, c.Key, value) : null;
                return new
                {
                    key = c.Key,
                    name = c.Name,
                    kind = c.Kind.ToString().ToLowerInvariant(),
                    offsets = c.Offsets,
                    takes = Describe(c),
                    value = value?.ToString(),
                    palette = resolved?.ToString(),
                    setBy = from >= 0 ? show.CueOrder[from].Cue : null,
                };
            }).ToList(),
        };
    }

    /// <summary>The answer of list_lamps.</summary>
    private object Lamps()
    {
        var show = Show;
        var mechanics = Model.Mechanics;
        return show.Rig.Items.Where(i => i.IsLamp).Select(i =>
        {
            mechanics.TryGetValue(i.Id, out var m);
            return new
            {
                lamp = i.Id, name = i.Name, head = i.Head, address = i.Address, model = i.Model ?? m?.Model,
                at = i.At,
                has = m == null
                    ? null
                    : new[] { m.Barndoors != null ? "barndoors" : null, m.Shutters != null ? "shutters" : null,
                              m.Iris < 1f ? "iris" : null, m.Gobo != null ? "gobo" : null }.Where(s => s != null).ToList(),
            };
        }).ToList();
    }

    /// <summary>The lamp the arguments name (rig item id, or a head's first lamp).</summary>
    private DesignItem LampOf(ToolArgs args)
    {
        var show = Show;
        if (args.Int("lamp") is { } id)
            return show.Rig.Items.FirstOrDefault(i => i.IsLamp && i.Id == id) ?? throw new ArgumentException($"No lamp {id}");
        if (args.Int("head") is { } head)
            return show.Rig.Items.FirstOrDefault(i => i.IsLamp && i.Head == head)
                   ?? throw new ArgumentException($"Head {head} has no lamp in the rig");
        throw new ArgumentException("Give 'lamp' (rig item id) or 'head'");
    }

    /// <summary>The answer of get_lamp.</summary>
    private object Lamp(ToolArgs args)
    {
        var lamp = LampOf(args);
        Model.Mechanics.TryGetValue(lamp.Id, out var mechanics);
        return new
        {
            lamp = lamp.Id, name = lamp.Name, head = lamp.Head, address = lamp.Address, model = lamp.Model, at = lamp.At,
            rot = lamp.Rot,
            mechanics = mechanics == null ? null : JsonSerializer.SerializeToNode(mechanics, ShowModel<DesignShow>.Json),
        };
    }

    /// <summary>set_lamp_mechanics.</summary>
    private async Task<object> SetMechanics(ToolArgs args)
    {
        var lamp = LampOf(args);
        var changes = args.Object("changes") ?? throw ToolArgs.Missing("changes");
        var patch = JsonNode.Parse(changes.GetRawText()).AsObject();
        if (!Model.Mechanics.ContainsKey(lamp.Id))
            throw new ArgumentException($"Lamp {lamp.Id} has no mechanics");
        Merged(Model.Mechanics[lamp.Id], patch);   // a wrong field is an error before anything changes
        string label = string.IsNullOrEmpty(lamp.Name) ? $"lamp {lamp.Id}" : lamp.Name;
        await _app.EditModel($"Mechanics of {label}", m => m.Mechanics[lamp.Id] = Merged(m.Mechanics[lamp.Id], patch),
            rebuild: true);
        return new
        {
            done = Done($"Mechanics of {label} changed"),
            mechanics = JsonSerializer.SerializeToNode(Model.Mechanics[lamp.Id], ShowModel<DesignShow>.Json),
        };
    }

    /// <summary>The mechanics' JSON options, refusing a field the mechanics don't have (a typo is an error).</summary>
    private static readonly JsonSerializerOptions StrictJson = new(ShowModel<DesignShow>.Json)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>A lamp's mechanics with a JSON merge patch applied (a copy). Throws for a value that doesn't read.</summary>
    private static FixtureMechanics Merged(FixtureMechanics mechanics, JsonObject patch)
    {
        var node = JsonSerializer.SerializeToNode(mechanics, ShowModel<DesignShow>.Json)!.AsObject();
        Merge(node, patch);
        try
        {
            return node.Deserialize<FixtureMechanics>(StrictJson)
                   ?? throw new ArgumentException("the mechanics can't be null");
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"changes: {e.Message}");
        }
    }

    /// <summary>JSON merge patch (RFC 7396) of `patch` into `target`; keys match without case (as the file reads).</summary>
    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach (var (key, value) in patch.ToList())
        {
            string name = target.Select(p => p.Key).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
            if (value == null)
                target.Remove(name);
            else if (value is JsonObject inner && target[name] is JsonObject existing)
                Merge(existing, inner);
            else
                target[name] = value.DeepClone();
        }
    }
}
