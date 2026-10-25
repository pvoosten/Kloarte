using System;
using System.Text.Json.Nodes;

namespace Kloarte.Mcp;

/// <summary>A tool call's result as MCP sends it (<c>CallToolResult</c>): its content blocks and whether it is an error.</summary>
internal sealed class McpReply : IMcpReply
{
    /// <summary>The content blocks so far.</summary>
    private readonly JsonArray _content = new();

    /// <inheritdoc />
    public void Text(string text) => _content.Add(new JsonObject { ["type"] = "text", ["text"] = text ?? "" });

    /// <inheritdoc />
    public void Image(byte[] data, string mimeType) => _content.Add(new JsonObject
    {
        ["type"] = "image",
        ["data"] = Convert.ToBase64String(data ?? Array.Empty<byte>()),
        ["mimeType"] = mimeType,
    });

    /// <summary>The result object (an empty result gets an empty text: clients expect content).</summary>
    public JsonObject ToResult(bool isError)
    {
        if (_content.Count == 0)
            Text("");
        return new JsonObject { ["content"] = _content, ["isError"] = isError };
    }

    /// <summary>An error result with one text.</summary>
    public static JsonObject Error(string message)
    {
        var reply = new McpReply();
        reply.Text(message);
        return reply.ToResult(true);
    }
}
