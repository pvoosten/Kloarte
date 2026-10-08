namespace Kloarte.Model;

/// <summary>Poses of an actor (blocking): standing, seated (on a stool) or lying on the back.</summary>
public static class ActorPose
{
    /// <summary>Standing.</summary>
    public const string Standing = "standing";

    /// <summary>Seated (a seated mannequin replaces the standing one).</summary>
    public const string Seated = "seated";

    /// <summary>Lying on the back, head upstage at facing 0.</summary>
    public const string Lying = "lying";

    /// <summary>All poses, in the order of the pose picker.</summary>
    public static readonly string[] All = { Standing, Seated, Lying };
}
