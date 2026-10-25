using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Kloarte.Mcp;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// The MCP server over HTTP on this PC: the handshake, the tool list, tool calls run only when pumped (on the pumping
/// thread), a tool's exception as an error result, JSON-RPC errors, notifications, and requests it refuses.
/// </summary>
public sealed class McpServerTests : IDisposable
{
    /// <summary>The server, on a free port.</summary>
    private readonly McpServer _server = new(FreePort(), "kloarte", "Kloarte test", "1.0");

    /// <summary>The client.</summary>
    private readonly HttpClient _http = new();

    /// <summary>The thread that pumped last.</summary>
    private int _pumpThread = -1;

    /// <summary>The thread the echo tool ran on (-1: not run).</summary>
    private int _ranOn = -1;

    /// <summary>Released by a test to let the slow tool finish.</summary>
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>True once the slow tool has started.</summary>
    private volatile bool _slowStarted;

    /// <summary>A server with an echo tool, a failing tool and a slow (awaiting) tool, listening.</summary>
    public McpServerTests()
    {
        _server.Instructions = "Test instructions";
        _server.Add(new Tool("echo", false, (args, reply) =>
        {
            _ranOn = Environment.CurrentManagedThreadId;
            reply.Text("echo " + args.GetProperty("text").GetString());
            reply.Image(new byte[] { 1, 2, 3 }, "image/png");
        }));
        _server.Add(new Tool("fail", true, (_, _) => throw new ArgumentException("no such cue")));
        _server.Add(new Tool("slow", true, async (_, reply) =>
        {
            _slowStarted = true;
            await _gate.Task;
            reply.Text("slow done");
        }));
        _server.Start();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _server.Dispose();
        _http.Dispose();
    }

    /// <summary>initialize answers with the asked version, the tools capability and the instructions; tools/list lists both tools.</summary>
    [Fact]
    public async Task HandshakeAndToolList()
    {
        var init = await Rpc("initialize", new JsonObject { ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JsonObject() });
        Assert.Equal("2025-06-18", (string)init["result"]!["protocolVersion"]);
        Assert.NotNull(init["result"]!["capabilities"]!["tools"]);
        Assert.Equal("Test instructions", (string)init["result"]!["instructions"]);
        var unknown = await Rpc("initialize", new JsonObject { ["protocolVersion"] = "1999-01-01" });
        Assert.Equal(McpServer.LatestVersion, (string)unknown["result"]!["protocolVersion"]);

        Assert.Equal(HttpStatusCode.Accepted, (await Post("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")).StatusCode);

        var tools = (await Rpc("tools/list"))["result"]!["tools"]!.AsArray();
        Assert.Equal(3, tools.Count);
        Assert.Equal("echo", (string)tools[0]!["name"]);
        Assert.Equal("object", (string)tools[0]!["inputSchema"]!["type"]);
        Assert.True((bool)tools[1]!["annotations"]!["readOnlyHint"]);
        Assert.NotNull((await Rpc("ping"))["result"]);
    }

    /// <summary>A tool call waits for Pump and runs on the pumping thread; its texts and images come back in order.</summary>
    [Fact]
    public async Task ToolCallsRunWhenPumped()
    {
        var call = Rpc("tools/call", new JsonObject { ["name"] = "echo", ["arguments"] = new JsonObject { ["text"] = "hi" } });
        await Task.Delay(200);
        Assert.False(call.IsCompleted);   // not pumped yet
        var result = (await Pumped(call))["result"]!;
        Assert.Equal(_pumpThread, _ranOn);
        Assert.False((bool)result["isError"]);
        Assert.Equal("echo hi", (string)result["content"]![0]!["text"]);
        Assert.Equal("image", (string)result["content"]![1]!["type"]);
        Assert.Equal("AQID", (string)result["content"]![1]!["data"]);
    }

