using System.Collections.Generic;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The 3D stage as the application changes it: after editing the show model (rig items, mechanics, patch) the
/// application tells the stage what changed, and the stage updates its nodes from the model.
/// </summary>
public interface IStage
{
    /// <summary>Where the show sits in the venue (rig &lt;-&gt; stage coordinates).</summary>
    StageFrame Frame { get; }

    /// <summary>True when the lamp turns towards its focus point (false for moving heads: pan / tilt aim them).</summary>
    bool AimsAtFocus(int lampId);

    /// <summary>A lamp was added to the show (rig item, patch and mechanics are in the model).</summary>
    void LampAdded(IRigItem lamp);

    /// <summary>A lamp was removed from the show.</summary>
    void LampRemoved(int lampId);

    /// <summary>A lamp's rig position or its focus point changed: it moves there and aims at its focus point.</summary>
    void LampMoved(int lampId);

    /// <summary>A support (truss / stand) was added.</summary>
    void SupportAdded(IRigItem support);

    /// <summary>A support's position or size changed.</summary>
    void SupportChanged(int supportId);

    /// <summary>A support was removed.</summary>
    void SupportRemoved(int supportId);

    /// <summary>The patch changed (addresses, head numbers, names, gels, groups).</summary>
    void PatchChanged();

    /// <summary>
    /// The beam angle of the lamps patched at `address` was set to `degrees` (`inPatch`: stored in the console's
    /// patch, which then wins over the mechanics).
    /// </summary>
    void BeamAngleSet(int address, float degrees, bool inPatch);

    /// <summary>The moving heads show these DMX values (the programmer is already on top).</summary>
    void ShowMovingHeads(IReadOnlyDictionary<int, int> dmx);

    /// <summary>Every lamp whose intensity channel is at `intensityAddress` gets this intensity (0..1).</summary>
    void SetIntensityAt(int intensityAddress, float level);

    /// <summary>A lamp without an intensity channel gets this intensity on stage (0..1).</summary>
    void SetLampIntensity(int lampId, float level);
}
