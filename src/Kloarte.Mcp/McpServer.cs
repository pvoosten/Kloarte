using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Kloarte.Mcp;

/// <summary>
/// An MCP server (Model Context Protocol, Streamable HTTP transport) for clients on this PC only: JSON-RPC messages are
/// POSTed to <see cref="Url"/> and every request gets one JSON response (no SSE streams, no sessions: the spec allows
/// both). It listens on a background thread; tool calls are queued and run, one at a time, on the thread that calls
/// <see cref="Pump"/> (the host: Godot's main thread, where the scene may be changed). It binds to localhost only and
/// refuses requests from a web page of another site (an Origin header that is not localhost: DNS rebinding).
/// </summary>
public sealed class McpServer : IMcpServer, IDisposable
{
    /// <summary>The port when none is chosen.</summary>
    public const int DefaultPort = 8462;

    /// <summary>The newest protocol version the server speaks (the answer to a version it doesn't know).</summary>
    internal const string LatestVersion = "2025-11-25";

    /// <summary>The protocol versions the server speaks (it uses nothing that differs between them).</summary>
    private static readonly string[] Versions = { LatestVersion, "2025-06-18", "2025-03-26", "2024-11-05" };

    /// <summary>The path of the endpoint.</summary>
    private const string EndpointPath = "/mcp";

    /// <summary>The listener.</summary>
    private readonly HttpListener _listener = new();

    /// <summary>The tools by name (read on the listener's threads, written by <see cref="Add"/>).</summary>
    private readonly ConcurrentDictionary<string, IMcpTool> _tools = new();

    /// <summary>The order the tools were added in (tools/list keeps it).</summary>
    private readonly List<string> _order = new();

    /// <summary>Tool calls waiting for <see cref="Pump"/>.</summary>
    private readonly ConcurrentQueue<PendingCall> _calls = new();

    /// <summary>The server's name and version (serverInfo).</summary>
    private readonly string _name, _title, _version;

    /// <summary>The loop accepting requests, once started.</summary>
    private Task _loop;

    /// <summary>The call <see cref="Pump"/> started last (null: none yet); guarded by <see cref="_calls"/>.</summary>
    private PendingCall _current;

    /// <summary>The run of <see cref="_current"/>; the next call starts only when it has completed.</summary>
    private Task _running;

    /// <summary>True once disposed: calls are answered with an error at once.</summary>
    private volatile bool _disposed;

    /// <summary>A server for `port` (0: <see cref="DefaultPort"/>), not listening yet (<see cref="Start"/>).</summary>
    public McpServer(int port, string name, string title, string version)
    {
        Port = port > 0 ? port : DefaultPort;
        _name = name;
        _title = title;
        _version = version;
        _listener.Prefixes.Add($"http://localhost:{Port}/");
    }

    /// <summary>The port.</summary>
    public int Port { get; }

    /// <summary>The endpoint's address (what a client's configuration names).</summary>
    public string Url => $"http://localhost:{Port}{EndpointPath}";

    /// <summary>
    /// How long a request waits for its tool call to be started by <see cref="Pump"/>; a call that has started is
    /// waited for until it finishes.
    /// </summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>True while the server listens.</summary>
    public bool Listening => _listener.IsListening;

    /// <summary>Requests answered (diagnostics: the status bar).</summary>
    public int Requests => _requests;

    /// <summary>Count behind <see cref="Requests"/>.</summary>
    private int _requests;

    /// <inheritdoc />
    public string Instructions { get; set; }

    /// <inheritdoc />
    public void Add(IMcpTool tool)
    {
        if (tool == null)
            throw new ArgumentNullException(nameof(tool));
        lock (_order)
        {
            if (!_tools.ContainsKey(tool.Name))
                _order.Add(tool.Name);
            _tools[tool.Name] = tool;
        }
    }

