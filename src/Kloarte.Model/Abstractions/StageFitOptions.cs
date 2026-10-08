namespace Kloarte.Model;

/// <summary>How <see cref="StageFrame.Fit"/> places the show in the venue when the show doesn't say.</summary>
public sealed record StageFitOptions
{
    /// <summary>Fit to the show's proscenium / podium objects.</summary>
    public bool AutoVenue { get; init; } = true;

    /// <summary>Rig z of the proscenium line when the show has no proscenium / podium.</summary>
    public float StageFrontZ { get; init; } = 9.75f;

    /// <summary>Rig z of a black back wall when there is no cyc.</summary>
    public float BackdropZ { get; init; } = -0.6f;

    /// <summary>Auditorium floor below the stage floor when the venue doesn't say.</summary>
    public float AuditoriumDrop { get; init; } = 1.0f;
}
