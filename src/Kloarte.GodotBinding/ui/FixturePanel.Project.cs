using System;
using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, project box at the top of the left panel (user: see which project is open and where): the project's
/// name and folder, the show file and its folder; a click on a folder shows it in Explorer. The window title names the
/// project. The project items of the File menu (FixturePanel.Menu.cs): New project (a new show: the house venue, no lamps), New project from show (a copy of a
/// .shw + .xhw; the original is never written), Open project (most recently saved first), Open project folder (any folder,
/// a folder dialog), Import MagicQ show (.shw).
/// </summary>
public partial class FixturePanel
{
    /// <summary>Project name / show file lines.</summary>
    private Label _projectName, _showName;

    /// <summary>The project's folder and the show's folder (click: show in Explorer).</summary>
    private Button _projectFolder, _showFolder;

    /// <summary>The window title without a project.</summary>
    private string _appTitle;

    /// <summary>Project menu item ids.</summary>
    private enum ProjectItem
    {
        New = 1,
        NewFromShow,
        OpenShow,
        OpenFolder,
        Open = 100,
    }

    /// <summary>Most projects in the Open project list.</summary>
    private const int ProjectListLength = 15;

    /// <summary>The Open project submenu (filled each time the menu opens).</summary>
    private PopupMenu _projectList;

    /// <summary>The project names behind <see cref="_projectList"/>'s items.</summary>
    private readonly System.Collections.Generic.List<string> _projectNames = new();

    /// <summary>Asks for a new project's name.</summary>
    private ConfirmationDialog _nameDialog;

    /// <summary>The name field and the error line of <see cref="_nameDialog"/>.</summary>
    private LineEdit _nameEdit;
    private Label _nameError;

    /// <summary>The show a new project starts from (null: a new show).</summary>
    private string _newFrom;

    /// <summary>Picks the show for "New project from show".</summary>
    private FileDialog _fromShowDialog;

    /// <summary>Picks a project's folder ("Open project folder").</summary>
    private FileDialog _folderDialog;

