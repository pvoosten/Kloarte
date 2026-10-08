using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel: workspaces and tools (UI review 28 / 31, decided with the user). A workspace (view bar, key map
/// <c>workspace_rig</c> / <c>_focus</c> / <c>_blocking</c>, View menu) shows its list in the left panel: Fixtures for Rig
/// and Focus, Actors for Blocking. The Present button is Present mode. The key map and the gobo editor are tools in
/// windows of their own (<see cref="ToolWindow"/>), opened and closed with their keys or the Tools menu.
/// </summary>
public partial class FixturePanel
{
    /// <summary>The key map tool's window.</summary>
    internal ToolWindow KeymapTool { get; private set; }

    /// <summary>The gobo editor tool's window.</summary>
    internal ToolWindow GoboTool { get; private set; }

    /// <summary>The cue table's window.</summary>
    internal ToolWindow CueTool { get; private set; }

    /// <summary>The palettes' window.</summary>
    internal ToolWindow PaletteTool { get; private set; }

    /// <summary>Makes the tool windows (hidden) and follows the host's workspace; called once from _Ready.</summary>
    private void BuildTools()
    {
        KeymapTool = new ToolWindow(new KeymapView { Name = "Keymap" }, "view_keymap", new Vector2I(820, 640));
        GoboTool = new ToolWindow(new GoboEditorView { Name = "GoboEditor" }, "view_gobo", new Vector2I(900, 720));
        CueTool = new ToolWindow(new CueTableView { Name = "CueTable" }, "view_cues", new Vector2I(1200, 720));
        PaletteTool = new ToolWindow(new PaletteView { Name = "PaletteList" }, "view_palettes", new Vector2I(820, 640));
        AddChild(KeymapTool);
        AddChild(GoboTool);
        AddChild(CueTool);
        AddChild(PaletteTool);
        _host.WorkspaceChanged += ShowWorkspaceList;
        _host.PresentRequested += TogglePresent;
    }

    /// <summary>The tools' views get the (re)built show like the host's views.</summary>
    private void ConnectTools(ShowBuilder builder)
    {
        KeymapTool.View.SetBuilder(builder);
        GoboTool.View.SetBuilder(builder);
        CueTool.View.SetBuilder(builder);
        PaletteTool.View.SetBuilder(builder);
    }

    /// <summary>A tool's key opens or closes its window; true when used.</summary>
    private bool ToolKey(InputEvent e, Viewport vp)
    {
        foreach (var tool in new[] { KeymapTool, GoboTool, CueTool, PaletteTool })
            if (tool != null && KeyMap.Pressed(e, tool.Action, vp))
            {
                tool.Toggle();
                return true;
            }
        return false;
    }

    /// <summary>The left panel shows the workspace's list.</summary>
    private void ShowWorkspaceList(Workspace workspace)
    {
        int tab = workspace == Workspace.Blocking ? ActorsTab : 0;
        if (_tabs.CurrentTab != tab)
            _tabs.CurrentTab = tab;
    }
}
