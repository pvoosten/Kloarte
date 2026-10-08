namespace Kloarte.Model;

/// <summary>
/// The side of the stage an actor enters or leaves by, in the French terms used backstage: jardin = the audience's
/// left (-X), cour = the audience's right (+X); an entrance near the centre line is lointain (upstage) or face
/// (downstage, e.g. from the auditorium).
/// </summary>
public enum StageSide
{
    /// <summary>The audience's left (-X).</summary>
    Jardin,

    /// <summary>The audience's right (+X).</summary>
    Cour,

    /// <summary>Upstage, near the centre line (-Z).</summary>
    Lointain,

    /// <summary>Downstage, near the centre line (+Z: the forestage or the auditorium).</summary>
    Face,
}
