using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The window's one status bar (UI review 15; decided with the user): one line across the bottom with the last message
/// of the panel or any view (errors in red, the rest grey; the full text in the tooltip), a click shows the last 50
/// messages with their time; on the right the project (with the unsaved marker) and what is connected (MagicQ's show
/// folder watched, live Art-Net). Messages arrive through <see cref="Post(string)"/> (<see cref="StatusText"/>).
/// </summary>
public partial class StatusBar : PanelContainer
{
    /// <summary>Most messages kept in the history.</summary>
    private const int HistoryLength = 50;

    /// <summary>Start of messages that report a failure or a refusal (shown in red).</summary>
    private static readonly string[] ErrorMarks =
    {
        "Could not", "Cannot", "can't", "is not a", "is not there", "refused", "Refused", "conflict", "failed", "Error",
        "There is a project", "No show", "not found",
    };

    /// <summary>The bars in the tree (one per window; tests may have more scenes).</summary>
    private static readonly List<StatusBar> Bars = new();

    /// <summary>The message line and the info on the right.</summary>
    private Label _message, _info;

    /// <summary>Earlier messages: time, text, error.</summary>
    private readonly List<(DateTime Time, string Text, bool Error)> _history = new();

    /// <summary>What a click on the last message does when it has one (a message with a location opens its file); else null.</summary>
    private Action _link;

    /// <summary>The history popup.</summary>
    private PopupPanel _historyPopup;
    private ItemList _historyList;

    /// <summary>The last message shown.</summary>
    internal string Message => _message?.Text ?? "";

    /// <summary>The messages so far, oldest first (tests).</summary>
    internal IReadOnlyList<(DateTime Time, string Text, bool Error)> History => _history;

    /// <summary>Shows a message in every status bar (normally one).</summary>
    internal static void Post(string text) => Post(text, null);

    /// <summary>Shows a message whose click runs `link` (an error with its file: the click opens the file).</summary>
    internal static void Post(string text, Action link) => Post(text, link, "Click: open the file");

    /// <summary>Shows a message whose click runs `link`; `linkHint` says what a click does ("Click: open the folder").</summary>
    internal static void Post(string text, Action link, string linkHint)
    {
        foreach (var bar in Bars.Where(IsInstanceValid))
            bar.ShowMessage(text, link, linkHint);
    }

    /// <summary>
    /// Shows a running task (UI review 16: the movie) in every status bar: its text, its progress (0..1, or negative when
    /// unknown) and a Cancel button that runs `cancel`.
    /// </summary>
    internal static void ShowTask(string text, double progress, Action cancel)
    {
        foreach (var bar in Bars.Where(IsInstanceValid))
            bar.SetTask(text, progress, cancel);
    }

    /// <summary>Hides the running task of every status bar.</summary>
    internal static void EndTask()
    {
        foreach (var bar in Bars.Where(IsInstanceValid))
            bar.SetTask(null, 0, null);
    }

    /// <summary>True when a message reports a failure or a refusal.</summary>
    internal static bool IsError(string text) => ErrorMarks.Any(m => text.Contains(m, StringComparison.Ordinal));

    /// <inheritdoc />
    public override void _EnterTree() => Bars.Add(this);

