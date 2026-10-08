using System.Collections.Generic;

namespace Kloarte.Model;

/// <summary>
/// The show as the lighting console stores it (rig, cues, palettes, dimmer curves), seen from the model. The console
/// binding implements it on its own file format, which it keeps lossless.
/// </summary>
public interface IShowDocument : IRigDocument
{
    /// <summary>All recorded cues.</summary>
    IEnumerable<ICue> Cues { get; }

    /// <summary>The groups of heads.</summary>
    IReadOnlyList<LampGroup> Groups { get; }

    /// <summary>The cue stacks.</summary>
    IReadOnlyList<ICueStack> CueStacks { get; }

    /// <summary>Head number patched at an address (0 = nothing patched there).</summary>
    int HeadAt(int address);

    /// <summary>The DMX addresses of every patched intensity channel (a head's dimmer), in address order.</summary>
    IReadOnlyList<int> IntensityAddresses { get; }

    /// <summary>Number of patched channels of a head.</summary>
    int ChannelCountOfHead(int head);

    /// <summary>What the patch says about a lamp (<see cref="IRigItem.IsFixture"/>).</summary>
    LampPatch LampPatchOf(IRigItem lamp);

    /// <summary>DMX address of the intensity channel of the head patched at `address` (0 = none).</summary>
    int IntensityAddressOf(int address);

    /// <summary>DMX value of a recorded cue channel: the value itself, or the current value of the palette it refers to.</summary>
    int Resolve(int address, int rawValue);

    /// <summary>What the console outputs for these channel values (its dimmer curves applied).</summary>
    Dictionary<int, int> WithCurves(Dictionary<int, int> dmx);

    /// <summary>Writes the show document to a file (and the files that belong to it).</summary>
    void Save(string path);

    /// <summary>
    /// A fingerprint of everything <see cref="Save"/> writes: it changes with every edit (unsaved changes = a
    /// fingerprint other than the one taken when it was loaded or saved).
    /// </summary>
    long ContentHash();
}
