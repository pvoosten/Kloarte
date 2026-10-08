using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: the view host between the side panels and the views in it. Clicks in a 3D view place the selected
/// actor (Actors tab) or select the lamp nearest to the mouse.
/// </summary>
public partial class FixturePanel
{
    private ViewHost _host;
    private Control _left;

    /// <summary>The view host (for tests and other views).</summary>
    public ViewHost Host => _host;

    /// <summary>Creates the views; called once from _Ready.</summary>
    private void BuildViews()
    {
        // The host is already in the tree (its _Ready has run).
        _host.AddView(new View3D("3D") { BeforeCamera = WheelIntensity });
        _host.AddView(new AudienceView());
        AddMoreViews();
        _host.FinishBar();
        BuildTools();
        _host.StageClicked += OnStageClicked;
        _host.ContextClicked += OnContextClicked;
        LampMenuRequests.Requested += ShowLampMenu;
        TreeExiting += () => LampMenuRequests.Requested -= ShowLampMenu;
        Selection.Changed += OnSelectionChanged;
        CueEditing.Edited += OnCueValuesEdited;
        CueEditing.JumpRequested += OnJumpRequested;
        TreeExiting += () =>
        {
            Selection.Changed -= OnSelectionChanged;
            CueEditing.Edited -= OnCueValuesEdited;
            CueEditing.JumpRequested -= OnJumpRequested;
            GetViewport().SizeChanged -= FitPanels;
            if (_lampsSource != null && IsInstanceValid(_lampsSource))
                _lampsSource.LampsChanged -= OnLampsChanged;
            _artNet?.Stop();
        };
    }

    /// <summary>Gives the views the current show and the scene camera.</summary>
    private void ConnectViews()
    {
        var camera = FindCamera(GetTree().Root);
        _host.SetShow(CurrentBuilder, camera);
        ConnectTools(CurrentBuilder);
    }

    private static FlyCamera FindCamera(Node node)
    {
        if (node is FlyCamera c)
            return c;
        foreach (Node child in node.GetChildren())
            if (FindCamera(child) is { } found)
                return found;
        return null;
    }

    /// <summary>Hides / shows the left panel and the inspector (more room for the views).</summary>
    public void SetSidePanels(bool visible)
    {
        _left.Visible = visible;
        _inspector.Visible = visible;
    }

    /// <summary>Key map actions of the wheel in the 3D view and their intensity steps (DMX).</summary>
    private static readonly (string Action, int Delta)[] IntensitySteps =
    {
        ("intensity_up", 5), ("intensity_down", -5), ("intensity_up_fine", 1), ("intensity_down_fine", -1),
        ("intensity_up_coarse", 25), ("intensity_down_coarse", -25),
    };

    /// <summary>
    /// Ctrl + wheel in the 3D view with lamps selected and a cue active (not while live Art-Net drives the stage): the
    /// intensity of the selected lamps' heads goes up / down in that MagicQ cue (every lamp on a head follows; saved
    /// with Save). The wheel without Ctrl always moves the camera. True when the event was used.
    /// </summary>
    private bool WheelIntensity(InputEvent e)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null || _player.Stack == null || _player.Step < 0 || _artNet?.Running == true)
            return false;
        int delta = IntensitySteps.FirstOrDefault(s => KeyMap.Pressed(e, s.Action, GetViewport())).Delta;
        var lamps = Selection.Fixtures.Where(IsInstanceValid).DefaultIfEmpty(_selected).Where(f => f != null && IsInstanceValid(f)).ToList();
        if (delta == 0 || lamps.Count == 0)
            return false;
        var addresses = lamps.Select(builder.IntensityAddress).Where(a => a > 0).Distinct().ToList();
        if (addresses.Count == 0)
        {
            _status.Text = "The selected fixtures have no intensity channel";
            return true;
        }
        var levels = addresses.Select(a => builder.Playback.NudgeLevel(_player.Stack, _player.Step, a, delta)).ToList();
        _player.Invalidate();
        _player.Seek(_player.Time);   // the stage shows the edited cue
        string heads = string.Join(", ", lamps.Select(f => f.HeadNumber).Distinct().Take(6));
        _status.Text = $"Head {heads}: {Mathf.RoundToInt(levels[0] / 2.55f)} % ({levels[0]}) in cue " +
                       $"{_player.Stack.Steps[_player.Step].CueNumberText} " + KeyMap.Hint("Save", "file_save");
        CallDeferred(MethodName.BuildParams);
        return true;
    }

    private void OnStageClicked(View3D view, Vector3 origin, Vector3 dir, Vector2 at)
    {
        if (_aimOnce)
        {
            _aimOnce = false;   // "Aim" from the lamp menu: this click aims, once
            FocusAtClick(origin, dir, force: true);
            return;
        }
        if (AimAtClick(origin, dir) || FocusAtClick(origin, dir))
            return;
        if (_tabs.CurrentTab == ActorsTab && _selectedActor != null)
        {
            PlaceActorAt(origin, dir);
            return;
        }
        if (LampNear(view, at) is { } best && !ToggleInSelection(best))
            Select(best);
    }

    /// <summary>The lamp nearest to a point of a 3D view on screen, within 24 px; null when none.</summary>
    private ProfileFixture LampNear(View3D view, Vector2 at)
    {
        ProfileFixture best = null;
        float bestDistance = 24f;
        foreach (var f in _all.Where(IsInstanceValid))
            if (view.ToView(f.GlobalPosition) is { } p && p.DistanceTo(at) < bestDistance)
            {
                best = f;
                bestDistance = p.DistanceTo(at);
            }
        return best;
    }

    /// <summary>A right click without dragging in a 3D view: the lamp menu of the lamp there.</summary>
    private void OnContextClicked(View3D view, Vector2 at)
    {
        if (LampNear(view, at) is { } lamp)
            ShowLampMenu(lamp, GetViewport().GetMousePosition());
    }
}