    /// <summary>A tool's exception is an error result with its message; an unknown tool or method is a JSON-RPC error.</summary>
    [Fact]
    public async Task ErrorsAreReported()
    {
        var failed = (await Pumped(Rpc("tools/call", new JsonObject { ["name"] = "fail" })))["result"]!;
        Assert.True((bool)failed["isError"]);
        Assert.Equal("no such cue", (string)failed["content"]![0]!["text"]);

        Assert.Equal(-32602, (int)(await Rpc("tools/call", new JsonObject { ["name"] = "nothing" }))["error"]!["code"]);
        Assert.Equal(-32601, (int)(await Rpc("resources/list"))["error"]!["code"]);
        var badJson = await Post("{not json");
        Assert.Equal(HttpStatusCode.BadRequest, badJson.StatusCode);
        var parse = JsonNode.Parse(await badJson.Content.ReadAsStringAsync())!;
        Assert.Equal(-32700, (int)parse["error"]!["code"]);
        var batch = await Post("[]");
        Assert.Equal(HttpStatusCode.BadRequest, batch.StatusCode);
        Assert.Equal(-32600, (int)JsonNode.Parse(await batch.Content.ReadAsStringAsync())!["error"]!["code"]);
    }

    /// <summary>A call that is never pumped ends with an error after the timeout.</summary>
    [Fact]
    public async Task UnpumpedCallTimesOut()
    {
        _server.CallTimeout = TimeSpan.FromMilliseconds(300);
        var result = (await Rpc("tools/call", new JsonObject { ["name"] = "echo", ["arguments"] = new JsonObject { ["text"] = "x" } }))["result"]!;
        Assert.True((bool)result["isError"]);
        _server.Pump();
        Assert.Equal(-1, _ranOn);   // a timed-out call is not run later
    }

    /// <summary>A call queued behind an awaiting call doesn't start until that one has finished.</summary>
    [Fact]
    public async Task CallsRunOneAtATime()
    {
        var slow = Rpc("tools/call", new JsonObject { ["name"] = "slow" });
        PumpUntil(() => _slowStarted);
        Assert.True(_slowStarted);
        var echo = Rpc("tools/call", new JsonObject { ["name"] = "echo", ["arguments"] = new JsonObject { ["text"] = "next" } });
        PumpFor(TimeSpan.FromMilliseconds(400));
        Assert.Equal(-1, _ranOn);   // still waiting behind the slow call
        Assert.False(echo.IsCompleted);
        _gate.SetResult();
        var echoed = (await Pumped(echo))["result"]!;
        Assert.Equal("echo next", (string)echoed["content"]![0]!["text"]);
        Assert.Equal("slow done", (string)(await slow)["result"]!["content"]![0]!["text"]);
    }

    /// <summary>A call that has started is waited for past the timeout and returns its result.</summary>
    [Fact]
    public async Task StartedCallOutlivesTimeout()
    {
        _server.CallTimeout = TimeSpan.FromMilliseconds(300);
        var slow = Rpc("tools/call", new JsonObject { ["name"] = "slow" });
        PumpUntil(() => _slowStarted);
        Assert.True(_slowStarted);
        PumpFor(TimeSpan.FromMilliseconds(800));
        Assert.False(slow.IsCompleted);
        _gate.SetResult();
        var result = (await Pumped(slow))["result"]!;
        Assert.False((bool)result["isError"]);
        Assert.Equal("slow done", (string)result["content"]![0]!["text"]);
    }

