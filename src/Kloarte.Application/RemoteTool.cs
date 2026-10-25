using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Kloarte.Mcp;

namespace Kloarte.Application;

/// <summary>One tool of <see cref="RemoteTools"/>: what the MCP server lists, and the call.</summary>
internal sealed class RemoteTool : IMcpTool
{
    /// <summary>The call.</summary>
    private readonly Func<ToolArgs, IMcpReply, Task> _run;

    /// <summary>A tool of these parts.</summary>
    public RemoteTool(string name, string title, string description, JsonObject schema, bool readOnly,
        Func<ToolArgs, IMcpReply, Task> run)
    {
        Name = name;
        Title = title;
        Description = description;
        InputSchema = schema.ToJsonString();
        ReadOnly = readOnly;
        _run = run;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Title { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public string InputSchema { get; }

    /// <inheritdoc />
    public bool ReadOnly { get; }

    /// <inheritdoc />
    public Task CallAsync(JsonElement arguments, IMcpReply reply) => _run(new ToolArgs(arguments), reply);
}
