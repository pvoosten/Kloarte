using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>RemoteToolsTests: the scenery tools (set pieces, and what each cue changes of them).</summary>
public sealed partial class RemoteToolsTests
{
    /// <summary>The blocking as JSON (to check that a refused call changed nothing).</summary>
    private string BlockingNow => _session.Model.BlockingJson();

    /// <summary>
    /// add_scenery / update_scenery / remove_scenery: a piece gets the shape's defaults and a slug id, a rename keeps the
    /// id, removing takes its cue changes along; each is one named undo step and the scene is rebuilt.
    /// </summary>
    [Fact]
    public async Task SetPiecesAreAddedChangedAndRemoved()
    {
        var added = await Call("add_scenery", new JsonObject { ["shape"] = "table", ["position"] = new JsonArray(1, -2) });
        Assert.False(added.Error, added.Text);
        Assert.Equal("table", (string)JsonNode.Parse(added.Text)!["id"]);
        var table = _session.Model.Blocking.FindPiece("table");
        Assert.Equal("Table", table.Name);
        Assert.Equal(new[] { 1f, 0f, -2f }, table.Position);
        Assert.Equal(1.6f, table.Width);
        Assert.Equal(0.75f, table.Height);
        Assert.Equal("Add set piece Table", _session.UndoName);
        Assert.Contains(_app.Reports, r => r.StartsWith("MCP: Added table"));

        var curtain = await Call("add_scenery", new JsonObject
        {
            ["shape"] = "Curtain", ["name"] = "Main curtain", ["position"] = new JsonArray(0, 0, -0.5), ["split"] = true,
            ["open"] = 20, ["fullness"] = 100, ["color"] = "#2A0000", ["material"] = "molton", ["rotation"] = 15,
        });
        Assert.False(curtain.Error, curtain.Text);
        var main = _session.Model.Blocking.FindPiece("main-curtain");
        Assert.True(main.Opens);
        Assert.Equal(20f, main.Open);
        Assert.Equal("2a0000", main.Color);
        Assert.Equal(SceneryMaterial.Molton, main.Material);
        Assert.Equal(6f, main.Width);   // the curtain's default
        Assert.Equal("table-2", (string)JsonNode.Parse((await Call("add_scenery", new JsonObject { ["shape"] = "table" })).Text)!["id"]);

        var changed = await Call("update_scenery", new JsonObject
        {
            ["id"] = "table", ["name"] = "Kitchen table", ["width"] = 2, ["rotation"] = 90, ["color"] = "336699",
            ["material"] = "wood", ["legThickness"] = 0.08,
        });
        Assert.False(changed.Error, changed.Text);
        table = _session.Model.Blocking.FindPiece("table");
        Assert.Equal("Kitchen table", table.Name);
        Assert.Equal(2f, table.Width);
        Assert.Equal(90f, table.Rotation);
        Assert.Equal("336699", table.Color);
        Assert.Equal(0.08f, table.LegThickness);
        Assert.Equal(new[] { 1f, 0f, -2f }, table.Position);   // not given: unchanged
        Assert.Equal("Change set piece Table", _session.UndoName);
        // by name too
        Assert.False((await Call("update_scenery", new JsonObject { ["id"] = "kitchen TABLE", ["position"] = new JsonArray(2, 0.2, -3) })).Error);
        Assert.Equal(new[] { 2f, 0.2f, -3f }, _session.Model.Blocking.FindPiece("table").Position);

        var list = JsonNode.Parse((await Call("list_scenery")).Text)!;
        Assert.Equal("1", (string)list["atCue"]);
        var pieces = list["pieces"]!.AsArray();
        Assert.Equal(3, pieces.Count);
        var listed = pieces.First(p => (string)p!["id"] == "main-curtain")!;
        Assert.Equal("curtain", (string)listed["shape"]);
        Assert.Equal("#2a0000", (string)listed["color"]);
        Assert.Equal("molton", (string)listed["material"]);
        Assert.True((bool)listed["split"]);
        Assert.Equal(20, (double)listed["open"]);
        Assert.Null(listed["depth"]);
        Assert.True((bool)listed["onStage"]);
        Assert.Equal(0.04, (double)pieces.First(p => (string)p!["id"] == "table")!["topThickness"]);

        _session.Model.Blocking.SceneryChangeOf(Design.CueOrder[1].Id.Value, "table-2", create: true).Visible = false;
        var removed = await Call("remove_scenery", new JsonObject { ["ids"] = new JsonArray("table-2", "Main curtain") });
        Assert.False(removed.Error, removed.Text);
        Assert.Equal("Remove 2 set pieces", _session.UndoName);
        Assert.Null(_session.Model.Blocking.FindPiece("table-2"));
        Assert.Null(_session.Model.Blocking.FindPiece("main-curtain"));
        Assert.Empty(_session.Model.Blocking.SceneryCues);
        Assert.False((await Call("remove_scenery", new JsonObject { ["id"] = "table" })).Error);
        Assert.Equal("Remove set piece Kitchen table", _session.UndoName);

        Assert.Equal("Remove set piece Kitchen table", await _app.Undo());
        Assert.Equal("Kitchen table", _session.Model.Blocking.FindPiece("table").Name);
    }

