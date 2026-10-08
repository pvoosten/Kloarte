using System;
using System.IO;
using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The export dialog (UI review 32, user: also per actor): what (stills, movie, contact sheet), from where (the 3D
/// view's camera, the audience view's seat or a named viewpoint such as Regie), which cues (from / to), cue captions,
/// the picture size, one export per actor, and where to. File > Export... (and the key frame / movie keys and the
/// timeline's buttons, with that kind chosen).
/// </summary>
public partial class FixturePanel
{
    /// <summary>The export dialog and its file / folder picker (made on first use).</summary>
    private ConfirmationDialog _exportDialog;
    private FileDialog _exportTargetDialog;

    /// <summary>The show the exports are made of.</summary>
    internal ShowBuilder ExportBuilder => CurrentBuilder;

    /// <summary>The export dialog's options as last confirmed (tests).</summary>
    internal ExportOptions LastExport { get; private set; }

    /// <summary>Opens the export dialog with `kind` chosen.</summary>
    private void ShowExportDialog(ExportKind kind)
    {
        var builder = CurrentBuilder;
        var stack = _player?.Stack;
        if (builder?.Model == null || stack == null || stack.Steps.Count == 0)
        {
            _status.Text = "No show / cue stack to export (Playback tab)";
            return;
        }
        _exportDialog?.QueueFree();
        _exportDialog = new ConfirmationDialog { Title = "Export", Name = "ExportDialog", OkButtonText = "Export" };
        var grid = new GridContainer { Columns = 2, CustomMinimumSize = new Vector2(460, 0) };
        grid.AddThemeConstantOverride("h_separation", 12);
        _exportDialog.AddChild(grid);

        var what = new OptionButton { TooltipText = "Stills: a PNG at every GO, fade end and actor step (+ index.csv); " +
                                                    "Movie: the show as it plays; Contact sheet: a PDF with every cue" };
        what.AddItem("Stills (PNG)");
        what.AddItem("Movie");
        what.AddItem("Contact sheet (PDF)");
        what.Select((int)kind);
        AddPreference(grid, "Export", what);

        var viewpoints = ExportViewpoints();
        var from = new OptionButton { TooltipText = "Where the camera is: the 3D view's camera, the audience view's seat or a named viewpoint of the venue" };
        foreach (var (_, name) in viewpoints)
            from.AddItem(name);
        AddPreference(grid, "Viewpoint", from);

        var first = new OptionButton { TooltipText = "The first cue (Start = the state before the first GO)" };
        var last = new OptionButton { TooltipText = "The last cue (with its hold)" };
        first.AddItem("Start (before the first cue)");
        foreach (var step in stack.Steps)
        {
            string text = $"{step.CueNumberText}  {step.Text}".TrimEnd();
            first.AddItem(text);
            last.AddItem(text);
        }
        first.Select(0);
        last.Select(stack.Steps.Count - 1);
        AddPreference(grid, "From cue", first);
        AddPreference(grid, "To cue", last);

        var captions = new CheckBox { Text = "Cue captions", ButtonPressed = true, TooltipText = "The cue number and text in the picture (the contact sheet prints them under each picture)" };
        AddPreference(grid, "", captions);
        int actors = builder.Model.Blocking.Actors.Count;
        var perActor = new CheckBox
        {
            Text = "One per actor",
            Disabled = actors == 0,
            TooltipText = actors == 0 ? "The show has no actors (Actors tab)"
                : $"An export for each of the {actors} actors: only their name on their shirt, their name in the caption",
        };
        AddPreference(grid, "", perActor);

        var size = new OptionButton { TooltipText = "Picture size in pixels (the default is in File > Preferences)" };
        foreach (string r in Preferences.Resolutions)
            size.AddItem(r.Replace("x", " x "));
        size.Select(Math.Max(0, Array.IndexOf(Preferences.Resolutions, Preferences.Current.MovieResolution)));
        AddPreference(grid, "Size", size);

        var target = new LineEdit { TooltipText = "The folder (stills, contact sheet) or the movie file (.avi)" };
        var browse = new Button { Text = "...", TooltipText = "Choose" };
        var targetRow = new HBoxContainer();
        target.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        targetRow.AddChild(target);
        targetRow.AddChild(browse);
        AddPreference(grid, "To", targetRow);

        string exports = Path.Combine(string.IsNullOrEmpty(builder.Files.ProjectFolder)
            ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) : builder.Files.ProjectFolder, "exports");
        string DefaultTarget(ExportKind k) => k switch
        {
            ExportKind.Movie => Path.Combine(exports, FileSafe(builder.ProjectName.Length > 0 ? builder.ProjectName : "show") + ".avi"),
            ExportKind.Stills => Path.Combine(exports, "stills"),
            _ => exports,
        };
        target.Text = DefaultTarget(kind);
        what.ItemSelected += i =>
        {
            target.Text = DefaultTarget((ExportKind)(int)i);
            captions.Text = (ExportKind)(int)i == ExportKind.ContactSheet ? "Cue captions (under the pictures)" : "Cue captions";
        };
        browse.Pressed += () => ChooseExportTarget((ExportKind)what.Selected, target);

        _exportDialog.Confirmed += async () =>
        {
            int to = Math.Max(last.Selected, first.Selected - 1);
            var options = new ExportOptions((ExportKind)what.Selected, viewpoints[from.Selected].Key, first.Selected - 1, to,
                captions.ButtonPressed, ParseSize(Preferences.Resolutions[size.Selected]), perActor.ButtonPressed && !perActor.Disabled,
                target.Text.Trim());
            LastExport = options;
            _status.Text = await Export(options);
        };
        AddChild(_exportDialog);
        _exportDialog.PopupCentered();
    }

    /// <summary>Chooses the export's folder or movie file with a file dialog; the choice goes into `target`.</summary>
    private void ChooseExportTarget(ExportKind kind, LineEdit target)
    {
        _exportTargetDialog?.QueueFree();
        bool movie = kind == ExportKind.Movie;
        _exportTargetDialog = new FileDialog
        {
            Title = movie ? "Movie file" : "Folder for the export",
            ModeOverridesTitle = false,   // keep the title (else "Open a File")
            FileMode = movie ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = movie ? new[] { "*.avi ; AVI (MJPEG), converted to MP4 when ffmpeg is found" } : Array.Empty<string>(),
            UseNativeDialog = true,
        };
        string current = target.Text.Trim();
        string dir = movie ? Path.GetDirectoryName(current) : current;
        if (Directory.Exists(dir))
            _exportTargetDialog.CurrentDir = dir;
        if (movie)
            _exportTargetDialog.CurrentFile = Path.GetFileName(current);
        _exportTargetDialog.FileSelected += path => target.Text = path;
        _exportTargetDialog.DirSelected += path => target.Text = path;
        AddChild(_exportTargetDialog);
        NativeFileDialogs.Popup(_exportTargetDialog, new Vector2I(900, 600));
    }
}
