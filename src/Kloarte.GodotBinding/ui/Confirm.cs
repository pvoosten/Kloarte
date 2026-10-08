using System;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Asks before something that can't be undone (UI review 7: until there is undo, removing lamps, stands, actors and
/// movement, resetting all keys, and restarting MagicQ PC are confirmed). One dialog per question, freed when answered.
/// </summary>
internal static class Confirm
{
    /// <summary>Name of the dialog node (tests find and answer it).</summary>
    internal const string NodeName = "Confirm";

    /// <summary>
    /// Shows "text" with the buttons `yes` and Cancel under `owner`; `onYes` runs when `yes` is pressed.
    /// </summary>
    internal static ConfirmationDialog Ask(Node owner, string title, string text, string yes, Action onYes)
    {
        var dialog = new ConfirmationDialog
        {
            Name = NodeName,
            Title = title,
            DialogText = text,
            OkButtonText = yes,
            CancelButtonText = "Cancel",
            DialogAutowrap = true,
        };
        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            onYes();
        };
        dialog.Canceled += dialog.QueueFree;
        owner.AddChild(dialog, true);
        dialog.PopupCentered(new Vector2I(460, 0));
        return dialog;
    }
}
