using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, the MagicQ round trip of a native show (decided 14 Oct 2026): "Open in MagicQ PC" exports the show as
/// the next numbered MagicQ show and starts MagicQ PC with it and Art-Net output to this PC
/// (<see cref="IConsoleCommands"/>), then turns live Art-Net input on; "Export MagicQ show..." writes one where you say;
/// "Re-import from MagicQ..." reads a MagicQ show of the project (edited at the technical rehearsal), lists what differs
/// and takes it as one undo step. While live input runs, every look MagicQ settles on (unchanged for a second) is compared
/// with the cues (<see cref="IConsoleCommands.Compare"/>): the best matching cue and the differing channels go to the
/// status line and to user://magicq_check.log. Command line: <c>-- --magicq</c> does "Open in MagicQ PC" once the show is
/// loaded.
/// </summary>
public partial class FixturePanel
{
    /// <summary>The comparison log.</summary>
    private const string CheckLog = "user://magicq_check.log";
    /// <summary>A look counts as settled after this many seconds without change.</summary>
    private const double SettleTime = 1.0;

    /// <summary>Signature of the last received DMX and how long it has been unchanged.</summary>
    private long _lookSignature;
    private double _lookAge;
    /// <summary>Signature of the last look that was compared (each look is reported once).</summary>
    private long _reportedSignature;
    /// <summary>Time to the next Art-Net sample.</summary>
    private double _sampleIn;
    /// <summary>"-- --magicq" still to do.</summary>
    private bool _magicqArgPending = OS.GetCmdlineUserArgs().Contains("--magicq");

    /// <summary>The dialog of "Re-import from MagicQ...".</summary>
    private FileDialog _reimportDialog;

    /// <summary>The Playback tab's MagicQ buttons: open in MagicQ PC, export, re-import.</summary>
    private Control MagicQRow()
    {
        var box = new VBoxContainer();
        var open = new Button
        {
            Text = "Open in MagicQ PC",
            TooltipText = "Export the show as the next numbered MagicQ show, close MagicQ PC (unsaved changes there are lost),\n" +
                          "start it with the exported show and Art-Net to this PC, listen to it and compare every look with the cues",
        };
        open.Pressed += OpenInMagicQ;
        var row = new HBoxContainer();
        var export = new Button { Text = "Export...", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        KeyMap.Tooltip(export, "Write the show as a MagicQ show (a copy of the one it came from, with Kloarte's changes)", "file_save_as");
        export.Pressed += ShowSaveDialog;
        var reimport = new Button { Text = "Re-import...", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Read a MagicQ show of this project (saved in MagicQ), see what differs and take it" };
        reimport.Pressed += ShowReimportDialog;
        row.AddChild(export);
        row.AddChild(reimport);
        box.AddChild(open);
        box.AddChild(row);
        return box;
    }

    /// <summary>
    /// Exports the show and starts MagicQ PC with it; when MagicQ PC runs already it asks first (it is closed, and what is
    /// not saved there is lost; UI review 17).
    /// </summary>
    private void OpenInMagicQ()
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
        {
            _status.Text = "No show loaded";
            return;
        }
        if (builder.ConsoleLink.ConsoleRunning)
            Confirm.Ask(this, "Restart MagicQ PC", "MagicQ PC is running. It is closed and started again with this show: " +
                        "changes in MagicQ that are not saved there are lost.", "Restart MagicQ PC", StartMagicQ);
        else
            StartMagicQ();
    }

    /// <summary>Starts MagicQ PC with the show exported (see <see cref="OpenInMagicQ"/>).</summary>
    private void StartMagicQ()
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            return;
        try
        {
            _status.Text = builder.ConsoleLink.OpenInConsole();
            UpdateProjectBox();
            Log($"--- {_status.Text}");
        }
        catch (Exception e)
        {
            _status.Text = $"MagicQ PC: {e.Message}";
            GD.PrintErr(_status.Text);
            return;
        }
        _reportedSignature = 0;
        SetLiveInput(true);
    }

    /// <summary>"Re-import from MagicQ...": a MagicQ show of the project, the newest one first.</summary>
    private void ShowReimportDialog()
    {
        var builder = CurrentBuilder;
        if (CueEditing.Design(builder) == null)
        {
            _status.Text = "Re-import is for a native project";
            return;
        }
        if (_reimportDialog == null)
        {
            _reimportDialog = new FileDialog
            {
                Title = "Re-import from MagicQ",
                ModeOverridesTitle = false,
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = new[] { "*.shw ; MagicQ show" },
                UseNativeDialog = true,
            };
            _reimportDialog.FileSelected += Reimport;
            AddChild(_reimportDialog);
        }
        string latest = builder.Files.LatestConsoleShow;
        string dir = latest != null ? System.IO.Path.GetDirectoryName(latest) : builder.Files.ShowFolder;
        if (System.IO.Directory.Exists(dir))
            _reimportDialog.CurrentDir = dir;
        if (latest != null)
            _reimportDialog.CurrentFile = System.IO.Path.GetFileName(latest);
        NativeFileDialogs.Popup(_reimportDialog, new Vector2I(900, 600));
    }

