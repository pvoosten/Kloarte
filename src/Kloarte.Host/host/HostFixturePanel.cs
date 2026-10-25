using System;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.Mcp;

namespace Kloarte.Host;

/// <summary>
/// The script scenes attach for the UI: <see cref="FixturePanel"/> (scripts attached in scenes must be files of the Godot project; the
/// behaviour is in the Godot binding). It also runs the MCP server of the app (made by <see cref="Startup"/>): started with
/// the UI, its tool calls run here on the main thread every frame, stopped when the UI goes.
/// </summary>
public partial class HostFixturePanel : FixturePanel
{
    /// <summary>Makes the MCP server for the UI (set by <see cref="Startup"/>; null = no server).</summary>
    public static Func<IRemoteControl, McpServer> McpFactory { get; set; }

    /// <summary>The running MCP server, or null.</summary>
    private McpServer _mcp;

    /// <inheritdoc />
    public override void _Ready()
    {
        base._Ready();
        if (McpFactory?.Invoke(this) is not { } server)
            return;
        try
        {
            server.Start();
            _mcp = server;
            GD.Print($"MCP server: {server.Url}");
        }
        catch (Exception e)
        {
            // Another Kloarte has the port, most likely: this one runs without a server.
            server.Dispose();
            GD.Print($"MCP server not started on port {server.Port}: {e.Message}");
        }
        TreeExiting += StopMcp;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        base._Process(delta);
        _mcp?.Pump();
    }

    /// <summary>Stops the MCP server.</summary>
    private void StopMcp()
    {
        _mcp?.Dispose();
        _mcp = null;
    }
}
