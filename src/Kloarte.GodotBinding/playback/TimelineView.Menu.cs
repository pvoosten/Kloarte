using System;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// TimelineView, right-click menu (UI review 9): in an actor lane "Add step here" and, on a step, "Delete step"; on a
/// cue (cue lane or its time) "Hold after cue N..." with the seconds typed in a dialog (or the default hold). Edits are
/// named for undo.
/// </summary>
public partial class TimelineView
{
    /// <summary>The menu (rebuilt for every right click).</summary>
    private PopupMenu _menu;

    /// <summary>Menu item ids.</summary>
    private enum MenuItem
    {
        AddStep = 1,
        DeleteStep,
        Hold,
    }

    /// <summary>Where the menu was opened (view position), the step there and the cue segment there.</summary>
    private Vector2 _menuAt;
    private TimelineStep _menuStep;
    private TimelineSegment _menuSegment;

    /// <summary>Shows the menu for a right click at `p`.</summary>
    private void ShowMenu(Vector2 p)
    {
        if (_menu == null)
        {
            _menu = new PopupMenu();
            _menu.IdPressed += OnMenu;
            AddChild(_menu);
        }
        _menu.Clear();
        _menuAt = p;
        _menuStep = p.Y >= RulerHeight + CueLane ? StepAt(p).Step : null;
        int segment = p.X > Gutter ? Timeline.SegmentAt(T(p.X)) : -1;
        _menuSegment = segment >= 0 ? Timeline.Segments[segment] : null;
        if (p.Y >= RulerHeight + CueLane && p.X > Gutter)
        {
            _menu.AddItem("Add step here", (int)MenuItem.AddStep);
            if (_menuStep != null)
                _menu.AddItem($"Delete step {_menuStep.StepIndex + 1}", (int)MenuItem.DeleteStep);
        }
        if (_menuSegment != null)
            _menu.AddItem($"Hold after cue {_menuSegment.CueNumber}...", (int)MenuItem.Hold);
        if (_menu.ItemCount == 0)
            return;
        _menu.ResetSize();
        _menu.Popup(new Rect2I((Vector2I)GetViewport().GetMousePosition(), Vector2I.Zero));
    }

    /// <summary>A menu item was chosen.</summary>
    private void OnMenu(long id)
    {
        switch ((MenuItem)(int)id)
        {
            case MenuItem.AddStep:
                Builder.Files.NameNextEdit("Add step");
                AddStepAt(_menuAt);
                break;
            case MenuItem.DeleteStep when _menuStep != null:
                var seg = Timeline.Segments[_menuStep.Segment];
                Builder.Files.NameNextEdit("Delete step");
                Builder.Model.Blocking.RemoveStep(seg.CueId, _menuStep.ActorId, _menuStep.StepIndex);
                Player.Invalidate();
                Selection.SelectActor(_menuStep.ActorId, Math.Max(0, _menuStep.StepIndex - 1));
                break;
            case MenuItem.Hold when _menuSegment != null:
                AskHold(_menuSegment);
                break;
        }
        QueueRedraw();
    }

    /// <summary>Asks the hold after a cue in seconds (or the player's default hold).</summary>
    private void AskHold(TimelineSegment segment)
    {
        var holds = Builder.Model.Blocking.Holds;
        var dialog = new ConfirmationDialog { Title = $"Hold after cue {segment.CueNumber}", OkButtonText = "Set" };
        dialog.AddButton("Default hold", false, "default");
        var seconds = new SpinBox
        {
            MinValue = 0,
            MaxValue = 600,
            Step = 0.1,
            Value = holds.TryGetValue(segment.CueId, out float hold) ? hold : Player.Hold,
            Suffix = "s",
            CustomMinimumSize = new Vector2(160, 0),
        };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = $"Wait after cue {segment.CueNumber} {segment.Text} until the next GO:" });
        box.AddChild(seconds);
        dialog.AddChild(box);
        dialog.Confirmed += () =>
        {
            Builder.Files.NameNextEdit($"Hold after cue {segment.CueNumber}");
            holds[segment.CueId] = (float)seconds.Value;
            Player.Invalidate();
            QueueRedraw();
            dialog.QueueFree();
        };
        dialog.CustomAction += action =>
        {
            if (action != "default")
                return;
            Builder.Files.NameNextEdit($"Default hold after cue {segment.CueNumber}");
            holds.Remove(segment.CueId);
            Player.Invalidate();
            QueueRedraw();
            dialog.Hide();
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
        seconds.GetLineEdit().GrabFocus();
        seconds.GetLineEdit().SelectAll();
    }
}
