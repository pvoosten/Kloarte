using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Kloarte.Model;
using static Kloarte.Application.ToolSchema;

namespace Kloarte.Application;

/// <summary>
/// RemoteTools: the scenery (decorstukken, Kloarte's own data in <c>blocking/scenery.yaml</c>, not exported to MagicQ):
/// list, add, change and remove set pieces (the scene is built again), and per cue take a piece out / bring it in or open
/// a split curtain (<c>blocking/steps.yaml</c>; the playback shows it). Arguments are named and ranged as in the files
/// (<see cref="SceneryPieceEntry"/>, <see cref="SceneryChangeEntry"/>). Each edit is one undo step.
/// </summary>
public sealed partial class RemoteTools
{
    /// <summary>The shapes' names, as the tools and the files take them.</summary>
    private const string ShapeNames = "block, pyramid, table, curtain";

    /// <summary>The tools of the scenery.</summary>
    private IEnumerable<RemoteTool> SceneryTools()
    {
        yield return Tool("list_scenery", "Scenery",
            "The set pieces (Kloarte's own, not in MagicQ): id, name, shape, position [x, y, z] (m, stage coordinates: the " +
            "centre of the piece's bottom; a curtain: of its bottom edge), rotation (deg about the vertical, counter-" +
            "clockwise seen from above), sizes (m), table / curtain fields, colour, material; and each piece's state at the " +
            "end of a cue (on stage or not, how far a split curtain is open).",
            Object(Optional("cue", TextOrNumber("The cue whose end state is given (default: the current cue)"))),
            readOnly: true, Scenery);

        yield return AsyncTool("add_scenery", "Add set piece",
            "Adds a set piece; what isn't given is the shape's default (block / pyramid 1 x 1 x 1 m, table 1.6 x 0.8 m and " +
            "0.75 m high, curtain 6 x 4 m black molton with 50 % fullness). Answers its id (a slug of the name). " +
            "Before the first cue every piece is on stage; set_cue_scenery takes it out / brings it in per cue. One undo step.",
            Object(new[] { Required("shape", String("Shape: " + ShapeNames)) }.Concat(PieceFields()).ToArray()),
            readOnly: false, AddScenery);

        yield return AsyncTool("update_scenery", "Change set piece",
            "Changes a set piece: move, rotate, resize, recolour, another material, rename (the id stays), curtain / table " +
            "fields. Only the fields given change. One undo step.",
            Object(new[]
            {
                Required("id", String("The piece's id (or its name)")),
                Optional("shape", String("Another shape: " + ShapeNames + " (the sizes become its defaults, except those given)")),
            }.Concat(PieceFields()).ToArray()),
            readOnly: false, UpdateScenery);

        yield return AsyncTool("remove_scenery", "Remove set pieces",
            "Removes set pieces and what the cues change of them. One undo step.",
            Object(Optional("id", String("A piece's id (or name)")), Optional("ids", List("Piece ids (or names)"))),
            readOnly: false, RemoveScenery);

        yield return AsyncTool("set_cue_scenery", "Cue scenery",
            "What a cue changes of set pieces (tracking like the lights: later cues keep it): per piece id 'visible' " +
            "(false = the piece goes out at the cue's GO, true = it comes in; no fade), 'open' (a split curtain opens / " +
            "closes to this %, 0 = closed) and 'time' (the seconds it takes, default " +
            FormattableString.Invariant($"{Blocking.DefaultOpenTime:0.#}") + "). The fields given are set, a field null " +
            "removes it; a piece null or \"clear\" removes all the cue changes of it. One undo step.",
            Object(Optional("cue", TextOrNumber("Cue number (default: the current cue)")),
                Required("pieces", Map("Piece id -> {\"visible\": false} / {\"open\": 100, \"time\": 8} / null or \"clear\", " +
                                       "e.g. {\"table\": {\"visible\": false}, \"main-curtain\": {\"open\": 100}}"))),
            readOnly: false, SetCueScenery);
    }

