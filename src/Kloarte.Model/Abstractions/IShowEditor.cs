using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// Changes to a show document made from the application (patch: head numbers, DMX addresses, beam angles, new lamps;
/// cues and palettes). Each kind of show document has its own (the console binding's, the native show's). A refused
/// change throws <see cref="ShowEditException"/> with the reason.
/// </summary>
public interface IShowEditor
{
    /// <summary>
    /// Patches a lamp to a head number and DMX address with the plan's rules (join another head, take its own head
    /// along, or get a head of its own). Returns what was done.
    /// </summary>
    string ApplyLampPatch(IRigItem lamp, int head, int address);

    /// <summary>Gives a new lamp a head of its own on the next free head number and DMX address. Returns what was done.</summary>
    string AddLampHead(IRigItem lamp, string headName);

    /// <summary>Removes a head's patch when nothing uses it any more (no lamp, cue, palette or group); true if removed.</summary>
    bool RemoveUnusedHead(int head);

    /// <summary>Number of patched channels of a head.</summary>
    int ChannelCount(int head);

    /// <summary>Number of lamps on a head.</summary>
    int LampCount(int head);

    /// <summary>
    /// Adds a lamp of a console personality the show doesn't have yet, from the console templates, on its own head
    /// (next free head number and address). `typeName` is for the message when the personality is unknown.
    /// </summary>
    IRigItem ImportLamp(string personality, string headName, string typeName);

    /// <summary>Sets the beam angle (whole degrees) of the head patched at `address`; false when nothing is patched there.</summary>
    bool SetBeamAngle(int address, int degrees);

    /// <summary>
    /// Names a head (the console keeps the name per head, in every patch channel of it); the name is made fit for the
    /// console (<see cref="HeadNames.Clean"/>). Returns the number of channels changed.
    /// </summary>
    int RenameHead(int head, string name);

    /// <summary>
    /// Sets the gel of a head (colour table id, 0 = none) in every patch channel of the head (the console keeps the gel
    /// per head). Returns the number of channels changed.
    /// </summary>
    int SetGel(int head, int gelId);

    /// <summary>
    /// Changes a channel's level in the cue of step `step` by `delta` from its tracked level at that step (clamped to
    /// 0..255) and records it in that cue as a hard value. Returns the new level.
    /// </summary>
    int NudgeLevel(ICueStack stack, int step, int address, int delta);

    /// <summary>
    /// Record merge of programmer values (address, 0..255) into the cue of step `step`: values equal to the cue before
    /// are left to track (removed from the cue), others stored, as an intensity palette reference when a palette holds
    /// exactly that value. Later cues track the change. Returns a status message.
    /// </summary>
    string RecordIntoCue(ICueStack stack, int step, IReadOnlyList<(int Address, int Value)> values);

    /// <summary>
    /// Records values as a combined position palette (new, or merged into the position palette of that name) and makes
    /// the cue (`cueId`; none when it doesn't exist) refer to it for those channels. Returns a status message.
    /// </summary>
    string RecordPalette(string name, int cueId, IReadOnlyList<(int Address, int Value)> values);

    /// <summary>Next free position palette entry (0-based).</summary>
    int FreePositionPaletteEntry();

    /// <summary>
    /// Inserts a cue into a stack at a cue number: a blackout (every intensity channel at 0) or a cue without changes
    /// (the look tracks). Returns the new step; throws <see cref="System.ArgumentException"/> when the number is taken.
    /// </summary>
    ICueStep InsertCue(ICueStack stack, float number, string text, bool blackout);
}
