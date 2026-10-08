using System.Collections.Generic;
using System.Linq;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Application;

/// <summary>
/// Cue playback: the last cue / Art-Net values, tracking (<see cref="CueTracking"/>) with the dimmer curves, and cue
/// level changes (through the show's <see cref="IShowEditor"/>). The stage shows the result (<see cref="IStage"/>).
/// </summary>
/// <typeparam name="TShow">The show document.</typeparam>
public sealed class ShowPlayback<TShow> : IPlaybackCommands where TShow : class, IShowDocument
{
    /// <summary>The open show.</summary>
    private readonly ShowSession<TShow> _session;

    /// <summary>The 3D stage.</summary>
    private readonly IStage _stage;

    /// <summary>The last cue / timeline / Art-Net values the moving heads showed.</summary>
    private Dictionary<int, int> _last = new();

    /// <summary>Creates the playback of a session's shows.</summary>
    public ShowPlayback(ShowSession<TShow> session, IStage stage)
    {
        _session = session;
        _stage = stage;
    }

    /// <inheritdoc />
    public void ShowMovingHeads(Dictionary<int, int> dmx)
    {
        _last = dmx ?? new Dictionary<int, int>();
        _stage.ShowMovingHeads(_last);
    }

    /// <inheritdoc />
    public Dictionary<int, int> CurrentDmx(int address, int count)
    {
        var result = new Dictionary<int, int>();
        for (int a = address; a < address + count; a++)
            if (_last.TryGetValue(a, out int v))
                result[a] = v;
        return result;
    }

    /// <inheritdoc />
    public Dictionary<int, int> DmxAfter(ICueStack stack, int upTo)
    {
        var show = _session.Model.Show;
        return show.WithCurves(CueTracking.LevelsAfter(show, stack, upTo));
    }

    /// <inheritdoc />
    public int SetLevel(ICueStack stack, int step, int lampId, float level)
    {
        var show = _session.Model.Show;
        var lamp = show.RigItems.FirstOrDefault(i => i.IsFixture && i.Id == lampId);
        int channel = lamp == null ? 0 : show.IntensityAddressOf(lamp.Address);
        if (channel <= 0 || stack == null || step < 0 || step >= stack.Steps.Count)
            return -1;
        int target = System.Math.Clamp((int)System.MathF.Round(level * 255f), 0, 255);
        int now = CueTracking.LevelsAfter(show, stack, step).GetValueOrDefault(channel);
        _session.NameNextEdit($"Level in cue {stack.Steps[step].CueNumberText}");
        return _session.Editor.NudgeLevel(stack, step, channel, target - now);
    }

    /// <inheritdoc />
    public int NudgeLevel(ICueStack stack, int step, int address, int delta)
    {
        _session.NameNextEdit("Level in the cue");
        return _session.Editor.NudgeLevel(stack, step, address, delta);
    }

    /// <inheritdoc />
    public ICueStep InsertCue(ICueStack stack, float number, string text, bool blackout)
    {
        _session.NameNextEdit(blackout ? "Insert BO cue" : "Insert cue");
        return _session.Editor.InsertCue(stack, number, text, blackout);
    }
}