    /// <summary>The fields of a piece in the schemas of add_scenery and update_scenery (new nodes each time).</summary>
    private static (string, JsonObject, bool)[] PieceFields() => new[]
    {
        Optional("name", String("Name as shown in Kloarte (add: default the shape's name)")),
        Optional("position", ListOfNumbers("[x, y, z] (m, stage coordinates) of the centre of the piece's bottom; " +
                                           "[x, z] = on the floor (y 0); add: default [0, 0, 0]")),
        Optional("rotation", Number("Degrees about the vertical, counter-clockwise seen from above (0 = width along x)", -360, 360)),
        Optional("width", Number("Width along the piece's x (m): a table's length, a curtain's width when closed", 0.01, 50)),
        Optional("depth", Number("Depth along the piece's z (m); not for a curtain", 0.01, 50)),
        Optional("height", Number("Height (m): a pyramid's apex, a table's top side, a curtain's cloth", 0.01, 30)),
        Optional("topThickness", Number("A table's top thickness (m)", 0.005, 1)),
        Optional("legThickness", Number("A table's legs: side of their square section (m)", 0.005, 1)),
        Optional("fullness", Number("A curtain's fullness (%): extra cloth in its pleats (0 = flat, 100 = twice the width)", 0, 300)),
        Optional("split", Boolean("A curtain in two halves that open to the sides")),
        Optional("open", Number("How far a split curtain is open before the first cue (%, 0 = closed)", 0, 100)),
        Optional("color", String("Colour \"#rrggbb\" (\"\" = the default)")),
        Optional("material", String("Material: " + SceneryMaterials.Names.Replace(",", ", ") +
                                    " (molton absorbs nearly all light; gauze is translucent and stops no beam)")),
    };