    /// <summary>After Dispose a call is answered with an error at once (Handle without HTTP).</summary>
    [Fact]
    public async Task CallAfterDisposeIsAnError()
    {
        _server.Dispose();
        using var message = JsonDocument.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","arguments":{"text":"x"}}}""");
        var answer = await _server.Handle(message.RootElement).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((bool)answer["result"]!["isError"]);
    }

    /// <summary>
    /// An unknown MCP-Protocol-Version is refused (400) except for initialize and server/discover; server/discover is a
    /// 200 with -32601 (a client then falls back to initialize); a known or missing version is fine.
    /// </summary>
    [Fact]
    public async Task ProtocolVersionHeader()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await PostVersion("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", "1999-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostVersion("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", "2025-06-18")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostVersion("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", "2026-07-28")).StatusCode);
        var discover = await PostVersion("""{"jsonrpc":"2.0","id":1,"method":"server/discover"}""", "2026-07-28");
        Assert.Equal(HttpStatusCode.OK, discover.StatusCode);
        Assert.Equal(-32601, (int)JsonNode.Parse(await discover.Content.ReadAsStringAsync())!["error"]!["code"]);
    }

    /// <summary>POSTs a body with an MCP-Protocol-Version header.</summary>
    private Task<HttpResponseMessage> PostVersion(string body, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _server.Url) { Content = Json(body) };
        request.Headers.Add("MCP-Protocol-Version", version);
        return _http.SendAsync(request);
    }

    /// <summary>Only POST to /mcp, and no requests from a web page of another site.</summary>
    [Fact]
    public async Task OtherRequestsAreRefused()
    {
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _http.GetAsync(_server.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.PostAsync($"http://localhost:{_server.Port}/other", Json("{}"))).StatusCode);
        var foreign = new HttpRequestMessage(HttpMethod.Post, _server.Url) { Content = Json("""{"jsonrpc":"2.0","id":1,"method":"ping"}""") };
        foreign.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(foreign)).StatusCode);
        var local = new HttpRequestMessage(HttpMethod.Post, _server.Url) { Content = Json("""{"jsonrpc":"2.0","id":1,"method":"ping"}""") };
        local.Headers.Add("Origin", "http://localhost:3000");
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(local)).StatusCode);
    }

    /// <summary>Pumps on this thread (blocking it, as a frame loop does) until the request is answered.</summary>
    private Task<JsonNode> Pumped(Task<JsonNode> call)
    {
        _pumpThread = Environment.CurrentManagedThreadId;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!call.IsCompleted && DateTime.UtcNow < deadline)
        {
            _server.Pump();
            System.Threading.Thread.Sleep(10);
        }
        return call;
    }

    /// <summary>Pumps on this thread until `done` holds (at most 10 s).</summary>
    private void PumpUntil(Func<bool> done)
    {
        _pumpThread = Environment.CurrentManagedThreadId;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            _server.Pump();
            System.Threading.Thread.Sleep(10);
        }
    }

    /// <summary>Pumps on this thread for a while.</summary>
    private void PumpFor(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        PumpUntil(() => DateTime.UtcNow >= until);
    }

    /// <summary>A JSON-RPC request and its answer.</summary>
    private async Task<JsonNode> Rpc(string method, JsonObject parameters = null)
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 7, ["method"] = method };
        if (parameters != null)
            request["params"] = parameters;
        var response = await Post(request.ToJsonString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(7, (int)answer["id"]);
        return answer;
    }

    /// <summary>POSTs a body to the endpoint.</summary>
    private Task<HttpResponseMessage> Post(string body) => _http.PostAsync(_server.Url, Json(body));

    /// <summary>A JSON body.</summary>
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    /// <summary>A port nothing listens on now.</summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>A tool of a name and a call.</summary>
    private sealed class Tool : IMcpTool
    {
        /// <summary>The call.</summary>
        private readonly Func<JsonElement, IMcpReply, Task> _run;

        /// <summary>A tool that runs `run`.</summary>
        public Tool(string name, bool readOnly, Action<JsonElement, IMcpReply> run)
            : this(name, readOnly, (arguments, reply) =>
            {
                run(arguments, reply);
                return Task.CompletedTask;
            })
        {
        }

        /// <summary>A tool that runs `run` and awaits it.</summary>
        public Tool(string name, bool readOnly, Func<JsonElement, IMcpReply, Task> run)
        {
            Name = name;
            ReadOnly = readOnly;
            _run = run;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public string Title => Name;

        /// <inheritdoc />
        public string Description => "A test tool";

        /// <inheritdoc />
        public string InputSchema => """{"type":"object","properties":{"text":{"type":"string"}}}""";

        /// <inheritdoc />
        public bool ReadOnly { get; }

        /// <inheritdoc />
        public Task CallAsync(JsonElement arguments, IMcpReply reply) => _run(arguments, reply);
    }
}
