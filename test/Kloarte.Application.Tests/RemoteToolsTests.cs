using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Kloarte.Application;
using Kloarte.MagicQ;
using Kloarte.Mcp;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// The MCP tools of the app (<see cref="RemoteTools"/>) on a native project without the UI: they read the show, edit it
/// through the app (one undo step each), check what they are given before anything changes, and answer with JSON.
/// </summary>
public sealed partial class RemoteToolsTests : TestBase
{
    /// <summary>The session of the test's project.</summary>
    private readonly ShowSession<DesignShow> _session;

    /// <summary>The app the tools act on.</summary>
    private readonly FakeRemoteControl _app;

    /// <summary>The tools, by name.</summary>
    private readonly Dictionary<string, IMcpTool> _tools;

    /// <summary>What the server was told.</summary>
    private readonly Server _server = new();

    /// <summary>The 4 dimmers' and 1 T1's head numbers.</summary>
    private readonly List<int> _dimmers, _spots;

    /// <summary>A native project "Remote": 4 dimmers, a T1, 3 cues (cue 1 sets the dimmers at 60 %); cue 1 is current.</summary>
    public RemoteToolsTests() : base("app_remote")
    {
        if (Directory.Exists(_out))
            Directory.Delete(_out, true);
        string shows = Path.Combine(_out, "show"), projects = Path.Combine(_out, "projects");
        Directory.CreateDirectory(shows);
        Directory.CreateDirectory(projects);
        var exchange = new MagicQExchange { ShowFolderOverride = shows };
        ShowSession<DesignShow> session = null;
        session = new ShowSession<DesignShow>(new DesignStore(exchange, () => session.Catalog), new ProjectLibrary(projects),
            Path.Combine(_out, "history"));
        _session = session;
        session.NewProject("Remote");
        var design = session.Load().Show;
        var t1 = DesignTypes.Catalogue.First(t => t.Gdtf?.Contains("T1") == true);
        _dimmers = Enumerable.Range(1, 4).Select(i => ((DesignItem)session.Editor.ImportLamp(DesignTypes.GenericName, $"Front {i}", "PC")).Head.Value).ToList();
        _spots = new List<int> { ((DesignItem)session.Editor.ImportLamp(t1.Name, "Spot", "T1")).Head.Value };
        // what the scene gives every lamp of the rig when it is built (ShowBuilder): the default mechanics of its type
        foreach (var lamp in design.Rig.Items.Where(i => i.IsLamp))
            session.Model.Mechanics[lamp.Id] = FixtureMechanics.DefaultFor(session.Catalog?.Resolve(FixtureKind.Pc), FixtureKind.Pc);
        design.Rig.Groups.Add(new DesignGroup { Number = 1, Name = "Front", Heads = _dimmers.ToArray() });
        var stack = ((IShowDocument)design).CueStacks[0];
        for (int c = 1; c <= 3; c++)
            session.Editor.InsertCue(stack, c, $"Cue {c}", blackout: false);
        foreach (int head in _dimmers)
            DesignEdits.Set(design.CueOrder[0].Heads, head, "intensity", LookValue.Of(60));
        design.Changed();
        session.Checkpoint();
        _app = new FakeRemoteControl(session);
        _app.GoToCue(design.CueOrder[0].Id);
        new RemoteTools(_app).AddTo(_server);
        _tools = _server.Tools;
    }

    /// <summary>The show.</summary>
    private DesignShow Design => _session.Model.Show;

    /// <summary>Every tool has a unique name, a description and an object schema; the server got the instructions.</summary>
    [Fact]
    public void ToolsAreDescribed()
    {
        Assert.Equal(_tools.Count, _server.Order.Distinct().Count());
        foreach (var tool in _tools.Values)
        {
            Assert.Matches("^[a-z_]+$", tool.Name);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
            var schema = JsonNode.Parse(tool.InputSchema)!.AsObject();
            Assert.Equal("object", (string)schema["type"]);
        }
        foreach (string name in new[] { "get_status", "list_cues", "get_cue", "set_cue_values", "add_cue", "delete_cue",
                     "set_palette_values", "get_lamp", "set_lamp_mechanics", "go_to_cue", "screenshot", "save_project",
                     "export_magicq", "undo" })
            Assert.Contains(name, _tools.Keys);
        Assert.True(_tools["list_cues"].ReadOnly);
        Assert.False(_tools["set_cue_values"].ReadOnly);
        Assert.Contains("tracking", _server.Instructions);
    }

