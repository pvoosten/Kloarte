using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The start screen (UI review 5): when no show is open (MagicQ's last show could not be opened at start, user: that
/// one comes first), the view area shows the recent projects (most recently saved first; a click opens one) and New
/// project, New project from show and Open show.
/// </summary>
public partial class FixturePanel
{
    /// <summary>Most projects on the start screen.</summary>
    private const int StartScreenProjects = 8;

    /// <summary>The start screen and its project list.</summary>
    private PanelContainer _startScreen;
    private VBoxContainer _startProjects;

    /// <summary>The start screen is shown (tests).</summary>
    internal bool StartScreenShown => _startScreen?.Visible == true;

    /// <summary>The projects listed on the start screen (tests).</summary>
    internal string[] StartScreenProjectNames =>
        _startProjects?.GetChildren().OfType<Button>().Select(b => b.Text).ToArray() ?? System.Array.Empty<string>();

    /// <summary>Makes the start screen (hidden) over the view area.</summary>
    private void BuildStartScreen()
    {
        _startScreen = new PanelContainer { Name = "StartScreen", Visible = false };
        var center = new CenterContainer();
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        box.AddThemeConstantOverride("separation", 8);
        var title = new Label { Text = "Kloarte up de plankn", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(title);
        box.AddChild(new Label
        {
            Text = "No show is open",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.7f, 0.7f),
        });
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Recent projects", Modulate = new Color(1f, 0.75f, 0.4f) });
        _startProjects = new VBoxContainer();
        box.AddChild(_startProjects);
        box.AddChild(new HSeparator());
        var newProject = new Button { Text = "New project...", TooltipText = "A new show with the house venue, no lamps" };
        newProject.Pressed += () => AskProjectName(null);
        var fromShow = new Button { Text = "New project from show...", TooltipText = "A MagicQ show imported as a new project (the show itself is never written)" };
        fromShow.Pressed += PickShowForProject;
        var openShow = new Button { Text = "Import MagicQ show (.shw)..." };
        KeyMap.Tooltip(openShow, "Open a MagicQ show with its project (or a new one)", "file_open");
        openShow.Pressed += ShowOpenDialog;
        var openFolder = new Button { Text = "Open project folder..." };
        KeyMap.Tooltip(openFolder, "Choose a project's folder, anywhere", "project_open_folder");
        openFolder.Pressed += ShowProjectFolderDialog;
        box.AddChild(openFolder);
        box.AddChild(newProject);
        box.AddChild(fromShow);
        box.AddChild(openShow);
        center.AddChild(box);
        _startScreen.AddChild(center);
        _host.ShowOverlay(_startScreen);
    }

    /// <summary>Shows the start screen when no show is open (the project list filled), else hides it.</summary>
    internal void RefreshStartScreen()
    {
        if (_startScreen == null)
            return;
        bool show = CurrentBuilder?.Model == null;
        if (show && !_startScreen.Visible)
            FillStartProjects();
        _startScreen.Visible = show;
    }

    /// <summary>Shows the start screen whatever is open (tests).</summary>
    internal void ShowStartScreen()
    {
        FillStartProjects();
        _startScreen.Visible = true;
    }

    /// <summary>Lists the recent projects as buttons that open them.</summary>
    private void FillStartProjects()
    {
        foreach (Node child in _startProjects.GetChildren())
        {
            _startProjects.RemoveChild(child);
            child.QueueFree();
        }
        var files = Builders().FirstOrDefault()?.Services?.Files;
        var projects = files?.AllProjects().Take(StartScreenProjects).ToList() ?? new();
        foreach (var project in projects)
        {
            var button = new Button
            {
                Text = project.Name,
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"{project.Folder}\nShow: {project.ShowFile}" +
                              (project.Saved != default ? $"\nSaved {project.Saved.ToLocalTime():d MMM yyyy HH:mm}" : ""),
            };
            string name = project.Name;
            button.Pressed += () => OpenProject(name);
            _startProjects.AddChild(button);
        }
        if (projects.Count == 0)
            _startProjects.AddChild(new Label { Text = "(no saved projects yet)", Modulate = new Color(0.7f, 0.7f, 0.7f) });
    }
}
