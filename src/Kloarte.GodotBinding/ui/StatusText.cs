using System;

namespace Kloarte.GodotBinding;

/// <summary>
/// A status message of the panel or a view: setting <see cref="Text"/> shows it in the window's one status bar
/// (<see cref="StatusBar"/>, UI review 15) instead of a label of its own.
/// </summary>
internal sealed class StatusText
{
    /// <summary>The last message set here.</summary>
    private string _text = "";

    /// <summary>The message; setting it shows it in the status bar (and its history).</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? "";
            StatusBar.Post(_text);
        }
    }

    /// <summary>Shows a message whose click runs `link` (an error with its file: the click opens the file).</summary>
    /// <param name="text">The message.</param>
    /// <param name="link">What a click on the message does.</param>
    public void Post(string text, Action link)
    {
        _text = text ?? "";
        StatusBar.Post(_text, link);
    }

    /// <summary>Shows a message whose click runs `link`; `linkHint` says what a click does.</summary>
    /// <param name="text">The message.</param>
    /// <param name="link">What a click on the message does.</param>
    /// <param name="linkHint">The tooltip's line about the click ("Click: open the folder").</param>
    public void Post(string text, Action link, string linkHint)
    {
        _text = text ?? "";
        StatusBar.Post(_text, link, linkHint);
    }
}