    /// <summary>get_status: the project, counts and the current cue.</summary>
    [Fact]
    public async Task StatusTellsWhatIsOpen()
    {
        var status = JsonNode.Parse((await Call("get_status")).Text)!;
        Assert.Equal("Remote", (string)status["project"]);
        Assert.True((bool)status["native"]);
        Assert.Equal(5, (int)status["heads"]);
        Assert.Equal(3, (int)status["cues"]);
        Assert.Equal("1", (string)status["currentCue"]!["cue"]);
    }

    /// <summary>
    /// set_cue_values: values per head and for a group go into the cue (one undo step), later cues track them; a wrong
    /// key or value is an error that changes nothing.
    /// </summary>
    [Fact]
    public async Task CueValuesAreCheckedAndTracked()
    {
        var layout = Design.LayoutOf(_spots[0]);
        string gobo = layout["Gobo1"].SetNames.First(n => n != "Open");
        var set = await Call("set_cue_values", new JsonObject
        {
            ["cue"] = 2,
            ["values"] = Values(_spots[0], new JsonObject { ["intensity"] = 100, ["pan"] = -20, ["Gobo1"] = gobo }),
            ["heads"] = new JsonArray("Front"),
            ["set"] = new JsonObject { ["intensity"] = 30 },
        });
        Assert.False(set.Error, set.Text);
        var cue2 = Design.CueOrder[1];
        Assert.Equal("100", cue2.Of(_spots[0])["intensity"].ToString());
        Assert.Equal("-20", cue2.Of(_spots[0])["Pan"].ToString());   // the key as the layout names it
        Assert.All(_dimmers, d => Assert.Equal("30", cue2.Of(d)["intensity"].ToString()));
        Assert.Equal("Values in cue 2", _session.UndoName);
        Assert.Contains(_app.Reports, r => r.StartsWith("MCP: Set"));

        var tracked = JsonNode.Parse((await Call("get_cue", new JsonObject { ["cue"] = "3", ["tracked"] = true, ["heads"] = new JsonArray(_dimmers[0]) })).Text)!;
        Assert.Equal("30 (cue 2)", (string)tracked["tracked"]![_dimmers[0].ToString()]!["intensity"]);

        string before = Design.ToText();
        var wrongKey = await Call("set_cue_values", new JsonObject { ["values"] = Values(_dimmers[0], new JsonObject { ["Pan"] = 10 }) });
        Assert.True(wrongKey.Error);
        Assert.Contains("no channel 'Pan'", wrongKey.Text);
        var wrongSet = await Call("set_cue_values", new JsonObject { ["values"] = Values(_spots[0], new JsonObject { ["Gobo1"] = "No such gobo" }) });
        Assert.True(wrongSet.Error);
        Assert.Contains("not a value", wrongSet.Text);
        var wrongPalette = await Call("set_cue_values", new JsonObject { ["values"] = Values(_spots[0], new JsonObject { ["Pan"] = "@Nothing" }) });
        Assert.True(wrongPalette.Error);
        Assert.Equal(before, Design.ToText());

        Assert.False((await Call("undo")).Error);
        Assert.Null(Design.CueOrder[1].Of(_spots[0]));
    }

    /// <summary>add_cue (a free number after the current cue, a copy), update_cue (renumbered, same id), delete_cue.</summary>
    [Fact]
    public async Task CuesAreAddedChangedDeleted()
    {
        float? number = DesignEdits.NumberAfter(Design, 0);   // the UI's "next free number" after the current cue (cue 1)
        Assert.NotNull(number);
        Design.CueOrder[0].Dmx = new Dictionary<string, int> { ["500"] = 255 };   // a copy keeps the DMX of no head
        Design.CueOrder[0].Name = "Opening";
        var added = await Call("add_cue", """{"text": "Copy", "copyFrom": 1, "fadeIn": 4}""");
        Assert.False(added.Error, added.Text);
        var cue = Design.CueOrder[1];
        Assert.Equal(number.Value, cue.Number);
        Assert.Equal("Copy", cue.Text);
        Assert.Equal(4, cue.FadeIn);
        Assert.Equal(_dimmers.Count, cue.Heads.Count);
        Assert.Equal(255, cue.Dmx["500"]);
        Assert.Equal("Opening", cue.Name);
        Assert.NotEqual(Design.CueOrder[0].Id, cue.Id);
        Assert.Equal(cue.Id, _app.CueId);
        Assert.True((await Call("add_cue", """{"cue": 2}""")).Error);   // taken

        int id = cue.Id.Value;
        Assert.False((await Call("update_cue", new JsonObject { ["cue"] = cue.Cue, ["number"] = 5, ["text"] = "" })).Error);
        Assert.Equal("5", Design.CueById(id).Cue);
        Assert.Null(Design.CueById(id).Text);
        Assert.Same(Design.CueById(id), Design.CueOrder.Last());

        Assert.False((await Call("delete_cue", """{"cue": 5}""")).Error);
        Assert.Null(Design.CueById(id));
        Assert.True((await Call("delete_cue", """{"cue": 5}""")).Error);
    }