    /// <summary>
    /// Reads a MagicQ show of the project, lists what differs from Kloarte's show and takes it on "Take MagicQ's" (one
    /// undo step; the scene is rebuilt). Kloarte's own data (mechanics, blocking, smoke, delays) stays.
    /// </summary>
    internal void Reimport(string path)
    {
        var builder = CurrentBuilder;
        var design = CueEditing.Design(builder);
        if (design == null)
            return;
        ShowImport import;
        try
        {
            import = builder.Files.ImportConsoleShow(path);
        }
        catch (Exception e) when (e is System.IO.IOException or FormatException or ArgumentException or InvalidOperationException)
        {
            _status.Text = $"Could not read {System.IO.Path.GetFileName(path)}: {e.Message}";
            return;
        }
        var changes = DesignDiff.Describe(design, import.Show);
        string name = System.IO.Path.GetFileName(path);
        if (changes.Count == 0)
        {
            _status.Text = $"{name} is the same as Kloarte's show";
            return;
        }
        string text = $"{name} differs from Kloarte's show in {changes.Count} thing{(changes.Count == 1 ? "" : "s")}:\n\n" +
                      string.Join("\n", changes.Take(25)) + (changes.Count > 25 ? $"\n... and {changes.Count - 25} more" : "") +
                      "\n\nTake MagicQ's show? (one undo step; mechanics, blocking and smoke stay Kloarte's)";
        Confirm.Ask(this, "Re-import from MagicQ", text, "Take MagicQ's", () => TakeImport(builder, import));
    }

    /// <summary>Takes a re-imported show and rebuilds the scene, keeping the view.</summary>
    private async void TakeImport(ShowBuilder builder, ShowImport import)
    {
        var view = TakeView(builder);
        string message = builder.Files.TakeImport(import);
        builder.RebuildFromModel();
        await RestoreView(builder, view);
        CueEditing.Refresh();
        _status.Text = $"{message} {KeyMap.Hint("Undo", "edit_undo")}";
    }

    /// <summary>
    /// Selects the cue stack with this number again (if the show has it) and jumps to the cue with number `cue` (the
    /// last one before it when it is gone: cues inserted or removed move the steps), or without a cue number to step
    /// `step`.
    /// </summary>
    private void RestoreStack(int? number, int step, float? cue = null)
    {
        if (number == null)
            return;
        for (int i = 0; i < _stackPicker.ItemCount; i++)
            if (_stackPicker.GetItemMetadata(i).AsInt32() == number)
            {
                _stackPicker.Select(i);
                var stack = CurrentBuilder.Document.CueStacks.First(s => s.Number == number);
                _player.SetStack(stack);
                if (cue is { } c)
                    step = stack.Steps.Count(s => s.CueNumber <= c + 0.0005f) - 1;
                _player.Jump(step);
                return;
            }
    }

    /// <summary>The cue number of the playhead's step, or null before the first cue.</summary>
    private float? CurrentCueNumber() =>
        _player.Stack is { } stack && _player.Step >= 0 && _player.Step < stack.Steps.Count ? stack.Steps[_player.Step].CueNumber : null;

    /// <summary>Every frame: the command-line option, and comparing settled looks while live input runs.</summary>
    private void CheckMagicQ(double delta)
    {
        if (_magicqArgPending && CurrentBuilder?.Model != null)
        {
            _magicqArgPending = false;
            GD.Print("--magicq: opening the show in MagicQ PC");
            OpenInMagicQ();
        }
        if (_artNet?.Running != true || _artNet.Packets == 0 || CurrentBuilder?.Model == null || _player?.Stack == null)
            return;
        _sampleIn -= delta;
        if (_sampleIn > 0)
            return;
        _sampleIn = 0.25;
        var dmx = _artNet.Snapshot();
        long signature = Signature(dmx);
        if (signature != _lookSignature)
        {
            _lookSignature = signature;
            _lookAge = 0;
            foreach (var (address, value) in dmx)
                if (_previousDmx != null && _previousDmx.GetValueOrDefault(address) != value)
                    _moving.Add(address);
            _previousDmx = dmx;
            // A look that keeps changing (FX, long fades) is reported once after ChangingTime.
            _changingFor += 0.25;
            if (_changingFor >= ChangingTime)
            {
                _changingFor = double.NegativeInfinity;
                var moving = _moving.Order().Take(12).Select(DmxAddress.Format);
                Report(dmx, $"still changing ({_moving.Count} channels: {string.Join(" ", moving)}): ");
            }
            return;
        }
        _previousDmx = dmx;
        _lookAge += 0.25;
        if (_lookAge < SettleTime || signature == _reportedSignature)
            return;
        _reportedSignature = signature;
        _changingFor = 0;
        _moving.Clear();
        Report(dmx);
    }

    /// <summary>The previous sample, and the channels that changed while the look did not settle.</summary>
    private Dictionary<int, int> _previousDmx;
    private readonly HashSet<int> _moving = new();

    /// <summary>Seconds the received DMX has kept changing without settling.</summary>
    private double _changingFor;
    /// <summary>A look that keeps changing is reported after this many seconds.</summary>
    private const double ChangingTime = 3.0;

    /// <summary>Compares a settled look with every step of the selected stack and reports the best match.</summary>
    private void Report(Dictionary<int, int> dmx, string prefix = "")
    {
        string text = prefix + CurrentBuilder.ConsoleLink.Compare(dmx, _player.Stack);
        _status.Text = text;
        Log(text);
    }

    /// <summary>Appends a line (with the time) to user://magicq_check.log.</summary>
    private static void Log(string line)
    {
        using var file = FileAccess.FileExists(CheckLog) ? FileAccess.Open(CheckLog, FileAccess.ModeFlags.ReadWrite)
            : FileAccess.Open(CheckLog, FileAccess.ModeFlags.Write);
        if (file == null)
            return;
        file.SeekEnd();
        file.StoreLine($"{DateTime.Now:HH:mm:ss} {line}");
        GD.Print($"magicq check: {line}");
    }

    /// <summary>A hash of the received channels (address order).</summary>
    private static long Signature(Dictionary<int, int> dmx)
    {
        long hash = 17;
        foreach (var (address, value) in dmx.OrderBy(kv => kv.Key))
            hash = hash * 31 + address * 257 + value;
        return hash;
    }
}
