using Godot;
using Kloarte.Application;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;
using Kloarte.Mcp;
using Kloarte.Model;
using Kloarte.Yaml;

namespace Kloarte.Host;

/// <summary>
/// Autoload (project.godot): the composition root. Runs before any scene (the show, the movie process, the tests) and
/// connects the engine-free code to its Godot implementations.
/// </summary>
public partial class Startup : Node
{
    /// <inheritdoc />
    public override void _EnterTree()
    {
        AppPaths.Current = new GodotAppPaths();
        FileCodec.Current = new YamlFileCodec();
        FixturePanel.LiveInputFactory = () => new LiveInput(new ArtNetReceiver());
        // Every project is a native show (decided 14 Oct 2026); MagicQ shows are imported and exported.
        ShowBuilder.ServicesFactory = stage =>
        {
            ShowSession<DesignShow> session = null;
            var store = new DesignStore(new MagicQExchange(), () => session.Catalog);
            var services = new ShowServices<DesignShow>(store, new MagicQConsole(), stage);
            session = (ShowSession<DesignShow>)services.Files;
            return services;
        };
        // The MCP server inside the app (user, 25 Oct 2026): the application's tools on the UI.
        HostFixturePanel.McpFactory = McpPort() is int port and > 0
            ? ui =>
            {
                var server = new McpServer(port, "kloarte", "Kloarte up de plankn",
                    ProjectSettings.GetSetting("application/config/version", "1.0").AsString());
                new RemoteTools(ui).AddTo(server);
                return server;
            }
            : null;
    }

    /// <summary>
    /// The MCP server's port: <c>--mcp-port=N</c> after <c>--</c> on the command line, else <c>KLOARTE_MCP_PORT</c>, else
    /// <see cref="McpServer.DefaultPort"/>; 0 (or <c>--no-mcp</c>) = no server. The Godot tests (and explore runs) get a
    /// server only on an explicit port: a client configured for the app must never drive a test instance.
    /// </summary>
    private static int McpPort()
    {
        string value = System.Environment.GetEnvironmentVariable("KLOARTE_MCP_PORT");
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--no-mcp")
                return 0;
            if (arg.StartsWith("--mcp-port="))
                value = arg["--mcp-port=".Length..];
        }
        if (int.TryParse(value, out int port))
            return port;
        bool tests = ProjectSettings.GetSetting("application/config/name", "").AsString() == "Kloarte tests";
        return tests ? 0 : McpServer.DefaultPort;
    }
}