    /// <summary>
    /// The current cue is a cue id, not a place in the list: a cue without an id (not played) before it doesn't shift it,
    /// and deleting a cue before the current one keeps the same current cue.
    /// </summary>
    [Fact]
    public async Task CurrentCueIsKeptById()
    {
        Design.CueOrder.Insert(0, new DesignCue { Cue = "0.5", Text = "No id" });   // as a hand-written cues.yaml may have it
        Design.Changed();
        Assert.False((await Call("go_to_cue", """{"cue": 2}""")).Error);
        int id = Design.CueOrder.First(c => c.Cue == "2").Id.Value;
        Assert.Equal(id, _app.CueId);
        Assert.Equal("2", (string)JsonNode.Parse((await Call("get_status")).Text)!["currentCue"]!["cue"]);
        Assert.True((await Call("go_to_cue", """{"cue": 0.5}""")).Error);   // not in the played cue list

        Assert.False((await Call("delete_cue", """{"cue": 1}""")).Error);
        Assert.Equal(id, _app.CueId);
        Assert.False((await Call("delete_cue", """{"cue": 2}""")).Error);   // the current one: the cue before it with an id
        Assert.Null(_app.CueId);
    }

    /// <summary>Palettes: made by set_palette_values, used by a cue, refused for deletion while used.</summary>
    [Fact]
    public async Task PalettesAreUsedByCues()
    {
        var made = await Call("set_palette_values", new JsonObject
        {
            ["name"] = "Centre", ["values"] = Values(_spots[0], new JsonObject { ["Pan"] = 0, ["Tilt"] = -45 }),
        });
        Assert.False(made.Error, made.Text);
        Assert.Equal("0", Design.Palette("Centre").Of(_spots[0])["Pan"].ToString());
        Assert.True((await Call("set_palette_values", new JsonObject
        {
            ["name"] = "Other", ["values"] = Values(_spots[0], new JsonObject { ["Pan"] = "@Centre" }),
        })).Error);

        Assert.False((await Call("set_cue_values", new JsonObject
        {
            ["cue"] = 3, ["values"] = Values(_spots[0], new JsonObject { ["Pan"] = "@Centre", ["Tilt"] = "@centre" }),
        })).Error);
        var head = JsonNode.Parse((await Call("get_head", new JsonObject { ["head"] = _spots[0], ["cue"] = 3 })).Text)!;
        var pan = head["channels"]!.AsArray().First(c => (string)c!["key"] == "Pan")!;
        Assert.Equal("@Centre", (string)pan["value"]);
        Assert.Equal("0", (string)pan["palette"]);

        var refused = await Call("delete_palette", """{"name": "Centre"}""");
        Assert.True(refused.Error);
        Assert.Contains("used by cue 3", refused.Text);
    }

    /// <summary>set_lamp_mechanics merges into the mechanics (one undo step); a field the mechanics don't have is refused.</summary>
    [Fact]
    public async Task MechanicsAreMerged()
    {
        int lamp = Design.Rig.Items.First(i => i.IsLamp && i.Head == _dimmers[0]).Id;
        var set = await Call("set_lamp_mechanics", new JsonObject
        {
            ["lamp"] = lamp, ["changes"] = new JsonObject { ["barndoors"] = new JsonObject { ["top"] = 0.4 }, ["fieldAngle"] = 22 },
        });
        Assert.False(set.Error, set.Text);
        var mechanics = _session.Model.Mechanics[lamp];
        Assert.Equal(0.4f, mechanics.Barndoors.Top);
        Assert.Equal(22f, mechanics.FieldAngle);
        Assert.StartsWith("Mechanics of", _session.UndoName);

        var typo = await Call("set_lamp_mechanics", new JsonObject
        {
            ["lamp"] = lamp, ["changes"] = new JsonObject { ["barndoor"] = new JsonObject { ["top"] = 1 } },
        });
        Assert.True(typo.Error);
        Assert.Equal(0.4f, _session.Model.Mechanics[lamp].Barndoors.Top);

        Assert.False((await Call("set_lamp_mechanics", new JsonObject
        {
            ["head"] = _dimmers[0], ["changes"] = new JsonObject { ["barndoors"] = null },
        })).Error);
        Assert.Null(_session.Model.Mechanics[lamp].Barndoors);
    }

