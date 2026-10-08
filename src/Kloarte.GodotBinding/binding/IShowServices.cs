namespace Kloarte.GodotBinding;

/// <summary>
/// What the application gives a show scene (<see cref="ShowBuilder"/>): the open show's files and model, rig and patch
/// changes, playback and the programmer, and the round trip with the console. Made by the host
/// (<see cref="ShowBuilder.ServicesFactory"/>).
/// </summary>
public interface IShowServices
{
    /// <summary>The open show and its files.</summary>
    IShowFiles Files { get; }

    /// <summary>Rig, plan and patch changes.</summary>
    IRigCommands Rig { get; }

    /// <summary>Cue playback and the programmer.</summary>
    IPlaybackCommands Playback { get; }

    /// <summary>The round trip with the console program.</summary>
    IConsoleCommands Console { get; }
}
