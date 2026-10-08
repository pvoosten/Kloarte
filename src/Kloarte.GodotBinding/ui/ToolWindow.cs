using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// A tool in a window of its own (UI review 31, decided with the user: the key map and the gobo editor are tools, not
/// views): a native OS window with the tool's view and a status bar, opened and closed with the tool's key (key map),
/// the Tools menu or the window's close button; it stays above Kloarte's window. Keys the tool doesn't use go on to the
/// main window (<see cref="ToolWindowKeys"/>).
/// </summary>
public partial class ToolWindow : Window
{
    /// <summary>The tool's view, always its main view.</summary>
    public ViewBase View { get; }

    /// <summary>Key map action that opens and closes the window (e.g. "view_gobo").</summary>
    public string Action { get; }

    /// <summary>The window was shown before (later it opens where the user left it).</summary>
    private bool _placed;

    /// <summary>Creates the window (hidden) for a tool's view.</summary>
    internal ToolWindow(ViewBase view, string action, Vector2I size)
    {
        View = view;
        Action = action;
        Name = view.Name + "Window";
        Title = $"{view.Title} - Kloarte";
        Visible = false;   // before ForceNative: Godot refuses to change it on a shown window
        Size = size;
        MinSize = new Vector2I(480, 360);
        ForceNative = true;
        Transient = true;
        WrapControls = false;
    }

    /// <summary>Godot needs a parameterless constructor for scripts; not used.</summary>
    public ToolWindow() { }

    /// <summary>The view over the window, the status bar at the bottom.</summary>
    public override void _Ready()
    {
        if (View == null)
            return;
        var bg = new Panel();
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);
        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 0);
        View.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        View.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(View);
        box.AddChild(new StatusBar { Name = "StatusBar" });
        AddChild(box);
        AddChild(new ToolWindowKeys(View));
        View.SetRole(true, false);
        CloseRequested += Hide;
    }

    /// <summary>Opens the window (centred the first time) and gives it the keyboard, or closes it when it is open.</summary>
    internal void Toggle()
    {
        if (Visible)
        {
            Hide();
            return;
        }
        // The UI scale of the preferences (UI review 18), also in a native window.
        float scale = Preferences.Current.EffectiveScale;
        if (!Mathf.IsEqualApprox(ContentScaleFactor, scale))
        {
            if (!_placed)
                Size = (Vector2I)((Vector2)Size * (scale / ContentScaleFactor));
            ContentScaleFactor = scale;
        }
        if (_placed)
            Show();
        else
        {
            PopupCentered(Size);
            _placed = true;
        }
        GrabFocus();
        if (View.FocusMode != Control.FocusModeEnum.None)
            View.GrabFocus();
    }
}
