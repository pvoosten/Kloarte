using System;
using System.Collections.Generic;
using System.Linq;

namespace Kloarte.Model;

/// <summary>Tracking playback of a cue stack, as the console does it.</summary>
public static class CueTracking
{
    /// <summary>
    /// Channel levels (address -&gt; 0..255) after steps 0..upTo of a cue stack: the console tracks, so every cue up to
    /// that step counts; palette references resolved; before the dimmer curves (<see cref="IShowDocument.WithCurves"/>).
    /// </summary>
    public static Dictionary<int, int> LevelsAfter(IShowDocument show, ICueStack stack, int upTo)
    {
        var cues = show.Cues.ToDictionary(c => c.Id);
        var state = new Dictionary<int, int>();
        var steps = stack?.Steps ?? Array.Empty<ICueStep>();
        for (int i = 0; i <= upTo && i < steps.Count; i++)
            if (cues.TryGetValue(steps[i].CueId, out var cue))
                foreach (var (address, value) in cue.Channels)
                    state[address] = show.Resolve(address, value);
        return state;
    }

    /// <summary>
    /// The levels of <see cref="LevelsAfter"/> for every step of a cue stack in one pass: [i] = after step i. Each
    /// entry is its own dictionary.
    /// </summary>
    public static List<Dictionary<int, int>> LevelsPerStep(IShowDocument show, ICueStack stack)
    {
        var cues = show.Cues.ToDictionary(c => c.Id);
        var state = new Dictionary<int, int>();
        var steps = stack?.Steps ?? Array.Empty<ICueStep>();
        var result = new List<Dictionary<int, int>>(steps.Count);
        foreach (var step in steps)
        {
            if (cues.TryGetValue(step.CueId, out var cue))
                foreach (var (address, value) in cue.Channels)
                    state[address] = show.Resolve(address, value);
            result.Add(new Dictionary<int, int>(state));
        }
        return result;
    }
}
