using System;
using System.Collections.Generic;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// The console program on this PC: "Open in MagicQ PC" writes the show as it is here (a native show: an export, the next
/// numbered show) and starts the console with it; received looks are compared with the cues (<see cref="Compare"/>).
/// </summary>
/// <typeparam name="TShow">The console's show document.</typeparam>
public sealed class ConsoleLink<TShow> : IConsoleCommands where TShow : class, IShowDocument
{
    /// <summary>The open show.</summary>
    private readonly ShowSession<TShow> _session;

    /// <summary>Cue playback (expected looks).</summary>
    private readonly IPlaybackCommands _playback;

    /// <summary>The console program.</summary>
    private readonly IConsoleApp<TShow> _console;

    /// <summary>Creates the link for a session.</summary>
    public ConsoleLink(ShowSession<TShow> session, IPlaybackCommands playback, IConsoleApp<TShow> console)
    {
        _session = session;
        _playback = playback;
        _console = console;
    }

    /// <inheritdoc />
    public string ShowFolder => _console.ShowFolder;

    /// <inheritdoc />
    public string OpenInConsole()
    {
        if (_session.Model == null)
            return "No show loaded";
        // The show file as it is, when it holds the show as it is here; else the next numbered version.
        string show = _session.CanLoad && !_session.IsWorkingCopy && _console.Matches(_session.Model.Show, _session.ShowFile)
            ? _session.ShowFile
            : _session.SaveShowVersion();
        _session.HandedToConsole(show);
        string launched = _console.Launch(show, _session.Model.Show) + $"\n{show}";
        return _session.LastConsoleShowReport.Length > 0 && !_session.CanLoad ? launched
            : _session.Model.Show is IProjectShow ? $"{launched}\n{_session.LastConsoleShowReport}" : launched;
    }

    /// <inheritdoc />
    public bool ConsoleRunning => _console.Running;

    /// <inheritdoc />
    public string Compare(Dictionary<int, int> received, ICueStack stack)
    {
        var expected = new List<Dictionary<int, int>>();
        for (int i = -1; i < stack.Steps.Count; i++)
            expected.Add(_playback.DmxAfter(stack, i));
        var (step, compared, differences, text) = _console.BestMatch(received, expected, _session.Model.Show);
        string cue = step < 0 ? "before the first cue" : $"cue {stack.Steps[step].CueNumberText} {stack.Steps[step].Text}";
        return differences == 0
            ? $"MagicQ = {cue}: all {compared} channels match"
            : $"MagicQ ~ {cue}: {differences} of {compared} channels differ: {text}";
    }
}
