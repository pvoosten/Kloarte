using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kloarte.Model;
using static Kloarte.Application.ToolSchema;

namespace Kloarte.Application;

/// <summary>
/// RemoteTools: the cue list: list and read cues (what a cue sets, and the tracked state after it), set values, add,
/// change and delete cues, smoke per cue (the scenery per cue: RemoteTools.Scenery). Each edit is one undo step; the stage shows the current cue again.
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>The tools of the cue list.</summary>
    private IEnumerable<RemoteTool> CueTools()
    {
        yield return Tool("list_cues", "Cue list",
            "Every cue in order: number, id, text, fade in / out and delay (s), how many heads it sets, smoke, what it " +
            "changes of the set pieces (piece id -> \"out\" / \"in\" / \"open 100 % in 5 s\"), and which one is current.",
            Object(), readOnly: true, _ => Cues());

        yield return Tool("get_cue", "Cue values",
            "What a cue sets (per head number, per channel key; tracking: only its changes). With 'tracked': the state " +
            "after the cue (every value of the heads, from whichever cue set it).",
            Object(Optional("cue", TextOrNumber("Cue number (default: the current cue)")),
                Optional("tracked", Boolean("Also the full tracked state after the cue")),
                Optional("heads", List("Only these heads (numbers or group names)"))),
            readOnly: true, Cue);

        yield return Tool("set_cue_values", "Set cue values",
            "Sets channel values in a cue (later cues track them). " + ValuesHelp +
            " Or 'heads' (numbers / group names) with 'set' (one set of values for all of them, skipping heads without " +
            "that channel).",
            Object(Optional("cue", TextOrNumber("Cue number (default: the current cue)")),
                Optional("values", Map(ValuesHelp)),
                Optional("heads", List("Head numbers or group names, for 'set'")),
                Optional("set", Map("Channel values for every head in 'heads', e.g. {\"intensity\": 60}"))),
            readOnly: false, SetCueValues);

        yield return Tool("add_cue", "Add cue",
            "Adds a cue (default number: a free one after the current cue) and goes to it. Optionally a copy of " +
            "another cue's values, a blackout, its times and values.",
            Object(Optional("cue", TextOrNumber("The new cue's number (must be free)")),
                Optional("text", String("Its text (MagicQ keeps 15 characters)")),
                Optional("fadeIn", Number("In time (s)", 0, 3600)), Optional("fadeOut", Number("Out time (s)", 0, 3600)),
                Optional("delay", Number("Delay after GO (s, Kloarte's playback only)", 0, 3600)),
                Optional("copyFrom", TextOrNumber("Copy what this cue sets")),
                Optional("blackout", Boolean("Set every intensity to 0")),
                Optional("values", Map(ValuesHelp))),
            readOnly: false, AddCue);

        yield return Tool("update_cue", "Change cue",
            "Changes a cue's number (renumbering keeps its id), text or times.",
            Object(Required("cue", TextOrNumber("Cue number")), Optional("number", TextOrNumber("New cue number")),
                Optional("text", String("New text (\"\" removes it)")),
                Optional("fadeIn", Number("In time (s)", 0, 3600)), Optional("fadeOut", Number("Out time (s)", 0, 3600)),
                Optional("delay", Number("Delay after GO (s)", 0, 3600))),
            readOnly: false, UpdateCue);

        yield return Tool("delete_cue", "Delete cue",
            "Deletes a cue (what it set is gone: later cues track from the cue before). One undo step.",
            Object(Required("cue", TextOrNumber("Cue number"))), readOnly: false, DeleteCue);

        yield return AsyncTool("set_cue_smoke", "Cue smoke",
            "The smoke / haze machine output from a cue on (Kloarte's own data, not a MagicQ fixture): amount in % and " +
            "the seconds it takes; amount null removes it.",
            Object(Optional("cue", TextOrNumber("Cue number (default: the current cue)")),
                Optional("amount", Number("Smoke amount (%); null = no smoke change at this cue", 0, 100)),
                Optional("time", Number("Seconds to reach it (default 20)", 0, 3600))),
            readOnly: false, SetSmoke);
    }

    /// <summary>The answer of list_cues.</summary>
    private object Cues()
    {
        var show = Show;
        var smoke = Model.Blocking.Smoke;
        int current = CurrentIndex(show);
        return show.CueOrder.Select((c, i) => new
        {
            cue = c.Cue, id = c.Id, text = c.Text, fadeIn = c.FadeIn, fadeOut = c.FadeOut, delay = c.Delay,
            heads = c.Heads.Count,
            smoke = c.Id is { } id && smoke.TryGetValue(id, out var s) ? s.Amount : (double?)null,
            scenery = SceneryOfCue(c.Id),
            current = i == current ? true : (bool?)null,
        }).ToList();
    }

    /// <summary>The answer of get_cue.</summary>
    private object Cue(ToolArgs args)
    {
        var show = Show;
        int index = CueIndex(show, args);
        var cue = show.CueOrder[index];
        var only = HeadsOf(show, args);
        bool Wanted(int head) => only.Count == 0 || only.Contains(head);
        var values = cue.Heads.Where(h => DesignShow.HeadOfKey(h.Key) is { } head && Wanted(head))
            .ToDictionary(h => h.Key, h => h.Value);
        Dictionary<string, Dictionary<string, string>> tracked = null;
        if (args.Bool("tracked"))
        {
            tracked = new();
            foreach (var head in show.Rig.Heads.Where(h => Wanted(h.Number)).OrderBy(h => h.Number))
            {
                var state = show.LayoutOf(head).Channels
                    .Select(c => (c.Key, Tracked: DesignEdits.Tracked(show, index, head.Number, c.Key)))
                    .Where(x => x.Tracked.Value != null)
                    .ToDictionary(x => x.Key, x => $"{x.Tracked.Value} (cue {show.CueOrder[x.Tracked.Cue].Cue})");
                if (state.Count > 0)
                    tracked[DesignShow.Key(head.Number)] = state;
            }
        }
        return new
        {
            cue = cue.Cue, id = cue.Id, text = cue.Text, fadeIn = cue.FadeIn, fadeOut = cue.FadeOut, delay = cue.Delay,
            values = Texts(values),
            dmx = cue.Dmx is { Count: > 0 } ? cue.Dmx : null,
            tracked,
        };
    }

    /// <summary>set_cue_values.</summary>
    private object SetCueValues(ToolArgs args)
    {
        var show = Show;
        int index = CueIndex(show, args);
        var cue = show.CueOrder[index];
        var values = ValuesOf(show, args, palettes: true);
        _app.EditShow($"Values in cue {cue.Cue}", _ => Apply(show, cue.Heads, values, index - 1));
        return Done($"Set {Summary(values)} in cue {cue.Cue}");
    }

    /// <summary>add_cue.</summary>
    private object AddCue(ToolArgs args)
    {
        var show = Show;
        float number = args.OptionalString("cue") is { } text
            ? CueNumbers.Parse(text) ?? throw new ArgumentException($"'{text}' is not a cue number")
            : DesignEdits.NumberAfter(show, CurrentIndex(show)) ?? throw new InvalidOperationException("No free cue number after the current cue: give 'cue'");
        if (show.CueOrder.Any(c => CueNumbers.Same(c.Number, number)))
            throw new ArgumentException($"Cue {CueText(number)} exists already");
        var source = args.Has("copyFrom") ? show.CueOrder[CueIndex(show, args, "copyFrom", current: false)] : null;
        var values = args.Has("values") ? ValuesOf(show, args, palettes: true) : new List<HeadValue>();
        // a copy keeps everything of the source (its DMX, name, ...); then the new cue's own fields
        var cue = source?.Copy() ?? new DesignCue();
        cue.Cue = CueText(number);
        cue.Id = show.NextCueId;
        cue.Text = NullIfEmpty(args.OptionalString("text"));
        cue.FadeIn = args.Number("fadeIn") ?? source?.FadeIn;
        cue.FadeOut = args.Number("fadeOut") ?? source?.FadeOut;
        cue.Delay = args.Number("delay") ?? source?.Delay;
        if (args.Bool("blackout"))
            cue.Heads = DesignEdits.Blackout(show);
        Apply(show, cue.Heads, values, show.CueOrder.FindLastIndex(c => c.Number < number));
        _app.EditShow($"Add cue {cue.Cue}", d => d.CueOrder.Add(cue));
        _app.GoToCue(cue.Id);
        return Done($"Added cue {cue.Cue}{(cue.Text != null ? " " + cue.Text : "")}" +
                    (source != null ? $" (a copy of cue {source.Cue})" : "") + "; it is the current cue");
    }

    /// <summary>update_cue.</summary>
    private object UpdateCue(ToolArgs args)
    {
        var show = Show;
        var cue = show.CueOrder[CueIndex(show, args, current: false)];
        float? number = args.OptionalString("number") is { } text
            ? CueNumbers.Parse(text) ?? throw new ArgumentException($"'{text}' is not a cue number")
            : null;
        if (number is { } n && show.CueOrder.Any(c => c != cue && CueNumbers.Same(c.Number, n)))
            throw new ArgumentException($"Cue {CueText(n)} exists already");
        string was = cue.Cue;
        _app.EditShow($"Change cue {was}", _ =>
        {
            if (number is { } renumber)
                cue.Cue = CueText(renumber);
            if (args.Has("text"))
                cue.Text = NullIfEmpty(args.OptionalString("text"));
            cue.FadeIn = args.Number("fadeIn") ?? cue.FadeIn;
            cue.FadeOut = args.Number("fadeOut") ?? cue.FadeOut;
            cue.Delay = args.Number("delay") ?? cue.Delay;
        });
        return Done($"Changed cue {was}" + (cue.Cue != was ? $" (now cue {cue.Cue})" : ""));
    }

    /// <summary>delete_cue.</summary>
    private object DeleteCue(ToolArgs args)
    {
        var show = Show;
        int index = CueIndex(show, args, current: false);
        var cue = show.CueOrder[index];
        bool current = CurrentIndex(show) == index;
        _app.EditShow($"Delete cue {cue.Cue}", d => d.CueOrder.Remove(cue));
        // the current cue stays the same cue (by id); when it is the deleted one, the cue before it (with an id)
        if (current)
            GoToIndex(show, index > 0 ? show.CueOrder.FindLastIndex(index - 1, c => c.Id != null) : -1);
        return Done($"Deleted cue {cue.Cue}");
    }

    /// <summary>set_cue_smoke.</summary>
    private async Task<object> SetSmoke(ToolArgs args)
    {
        var show = Show;
        var cue = show.CueOrder[CueIndex(show, args)];
        int id = cue.Id ?? throw new InvalidOperationException($"Cue {cue.Cue} has no id yet (save the project first)");
        double? amount = args.Number("amount");
        float? time = args.Number("time") is { } t ? (float)t : null;
        await _app.EditModel($"Smoke in cue {cue.Cue}", m =>
        {
            if (amount is { } a)
                m.Blocking.Smoke[id] = new CueSmoke { Amount = Math.Clamp(a, 0, 100), Time = time };
            else
                m.Blocking.Smoke.Remove(id);
        }, rebuild: false);
        return Done(amount is { } set
            ? FormattableString.Invariant($"Smoke {set:0.#} % from cue {cue.Cue} ({time ?? Blocking.DefaultSmokeTime:0.#} s)")
            : $"No smoke change at cue {cue.Cue}");
    }

    /// <summary>Null for an empty text.</summary>
    private static string NullIfEmpty(string text) => string.IsNullOrEmpty(text) ? null : text;
}