    /// <inheritdoc />
    public override void _ExitTree() => Bars.Remove(this);

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.12f, 0.13f),
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 2, ContentMarginBottom = 2,
        });
        var row = new HBoxContainer();
        _message = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            Modulate = new Color(0.75f, 0.75f, 0.75f),
        };
        _message.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                if (_link != null)
                    _link();
                else
                    ShowHistory();
            }
        };
        _info = new Label { Modulate = new Color(0.65f, 0.65f, 0.65f), HorizontalAlignment = HorizontalAlignment.Right };
        _taskRow = new HBoxContainer { Name = "Task", Visible = false };
        _taskLabel = new Label { Modulate = new Color(0.85f, 0.85f, 0.85f) };
        _taskBar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(120, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MaxValue = 1,
            Step = 0.001,
            ShowPercentage = false,
        };
        _taskCancel = new Button { Text = "Cancel", FocusMode = FocusModeEnum.None };
        _taskCancel.Pressed += () => _cancel?.Invoke();
        _taskRow.AddChild(_taskLabel);
        _taskRow.AddChild(_taskBar);
        _taskRow.AddChild(_taskCancel);
        row.AddChild(_taskRow);
        _chipRow = new HBoxContainer { Name = "ModeChips" };
        _chipRow.AddThemeConstantOverride("separation", 4);
        row.AddChild(_chipRow);
        row.AddChild(_message);
        row.AddChild(_info);
        AddChild(row);

        _historyPopup = new PopupPanel { Title = "Messages" };
        _historyList = new ItemList { CustomMinimumSize = new Vector2(720, 320) };
        _historyPopup.AddChild(_historyList);
        AddChild(_historyPopup);
    }

    /// <summary>The running task: its row, text, progress bar, Cancel button and what Cancel does.</summary>
    private HBoxContainer _taskRow;
    private Label _taskLabel;
    private ProgressBar _taskBar;
    private Button _taskCancel;
    private Action _cancel;

    /// <summary>The running task's text, or null (tests).</summary>
    internal string Task => _taskRow?.Visible == true ? _taskLabel.Text : null;

    /// <summary>Shows the running task (null text = none).</summary>
    private void SetTask(string text, double progress, Action cancel)
    {
        if (_taskRow == null)
            return;
        _taskRow.Visible = text != null;
        _cancel = cancel;
        if (text == null)
            return;
        _taskLabel.Text = text;
        _taskBar.Visible = progress >= 0;
        _taskBar.Value = Math.Clamp(progress, 0, 1);
        _taskCancel.Visible = cancel != null;
    }

    /// <summary>Shows a message (a new one goes into the history; an empty one only clears the line).</summary>
    private void ShowMessage(string text, Action link, string linkHint = "Click: open the file")
    {
        if (_message == null)
            return;
        string line = text.Replace('\n', ' ').Trim();
        bool error = IsError(line);
        _link = link;
        _message.Text = line;
        _message.TooltipText = link != null ? text + "\n" + linkHint
            : text.Length > 0 ? text + "\nClick: earlier messages" : "Click: earlier messages";
        _message.Modulate = error ? new Color(1f, 0.45f, 0.4f) : new Color(0.75f, 0.75f, 0.75f);
        if (line.Length == 0 || _history.Count > 0 && _history[^1].Text == line)
            return;
        _history.Add((DateTime.Now, line, error));
        if (_history.Count > HistoryLength)
            _history.RemoveAt(0);
    }

    /// <summary>The info on the right: project, unsaved marker, connections.</summary>
    internal void SetInfo(string info)
    {
        if (_info != null && _info.Text != info)
            _info.Text = info;
    }

    /// <summary>The chips on the left of the message (UI review 14): the modes on now, each with an ✕ that ends it.</summary>
    private HBoxContainer _chipRow;

    /// <summary>The chips shown (text + tooltip), to rebuild the row only when they change.</summary>
    private string _chipsShown = "";

    /// <summary>The modes shown as chips now (tests).</summary>
    internal IReadOnlyList<ModeChip> Chips { get; private set; } = Array.Empty<ModeChip>();

    /// <summary>Shows the modes that are on as chips (the row is rebuilt only when they changed).</summary>
    internal void SetChips(IReadOnlyList<ModeChip> chips)
    {
        if (_chipRow == null)
            return;
        Chips = chips;
        string key = string.Join("\n", chips.Select(c => c.Text + "\t" + c.Tooltip));
        if (key == _chipsShown)
            return;
        _chipsShown = key;
        foreach (Node child in _chipRow.GetChildren())
        {
            _chipRow.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var chip in chips)
        {
            var button = new Button
            {
                Text = chip.Text + "  ✕",
                TooltipText = chip.Tooltip + "\nClick: end it",
                Flat = false,
                FocusMode = Control.FocusModeEnum.None,
            };
            button.AddThemeColorOverride("font_color", new Color(1f, 0.82f, 0.4f));
            button.AddThemeFontSizeOverride("font_size", 12);
            button.AddThemeStyleboxOverride("normal", ChipStyle(new Color(0.3f, 0.24f, 0.1f)));
            button.AddThemeStyleboxOverride("hover", ChipStyle(new Color(0.42f, 0.32f, 0.12f)));
            button.AddThemeStyleboxOverride("pressed", ChipStyle(new Color(0.42f, 0.32f, 0.12f)));
            var end = chip.End;
            button.Pressed += () => end();
            _chipRow.AddChild(button);
        }
    }

    /// <summary>A chip's rounded background.</summary>
    private static StyleBoxFlat ChipStyle(Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        ContentMarginLeft = 8, ContentMarginRight = 6, ContentMarginTop = 0, ContentMarginBottom = 0,
    };

    /// <summary>The last messages, newest first, errors in red.</summary>
    private void ShowHistory()
    {
        _historyList.Clear();
        foreach (var (time, text, error) in Enumerable.Reverse(_history))
        {
            int i = _historyList.AddItem($"{time:HH:mm:ss}  {text}");
            if (error)
                _historyList.SetItemCustomFgColor(i, new Color(1f, 0.45f, 0.4f));
        }
        if (_historyList.ItemCount == 0)
            _historyList.AddItem("No messages yet");
        _historyPopup.PopupCentered();
    }
}
