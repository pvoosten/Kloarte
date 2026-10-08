using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: the views after 3D and audience (plan, truss heights, timeline). The timeline's lane header adds,
/// renames and removes actors through the panel (Blocking workspace, UI review 28). The actor view looks through an
/// actor's eyes; every actor going on / off stage while the cues run is announced on the status bar ("OP cour").
/// </summary>
public partial class FixturePanel
{
    /// <summary>Adds the other views to the host.</summary>
    private void AddMoreViews()
    {
        _host.AddView(new PlanView { Name = "Plan" });
        _host.AddView(new TrussHeightView { Name = "TrussHeights" });
        var timeline = new TimelineView { Name = "Timeline", Player = _player };
        timeline.ExportKeyFramesRequested += () => ShowExportDialog(ExportKind.Stills);
        timeline.RecordMovieRequested += () => ShowExportDialog(ExportKind.Movie);
        timeline.AddActorRequested += AddActor;
        timeline.RenameActorRequested += RenameActor;
        timeline.ActorMenuRequested += ShowActorMenu;
        _host.AddView(timeline);
        var actorView = new ActorView { Player = _player };
        actorView.FacingRequested += TurnActor;
        _host.AddView(actorView);
        _player.EntrancePassed += AnnounceEntrance;
    }

    /// <summary>
    /// The actor view's right mouse drag turned its actor: that actor becomes the selected one (Actors tab) and the new
    /// facing is recorded in its current step, like the Facing slider (a drag is one undo step).
    /// </summary>
    private void TurnActor(string actorId, float facing)
    {
        if (_selectedActor != actorId)
            SelectActor(actorId);
        RecordActor(s => s.Facing = facing);
    }

    /// <summary>An actor goes on / off stage while the cues run: "Anna: OP cour (cue 4)" on the status bar.</summary>
    private void AnnounceEntrance(ActorEntrance e)
    {
        string name = CurrentBuilder?.Model?.Blocking.Find(e.ActorId)?.Name ?? e.ActorId;
        _status.Text = $"{name}: {e.Text} (cue {e.CueNumber})";
    }
}
