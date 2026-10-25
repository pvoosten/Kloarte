using System;
using System.Collections.Generic;
using System.Linq;
using Kloarte.Model;
using static Kloarte.Application.ToolSchema;

namespace Kloarte.Application;

/// <summary>
/// RemoteTools: the stage as it is shown: the playback (go to a cue, GO), the room's look (haze, stray light), the
/// selected lamps, and pictures of the views.
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>The width of a picture when none is asked for (enough to judge a beam, small for the client).</summary>
    private const int DefaultPictureWidth = 1280;

    /// <summary>The tools of the stage.</summary>
    private IEnumerable<RemoteTool> StageTools()
    {
        yield return Tool("go_to_cue", "Go to cue",
            "Shows the end state of a cue on stage at once (no fades). 'before' = the state before the first cue.",
            Object(Required("cue", TextOrNumber("Cue number, or \"before\""))), readOnly: true, args =>
            {
                var show = Show;
                if (string.Equals(args.String("cue"), "before", StringComparison.OrdinalIgnoreCase))
                {
                    _app.GoToCue(null);
                    return "Before the first cue";
                }
                int index = CueIndex(show, args, current: false);
                GoToIndex(show, index);
                var cue = show.CueOrder[index];
                return $"Cue {cue.Cue}{(cue.Text != null ? " " + cue.Text : "")}";
            });

        yield return Tool("go", "GO",
            "The next cue with its fade times (as GO on the console). A screenshot right after shows the fade under way.",
            Object(), readOnly: true, _ =>
            {
                _app.Go();
                var show = _app.Design;
                return show != null && _app.CueId is { } id && show.CueById(id) is { } cue
                    ? $"GO: cue {cue.Cue}"
                    : "GO";
            });

        yield return Tool("set_look", "Room look",
            "The room's haze (0..100 %, the Haze slider; cues with smoke take over from there) and stray light " +
            "(0..2: spill and bounce that keep the room from going black). Saved with the project; not an undo step " +
            "(undo doesn't bring the old look back: set it again).",
            Object(Optional("haze", Number("Haze in %", 0, 100)), Optional("strayLight", Number("Stray light", 0, 2))),
            readOnly: false, args =>
            {
                if (Files.Look == null)
                    throw new InvalidOperationException("No project open");
                if (args.Number("haze") is { } haze)
                    _app.Haze = ProjectLook.DensityOf(haze);
                if (args.Number("strayLight") is { } stray)
                    _app.StrayLight = (float)stray;
                return Done(FormattableString.Invariant($"Haze {ProjectLook.PercentOf(_app.Haze):0.#} %, stray light {_app.StrayLight:0.##}"));
            });

        yield return Tool("select_lamps", "Select lamps",
            "Selects lamps in Kloarte (the inspector and the views show them; the user sees what you mean). By rig item " +
            "ids and / or heads (numbers or group names); nothing = clear the selection.",
            Object(Optional("lamps", List("Rig item ids")), Optional("heads", List("Head numbers or group names"))),
            readOnly: true, args =>
            {
                var show = Show;
                var ids = args.Strings("lamps").Select(s => int.TryParse(s, out int id) ? id : throw new ArgumentException($"'{s}' is not a rig item id")).ToHashSet();
                var heads = HeadsOf(show, args);
                ids.UnionWith(show.Rig.Items.Where(i => i.IsLamp && i.Head is { } h && heads.Contains(h)).Select(i => i.Id));
                int count = _app.SelectLamps(ids);
                return count == 0 ? "Nothing selected" : $"{count} lamp{(count == 1 ? "" : "s")} selected";
            });

        yield return new RemoteTool("screenshot", "Screenshot",
            "A picture of Kloarte: the whole window, or one view by its title (get_status lists them: e.g. \"3D\", " +
            "\"Audience\"; 3D views are rendered even when hidden). Use it to judge looks, beams and haze.",
            Object(Optional("view", String("View title (default: the whole window)")),
                Optional("maxWidth", Integer($"Largest width in pixels (default {DefaultPictureWidth}; 0 = as rendered)"))),
            readOnly: true, async (raw, reply) =>
            {
                string view = raw.OptionalString("view");
                byte[] png = await _app.Capture(view, raw.Int("maxWidth") ?? DefaultPictureWidth);
                reply.Image(png, "image/png");
                var show = _app.Design;
                string at = show != null && _app.CueId is { } id && show.CueById(id) is { } cue ? $"cue {cue.Cue}" : "before the first cue";
                reply.Text($"{(string.IsNullOrEmpty(view) ? "Window" : view)} at {at}{(_app.Running ? " (fading)" : "")}");
            });
    }
}
