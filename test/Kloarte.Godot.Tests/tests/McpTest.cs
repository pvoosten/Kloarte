using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Kloarte.GodotBinding;

namespace Kloarte.Tests;

/// <summary>
/// The MCP server inside the app, end to end: runs the app's show scene on a copy of the Risquons-Tout show in
/// tests/output/mcp (never the user's files) and calls the server over real HTTP (initialize, tools/list, get_status,
/// list_cues, add_cue, set_cue_values, go_to_cue, set_look, screenshot of the 3D view and of the window, undo). The
/// requests are awaited, so the main thread keeps pumping the tool calls. Pictures in tests/output/mcp/*.png.
/// Prints "MCP OK" or "MCP FAIL: ...". The app in the test has a server only on an explicit port (Startup): the test
/// sets KLOARTE_MCP_PORT to a free port before the scene starts (never the app's 8462, where the user's Kloarte may
/// listen); <c>-- --mcp-port=N</c> names one.
/// Run: <c>Godot_console.exe --path test/Kloarte.Godot.Tests --resolution 1600x900 res://tests/mcp_test.tscn</c>
/// </summary>
public partial class McpTest : Node
{
    /// <summary>The output folder (absolute).</summary>
    private static string Out => ProjectSettings.GlobalizePath("res://tests/output/mcp");

    /// <summary>The client.</summary>
    private readonly System.Net.Http.HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>The endpoint.</summary>
    private string _url;

    /// <summary>The next JSON-RPC id.</summary>
    private int _id;

    /// <summary>Frames so far.</summary>
    private int _frame;

    /// <summary>The show scene.</summary>
    private Node _scene;

