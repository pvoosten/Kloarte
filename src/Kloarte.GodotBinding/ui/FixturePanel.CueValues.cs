using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: the values of the selected lamps' heads in the current cue (decided 14 Oct 2026: no programmer, a value
/// set by hand goes into the current cue): intensity, and for moving heads position, colour and beam in physical units
/// (<see cref="LookEditor"/>), from palettes or tracked from the cues before; "Aim at the stage" points moving heads at a
/// clicked spot (pan / tilt into the cue). Live Art-Net input (from MagicQ) is in the Playback tab.
/// </summary>
public partial class FixturePanel
{
    private ILiveInput _artNet;
    private CheckBox _liveInput;

    /// <summary>The head numbers of the selected lamps (the lamp the settings are of first).</summary>
    private List<int> SelectedHeads(ProfileFixture f) =>
        new[] { f }.Concat(Selection.Fixtures).Where(l => l != null && IsInstanceValid(l) && l.HeadNumber > 0)
            .Select(l => l.HeadNumber).Distinct().ToList();

    /// <summary>
    /// The lamp's head (and the other selected heads') values in the current cue. Built again when the cue changes or a
    /// value is edited.
    /// </summary>
    private void CueValues(ProfileFixture f, ShowBuilder builder)
    {
        _lampParamsCue = PlaybackPosition;
        var heads = SelectedHeads(f);
        if (builder == null || heads.Count == 0)
            return;
        _params.AddChild(LookEditor.ForCue(builder, _player.Step, heads, () => CallDeferred(MethodName.BuildParams)));
    }

    /// <summary>The cue's values changed (any view): the stage shows the cue again, the lists follow.</summary>
    private void OnCueValuesEdited()
    {
        if (!IsInstanceValid(this) || _player == null)
            return;
        _player.Invalidate();
        if (_artNet?.Running != true && _player.Stack != null && _player.Step >= 0)
            _player.Seek(_player.Time);
        FillCues();
        _list?.UpdateLevels();
    }

    /// <summary>
    /// A click on the stage while heads wait to be aimed ("Aim at the stage" in the cue values): their pan / tilt go into
    /// the current cue. True when the click was used for that.
    /// </summary>
    private bool AimAtClick(Vector3 origin, Vector3 dir)
    {
        var builder = CurrentBuilder;
        var heads = CueEditing.AimHeads;
        if (heads == null || builder == null)
            return false;
        CueEditing.StopAim();
        if (builder.StagePointOnRay(origin, dir) is not { } point)
        {
            _status.Text = "Click on the stage floor or the back wall to aim";
            return true;
        }
        var lamps = builder.Fixtures.Where(l => heads.Contains(l.HeadNumber) && builder.MoverOf(l) != null).ToList();
        _status.Text = AimMovers(builder, lamps, builder.ToGlobal(point)) ?? $"Aimed at x {point.X:0.00}, y {point.Y:0.00}, z {point.Z:0.00}";
        return true;
    }

    /// <summary>
    /// Shift + click in a 3D view (key map "aim_click"): every selected lamp points at the clicked spot on the stage
    /// floor or back wall - conventionals change their focus point (mechanics, the whole show), moving heads get pan /
    /// tilt in the current cue. True when the click was used for that.
    /// </summary>
    private bool FocusAtClick(Vector3 origin, Vector3 dir, bool force = false)
    {
        var builder = CurrentBuilder;
        var lamps = Selection.Fixtures.Where(IsInstanceValid).ToList();
        if (builder == null || lamps.Count == 0 || !force && !KeyMap.Held("aim_click", GetTree().Root))
            return false;
        if (builder.StagePointOnRay(origin, dir) is not { } point)
        {
            _status.Text = "Click on the stage floor or the back wall to aim";
            return true;
        }
        foreach (var f in lamps.Where(l => builder.MoverOf(l) == null))
            builder.FocusAt(f, point);
        string problem = null;
        if (lamps.Any(l => builder.MoverOf(l) != null))
            problem = AimMovers(builder, lamps.Where(l => builder.MoverOf(l) != null).ToList(), builder.ToGlobal(point));
        _marker.GlobalPosition = _selected?.GlobalPosition ?? _marker.GlobalPosition;
        _status.Text = problem ?? $"{lamps.Count} lamp(s) aimed at x {point.X:0.00}, y {point.Y:0.00}, z {point.Z:0.00}";
        Selection.Touch();   // settings (focus point) and plan follow
        return true;
    }

    /// <summary>
    /// Points moving heads at a global point: pan / tilt of their heads into the current cue (one undo step). Returns why
    /// not (no cue), or null.
    /// </summary>
    private string AimMovers(ShowBuilder builder, List<ProfileFixture> lamps, Vector3 target)
    {
        if (_player.Stack == null || _player.Step < 0)
            return "Choose a cue first: pan and tilt go into the current cue";
        var cue = CueEditing.Design(builder)?.CueOrder.ElementAtOrDefault(_player.Step);
        if (cue == null)
            return "Aiming moving heads needs a native project";
        CueEditing.Edit(builder, $"Aim in cue {cue.Cue}", _ =>
        {
            foreach (var lamp in lamps)
            {
                var (pan, tilt) = builder.MoverOf(lamp).AimAt(target);
                DesignEdits.Set(cue.Heads, lamp.HeadNumber, "Pan", LookValue.Of(System.Math.Round(pan, 2)));
                DesignEdits.Set(cue.Heads, lamp.HeadNumber, "Tilt", LookValue.Of(System.Math.Round(tilt, 2)));
            }
        });
        CallDeferred(MethodName.BuildParams);
        return null;
    }

    /// <summary>The Playback tab's "Live Art-Net" switch.</summary>
    private Control LiveInputRow()
    {
        _liveInput = new CheckBox { Text = "Live Art-Net", TooltipText = "Listen on UDP 6454 and show what MagicQ outputs (cue playback off while on)" };
        _liveInput.Toggled += SetLiveInput;
        return _liveInput;
    }

    /// <summary>Starts / stops listening to Art-Net.</summary>
    public void SetLiveInput(bool on)
    {
        if (on)
        {
            try
            {
                _artNet ??= LiveInputFactory();
                _artNet.UniverseOffset = Preferences.Current.ArtNetUniverseOffset;
                _artNet.Start();
                _player.SetPlaying(false);
                _status.Text = "Listening to Art-Net on UDP 6454";
            }
            catch (System.Net.Sockets.SocketException e)
            {
                _status.Text = $"Art-Net: {e.Message}";
                _liveInput?.SetPressedNoSignal(false);
            }
        }
        else
            _artNet?.Stop();
        _liveInput?.SetPressedNoSignal(on && _artNet?.Running == true);
    }

    /// <summary>The Art-Net receiver (for tests).</summary>
    public ILiveInput ArtNet => _artNet;

    /// <summary>Makes the live input (set by the host at start: Art-Net through the MagicQ binding).</summary>
    public static System.Func<ILiveInput> LiveInputFactory { get; set; }

    /// <summary>Live input: every frame the received DMX drives all lamps and moving heads.</summary>
    private void ApplyLiveInput()
    {
        if (_artNet?.Running != true || CurrentBuilder?.Model == null || _artNet.Packets == 0)
            return;
        var dmx = _artNet.Snapshot();
        foreach (var (fixture, level) in CurrentBuilder.LevelsFrom(dmx))
            if (IsInstanceValid(fixture) && !_offSaved.ContainsKey(fixture))
                fixture.Intensity = level;
        CurrentBuilder.ApplyMovingHeads(dmx);
    }
}