    /// <summary>Smoke per cue goes into the blocking, by cue id.</summary>
    [Fact]
    public async Task SmokeIsPerCue()
    {
        Assert.False((await Call("set_cue_smoke", """{"cue": 2, "amount": 40, "time": 8}""")).Error);
        var smoke = _session.Model.Blocking.Smoke[Design.CueOrder[1].Id.Value];
        Assert.Equal(40, smoke.Amount);
        Assert.Equal(8f, smoke.Time);
        var cues = JsonNode.Parse((await Call("list_cues")).Text)!.AsArray();
        Assert.Equal(40, (double)cues[1]!["smoke"]);
        Assert.True((bool)cues[0]!["current"]);
    }

    /// <summary>The stage: go to a cue, the look, the selection and a picture.</summary>
    [Fact]
    public async Task StageIsPlayedAndPictured()
    {
        Assert.False((await Call("go_to_cue", """{"cue": 3}""")).Error);
        Assert.Equal(Design.CueOrder[2].Id, _app.CueId);
        Assert.False((await Call("go_to_cue", """{"cue": "before"}""")).Error);
        Assert.Null(_app.CueId);

        Assert.False((await Call("set_look", """{"haze": 50, "strayLight": 0.2}""")).Error);
        Assert.Equal(ProjectLook.DensityOf(50), _app.Haze);
        Assert.Equal(0.2f, _app.StrayLight);

        Assert.Equal("4 lamps selected", (await Call("select_lamps", """{"heads": ["Front"]}""")).Text);

        var picture = await Call("screenshot", """{"view": "Audience", "maxWidth": 640}""");
        Assert.False(picture.Error, picture.Text);
        Assert.Equal(("Audience", 640), _app.Captured);
        Assert.Equal(Convert.ToBase64String(FakeRemoteControl.Picture), picture.Images.Single());
        Assert.True((await Call("screenshot", """{"view": "Nothing"}""")).Error);
    }

    /// <summary>save_project writes the project's files.</summary>
    [Fact]
    public async Task SaveWritesTheFiles()
    {
        await Call("set_cue_values", new JsonObject
        {
            ["cue"] = 3, ["values"] = Values(_dimmers[1], new JsonObject { ["intensity"] = 15 }),
        });
        var saved = await Call("save_project");
        Assert.False(saved.Error, saved.Text);
        Assert.Empty(_session.UnsavedChanges());
        Assert.Contains("intensity: 15", File.ReadAllText(Path.Combine(_session.ProjectFolder, "cues.yaml")));
    }

    /// <summary>
    /// Three actors in the blocking: Niels (standing at [1, 0, -2], walks to [-1, 0, -3] in cue 2), An (sits down at
    /// [2, 0, -1] in cue 2) and Jo (leaves the stage in cue 2).
    /// </summary>
    private void AddActors()
    {
        var blocking = _session.Model.Blocking;
        ActorDef Actor(string name, bool female, float x, float z) => new()
        {
            Id = ActorIds.Slug(name), Name = name, Female = female,
            Initial = new ActorState { Visible = true, Pose = ActorPose.Standing, Position = new[] { x, 0f, z }, Facing = 0f },
        };
        blocking.Actors.AddRange(new[] { Actor("Niels", false, 1, -2), Actor("An", true, 2, -1), Actor("Jo", false, 0, -4) });
        int cue2 = Design.CueOrder[1].Id.Value;
        blocking.Record(cue2, "niels", 0, new ActorState { Position = new[] { -1f, 0f, -3f } });
        blocking.Record(cue2, "an", 0, new ActorState { Pose = ActorPose.Seated });
        blocking.Record(cue2, "jo", 0, new ActorState { Visible = false });
        _session.Checkpoint();
    }

