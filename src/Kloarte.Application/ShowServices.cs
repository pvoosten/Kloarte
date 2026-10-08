using Kloarte.GodotBinding;
using Kloarte.MagicQ;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>The application's services for one show scene, on a console binding (MagicQ: <c>MagicQShow</c>).</summary>
/// <typeparam name="TShow">The console's show document.</typeparam>
public sealed class ShowServices<TShow> : IShowServices where TShow : class, IShowDocument
{
    /// <summary>Creates the services of a show scene: its session on `store`, the editors and the console link.</summary>
    public ShowServices(IShowStore<TShow> store, IConsoleApp<TShow> console, IStage stage)
    {
        var session = new ShowSession<TShow>(store);
        var playback = new ShowPlayback<TShow>(session, stage);
        Files = session;
        Rig = new RigEditor<TShow>(session, stage);
        Playback = playback;
        Console = new ConsoleLink<TShow>(session, playback, console);
    }

    /// <inheritdoc />
    public IShowFiles Files { get; }

    /// <inheritdoc />
    public IRigCommands Rig { get; }

    /// <inheritdoc />
    public IPlaybackCommands Playback { get; }

    /// <inheritdoc />
    public IConsoleCommands Console { get; }
}