    /// <summary>Adds the project items to a menu (the File menu) and makes the project dialogs.</summary>
    private void AddProjectItems(PopupMenu popup)
    {
        popup.AddItem("New project...", (int)ProjectItem.New);
        popup.AddItem("New project from show...", (int)ProjectItem.NewFromShow);
        _projectList = new PopupMenu { Name = "Projects" };
        _projectList.IdPressed += id =>
        {
            string name = _projectNames[(int)id - (int)ProjectItem.Open];
            AskToSave($"opening project {name}", () => OpenProject(name));
        };
        popup.AddChild(_projectList);
        popup.AddSubmenuNodeItem("Open project", _projectList);
        popup.AddItem("Open project folder...", (int)ProjectItem.OpenFolder);
        popup.SetItemTooltip(popup.GetItemIndex((int)ProjectItem.OpenFolder), "Choose a project's folder, anywhere (a folder with a .kloarte.yaml, or a show's rig.yaml)");
        KeyMap.Changed += () => ShowKeys(popup, (int)ProjectItem.OpenFolder, "project_open_folder");
        ShowKeys(popup, (int)ProjectItem.OpenFolder, "project_open_folder");
        popup.AddItem("Import MagicQ show (.shw)...", (int)ProjectItem.OpenShow);
        popup.SetItemTooltip(popup.GetItemIndex((int)ProjectItem.OpenShow), "A MagicQ show of this project: re-import it (what differs is listed); another: its project, or a new one");
        KeyMap.Changed += () => ShowKeys(popup, (int)ProjectItem.OpenShow, "file_open");
        ShowKeys(popup, (int)ProjectItem.OpenShow, "file_open");
        popup.AboutToPopup += FillProjectList;
        popup.IdPressed += id =>
        {
            switch ((ProjectItem)(int)id)
            {
                case ProjectItem.New:
                    AskToSave("making a new project", () => AskProjectName(null));
                    break;
                case ProjectItem.NewFromShow:
                    AskToSave("making a new project", PickShowForProject);
                    break;
                case ProjectItem.OpenShow:
                    AskToSave("opening another show", ShowOpenDialog);
                    break;
                case ProjectItem.OpenFolder:
                    AskToSave("opening another project", ShowProjectFolderDialog);
                    break;
            }
        };

        _fromShowDialog = new FileDialog
        {
            Title = "New project from show",
            ModeOverridesTitle = false,   // keep the title (else "Open a File")
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.shw ; MagicQ show" },
            UseNativeDialog = true,
        };
        _fromShowDialog.FileSelected += AskProjectName;
        AddChild(_fromShowDialog);

        _folderDialog = new FileDialog
        {
            Title = "Open project folder",
            ModeOverridesTitle = false,   // keep the title (else "Select a Directory")
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
        };
        _folderDialog.DirSelected += OpenProjectFolder;
        AddChild(_folderDialog);

        _nameDialog = new ConfirmationDialog { Title = "New project", OkButtonText = "Create", DialogHideOnOk = false };
        var box = new VBoxContainer();
        _nameEdit = new LineEdit { PlaceholderText = "Project name", CustomMinimumSize = new Vector2(360, 0) };
        _nameEdit.TextSubmitted += _ => CreateProject();
        // A wrapping label needs a width, else the dialog grows as tall as the text in a one-character column.
        _nameError = new Label
        {
            Modulate = new Color(1f, 0.45f, 0.4f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(360, 0),
        };
        box.AddChild(new Label { Text = "Project name (also the name of its exported MagicQ shows):" });
        box.AddChild(_nameEdit);
        box.AddChild(_nameError);
        _nameDialog.AddChild(box);
        _nameDialog.Confirmed += CreateProject;
        AddChild(_nameDialog);
    }

    /// <summary>"New project from show": picks the show (then the name).</summary>
    private void PickShowForProject()
    {
        _fromShowDialog.CurrentDir = CurrentBuilder?.Files.ShowFolder ?? "";
        NativeFileDialogs.Popup(_fromShowDialog, new Vector2I(900, 600));
    }

    /// <summary>Lists the projects, most recently saved first, the open one checked.</summary>
    private void FillProjectList()
    {
        _projectList.Clear();
        _projectNames.Clear();
        // Also without an open show (the start screen's situation): any show builder's files know the projects.
        var files = CurrentBuilder?.Files ?? Builders().FirstOrDefault()?.Services?.Files;
        if (files == null)
            return;
        foreach (var project in files.AllProjects().Take(ProjectListLength))
        {
            int id = (int)ProjectItem.Open + _projectNames.Count;
            _projectList.AddCheckItem(project.Name, id);
            _projectList.SetItemChecked(_projectList.GetItemIndex(id), project.Name == files.ProjectName);
            _projectList.SetItemTooltip(_projectList.GetItemIndex(id), $"{project.Folder}\nShow: {project.ShowFile}");
            _projectNames.Add(project.Name);
        }
        if (_projectNames.Count == 0)
        {
            _projectList.AddItem("(no saved projects)");
            _projectList.SetItemDisabled(0, true);
        }
    }

    /// <summary>Asks for the name of a new project (from `fromShow`, or a new show when null).</summary>
    private void AskProjectName(string fromShow)
    {
        _newFrom = fromShow;
        _nameDialog.Title = fromShow == null ? "New project" : $"New project from {System.IO.Path.GetFileName(fromShow)}";
        _nameEdit.Text = CurrentBuilder?.Files.ProposeProjectName(fromShow) ?? "";
        _nameError.Text = fromShow == null
            ? "A new MagicQ show with the house venue (CC De Brouckere), no lamps."
            : "The show is copied (with its .xhw) under the project's name; the original is not changed.";
        _nameError.Modulate = new Color(0.7f, 0.7f, 0.7f);
        _nameDialog.PopupCentered();
        _nameEdit.GrabFocus();
        _nameEdit.SelectAll();
    }

    /// <summary>Makes the project named in the dialog; a refused name keeps the dialog open with the reason.</summary>
    private async void CreateProject()
    {
        string name = _nameEdit.Text.Trim();
        string from = _newFrom;
        string error = null;
        bool ok = await ChangeShowAsync($"create project {name}", builder =>
        {
            try
            {
                builder.NewProject(name, from);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                error = e.Message;
                throw;
            }
        });
        if (!ok && error != null)
        {
            _nameError.Text = error;
            _nameError.Modulate = new Color(1f, 0.45f, 0.4f);
            return;
        }
        _nameDialog.Hide();
        if (ok)
            _status.Text = $"New project {name}";
    }

    /// <summary>Opens a project by name.</summary>
    private async void OpenProject(string name)
    {
        if (await ChangeShowAsync($"open project {name}", builder => builder.OpenProject(name)))
            _status.Text = $"Opened project {name}";
    }

    /// <summary>
    /// "Open project folder": a folder dialog, starting at the open project's folder's parent (else the projects folder).
    /// </summary>
    private void ShowProjectFolderDialog()
    {
        var files = CurrentBuilder?.Files ?? Builders().FirstOrDefault()?.Services?.Files;
        string current = files?.ProjectFolder ?? "";
        string dir = current.Length > 0 ? System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(current))
            : Kloarte.Model.AppPaths.File(Kloarte.Model.AppPaths.Projects);
        if (System.IO.Directory.Exists(dir))
            _folderDialog.CurrentDir = dir;
        NativeFileDialogs.Popup(_folderDialog, new Vector2I(900, 600));
    }