    /// <summary>Starts listening. Throws <see cref="HttpListenerException"/> when the port is taken.</summary>
    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(Listen);
    }

    /// <summary>
    /// Starts the next queued tool call on this thread (call it every frame on the thread that may change the app): at
    /// most one per pump, and only when the previous call has completed, so calls never interleave. A call that awaits
    /// finishes in later frames (Godot's main thread resumes it).
    /// </summary>
    public void Pump()
    {
        if (_disposed || (_running != null && !_running.IsCompleted))
            return;
        while (_calls.TryDequeue(out var call))
        {
            if (!call.TryStart())
                continue;   // timed out before it started
            lock (_calls)
                _current = call;
            _running = Run(call);
            return;
        }
    }

    /// <summary>Stops listening; waiting requests are answered with an error.</summary>
    public void Dispose()
    {
        _disposed = true;
        if (_listener.IsListening)
            _listener.Stop();
        _listener.Close();
        while (_calls.TryDequeue(out var call))
            call.Done.TrySetResult(McpReply.Error(ClosingMessage));
        lock (_calls)
            _current?.Done.TrySetResult(McpReply.Error(ClosingMessage));
    }

    /// <summary>The error of a call that is cut off or refused because the server is disposed.</summary>
    private const string ClosingMessage = "Kloarte is closing";

    /// <summary>Accepts requests until the listener stops.</summary>
    private async Task Listen()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException)
            {
                continue;
            }
            _ = Task.Run(() => Serve(context));
        }
    }

    /// <summary>Answers one HTTP request.</summary>
    private async Task Serve(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            var request = context.Request;
            string path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (!IsLocalOrigin(request.Headers["Origin"]))
            {
                Plain(response, 403, "Requests from other sites are refused");
                return;
            }
            if (!path.Equals(EndpointPath, StringComparison.OrdinalIgnoreCase))
            {
                Plain(response, 404, $"The MCP endpoint is {EndpointPath}");
                return;
            }
            if (request.HttpMethod != "POST")
            {
                // No server-sent events (GET) and no sessions to end (DELETE).
                response.AddHeader("Allow", "POST");
                Plain(response, 405, "POST JSON-RPC messages");
                return;
            }
            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            Interlocked.Increment(ref _requests);
            JsonNode answer;
            try
            {
                using var message = JsonDocument.Parse(body);
                string version = request.Headers["MCP-Protocol-Version"];
                if (!string.IsNullOrEmpty(version) && !Versions.Contains(version) && !IsVersionFree(message.RootElement))
                {
                    Plain(response, 400, $"Unsupported MCP-Protocol-Version {version}");
                    return;
                }
                answer = await Handle(message.RootElement).ConfigureAwait(false);
            }
            catch (JsonException e)
            {
                answer = Error(null, -32700, "Parse error: " + e.Message);
            }
            if (answer == null)
            {
                response.StatusCode = 202;   // a notification or a response: accepted, no body
                response.Close();
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(answer.ToJsonString());
            response.StatusCode = IsBadRequest(answer) ? 400 : 200;
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            response.Close();
        }
        catch (Exception e)
        {
            try
            {
                Plain(response, 500, e.Message);
            }
            catch (Exception)
            {
                // The client is gone.
            }
        }
    }

    /// <summary>True for a message whose method may come with any protocol version (initialize, server/discover).</summary>
    private static bool IsVersionFree(JsonElement message) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty("method", out var m) &&
        m.ValueKind == JsonValueKind.String && m.GetString() is "initialize" or "server/discover";

    /// <summary>
    /// True for a parse error or an invalid request without an id (HTTP 400). Errors with an id stay 200: a client's
    /// probe takes a 200 with -32601 for server/discover as "fall back to initialize".
    /// </summary>
    private static bool IsBadRequest(JsonNode answer) =>
        answer["id"] == null && answer["error"]?["code"] is JsonValue code &&
        code.GetValue<int>() is -32700 or -32600;

    /// <summary>The answer to one JSON-RPC message; null for a notification (or a client's response).</summary>
    internal async Task<JsonNode> Handle(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return Error(null, -32600, "Invalid request: send one JSON-RPC object (batches are not supported)");
        bool hasId = message.TryGetProperty("id", out var idElement);
        JsonNode id = hasId ? JsonNode.Parse(idElement.GetRawText()) : null;
        if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            return hasId && !message.TryGetProperty("result", out _) && !message.TryGetProperty("error", out _)
                ? Error(id, -32600, "Invalid request: no method")
                : null;
        string method = methodElement.GetString();
        if (!hasId)
            return null;   // notifications/initialized, notifications/cancelled, ...
        message.TryGetProperty("params", out var parameters);
        switch (method)
        {
            case "initialize":
                return Result(id, Initialize(parameters));
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = ToolList() });
            case "tools/call":
                if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String)
                    return Error(id, -32602, "tools/call needs the tool's name");
                if (!_tools.TryGetValue(name.GetString(), out var tool))
                    return Error(id, -32602, $"Unknown tool: {name.GetString()}");
                var arguments = parameters.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
                    ? a.Clone()
                    : JsonDocument.Parse("{}").RootElement.Clone();
                return Result(id, await Call(tool, arguments).ConfigureAwait(false));
            default:
                return Error(id, -32601, $"Method not found: {method}");
        }
    }

    /// <summary>The answer to initialize: the version, the capabilities (tools), who the server is.</summary>
    private JsonObject Initialize(JsonElement parameters)
    {
        string asked = parameters.ValueKind == JsonValueKind.Object &&
                       parameters.TryGetProperty("protocolVersion", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
        var result = new JsonObject
        {
            ["protocolVersion"] = Versions.Contains(asked) ? asked : LatestVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = _name, ["title"] = _title, ["version"] = _version },
        };
        if (!string.IsNullOrEmpty(Instructions))
            result["instructions"] = Instructions;
        return result;
    }

    /// <summary>The tools as tools/list gives them.</summary>
    private JsonArray ToolList()
    {
        var list = new JsonArray();
        List<string> order;
        lock (_order)
            order = _order.ToList();
        foreach (string name in order)
        {
            var tool = _tools[name];
            list.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["title"] = tool.Title,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema),
                ["annotations"] = new JsonObject
                {
                    ["title"] = tool.Title,
                    ["readOnlyHint"] = tool.ReadOnly,
                    ["destructiveHint"] = false,   // edits can be undone in Kloarte (and nothing is saved until the client saves)
                    ["openWorldHint"] = false,
                },
            });
        }
        return list;
    }

    /// <summary>
    /// Queues a tool call for <see cref="Pump"/> and waits for its result. A call not started within
    /// <see cref="CallTimeout"/> is an error (and never runs); a started call is waited for until it finishes.
    /// </summary>
    private async Task<JsonObject> Call(IMcpTool tool, JsonElement arguments)
    {
        if (_disposed)
            return McpReply.Error(ClosingMessage);
        var call = new PendingCall(tool, arguments);
        _calls.Enqueue(call);
        if (_disposed)
            call.Done.TrySetResult(McpReply.Error(ClosingMessage));   // Dispose may have drained the queue already
        var finished = await Task.WhenAny(call.Done.Task, Task.Delay(CallTimeout)).ConfigureAwait(false);
        if (finished == call.Done.Task || !call.TrySkip())
            return await call.Done.Task.ConfigureAwait(false);
        return McpReply.Error($"Kloarte did not start {tool.Name} within {CallTimeout.TotalSeconds:0} s " +
                              "(busy, or a dialog is open?)");
    }

    /// <summary>Runs a call on the pumping thread; its result (or its exception as an error) completes it.</summary>
    private static async Task Run(PendingCall call)
    {
        var reply = new McpReply();
        try
        {
            await call.Tool.CallAsync(call.Arguments, reply);
            call.Done.TrySetResult(reply.ToResult(false));
        }
        catch (Exception e)
        {
            call.Done.TrySetResult(McpReply.Error(e.Message));
        }
    }

    /// <summary>True for no origin (not a browser) or a page on this PC.</summary>
    internal static bool IsLocalOrigin(string origin)
    {
        if (string.IsNullOrEmpty(origin))
            return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
               (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A JSON-RPC result.</summary>
    private static JsonObject Result(JsonNode id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    /// <summary>A JSON-RPC error.</summary>
    private static JsonObject Error(JsonNode id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    /// <summary>Answers with a status and a plain text.</summary>
    private static void Plain(HttpListenerResponse response, int status, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.Close();
    }

    /// <summary>A tool call waiting for <see cref="Pump"/>, and its result.</summary>
    private sealed class PendingCall
    {
        /// <summary>A call of `tool` with `arguments` (a copy that outlives the request's document).</summary>
        public PendingCall(IMcpTool tool, JsonElement arguments)
        {
            Tool = tool;
            Arguments = arguments;
        }

        /// <summary>The tool.</summary>
        public IMcpTool Tool { get; }

        /// <summary>Its arguments.</summary>
        public JsonElement Arguments { get; }

        /// <summary>Queued (0), started by <see cref="Pump"/> (1) or skipped after the timeout (2).</summary>
        private int _state;

        /// <summary>Marks the call started; false when it was skipped (timed out) already.</summary>
        public bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        /// <summary>Marks the call skipped (a later <see cref="Pump"/> won't run it); false when it has started already.</summary>
        public bool TrySkip() => Interlocked.CompareExchange(ref _state, 2, 0) == 0;

        /// <summary>The result (continuations don't run on the pumping thread).</summary>
        public TaskCompletionSource<JsonObject> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
