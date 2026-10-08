using System.Collections.Generic;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Cue playback as the views use it: what the stage shows (cue, timeline or live Art-Net values) and the cue changes
/// made from the views (levels into a cue, new cues). There is no programmer (decided 14 Oct 2026): a value set by hand
/// goes into the current cue, undoable.
/// </summary>
public interface IPlaybackCommands
{
    /// <summary>Cue / timeline / Art-Net values for the moving heads.</summary>
    void ShowMovingHeads(Dictionary<int, int> dmx);

    /// <summary>The values of `count` channels from `address` as the stage shows them now.</summary>
    Dictionary<int, int> CurrentDmx(int address, int count);

    /// <summary>
    /// DMX values after steps 0..upTo of a cue stack (tracking; palette references resolved; dimmer curves applied, as
    /// the console outputs them).
    /// </summary>
    Dictionary<int, int> DmxAfter(ICueStack stack, int upTo);

    /// <summary>
    /// Sets a lamp's intensity (0..1) in the cue of step `step`: at its head's intensity channel, so every lamp on the
    /// head follows; later cues track it. One undo step. Returns the DMX level, or -1 when the lamp has no intensity
    /// channel or no cue is chosen.
    /// </summary>
    int SetLevel(ICueStack stack, int step, int lampId, float level);

    /// <summary>
    /// Changes a channel's level in the cue of a step by `delta` from its tracked level (clamped) and records it as a
    /// hard value; later cues track it. Returns the new level.
    /// </summary>
    int NudgeLevel(ICueStack stack, int step, int address, int delta);

    /// <summary>
    /// Inserts a blackout cue or a cue without changes into a stack at a cue number. Returns the new step; throws
    /// <see cref="System.ArgumentException"/> when the number is taken.
    /// </summary>
    ICueStep InsertCue(ICueStack stack, float number, string text, bool blackout);
}
