using System.Text.Json;
using System.Threading.Tasks;

namespace Kloarte.Mcp;

/// <summary>
/// A tool an MCP client can call: its name, what it does, the JSON Schema of its arguments, and the call. Calls run on the
/// thread that pumps the server (<see cref="McpServer.Pump"/>), one at a time.
/// </summary>
public interface IMcpTool
{
    /// <summary>The tool's name (letters, digits, '_'; unique in the server).</summary>
    string Name { get; }

    /// <summary>A short title for people.</summary>
    string Title { get; }

    /// <summary>What the tool does, for the client's model.</summary>
    string Description { get; }

    /// <summary>The JSON Schema (an object schema, as JSON text) of the tool's arguments.</summary>
    string InputSchema { get; }

    /// <summary>True when the tool changes nothing.</summary>
    bool ReadOnly { get; }

    /// <summary>
    /// Runs the tool with its arguments (a JSON object) and puts the result into `reply`. An exception is the tool's error
    /// (its message goes to the client); the arguments element stays valid during the call only.
    /// </summary>
    Task CallAsync(JsonElement arguments, IMcpReply reply);
}