    /// <summary>A list of 2 or 3 numbers.</summary>
    private static JsonObject ListOfNumbers(string description) => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject { ["type"] = "number" },
        ["minItems"] = 2,
        ["maxItems"] = 3,
        ["description"] = description,
    };

    /// <summary>The answer of list_scenery.</summary>
    private object Scenery(ToolArgs args)
    {
        var show = Show;
        var blocking = Model.Blocking;
        int index = show.CueOrder.Count == 0 ? -1 : args.Has("cue") || CurrentIndex(show) >= 0 ? CueIndex(show, args) : -1;
        var cueIds = show.CueOrder.Take(index + 1).Where(c => c.Id != null).Select(c => c.Id.Value).ToList();
        return new
        {
            atCue = index >= 0 ? show.CueOrder[index].Cue : "before the first cue",
            pieces = blocking.Scenery.Select(p =>
            {
                var frame = blocking.SceneryAfter(p.Id, cueIds);
                bool table = p.Shape == SceneryShape.Table, curtain = p.Shape == SceneryShape.Curtain;
                return new
                {
                    id = p.Id, name = p.Name, shape = ShapeName(p.Shape),
                    position = p.Position.Select(v => Math.Round(v, 3)).ToArray(),
                    rotation = Math.Round(p.Rotation, 1),
                    width = Round(p.Width), depth = curtain ? null : Round(p.Depth), height = Round(p.Height),
                    topThickness = table ? Round(p.TopThickness) : null, legThickness = table ? Round(p.LegThickness) : null,
                    fullness = curtain ? Round(p.Fullness) : null, split = curtain ? p.Split : (bool?)null,
                    open = p.Opens ? Round(p.Open) : null,
                    color = "#" + p.ColorOrDefault, material = SceneryMaterials.NameOf(p.Material),
                    onStage = frame.Visible, openAtCue = p.Opens ? Round(frame.Open) : null,
                };
            }).ToList(),
        };
    }

    /// <summary>add_scenery.</summary>
    private async Task<object> AddScenery(ToolArgs args)
    {
        _ = Show;
        var shape = ShapeOf(args.String("shape"));
        string name = args.OptionalString("name") is { Length: > 0 } given ? given.Trim() : Capitalized(ShapeName(shape));
        string id = ActorIds.For(name, Model.Blocking.Scenery.Select(p => p.Id));
        var piece = SceneryPiece.New(id, name, shape);
        ApplyPieceFields(piece, args);
        await _app.EditModel($"Add set piece {name}", m => m.Blocking.Scenery.Add(piece.Copy()), rebuild: true);
        return new { id, done = Done($"Added {ShapeName(shape)} {name} (id {id})") };
    }

    /// <summary>update_scenery.</summary>
    private async Task<object> UpdateScenery(ToolArgs args)
    {
        _ = Show;
        var piece = PieceOf(args.String("id"));
        var changed = piece.Copy();
        if (args.OptionalString("shape") is { } shape && ShapeOf(shape) is var newShape && newShape != changed.Shape)
        {
            // The sizes become the new shape's defaults (as in the inspector); sizes given in this call are applied after.
            var defaults = SceneryPiece.New(changed.Id, changed.Name, newShape);
            changed.Shape = newShape;
            (changed.Width, changed.Depth, changed.Height) = (defaults.Width, defaults.Depth, defaults.Height);
            (changed.TopThickness, changed.LegThickness) = (defaults.TopThickness, defaults.LegThickness);
            (changed.Fullness, changed.Split, changed.Open) = (defaults.Fullness, defaults.Split, defaults.Open);
        }
        if (args.OptionalString("name") is { } name)
            changed.Name = name.Trim().Length > 0 ? name.Trim() : throw new ArgumentException("argument 'name' must not be empty");
        ApplyPieceFields(changed, args);
        await _app.EditModel($"Change set piece {piece.Name}", m =>
        {
            int at = m.Blocking.Scenery.FindIndex(p => p.Id == changed.Id);
            if (at >= 0)
                m.Blocking.Scenery[at] = changed.Copy();
        }, rebuild: true);
        return Done($"Changed set piece {piece.Name}" + (changed.Name != piece.Name ? $" (now {changed.Name}, id {changed.Id})" : ""));
    }

    /// <summary>remove_scenery.</summary>
    private async Task<object> RemoveScenery(ToolArgs args)
    {
        _ = Show;
        var names = args.Strings("ids").Concat(args.OptionalString("id") is { } one ? new[] { one } : Array.Empty<string>()).ToList();
        if (names.Count == 0)
            throw new ArgumentException("Nothing to remove: give 'id' or 'ids'");
        var pieces = names.Select(PieceOf).Distinct().ToList();
        string label = pieces.Count == 1 ? $"set piece {pieces[0].Name}" : $"{pieces.Count} set pieces";
        await _app.EditModel($"Remove {label}", m =>
        {
            foreach (var p in pieces)
                m.Blocking.RemovePiece(p.Id);
        }, rebuild: true);
        return Done($"Removed {label}");
    }

    /// <summary>set_cue_scenery.</summary>
    private async Task<object> SetCueScenery(ToolArgs args)
    {
        var show = Show;
        var cue = show.CueOrder[CueIndex(show, args)];
        int id = cue.Id ?? throw new InvalidOperationException($"Cue {cue.Cue} has no id yet (save the project first)");
        var pieces = args.Object("pieces") ?? throw ToolArgs.Missing("pieces");
        // every change checked before anything changes: piece id -> the cue's new change of it (null = none)
        var changes = new List<(string Piece, SceneryChange Change)>();
        foreach (var entry in pieces.EnumerateObject())
        {
            var piece = PieceOf(entry.Name);
            var value = entry.Value;
            if (value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "clear", StringComparison.OrdinalIgnoreCase))
            {
                changes.Add((piece.Id, null));
                continue;
            }
            if (value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"pieces.{entry.Name}: give {{\"visible\": ..., \"open\": ..., \"time\": ...}}, null or \"clear\"");
            changes.Add((piece.Id, ChangeOf(piece, Model.Blocking.SceneryChangeOf(id, piece.Id), value, $"pieces.{entry.Name}")));
        }
        if (changes.Count == 0)
            throw new ArgumentException("Nothing to change: 'pieces' is empty");
        await _app.EditModel($"Scenery in cue {cue.Cue}", m =>
        {
            foreach (var (piece, change) in changes)
            {
                m.Blocking.ClearSceneryChange(id, piece);
                if (change is { IsEmpty: false })
                {
                    var into = m.Blocking.SceneryChangeOf(id, piece, create: true);
                    (into.Visible, into.Open, into.Time) = (change.Visible, change.Open, change.Time);
                }
            }
        }, rebuild: false);
        return Done($"Scenery in cue {cue.Cue}: " + string.Join("; ", changes.Select(c =>
            $"{Model.Blocking.FindPiece(c.Piece)?.Name ?? c.Piece} {(c.Change is { IsEmpty: false } ch ? ChangeText(ch) : "no change")}")));
    }

    /// <summary>
    /// A cue's change of a piece after a set_cue_scenery item `value` (fields given are set, a null field is removed)
    /// on what the cue changes now (`was`, or null). Throws for a wrong field (path `at` in the message).
    /// </summary>
    private static SceneryChange ChangeOf(SceneryPiece piece, SceneryChange was, JsonElement value, string at)
    {
        var change = new SceneryChange { Visible = was?.Visible, Open = was?.Open, Time = was?.Time };
        var item = new ToolArgs(value);
        foreach (var field in value.EnumerateObject())
        {
            bool clear = field.Value.ValueKind == JsonValueKind.Null;
            switch (field.Name)
            {
                case "visible":
                    change.Visible = clear ? null : item.Bool("visible");
                    break;
                case "open":
                case "time":
                    if (!piece.Opens)
                        throw new ArgumentException($"{at}.{field.Name}: {piece.Name} is not a split curtain (only a split curtain opens)");
                    if (field.Name == "open")
                        change.Open = clear ? null : (float)Ranged(item, "open", 0, 100, "%", at);
                    else
                        change.Time = clear ? null : (float)Ranged(item, "time", 0, 600, "s", at);
                    break;
                default:
                    throw new ArgumentException($"{at}: unknown field '{field.Name}' (visible, open, time)");
            }
        }
        if (change.Time != null && change.Open == null)
            throw new ArgumentException($"{at}.time: give 'open' too (the time is how long the curtain takes to get there)");
        return change;
    }

    /// <summary>A cue's change of a piece in words ("out", "in, open 100 % in 5 s").</summary>
    private static string ChangeText(SceneryChange change)
    {
        var parts = new List<string>();
        if (change.Visible is { } visible)
            parts.Add(visible ? "in" : "out");
        if (change.Open is { } open)
            parts.Add(FormattableString.Invariant($"open {open:0.#} % in {change.Seconds:0.#} s"));
        return string.Join(", ", parts);
    }

    /// <summary>What a cue changes of the set pieces, piece id -&gt; in words; null when it changes none (list_cues).</summary>
    private Dictionary<string, string> SceneryOfCue(int? cueId) =>
        cueId is { } id && Model.Blocking.SceneryCues.TryGetValue(id, out var pieces) &&
        pieces.Where(p => p.Value is { IsEmpty: false }).ToDictionary(p => p.Key, p => ChangeText(p.Value)) is { Count: > 0 } texts
            ? texts
            : null;

    /// <summary>
    /// Sets the fields add_scenery / update_scenery give on a piece (its shape already set), checking them against the
    /// file's ranges and the shape. Throws before anything is set elsewhere (the piece is the caller's own copy).
    /// </summary>
    private static void ApplyPieceFields(SceneryPiece piece, ToolArgs args)
    {
        bool table = piece.Shape == SceneryShape.Table, curtain = piece.Shape == SceneryShape.Curtain;
        void Only(string name, bool allowed, string what)
        {
            if (args.Has(name) && !allowed)
                throw new ArgumentException($"'{name}' is only for {what} ({piece.Name} is a {ShapeName(piece.Shape)})");
        }
        Only("depth", !curtain, "a block, pyramid or table (a curtain's depth comes from its fullness)");
        Only("topThickness", table, "a table");
        Only("legThickness", table, "a table");
        Only("fullness", curtain, "a curtain");
        Only("split", curtain, "a curtain");
        if (args.Get("position") is { } position)
        {
            var numbers = position.ValueKind == JsonValueKind.Array && position.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number)
                ? position.EnumerateArray().Select(v => (float)v.GetDouble()).ToList()
                : new List<float>();
            piece.Position = numbers.Count switch
            {
                3 => numbers.ToArray(),
                2 => new[] { numbers[0], 0f, numbers[1] },
                _ => throw new ArgumentException("argument 'position' must be [x, y, z] or [x, z] (metres, stage coordinates)"),
            };
        }
        if (args.Has("rotation"))
            piece.Rotation = (float)Ranged(args, "rotation", -360, 360, "°");
        if (args.Has("width"))
            piece.Width = (float)Ranged(args, "width", 0.01, 50, "m");
        if (args.Has("depth"))
            piece.Depth = (float)Ranged(args, "depth", 0.01, 50, "m");
        if (args.Has("height"))
            piece.Height = (float)Ranged(args, "height", 0.01, 30, "m");
        if (args.Has("topThickness"))
            piece.TopThickness = (float)Ranged(args, "topThickness", 0.005, 1, "m");
        if (args.Has("legThickness"))
            piece.LegThickness = (float)Ranged(args, "legThickness", 0.005, 1, "m");
        if (args.Has("fullness"))
            piece.Fullness = (float)Ranged(args, "fullness", 0, 300, "%");
        if (args.Has("split"))
            piece.Split = args.Bool("split");
        if (args.Has("open"))
        {
            if (!piece.Opens)
                throw new ArgumentException($"'open' is only for a split curtain (give \"split\": true; {piece.Name} is a " +
                                            $"{(curtain ? "curtain in one cloth" : ShapeName(piece.Shape))})");
            piece.Open = (float)Ranged(args, "open", 0, 100, "%");
        }
        if (args.OptionalString("color") is { } color)
            piece.Color = color.Length == 0 ? null
                : ShirtColors.Parse(color) != null ? color.Trim().TrimStart('#').ToLowerInvariant()
                : throw new ArgumentException($"'{color}' is not a colour: give \"#rrggbb\" (e.g. \"#8c7355\")");
        if (args.OptionalString("material") is { } material)
            piece.Material = SceneryMaterials.Parse(material)
                             ?? throw new ArgumentException($"Unknown material '{material}' ({SceneryMaterials.Names.Replace(",", ", ")})");
    }

    /// <summary>A number argument within min..max (inclusive); throws naming the range and unit (and the path `at`).</summary>
    private static double Ranged(ToolArgs args, string name, double min, double max, string unit, string at = null)
    {
        double value = args.Number(name) ?? throw ToolArgs.Missing(name);
        string label = at != null ? $"{at}.{name}" : $"argument '{name}'";
        return value >= min && value <= max
            ? value
            : throw new ArgumentException(FormattableString.Invariant($"{label} must be {min}..{max} {unit} (not {value})"));
    }

    /// <summary>The set piece with this id or name (without case). Throws naming the pieces there are.</summary>
    private SceneryPiece PieceOf(string idOrName)
    {
        var pieces = Model.Blocking.Scenery;
        return pieces.FirstOrDefault(p => p.Id == idOrName)
               ?? pieces.FirstOrDefault(p => string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException(pieces.Count == 0
                   ? $"No set piece '{idOrName}' (there are none: add_scenery adds one)"
                   : $"No set piece '{idOrName}' (pieces: {string.Join(", ", pieces.Select(p => p.Id))})");
    }

    /// <summary>The shape of a name (without case). Throws for another name.</summary>
    private static SceneryShape ShapeOf(string name) =>
        Enum.TryParse(name?.Trim(), true, out SceneryShape shape) && Enum.IsDefined(shape) && !int.TryParse(name, out _)
            ? shape
            : throw new ArgumentException($"Unknown shape '{name}' ({ShapeNames})");

    /// <summary>A shape's name as the files write it ("table").</summary>
    private static string ShapeName(SceneryShape shape) => shape.ToString().ToLowerInvariant();

    /// <summary>A text with a capital first letter.</summary>
    private static string Capitalized(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];

    /// <summary>A size rounded to the millimetre (floats such as 0.04 print as 0.04, not 0.03999).</summary>
    private static double? Round(float value) => Math.Round(value, 3);
}
