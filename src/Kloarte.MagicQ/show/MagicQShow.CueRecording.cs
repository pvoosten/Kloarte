using System.Collections.Generic;

namespace Kloarte.MagicQ;

public partial class MagicQShow
{
    /// <summary>
    /// What <see cref="RecordIntoCue"/> did with the programmer's values: the channels stored in the cue (hard, or as a
    /// reference to a palette holding exactly that value), the channels that equal the state before the cue and so are
    /// left to track, and how many later cues of the stack the change tracks into.
    /// </summary>
    /// <param name="Recorded">Addresses stored in the cue.</param>
    /// <param name="Palettes">Of those, the ones stored as a palette reference: address -&gt; palette.</param>
    /// <param name="Tracked">Addresses equal to the cue before: not stored (removed from the cue when it had them).</param>
    /// <param name="LaterCues">Later steps of the stack that track at least one of the changed channels.</param>
    public sealed record CueRecording(
        IReadOnlyList<int> Recorded,
        IReadOnlyDictionary<int, Palette> Palettes,
        IReadOnlyList<int> Tracked,
        int LaterCues);
}