    /// <summary>Loads the show scene on a copy of the show.</summary>
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");   // not the user's bindings
        ViewHost.Persist = false;
        GoboLibrary.Folder = "res://tests/output/gobos";
        FixturePanel.PresentFullScreen = false;
        if (Directory.Exists(Out))
            Directory.Delete(Out, true);
        // The app reads its server's port when its UI starts (Startup): a free one, unless the command line names one.
        string port = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--mcp-port="))?["--mcp-port=".Length..]
                      ?? FreePort().ToString();
        System.Environment.SetEnvironmentVariable("KLOARTE_MCP_PORT", port);
        _url = $"http://localhost:{port}/mcp";
        var scene = TestShow.Load("mcp");   // exports and new shows go to its folder, never to MagicQ's
        AddChild(scene);
        _scene = scene;
    }

    /// <summary>A TCP port that is free on this machine now.</summary>
    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Starts the client once the app is up.</summary>
    public override void _Process(double delta)
    {
        if (++_frame == 30)
            _ = Run();
    }

    /// <summary>The calls and checks.</summary>
    private async Task Run()
    {
        try
        {
            var init = await Rpc("initialize", new JsonObject
            {
                ["protocolVersion"] = "2025-06-18",
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "mcp-test", ["version"] = "1" },
            });
            Check(init["serverInfo"]?["name"]?.GetValue<string>() == "kloarte", "initialize: serverInfo.name kloarte");
            Check(init["protocolVersion"]?.GetValue<string>() == "2025-06-18", "initialize: protocol version echoed");
            await Notify("notifications/initialized");

            var tools = (await Rpc("tools/list", new JsonObject()))["tools"]!.AsArray();
            var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
            GD.Print($"MCP tools ({names.Count}): {string.Join(", ", names)}");
            foreach (string need in new[] { "get_status", "list_cues", "add_cue", "set_cue_values", "go_to_cue", "set_look", "screenshot", "undo" })
                Check(names.Contains(need), $"tools/list has {need}");

            var status = Json(await Tool("get_status"));
            GD.Print($"MCP status: {status.ToJsonString()}");
            Check(status["native"]?.GetValue<bool>() == true, "get_status: a native show");
            Check(status["views"]!.AsArray().Any(v => v!.GetValue<string>() == "3D"), "get_status: a view '3D'");

            var cues = Json(await Tool("list_cues")).AsArray();
            int before = cues.Count;
            Check(before > 0, "list_cues: cues");

            string added = Text(await Tool("add_cue", new JsonObject { ["cue"] = "999", ["text"] = "MCP test", ["fadeIn"] = 2 }));
            GD.Print($"MCP add_cue: {added}");
            cues = Json(await Tool("list_cues")).AsArray();
            Check(cues.Count == before + 1, $"add_cue: {before} -> {cues.Count} cues");
            var last = cues.FirstOrDefault(c => c!["cue"]!.GetValue<string>() == "999");
            Check(last?["current"]?.GetValue<bool>() == true, "add_cue: the new cue is current");

            // The first head with an intensity channel.
            var heads = Json(await Tool("list_heads"))["heads"]!.AsArray();
            int head = -1;
            foreach (var h in heads.Take(20))
            {
                int number = h!["head"]!.GetValue<int>();
                string channels = Text(await Tool("get_head", new JsonObject { ["head"] = number }));
                if (channels.Contains("\"intensity\"", StringComparison.OrdinalIgnoreCase))
                {
                    head = number;
                    break;
                }
            }
            Check(head > 0, "a head with an intensity channel");
            string set = Text(await Tool("set_cue_values", new JsonObject
            {
                ["cue"] = "999",
                ["values"] = new JsonObject { [head.ToString()] = new JsonObject { ["intensity"] = 100 } },
            }));
            GD.Print($"MCP set_cue_values: {set}");
            var cue = Json(await Tool("get_cue", new JsonObject { ["cue"] = "999" }));
            string value = cue["values"]?[head.ToString()]?["intensity"]?.GetValue<string>();
            Check(value != null && value.StartsWith("100"), $"get_cue: head {head} intensity 100 (got {value})");

            GD.Print($"MCP go_to_cue: {Text(await Tool("go_to_cue", new JsonObject { ["cue"] = "999" }))}");
            status = Json(await Tool("get_status"));
            Check(status["currentCue"]?["cue"]?.GetValue<string>() == "999", "go_to_cue: cue 999 current");

            string look = Text(await Tool("set_look", new JsonObject { ["haze"] = 40 }));
            GD.Print($"MCP set_look: {look}");
            status = Json(await Tool("get_status"));
            Check(Math.Abs(status["hazePercent"]!.GetValue<double>() - 40) < 0.6, $"set_look: haze 40 % (got {status["hazePercent"]})");

            await Picture("3D", "mcp_3d");
            await Picture(null, "mcp_window");
            // A small card (or a hidden view) is rendered at a picture's size (user, 25 Oct 2026: a few dark pixels).
            await Picture("Audience", "mcp_audience", minWidth: 1280);

            string undone = Text(await Tool("undo"));
            GD.Print($"MCP undo: {undone}");
            Check(undone.StartsWith("Undone"), "undo: something undone");
            cue = Json(await Tool("get_cue", new JsonObject { ["cue"] = "999" }));
            value = cue["values"]?[head.ToString()]?["intensity"]?.GetValue<string>();
            Check(value == null, $"undo: the value in cue 999 is gone (got {value})");

            // Lamps hung on a truss through the app's rig commands (the scene shows them), and taken down again (dark).
            var supports = Json(await Tool("list_supports")).AsArray();
            var truss = supports.First(s => s!["stand"] == null && s!["length"]!.GetValue<double>() > 4)!;
            var builder = _scene.GetNode<ShowBuilder>("Show");
            int lampsBefore = builder.Document.RigItems.Count(i => i.IsFixture);
            var hung = Json(await Tool("add_lamps", new JsonObject
            {
                ["type"] = "robe-ledbeam150", ["support"] = truss["name"]!.GetValue<string>(), ["count"] = 2,
            }));
            var newLamps = hung["lamps"]!.AsArray().Select(l => l!["lamp"]!.GetValue<int>()).ToList();
            GD.Print($"MCP add_lamps: {hung["done"]} ({string.Join(", ", newLamps)})");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(newLamps.Count == 2 && newLamps.All(id => builder.FixtureOf(id) != null), "add_lamps: 2 lamps on stage");
            string removed = Text(await Tool("remove_lamps", new JsonObject { ["lamps"] = new JsonArray(newLamps.Select(id => (JsonNode)id).ToArray()) }));
            GD.Print($"MCP remove_lamps: {removed}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(builder.Document.RigItems.Count(i => i.IsFixture) == lampsBefore && newLamps.All(id => builder.FixtureOf(id) == null),
                "remove_lamps: the lamps are gone");

            // A tool error is a result with isError, not a JSON-RPC error.
            var bad = await Rpc("tools/call", new JsonObject { ["name"] = "go_to_cue", ["arguments"] = new JsonObject { ["cue"] = "12345" } });
            Check(bad["isError"]?.GetValue<bool>() == true, "a wrong cue is an error result");

            Finish(null);
        }
        catch (Exception e)
        {
            Finish(e.Message);
        }
    }

    /// <summary>A screenshot of a view (null: the window), saved as tests/output/mcp/`name`.png, at least `minWidth` wide.</summary>
    private async Task Picture(string view, string name, int minWidth = 0)
    {
        var args = new JsonObject { ["maxWidth"] = 0 };
        if (view != null)
            args["view"] = view;
        var result = await Rpc("tools/call", new JsonObject { ["name"] = "screenshot", ["arguments"] = args });
        Check(result["isError"]?.GetValue<bool>() == false, $"screenshot {view ?? "window"}: no error ({result.ToJsonString()[..Math.Min(300, result.ToJsonString().Length)]})");
        var image = result["content"]!.AsArray().FirstOrDefault(c => c!["type"]!.GetValue<string>() == "image");
        if (image == null)
            throw new InvalidOperationException($"screenshot {view ?? "window"}: no image");
        byte[] png = Convert.FromBase64String(image["data"]!.GetValue<string>());
        File.WriteAllBytes(Path.Combine(Out, name + ".png"), png);
        string caption = result["content"]!.AsArray().FirstOrDefault(c => c!["type"]!.GetValue<string>() == "text")?["text"]?.GetValue<string>();
        GD.Print($"MCP screenshot {view ?? "window"}: {png.Length} bytes, '{caption}'");
        Check(png.Length > 1000 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "screenshot: a PNG");
        int width = png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19];   // IHDR width, big-endian
        Check(width >= minWidth, $"screenshot {view ?? "window"}: {width} px wide (at least {minWidth})");
    }

    /// <summary>Calls a tool; its result (throws on an error result).</summary>
    private async Task<JsonNode> Tool(string name, JsonObject arguments = null)
    {
        var result = await Rpc("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments ?? new JsonObject() });
        if (result["isError"]?.GetValue<bool>() == true)
            throw new InvalidOperationException($"{name}: {Text(result)}");
        return result;
    }

    /// <summary>The first text of a tool result.</summary>
    private static string Text(JsonNode result) =>
        result["content"]!.AsArray().First(c => c!["type"]!.GetValue<string>() == "text")!["text"]!.GetValue<string>();

    /// <summary>A tool result's text as JSON.</summary>
    private static JsonNode Json(JsonNode result) => JsonNode.Parse(Text(result));

    /// <summary>A JSON-RPC request over HTTP; its result (throws on a JSON-RPC error).</summary>
    private async Task<JsonNode> Rpc(string method, JsonObject parameters)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = method, ["params"] = parameters };
        var response = await Post(message);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{method}: HTTP {(int)response.StatusCode} {body}");
        var answer = JsonNode.Parse(body)!;
        if (answer["id"]?.GetValue<int>() != _id)
            throw new InvalidOperationException($"{method}: answer for id {answer["id"]}, not {_id}");
        if (answer["error"] is { } error)
            throw new InvalidOperationException($"{method}: JSON-RPC error {error.ToJsonString()}");
        return answer["result"]!;
    }

    /// <summary>A JSON-RPC notification (the server answers 202 without a body).</summary>
    private async Task Notify(string method)
    {
        var response = await Post(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method });
        Check((int)response.StatusCode == 202, $"{method}: HTTP 202 (got {(int)response.StatusCode})");
    }

    /// <summary>POSTs a message as a client does (JSON, accepting JSON and event streams).</summary>
    private Task<HttpResponseMessage> Post(JsonObject message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        return _http.SendAsync(request);
    }

    /// <summary>Throws on a failed check (the first failure ends the test).</summary>
    private static void Check(bool ok, string what)
    {
        if (!ok)
            throw new InvalidOperationException(what);
    }

    /// <summary>Prints the marker and quits.</summary>
    private void Finish(string failure)
    {
        GD.Print(failure == null ? "MCP OK" : $"MCP FAIL: {failure}");
        GetTree().Quit(failure == null ? 0 : 1);
    }
}