    /// <summary>
    /// update_scenery with another shape gives the piece that shape's default sizes (as the inspector does), except the
    /// sizes given in the same call; the colour stays.
    /// </summary>
    [Fact]
    public async Task ShapeChangeGivesTheNewShapesSizes()
    {
        await Call("add_scenery", new JsonObject { ["shape"] = "curtain", ["name"] = "Cloth", ["width"] = 3, ["color"] = "#336699" });
        var changed = await Call("update_scenery", new JsonObject { ["id"] = "cloth", ["shape"] = "block" });
        Assert.False(changed.Error, changed.Text);
        var block = _session.Model.Blocking.FindPiece("cloth");
        Assert.Equal(SceneryShape.Block, block.Shape);
        Assert.Equal((1f, 1f, 1f), (block.Width, block.Depth, block.Height));   // the block's defaults
        Assert.Equal(0f, block.Fullness);
        Assert.Equal("336699", block.Color);   // the look stays

        changed = await Call("update_scenery", new JsonObject { ["id"] = "cloth", ["shape"] = "table", ["height"] = 0.9 });
        Assert.False(changed.Error, changed.Text);
        var table = _session.Model.Blocking.FindPiece("cloth");
        Assert.Equal((1.6f, 0.8f), (table.Width, table.Depth));   // the table's defaults
        Assert.Equal(0.9f, table.Height, 3);   // given in the same call
    }

    /// <summary>The piece tools check their arguments before anything changes, with messages that say what is wrong.</summary>
    [Fact]
    public async Task SetPieceArgumentsAreChecked()
    {
        await Call("add_scenery", new JsonObject { ["shape"] = "block", ["name"] = "Box" });
        await Call("add_scenery", new JsonObject { ["shape"] = "curtain", ["name"] = "Back cloth" });
        string before = BlockingNow;
        async Task Refused(string tool, JsonObject args, string message)
        {
            var result = await Call(tool, args);
            Assert.True(result.Error, $"{tool} {args.ToJsonString()}: {result.Text}");
            Assert.Contains(message, result.Text);
        }
        await Refused("add_scenery", new JsonObject { ["shape"] = "sofa" }, "Unknown shape 'sofa'");
        await Refused("add_scenery", new JsonObject { ["shape"] = "block", ["color"] = "red" }, "not a colour");
        await Refused("add_scenery", new JsonObject { ["shape"] = "block", ["width"] = 99 }, "'width' must be 0.01..50 m");
        await Refused("add_scenery", new JsonObject { ["shape"] = "block", ["material"] = "velvet" }, "Unknown material 'velvet'");
        await Refused("add_scenery", new JsonObject { ["shape"] = "block", ["fullness"] = 50 }, "'fullness' is only for a curtain");
        await Refused("add_scenery", new JsonObject { ["shape"] = "block", ["topThickness"] = 0.1 }, "only for a table");
        await Refused("add_scenery", new JsonObject { ["shape"] = "curtain", ["depth"] = 1 }, "'depth' is only for");
        await Refused("add_scenery", new JsonObject { ["shape"] = "block", ["position"] = new JsonArray(1) }, "'position' must be");
        await Refused("update_scenery", new JsonObject { ["id"] = "back-cloth", ["open"] = 50 }, "only for a split curtain");
        await Refused("update_scenery", new JsonObject { ["id"] = "nope", ["width"] = 2 }, "No set piece 'nope' (pieces: box, back-cloth)");
        await Refused("update_scenery", new JsonObject { ["id"] = "box", ["rotation"] = 400 }, "'rotation' must be -360..360");
        await Refused("remove_scenery", new JsonObject(), "Nothing to remove");
        await Refused("remove_scenery", new JsonObject { ["ids"] = new JsonArray("box", "nope") }, "No set piece 'nope'");
        Assert.Equal(before, BlockingNow);

        // a curtain made split can open; another shape keeps its sizes
        Assert.False((await Call("update_scenery", new JsonObject { ["id"] = "back-cloth", ["split"] = true, ["open"] = 50 })).Error);
        Assert.Equal(50f, _session.Model.Blocking.FindPiece("back-cloth").Open);
        Assert.False((await Call("update_scenery", new JsonObject { ["id"] = "box", ["shape"] = "pyramid" })).Error);
        Assert.Equal(SceneryShape.Pyramid, _session.Model.Blocking.FindPiece("box").Shape);
        Assert.False((await Call("update_scenery", new JsonObject { ["id"] = "box", ["color"] = "" })).Error);
        Assert.Null(_session.Model.Blocking.FindPiece("box").Color);
    }