    /// <summary>list_actors: where the actors are at the end of a cue, and the face point the aiming tools use.</summary>
    [Fact]
    public async Task ActorsAreReadAtACue()
    {
        AddActors();
        var before = JsonNode.Parse((await Call("list_actors", new JsonObject { ["cue"] = 1 })).Text)!["actors"]!.AsArray();
        var niels = before.First(a => (string)a!["name"] == "Niels")!;
        Assert.Equal("[1,0,-2]", niels["position"]!.ToJsonString());
        Assert.Equal(System.Math.Round(ActorDef.DefaultMaleHeight * 0.93, 2), (double)niels["face"]![1], 2);

        var after = JsonNode.Parse((await Call("list_actors", new JsonObject { ["cue"] = 2 })).Text)!["actors"]!.AsArray();
        Assert.Equal("[-1,0,-3]", after.First(a => (string)a!["name"] == "Niels")!["position"]!.ToJsonString());
        var an = after.First(a => (string)a!["name"] == "An")!;
        Assert.Equal("seated", (string)an["pose"]);
        Assert.Equal(System.Math.Round(ActorDef.DefaultFemaleHeight * 0.68, 2), (double)an["face"]![1], 2);
        Assert.False((bool)after.First(a => (string)a!["name"] == "Jo")!["onStage"]);
    }

    /// <summary>
    /// aim_moving_heads: pan / tilt from the rigged head (the app's) at an actor's face go into the cue (later cues track
    /// them), one undo step; a conventional lamp or an actor off stage is refused.
    /// </summary>
    [Fact]
    public async Task MovingHeadsAreAimedInACue()
    {
        AddActors();
        int spot = _spots[0];
        var aimed = await Call("aim_moving_heads", new JsonObject
        {
            ["cue"] = 2, ["aims"] = new JsonArray(new JsonObject { ["head"] = spot, ["actor"] = "niels" }),
        });
        Assert.False(aimed.Error, aimed.Text);
        var face = new System.Numerics.Vector3(-1, ActorDef.DefaultMaleHeight * 0.93f, -3);
        Assert.True(System.Numerics.Vector3.Distance(face, _app.AimedAt[spot]) < 1e-4f);
        var (pan, tilt) = FakeRemoteControl.AnglesFor(face);
        Assert.Equal(System.Math.Round(pan, 2), Design.CueOrder[1].Of(spot)["Pan"].AsNumber!.Value, 2);
        Assert.Equal(System.Math.Round(tilt, 2), Design.CueOrder[1].Of(spot)["Tilt"].AsNumber!.Value, 2);
        Assert.Equal("Aim in cue 2", _session.UndoName);
        Assert.Equal(Design.CueOrder[1].Of(spot)["Pan"], DesignEdits.Tracked(Design, 2, spot, "Pan").Value);   // cue 3 tracks it

        // One target for several heads: the middle of two faces.
        Assert.False((await Call("aim_moving_heads", new JsonObject
        {
            ["cue"] = 2, ["heads"] = new JsonArray(spot), ["actors"] = new JsonArray("Niels", "An"),
        })).Error);
        Assert.Equal(0.5f, _app.AimedAt[spot].X, 3);

        var conventional = await Call("aim_moving_heads", new JsonObject { ["heads"] = new JsonArray(_dimmers[0]), ["actor"] = "An" });
        Assert.True(conventional.Error);
        Assert.Contains("focus_lamps", conventional.Text);
        var offstage = await Call("aim_moving_heads", new JsonObject { ["cue"] = 2, ["heads"] = new JsonArray(spot), ["actor"] = "Jo" });
        Assert.True(offstage.Error);
        Assert.Contains("not on stage", offstage.Text);
    }

