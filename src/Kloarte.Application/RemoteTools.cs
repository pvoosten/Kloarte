using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Kloarte.GodotBinding;
using Kloarte.Mcp;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// The running Kloarte as MCP tools (user, 25 Oct 2026: an MCP server inside the app): read the open show (rig, cues,
/// palettes, lamps, problems), edit it (cue values, cues, palettes, mechanics, smoke, scenery; one undo step each, the stage shows
/// the result), play it (go to a cue, haze) and look at it (pictures of the views), save and export it. Works through the
/// UI's own paths (<see cref="IRemoteControl"/>); the host gives it an MCP server (<see cref="IMcpServer"/>).
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>What a status bar message of a remote edit starts with.</summary>
    private const string ReportPrefix = "MCP: ";

    /// <summary>The app.</summary>
    private readonly IRemoteControl _app;

    /// <summary>JSON of the tools' answers: camelCase, no nulls, readable non-ASCII (°, é).</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The tools of an app.</summary>
    public RemoteTools(IRemoteControl app) => _app = app ?? throw new ArgumentNullException(nameof(app));

    /// <summary>What the server tells a client about Kloarte and the tools.</summary>
    internal const string Instructions =
        "Kloarte up de plankn is a theatre lighting visualizer; these tools act on the show open in the running app. " +
        "Coordinates are metres: origin on the stage floor at the proscenium line, +X = audience's right, +Y = up, " +
        "+Z = towards the audience. The show has one tracking cue list: a cue holds only the values it changes; " +
        "everything else tracks from the cues before it. Values are per head number and channel key (see get_head: " +
        "'intensity' in %, GDTF attributes such as Pan / Tilt in degrees, wheel slot names such as 'Open', \"dmx 37\" " +
        "for an exact DMX value, \"@Name\" for a palette). Conventional lamps get barndoors, shutters, iris, gobo and " +
        "focus from their mechanics (get_lamp / set_lamp_mechanics), not from cues. Two kinds of aiming: moving heads " +
        "with pan / tilt per cue (aim_moving_heads), conventional lamps mechanically, the same in every cue " +
        "(focus_lamps); list_actors gives where the actors are at a cue. Set pieces (scenery: blocks, pyramids, " +
        "tables, curtains; Kloarte's own, not in MagicQ) are on stage before the first cue (list_scenery, add_scenery, " +
        "update_scenery, remove_scenery); per cue a piece goes out / comes in and a split curtain opens " +
        "(set_cue_scenery). Every edit is one undo step in " +
        "Kloarte and is not saved until save_project; export_magicq writes the next numbered MagicQ show and never " +
        "overwrites. Use screenshot to look at the result.";

    /// <summary>Offers every tool on a server.</summary>
    public void AddTo(IMcpServer server)
    {
        server.Instructions = Instructions;
        foreach (var tool in All())
            server.Add(tool);
    }

    /// <summary>Every tool, in the order a client lists them.</summary>
    private IEnumerable<RemoteTool> All() =>
        ShowTools().Concat(RigTools()).Concat(CueTools()).Concat(PaletteTools()).Concat(StageTools()).Concat(AimTools()).Concat(SceneryTools()).Concat(LampTools()).Concat(GelTools());

    /// <summary>A tool that answers with JSON (a text answer goes as it is).</summary>
    private static RemoteTool Tool(string name, string title, string description, JsonObject schema, bool readOnly,
        Func<ToolArgs, object> run) =>
        new(name, title, description, schema, readOnly, (args, reply) =>
        {
            reply.Text(Json(run(args)));
            return Task.CompletedTask;
        });

    /// <summary>A tool that awaits the app and answers with JSON.</summary>
    private static RemoteTool AsyncTool(string name, string title, string description, JsonObject schema, bool readOnly,
        Func<ToolArgs, Task<object>> run) =>
        new(name, title, description, schema, readOnly, async (args, reply) => reply.Text(Json(await run(args))));

    /// <summary>An answer as JSON (a text as it is).</summary>
    private static string Json(object value) => value as string ?? JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>The open show's files.</summary>
    private IShowFiles Files => _app.Files is { Model: not null } files
        ? files
        : throw new InvalidOperationException("No show is loaded in Kloarte (open a project first)");

    /// <summary>The loaded model.</summary>
    private IShowModel Model => Files.Model;

    /// <summary>The open native show.</summary>
    private DesignShow Show => _app.Design ?? throw new InvalidOperationException(
        _app.Files?.Model == null
            ? "No show is loaded in Kloarte (open a project first)"
            : "The open show is not a native Kloarte show (save the project once to make it one)");

    /// <summary>Shows what a remote edit did on the status bar, and returns it.</summary>
    private string Done(string text)
    {
        _app.Report(ReportPrefix + text);
        return text;
    }

    /// <summary>A cue number as the files write it ("12.5").</summary>
    private static string CueText(float number) => CueNumbers.Text(number);

    /// <summary>
    /// The index in the cue list of the cue named by argument `name` (a cue number); without it the current cue (when
    /// `current` allows it). Throws when there is no such cue.
    /// </summary>
    private int CueIndex(DesignShow show, ToolArgs args, string name = "cue", bool current = true)
    {
        string text = args.OptionalString(name);
        if (text == null)
        {
            if (!current)
                throw ToolArgs.Missing(name);
            int index = CurrentIndex(show);
            if (index < 0)
                throw new InvalidOperationException($"No current cue: give '{name}' (a cue number)");
            return index;
        }
        float number = CueNumbers.Parse(text) ?? throw new ArgumentException($"'{text}' is not a cue number");
        int found = show.CueOrder.FindIndex(c => CueNumbers.Same(c.Number, number));
        return found >= 0
            ? found
            : throw new ArgumentException($"No cue {text} (cues: {CueRange(show)})");
    }

    /// <summary>The index in the cue list of the playback's current cue (by its id); -1 before the first cue.</summary>
    private int CurrentIndex(DesignShow show) =>
        _app.CueId is { } id ? show.CueOrder.FindIndex(c => c.Id == id) : -1;

    /// <summary>
    /// Shows the end state of cue `index` of the cue list (-1 = before the first cue). Throws for a cue without an id (it
    /// isn't in the played cue list).
    /// </summary>
    private void GoToIndex(DesignShow show, int index)
    {
        if (index < 0)
        {
            _app.GoToCue(null);
            return;
        }
        var cue = show.CueOrder[index];
        _app.GoToCue(cue.Id ?? throw new InvalidOperationException($"Cue {cue.Cue} has no id yet (save the project first)"));
    }

    /// <summary>The cue numbers of the show, short ("1, 2, 2.5 ... 40").</summary>
    private static string CueRange(DesignShow show)
    {
        var numbers = show.CueOrder.Select(c => c.Cue).ToList();
        if (numbers.Count == 0)
            return "none";
        return numbers.Count <= 12
            ? string.Join(", ", numbers)
            : string.Join(", ", numbers.Take(8)) + " ... " + string.Join(", ", numbers.TakeLast(3));
    }

    /// <summary>
    /// The head numbers argument `name` names: head numbers and group names (a group stands for its heads). Throws for an
    /// unknown head or group; empty when the argument is missing.
    /// </summary>
    private static List<int> HeadsOf(DesignShow show, ToolArgs args, string name = "heads")
    {
        var heads = new List<int>();
        foreach (string item in args.Strings(name))
        {
            if (int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                if (show.Head(number) == null)
                    throw new ArgumentException($"No head {number}");
                heads.Add(number);
                continue;
            }
            var group = show.Rig.Groups.FirstOrDefault(g => g.Name.Equals(item, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException(
                            $"'{item}' is neither a head number nor a group (groups: {string.Join(", ", show.Rig.Groups.Select(g => g.Name))})");
            heads.AddRange(group.Heads ?? Array.Empty<int>());
        }
        return heads.Distinct().ToList();
    }

    /// <summary>A set of looks (per head, per key) as plain texts.</summary>
    private static Dictionary<string, Dictionary<string, string>> Texts(Dictionary<string, Dictionary<string, LookValue>> looks) =>
        looks.ToDictionary(h => h.Key, h => h.Value.ToDictionary(v => v.Key, v => v.Value.ToString()));
}