    /// <summary>
    /// set_cue_scenery: per cue a piece goes out / comes in and a split curtain opens (merged into what the cue changes;
    /// tracks into later cues); list_scenery and list_cues show it; "clear" removes it; wrong values change nothing.
    /// </summary>
    [Fact]
    public async Task SceneryChangesPerCue()
    {
        await Call("add_scenery", new JsonObject { ["shape"] = "table" });
        await Call("add_scenery", new JsonObject { ["shape"] = "curtain", ["name"] = "Main", ["split"] = true });
        int cue2 = Design.CueOrder[1].Id.Value;

        var set = await Call("set_cue_scenery", new JsonObject
        {
            ["cue"] = 2,
            ["pieces"] = new JsonObject
            {
                ["table"] = new JsonObject { ["visible"] = false },
                ["main"] = new JsonObject { ["open"] = 100, ["time"] = 8 },
            },
        });
        Assert.False(set.Error, set.Text);
        Assert.Equal("Scenery in cue 2", _session.UndoName);
        Assert.Contains("Table out", set.Text);
        Assert.False(_session.Model.Blocking.SceneryChangeOf(cue2, "table").Visible);
        var curtain = _session.Model.Blocking.SceneryChangeOf(cue2, "main");
        Assert.Equal(100f, curtain.Open);
        Assert.Equal(8f, curtain.Time);

        // merged: the curtain also goes out, its opening stays; a null field removes that field
        Assert.False((await Call("set_cue_scenery", """{"cue": 2, "pieces": {"main": {"visible": false, "time": null}}}""")).Error);
        curtain = _session.Model.Blocking.SceneryChangeOf(cue2, "main");
        Assert.Equal((false, 100f, (float?)null), (curtain.Visible.Value, curtain.Open.Value, curtain.Time));

        var atCue3 = JsonNode.Parse((await Call("list_scenery", new JsonObject { ["cue"] = 3 })).Text)!["pieces"]!.AsArray();
        Assert.False((bool)atCue3.First(p => (string)p!["id"] == "table")!["onStage"]);
        Assert.Equal(100, (double)atCue3.First(p => (string)p!["id"] == "main")!["openAtCue"]);
        var atCue1 = JsonNode.Parse((await Call("list_scenery", new JsonObject { ["cue"] = 1 })).Text)!["pieces"]!.AsArray();
        Assert.True((bool)atCue1.First(p => (string)p!["id"] == "table")!["onStage"]);
        Assert.Equal(0, (double)atCue1.First(p => (string)p!["id"] == "main")!["openAtCue"]);

        var cues = JsonNode.Parse((await Call("list_cues")).Text)!.AsArray();
        Assert.Equal("out", (string)cues[1]!["scenery"]!["table"]);
        Assert.Equal("out, open 100 % in 5 s", (string)cues[1]!["scenery"]!["main"]);
        Assert.Null(cues[0]!["scenery"]);

        string before = BlockingNow;
        async Task Refused(string arguments, string message)
        {
            var result = await Call("set_cue_scenery", arguments);
            Assert.True(result.Error, $"{arguments}: {result.Text}");
            Assert.Contains(message, result.Text);
        }
        await Refused("""{"cue": 2, "pieces": {"table": {"open": 50}}}""", "Table is not a split curtain");
        await Refused("""{"cue": 3, "pieces": {"main": {"time": 3}}}""", "give 'open' too");
        await Refused("""{"cue": 2, "pieces": {"main": {"open": 150}}}""", "pieces.main.open must be 0..100 %");
        await Refused("""{"cue": 2, "pieces": {"main": {"visible": "no"}}}""", "must be true or false");
        await Refused("""{"cue": 2, "pieces": {"main": {"colour": 1}}}""", "unknown field 'colour'");
        await Refused("""{"cue": 2, "pieces": {"sofa": {"visible": false}}}""", "No set piece 'sofa'");
        await Refused("""{"cue": 9, "pieces": {"table": null}}""", "No cue 9");
        await Refused("""{"cue": 2, "pieces": {}}""", "Nothing to change");
        await Refused("""{"cue": 2}""", "'pieces' is missing");
        Assert.Equal(before, BlockingNow);

        Assert.False((await Call("set_cue_scenery", """{"cue": 2, "pieces": {"table": "clear", "main": null}}""")).Error);
        Assert.Empty(_session.Model.Blocking.SceneryCues);
        Assert.Equal("Scenery in cue 2", await _app.Undo());
        Assert.False(_session.Model.Blocking.SceneryChangeOf(cue2, "table").Visible);
    }
}