    /// <summary>
    /// focus_lamps: a conventional lamp's focus point in its mechanics (every cue), at an actor's face or a point; no cue
    /// changes; a moving head is refused.
    /// </summary>
    [Fact]
    public async Task LampsAreFocusedMechanically()
    {
        AddActors();
        string cues = Design.ToText();
        int lamp = Design.Rig.Items.First(i => i.IsLamp && i.Head == _dimmers[0]).Id;
        var focused = await Call("focus_lamps", new JsonObject
        {
            ["cue"] = 2, ["heads"] = new JsonArray(_dimmers[0]), ["actor"] = "Niels", ["height"] = 1.2,
        });
        Assert.False(focused.Error, focused.Text);
        Assert.Equal(new[] { -1f, 1.2f, -3f }, _session.Model.Mechanics[lamp].FocusPoint);
        Assert.Equal(cues, Design.ToText());   // mechanics, not a cue
        Assert.Equal($"Focus lamp {lamp}", _session.UndoName);

        Assert.False((await Call("focus_lamps", new JsonObject
        {
            ["aims"] = new JsonArray(new JsonObject { ["lamp"] = lamp, ["at"] = new JsonArray(0.5, 0, -1) }),
        })).Error);
        Assert.Equal(new[] { 0.5f, 0f, -1f }, _session.Model.Mechanics[lamp].FocusPoint);

        var mover = await Call("focus_lamps", new JsonObject { ["heads"] = new JsonArray(_spots[0]), ["at"] = new JsonArray(0, 0, 0) });
        Assert.True(mover.Error);
        Assert.Contains("aim_moving_heads", mover.Text);
    }

    /// <summary>
    /// add_lamps: lamps of a catalogue type on a support, spread over it or at given x, each on a head of its own (the
    /// next free one, or from a given head / address on); one undo step; a wrong type or an x off the support is refused.
    /// </summary>
    [Fact]
    public async Task LampsAreHungOnASupport()
    {
        var supports = JsonNode.Parse((await Call("list_supports")).Text)!.AsArray();
        var pipe = supports.First(s => s!["stand"] == null && (double)s!["length"] > 4)!;
        string name = (string)pipe["name"];
        double z = (double)pipe["start"]![2], x0 = (double)pipe["start"]![0], x1 = (double)pipe["end"]![0];
        int heads = Design.Rig.Heads.Count;

        var hung = await Call("add_lamps", new JsonObject
        {
            ["type"] = "robe-ledbeam150", ["support"] = name.Replace(" ", ""), ["count"] = 4,
        });
        Assert.False(hung.Error, hung.Text);
        var lamps = JsonNode.Parse(hung.Text)!["lamps"]!.AsArray();
        Assert.Equal(4, lamps.Count);
        Assert.Equal(heads + 4, Design.Rig.Heads.Count);
        Assert.Equal(4, lamps.Select(l => (int)l!["head"]).Distinct().Count());
        foreach (var lamp in lamps)
        {
            var item = Design.Rig.Items.First(i => i.Id == (int)lamp!["lamp"]);
            Assert.Equal((int)pipe["id"], item.Parent);
            Assert.Equal(z, (double)lamp!["position"]![2], 1);   // on the pipe (in plan)
            Assert.InRange((double)lamp["position"]![0], System.Math.Min(x0, x1), System.Math.Max(x0, x1));
        }
        Assert.StartsWith("Hang 4", _session.UndoName);

        var patched = await Call("add_lamps", new JsonObject
        {
            ["type"] = "robe-ledbeam150", ["support"] = name, ["x"] = new JsonArray((x0 + x1) / 2),
            ["firstHead"] = 301, ["firstAddress"] = 401,
        });
        Assert.False(patched.Error, patched.Text);
        var one = JsonNode.Parse(patched.Text)!["lamps"]![0]!;
        Assert.Equal(301, (int)one["head"]);
        Assert.Equal(401, (int)one["address"]);

        Assert.True((await Call("add_lamps", new JsonObject { ["type"] = "no-such-lamp", ["support"] = name, ["count"] = 1 })).Error);
        var off = await Call("add_lamps", new JsonObject { ["type"] = "robe-ledbeam150", ["support"] = name, ["x"] = new JsonArray(99) });
        Assert.True(off.Error);
        Assert.Contains("is off", off.Text);
    }

