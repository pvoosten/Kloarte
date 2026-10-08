using System.Collections.Generic;
using Kloarte.GodotBinding;
using Kloarte.Model;

namespace Kloarte.Tests;

/// <summary>A stage that shows nothing: application edits without the engine.</summary>
public sealed class NullStage : IStage
{
    /// <inheritdoc />
    public StageFrame Frame { get; } = new();

    /// <inheritdoc />
    public bool AimsAtFocus(int lampId) => true;

    /// <inheritdoc />
    public void LampAdded(IRigItem lamp) { }

    /// <inheritdoc />
    public void LampRemoved(int lampId) { }

    /// <inheritdoc />
    public void LampMoved(int lampId) { }

    /// <inheritdoc />
    public void SupportAdded(IRigItem support) { }

    /// <inheritdoc />
    public void SupportChanged(int supportId) { }

    /// <inheritdoc />
    public void SupportRemoved(int supportId) { }

    /// <inheritdoc />
    public void PatchChanged() { }

    /// <inheritdoc />
    public void BeamAngleSet(int address, float degrees, bool inPatch) { }

    /// <inheritdoc />
    public void ShowMovingHeads(IReadOnlyDictionary<int, int> dmx) { }

    /// <inheritdoc />
    public void SetIntensityAt(int intensityAddress, float level) { }

    /// <inheritdoc />
    public void SetLampIntensity(int lampId, float level) { }
}
