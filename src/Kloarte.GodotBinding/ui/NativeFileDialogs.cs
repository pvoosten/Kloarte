using System.Collections.Generic;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The app's native (Windows) file dialogs: which are open, logged when they open and close. Godot 4.7.2 hangs at
/// exit while a native file dialog is open (its display server destructor asks the dialog thread to close and waits
/// for it, but the request never reaches the dialog), so <see cref="FixturePanel"/> refuses a window close while
/// <see cref="AnyOpen"/> (a release build froze like that, 10 Oct 2026: main thread joining the dialog thread).
/// </summary>
internal static class NativeFileDialogs
{
    /// <summary>The dialogs that are open now.</summary>
    private static readonly HashSet<FileDialog> Open = new();

    /// <summary>Dialogs whose close signals are already connected.</summary>
    private static readonly HashSet<FileDialog> Tracked = new();

    /// <summary>True while one of the app's native file dialogs is open.</summary>
    internal static bool AnyOpen
    {
        get
        {
            Open.RemoveWhere(d => !GodotObject.IsInstanceValid(d));
            return Open.Count > 0;
        }
    }

    /// <summary>Titles of the open dialogs, for messages.</summary>
    internal static string OpenTitles => string.Join(", ", System.Linq.Enumerable.Select(Open, d => $"\"{d.Title}\""));

    /// <summary>Shows <paramref name="dialog"/> centred and remembers that it is open until it gives a result or is
    /// cancelled.</summary>
    internal static void Popup(FileDialog dialog, Vector2I size)
    {
        if (Tracked.Add(dialog))
        {
            dialog.FileSelected += _ => Closed(dialog, "file selected");
            dialog.FilesSelected += _ => Closed(dialog, "files selected");
            dialog.DirSelected += _ => Closed(dialog, "folder selected");
            dialog.Canceled += () => Closed(dialog, "cancelled");
        }
        Open.Add(dialog);
        GD.Print($"File dialog \"{dialog.Title}\" opened ({dialog.CurrentDir})");
        dialog.PopupCentered(size);
    }

    /// <summary>A dialog gave its result or was cancelled.</summary>
    private static void Closed(FileDialog dialog, string how)
    {
        Open.Remove(dialog);
        GD.Print($"File dialog \"{dialog.Title}\" closed: {how}");
    }
}