    /// <summary>
    /// remove_lamps: refused while the head lights a cue (with the cues); a head dark in every cue goes, and its values
    /// leave the cues and palettes.
    /// </summary>
    [Fact]
    public async Task OnlyDarkLampsAreRemoved()
    {
        int dimmer = _dimmers[0], spot = _spots[0];
        var refused = await Call("remove_lamps", new JsonObject { ["heads"] = new JsonArray(dimmer) });
        Assert.True(refused.Error);
        Assert.Contains("lights cue 1", refused.Text);
        Assert.Contains(Design.Rig.Items, i => i.IsLamp && i.Head == dimmer);

        // The spot has values in a cue and a palette, but its intensity is 0 everywhere: it may go.
        await Call("set_palette_values", new JsonObject { ["name"] = "Centre", ["values"] = Values(spot, new JsonObject { ["Pan"] = 0 }) });
        await Call("set_cue_values", new JsonObject { ["cue"] = 2, ["values"] = Values(spot, new JsonObject { ["Pan"] = "@Centre", ["intensity"] = 0 }) });
        var removed = await Call("remove_lamps", new JsonObject { ["heads"] = new JsonArray(spot) });
        Assert.False(removed.Error, removed.Text);
        Assert.DoesNotContain(Design.Rig.Items, i => i.IsLamp && i.Head == spot);
        Assert.Null(Design.Head(spot));
        Assert.Null(Design.CueOrder[1].Of(spot));
        Assert.Null(Design.Palette("Centre").Of(spot));
        Assert.StartsWith("Remove lamp", _session.UndoName);

        // A dimmer set to 0 in the one cue that lit it is dark too.
        await Call("set_cue_values", new JsonObject { ["cue"] = 1, ["values"] = Values(dimmer, new JsonObject { ["intensity"] = 0 }) });
        Assert.False((await Call("remove_lamps", new JsonObject { ["heads"] = new JsonArray(dimmer) })).Error);
        Assert.Null(Design.Head(dimmer));
        Assert.DoesNotContain(dimmer, Design.Rig.Groups.First(g => g.Name == "Front").Heads);
    }

    /// <summary>find_gels / set_gel: a gel by code into the head's patch (every cue), one undo step; moving heads refused.</summary>
    [Fact]
    public async Task GelsAreSetPerHead()
    {
        var found = JsonNode.Parse((await Call("find_gels", new JsonObject { ["text"] = "119" })).Text)!.AsArray();
        Assert.Equal("L119", (string)found[0]!["code"]);
        var set = await Call("set_gel", new JsonObject { ["heads"] = new JsonArray(_dimmers[0]), ["gel"] = "L119" });
        Assert.False(set.Error, set.Text);
        Assert.Equal(1119, Design.Head(_dimmers[0]).Gel);
        Assert.StartsWith("Gel L119", _session.UndoName);
        var mover = await Call("set_gel", new JsonObject { ["heads"] = new JsonArray(_spots[0]), ["gel"] = "L119" });
        Assert.True(mover.Error);
        Assert.Contains("set_cue_values", mover.Text);
        Assert.True((await Call("set_gel", new JsonObject { ["heads"] = new JsonArray(_dimmers[0]), ["gel"] = "L9999" })).Error);
    }

    /// <summary>The <c>values</c> argument for one head: its number (as the key) and its channel values.</summary>
    private static JsonObject Values(int head, JsonObject channels) => new() { [head.ToString()] = channels };

    /// <summary>Calls a tool with arguments built as JSON (<see cref="Call(string, string)"/>).</summary>
    private Task<(string Text, bool Error, List<string> Images)> Call(string tool, JsonObject arguments) =>
        Call(tool, arguments.ToJsonString());

    /// <summary>Calls a tool: its texts, images, and whether it failed (the exception's message as the text).</summary>
    private async Task<(string Text, bool Error, List<string> Images)> Call(string tool, string arguments = "{}")
    {
        var reply = new Reply();
        try
        {
            using var json = JsonDocument.Parse(arguments);
            await _tools[tool].CallAsync(json.RootElement, reply);
            return (string.Join("\n", reply.Texts), false, reply.Images);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return (e.Message, true, reply.Images);
        }
    }

    /// <summary>A server that keeps what it is given.</summary>
    private sealed class Server : IMcpServer
    {
        /// <summary>The tools, by name.</summary>
        public Dictionary<string, IMcpTool> Tools { get; } = new();

        /// <summary>The names in the order added.</summary>
        public List<string> Order { get; } = new();

        /// <inheritdoc />
        public string Instructions { get; set; }

        /// <inheritdoc />
        public void Add(IMcpTool tool)
        {
            Tools[tool.Name] = tool;
            Order.Add(tool.Name);
        }
    }

    /// <summary>A reply that keeps texts and images (base64).</summary>
    private sealed class Reply : IMcpReply
    {
        /// <summary>The texts.</summary>
        public List<string> Texts { get; } = new();

        /// <summary>The images, base64.</summary>
        public List<string> Images { get; } = new();

        /// <inheritdoc />
        public void Text(string text) => Texts.Add(text);

        /// <inheritdoc />
        public void Image(byte[] data, string mimeType) => Images.Add(Convert.ToBase64String(data));
    }
}
