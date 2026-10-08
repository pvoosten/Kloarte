using System;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// TimelineView, the lane header (UI review 28, Blocking workspace): "+ Actor" (man / woman) at the top of the names,
/// and on an actor's name a click selects the actor, a double click renames it and a right click opens the actor menu
/// (rename, remove, clear its movement in this cue). The panel does the work (the same as the Actors list).
/// </summary>
public partial class TimelineView
{
    /// <summary>Raised by "+ Actor": true for a woman.</summary>
    public event Action<bool> AddActorRequested;

    /// <summary>Raised by a double click on an actor's name: the actor's id.</summary>
    public event Action<string> RenameActorRequested;

    /// <summary>Raised by a right click on an actor's name: the actor's id and the mouse position in the window.</summary>
    public event Action<string, Vector2> ActorMenuRequested;

    /// <summary>The "+ Actor" button (hidden on a card).</summary>
    private MenuButton _addActor;

    /// <summary>Makes the "+ Actor" button over the ruler's left end; called from _Ready.</summary>
    private void BuildLaneHeader()
    {
        _addActor = new MenuButton
        {
            Text = "+ Actor",
            Flat = false,
            Position = new Vector2(4, 2),
            Size = new Vector2(Gutter - 8, RulerHeight - 4),
            TooltipText = "Add an actor (a lane): a man or a woman",
        };
        var popup = _addActor.GetPopup();
        popup.AddItem("Man", 0);
        popup.AddItem("Woman", 1);
        popup.IdPressed += id => AddActorRequested?.Invoke(id == 1);
        AddChild(_addActor);
    }

    /// <summary>The actor whose name is at a point of the lane header (the gutter), or null.</summary>
    private string ActorNameAt(Vector2 p)
    {
        if (p.X >= Gutter || p.Y < RulerHeight + CueLane)
            return null;
        var actors = ActorIds;
        int a = (int)((p.Y - RulerHeight - CueLane) / LaneHeight(actors.Count));
        return a >= 0 && a < actors.Count ? actors[a] : null;
    }

    /// <summary>Mouse on an actor's name: select, rename (double click), actor menu (right click); true when used.</summary>
    private bool LaneHeaderInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } button || ActorNameAt(button.Position) is not { } id)
            return false;
        switch (button.ButtonIndex)
        {
            case MouseButton.Left when button.DoubleClick:
                RenameActorRequested?.Invoke(id);
                break;
            case MouseButton.Left:
                GrabFocus();
                Selection.SelectActor(id, 0);
                break;
            case MouseButton.Right:
                ActorMenuRequested?.Invoke(id, GetViewport().GetMousePosition());
                break;
            default:
                return false;
        }
        return true;
    }

    /// <summary>What the mouse can do on an actor's name.</summary>
    public override string _GetTooltip(Vector2 atPosition) =>
        ActorNameAt(atPosition) != null
            ? "Click: select the actor · double click: rename · right click: rename, remove, clear its movement"
            : TooltipText;
}
