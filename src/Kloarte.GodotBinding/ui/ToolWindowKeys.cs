using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The keys of a <see cref="ToolWindow"/>: a node inside the tool's window, so it gets that window's unhandled keys (the
/// window node itself gets the main window's). The tool's view takes its keys first; other keys go on to the main
/// window (GO, Save, the tool's own key to close it).
/// </summary>
public partial class ToolWindowKeys : Node
{
    /// <summary>The tool's view.</summary>
    private readonly ViewBase _view;

    /// <summary>Creates the key node for a tool's view.</summary>
    internal ToolWindowKeys(ViewBase view)
    {
        _view = view;
        Name = "Keys";
    }

    /// <summary>Godot needs a parameterless constructor for scripts; not used.</summary>
    public ToolWindowKeys() { }

    /// <summary>The tool's keys first; other keys go to the main window.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (_view == null || e is not InputEventKey { Pressed: true })
            return;
        GetViewport().SetInputAsHandled();
        if (!_view.HandleKey(e))
            GetTree().Root.PushInput(e, true);
    }
}
