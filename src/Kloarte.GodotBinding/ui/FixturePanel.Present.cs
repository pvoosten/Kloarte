using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, Present mode (UI review 27; decided with the user): for rehearsals with the director. F11 (key map
/// <c>present_toggle</c>) shows only the audience view, full screen, with the cue caption (current and next cue; C hides
/// it), where the camera sits (viewpoint or row and seat) and a short key hint; Space / Backspace are GO / Back as
/// always, the number keys sit at the venue's named viewpoints (1 = the first, Regie), [ ] and PgUp / PgDn move seats
/// and rows. F11 or Esc goes back: panels, bars and window mode as they were. The audience view is the host's
/// <see cref="ViewHost.Exclusive"/> view, so the workspace doesn't change.
/// </summary>
public partial class FixturePanel
{
    /// <summary>Full screen while presenting (tests keep the window as it is).</summary>
    internal static bool PresentFullScreen { get; set; } = true;

    /// <summary>True while presenting.</summary>
    internal bool Presenting { get; private set; }

    /// <summary>How long the key hint stays (s).</summary>
    private const double PresentHintSeconds = 5;

    /// <summary>The menu bar (hidden while presenting).</summary>
    private Control _menuBar;

    /// <summary>What Present mode changed, to put back: side panels, window mode.</summary>
    private bool _presentPanels;
    private DisplayServer.WindowMode _presentWindow;

    /// <summary>The overlay: caption (cue, next), where the camera sits, the key hint.</summary>
    private Control _presentOverlay;
    private PanelContainer _captionBox;
    private Label _captionCue, _captionNext, _presentWhere, _presentHint;
    private double _presentHintLeft;

    /// <summary>Goes into or out of Present mode.</summary>
    internal void TogglePresent()
    {
        if (Presenting)
            StopPresenting();
        else
            StartPresenting();
    }

    /// <summary>Only the audience view, full screen, with the overlay.</summary>
    private void StartPresenting()
    {
        var audience = _host.Find<AudienceView>();
        if (audience == null || CurrentBuilder?.Model == null)
        {
            _status.Text = "Present mode needs a show with a venue";
            return;
        }
        Presenting = true;
        _presentPanels = _left.Visible;
        _presentWindow = DisplayServer.WindowGetMode();
        Deselect();
        SetSidePanels(false);
        _menuBar.Visible = false;
        _transport.Visible = false;
        _statusBar.Visible = false;
        _host.ShowBar = false;
        _host.Exclusive = audience;
        audience.Presenting = true;
        audience.Moved += UpdatePresentWhere;
        if (PresentFullScreen)
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        BuildPresentOverlay();
        _presentOverlay.Visible = true;
        _presentHintLeft = PresentHintSeconds;
        _presentHint.Text = $"{KeyMap.KeysText("playback_go")} GO  ·  {KeyMap.KeysText("playback_back")} Back  ·  " +
                            $"1-{Mathf.Min(9, CurrentBuilder.Venue?.Auditorium.Viewpoints.Count ?? 0)} viewpoints  ·  " +
                            $"{KeyMap.KeysText("seat_prev")} {KeyMap.KeysText("seat_next")} seats  ·  " +
                            $"{KeyMap.KeysText("present_caption")} caption  ·  {KeyMap.KeysText("present_toggle")} / " +
                            $"{KeyMap.KeysText("deselect")} back";
        UpdatePresentWhere();
        UpdateCaption();
        GD.Print("Present mode on");
    }

    /// <summary>Everything back as it was before presenting.</summary>
    private void StopPresenting()
    {
        Presenting = false;
        var audience = _host.Find<AudienceView>();
        if (audience != null)
        {
            audience.Presenting = false;
            audience.Moved -= UpdatePresentWhere;
        }
        _presentOverlay.Visible = false;
        if (PresentFullScreen)
            DisplayServer.WindowSetMode(_presentWindow);
        _menuBar.Visible = true;
        _transport.Visible = true;
        _statusBar.Visible = true;
        _host.ShowBar = true;
        _host.Exclusive = null;
        SetSidePanels(_presentPanels);
        GD.Print("Present mode off");
    }

    /// <summary>Makes the overlay (once).</summary>
    private void BuildPresentOverlay()
    {
        if (_presentOverlay != null)
            return;
        _presentOverlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _presentOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        _captionBox = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _captionBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.55f),
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 6, ContentMarginBottom = 6,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        });
        _captionBox.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _captionBox.GrowHorizontal = Control.GrowDirection.Both;
        _captionBox.GrowVertical = Control.GrowDirection.Begin;
        _captionBox.OffsetBottom = -16;
        var lines = new VBoxContainer();
        _captionCue = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _captionCue.AddThemeFontSizeOverride("font_size", 26);
        _captionNext = new Label { HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1, 1, 1, 0.7f) };
        _captionNext.AddThemeFontSizeOverride("font_size", 16);
        lines.AddChild(_captionCue);
        lines.AddChild(_captionNext);
        _captionBox.AddChild(lines);
        _presentOverlay.AddChild(_captionBox);

        _presentWhere = new Label { Position = new Vector2(12, 8), Modulate = new Color(1, 1, 1, 0.75f) };
        _presentOverlay.AddChild(_presentWhere);
        _presentHint = new Label { Modulate = new Color(1, 1, 1, 0.8f), HorizontalAlignment = HorizontalAlignment.Center };
        _presentHint.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _presentHint.GrowHorizontal = Control.GrowDirection.Both;
        _presentHint.OffsetTop = 8;
        _presentOverlay.AddChild(_presentHint);
        AddChild(_presentOverlay);
    }

    /// <summary>Where the audience camera sits, top left.</summary>
    private void UpdatePresentWhere()
    {
        if (Presenting && _host.Find<AudienceView>() is { } audience)
            _presentWhere.Text = audience.Where;
    }

    /// <summary>The caption: the current cue (number and text) and the next one.</summary>
    private void UpdateCaption()
    {
        var stack = _player?.Stack;
        if (stack == null)
        {
            _captionCue.Text = "No cue stack";
            _captionNext.Text = "";
            return;
        }
        var steps = stack.Steps;
        _captionCue.Text = _player.Step < 0 ? "Before the first cue"
            : $"Cue {steps[_player.Step].CueNumberText}  {steps[_player.Step].Text}".TrimEnd() + (_player.Running ? "  ▶" : "");
        _captionNext.Text = _player.Step + 1 < steps.Count
            ? $"next: {steps[_player.Step + 1].CueNumberText}  {steps[_player.Step + 1].Text}".TrimEnd()
            : "end of the stack";
    }

    /// <summary>Every frame while presenting: caption and the hint's time.</summary>
    private void PollPresent(double delta)
    {
        if (!Presenting)
            return;
        UpdateCaption();
        if (_presentHintLeft > 0)
        {
            _presentHintLeft -= delta;
            _presentHint.Visible = _presentHintLeft > 0;
        }
    }

    /// <summary>Present mode's own keys (number keys = viewpoints, the caption, Esc back); true when used.</summary>
    private bool PresentKey(InputEvent e, Viewport vp)
    {
        if (KeyMap.Pressed(e, "present_toggle", vp))
        {
            TogglePresent();
            return true;
        }
        if (!Presenting)
            return false;
        if (KeyMap.Pressed(e, "deselect", vp))
        {
            StopPresenting();
            return true;
        }
        if (KeyMap.Pressed(e, "present_caption", vp))
        {
            _captionBox.Visible = !_captionBox.Visible;
            return true;
        }
        for (int i = 1; i <= 9; i++)
            if (KeyMap.Pressed(e, $"viewpoint_{i}", vp))
            {
                if (_host.Find<AudienceView>() is { } audience && !audience.SitAtViewpoint(i - 1))
                    _presentWhere.Text = $"{audience.Where}  (no viewpoint {i})";
                return true;
            }
        return false;
    }
}
