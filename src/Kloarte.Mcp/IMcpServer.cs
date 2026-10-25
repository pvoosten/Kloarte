namespace Kloarte.Mcp;

/// <summary>
/// An MCP server as the application fills it: the tools it offers and what it tells a client about them. Made and run by
/// the host (<see cref="McpServer"/>).
/// </summary>
public interface IMcpServer
{
    /// <summary>What the server tells a client at the start (how to use the tools); null = nothing.</summary>
    string Instructions { get; set; }

    /// <summary>Offers a tool (its name is unique; a tool with the same name is replaced).</summary>
    void Add(IMcpTool tool);
}
