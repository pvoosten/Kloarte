using Godot;
using Kloarte.Application;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;
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
    }
}
