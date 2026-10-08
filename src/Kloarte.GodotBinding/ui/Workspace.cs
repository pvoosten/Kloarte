namespace Kloarte.GodotBinding;

/// <summary>
/// A workspace of the view host (UI review 31, decided with the user): a main view and small cards for one kind of
/// work, each remembering how the user changed it. Present is not a workspace but Present mode (F11).
/// </summary>
public enum Workspace
{
    /// <summary>Rig and patch: the plan, with 3D and the truss heights as cards; the Fixtures list.</summary>
    Rig,

    /// <summary>Focus and looks: 3D, with the audience view and the plan as cards; the Fixtures list.</summary>
    Focus,

    /// <summary>Blocking: the timeline with its actor lanes, with 3D and the audience view as cards; the Actors list.</summary>
    Blocking,
}