    /// <summary>Opens the project in a chosen folder.</summary>
    private async void OpenProjectFolder(string folder)
    {
        string name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(folder));
        if (await ChangeShowAsync($"open project {name}", builder => builder.OpenProjectFolder(folder)))
            _status.Text = $"Opened project {CurrentBuilder?.ProjectName ?? name} ({folder})";
    }

    /// <summary>The project box (filled by <see cref="UpdateProjectBox"/>).</summary>
    private Control ProjectBox()
    {
        var box = new VBoxContainer();
        _projectName = new Label { MouseFilter = Control.MouseFilterEnum.Stop };   // tooltip: unsaved parts
        _projectFolder = FolderButton();
        _showName = new Label();
        _showFolder = FolderButton();
        box.AddChild(_projectName);
        box.AddChild(_projectFolder);
        box.AddChild(_showName);
        box.AddChild(_showFolder);
        return box;
    }

    /// <summary>A flat button showing a folder; pressed: the folder (or file) in Explorer.</summary>
    private static Button FolderButton()
    {
        var button = new Button
        {
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            Modulate = new Color(0.75f, 0.85f, 1f),
            FocusMode = Control.FocusModeEnum.None,
        };
        button.Pressed += () =>
        {
            if (button.GetMeta("path", "").AsString() is { Length: > 0 } path)
                OS.ShellShowInFileManager(path);
        };
        return button;
    }

    /// <summary>Points a folder button at `path` (shown in Explorer when pressed); `text` is what it shows.</summary>
    private static void SetFolder(Button button, string path, string text, string tooltip)
    {
        button.SetMeta("path", path ?? "");
        UiFit.ShowPath(button, text);
        button.TooltipText = tooltip;
        button.Disabled = string.IsNullOrEmpty(path);
    }

    /// <summary>Shows the open project and show (or none) and names the project in the window title.</summary>
    private void UpdateProjectBox()
    {
        _appTitle ??= GetWindow().Title;
        var builder = CurrentBuilder;
        var files = builder?.Model != null ? builder.Files : null;
        if (files == null || string.IsNullOrEmpty(files.ProjectName))
        {
            _projectName.Text = "No project";
            SetFolder(_projectFolder, null, "", "");
            _showName.Text = "";
            SetFolder(_showFolder, null, "", "");
            GetWindow().Title = _appTitle;
            return;
        }
        bool saved = files.ProjectSaved;
        string dot = _unsaved.Count > 0 ? " \u2022" : "";
        _projectName.Text = $"Project: {files.ProjectName}{dot}{(saved ? "" : " (not saved yet)")}";
        _projectName.TooltipText = _unsaved.Count > 0 ? $"Unsaved changes: {string.Join(", ", _unsaved)} {KeyMap.Hint("Save", "file_save")}" : "All saved";
        string folder = files.ProjectFolder;
        string existing = saved ? folder : System.IO.Path.GetDirectoryName(folder);
        SetFolder(_projectFolder, System.IO.Directory.Exists(existing) ? existing : null, FolderText(folder),
            saved ? $"{folder}\nClick: open in Explorer" : $"{folder}\nCreated when the project is first saved");

        if (files.Model?.Show is Kloarte.Model.IProjectShow)
        {
            // A native show is the project's own files: no show file to name (a MagicQ show is only imported / exported).
            bool own = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(files.ShowFile))
                is { } dir && string.Equals(dir, System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase);
            _showName.Text = own ? "" : "Show: imported; Save writes it into the project";
            _showName.TooltipText = own ? "" : "The show of this project is still the MagicQ show it was made from: Save makes it the project's own (rig.yaml, palettes.yaml, cues.yaml)";
            SetFolder(_showFolder, null, "", "");
            GetWindow().Title = $"{(_unsaved.Count > 0 ? "• " : "")}{files.ProjectName} - {_appTitle}";
            return;
        }
        string show = System.IO.Path.GetFullPath(files.SourceFile);
        _showName.Text = $"Show: {System.IO.Path.GetFileName(show)}{(files.Companion is { } companion ? $" + {companion}" : "")}" +
                         (files.IsWorkingCopy ? " (MagicQ's last show)" : "");
        SetFolder(_showFolder, System.IO.File.Exists(show) ? show : null, FolderText(System.IO.Path.GetDirectoryName(show)),
            $"{show}\nClick: show in Explorer");
        GetWindow().Title = $"{(_unsaved.Count > 0 ? "\u2022 " : "")}{files.ProjectName} - {_appTitle}";
    }

    /// <summary>
    /// A folder as the user knows it: "Documents\..." for the Documents folder (also when OneDrive keeps it), else the
    /// full path (the button trims it at the start to fit, <see cref="UiFit.ShowPath"/>).
    /// </summary>
    internal static string FolderText(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        string full = System.IO.Path.GetFullPath(path);
        string documents = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents) && full.StartsWith(documents + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            full = "Documents" + full[documents.Length..];
        return full;
    }
}
