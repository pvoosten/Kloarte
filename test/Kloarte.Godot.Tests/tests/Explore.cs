using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.GodotBinding;
using Kloarte.MagicQ;

namespace Kloarte.Tests;

/// <summary>
/// Exploratory testing: runs the show scene and a list of steps from the command line, so the app can be driven and
/// looked at without a person at the mouse. Shows written by the app go to a MagicQ show folder of its own
/// (tests/output/explore/show), project data to the test project's res://Projects.
/// Run: <c>Godot_console.exe --path test/Kloarte.Godot.Tests --resolution 1600x900 res://tests/explore.tscn --
/// --steps="open:C:/.../Kruimels_001.shw;wait:60;shot:a;press:Save;dump;quit"</c>
/// Steps (separated by ';'):
/// <list type="bullet">
/// <item><c>wait:N</c> N frames; <c>shot:name</c> screenshot tests/output/explore/name.png;</item>
/// <item><c>open:path</c> a show through the open dialog's signal; <c>copy:from|name</c> copies a show (+ .xhw) into
///   the explore show folder; <c>copyto:from|res://...</c> copies any file (keeps its time);</item>
/// <item><c>press:text</c> the first visible button whose text starts with `text`; <c>tab:name</c> a tab of the left
///   panel; <c>menu:File|Open project|name</c> a menu item; <c>showmenu:File</c> opens a menu; <c>key:Ctrl+S</c> a key press; <c>dialog:text</c> types into the visible dialog and
///   confirms it; <c>close</c> = the window close button; <c>magicqsave:shw</c> = MagicQ saved that show (the watcher); <c>select:text</c> = a list row (<c>selectadd</c> = Ctrl + click); <c>rclick:text</c> = right click on a list row; <c>ctx:item</c> = an item of the open context menu; <c>field:Label|text</c> / <c>submit:Label</c> = type / Enter in a labelled field; <c>spin:Label|value</c> = a labelled spin box; <c>prompt:Save|Don't save|Cancel</c> answers a prompt; <c>file:path</c> answers the open file dialog (Godot's own here, never native); <c>toolshot:Window|name</c> a tool window's screenshot; <c>cell:row|column</c> a cell of the cue table (<c>#1801</c> = the column of head 1801); <c>dump</c> prints the visible texts of the left panel; <c>files</c> lists the explore folders;</item>
/// <item><c>quit</c>.</item>
/// </list>
/// </summary>
public partial class Explore : Node
{
    /// <summary>The steps still to do.</summary>
    private readonly Queue<string> _steps = new();

    /// <summary>Frames to wait before the next step.</summary>
    private int _wait = 30;

    /// <summary>The show scene and its panel.</summary>
    private Node _scene, _panel;

    /// <summary>The explore output folder (absolute).</summary>
    private static string Out => ProjectSettings.GlobalizePath("res://tests/output/explore");

    /// <summary>The MagicQ show folder of the exploration.</summary>
    private static string ShowFolder => System.IO.Path.Combine(Out, "show");

    /// <inheritdoc />
    public override void _Ready()
    {
        KeyMap.EnsureLoaded("res://tests/output/test_keymap.json");
        ViewHost.Persist = false;
        FixturePanel.PresentFullScreen = false;   // Present mode stays in the test window
        System.IO.Directory.CreateDirectory(ShowFolder);
        MagicQPc.ShowFolderOverride = ShowFolder;
        string steps = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--steps="))?["--steps=".Length..] ?? "dump;quit";
        foreach (string step in steps.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _steps.Enqueue(step);
        _scene = GD.Load<PackedScene>("res://shows/risquons_tout.tscn").Instantiate();
        AddChild(_scene);
        _panel = _scene.GetNode("FixturePanel");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (--_wait > 0)
            return;
        _wait = 1;
        // A native file dialog would stay open over the test and hang Godot at exit: use Godot's own.
        foreach (var d in All<FileDialog>())
            d.UseNativeDialog = false;
        if (!_steps.TryDequeue(out string step))
        {
            GetTree().Quit();
            return;
        }
        string verb = step.Split(':')[0];
        string arg = step.Length > verb.Length ? step[(verb.Length + 1)..] : "";
        GD.Print($"EXPLORE {step}");
        try
        {
            Do(verb, arg);
        }
        catch (Exception e)
        {
            GD.PrintErr($"EXPLORE step '{step}' failed: {e}");
        }
    }

    /// <summary>Does one step.</summary>
    private void Do(string verb, string arg)
    {
        switch (verb)
        {
            case "wait":
                _wait = int.Parse(arg);
                break;
            case "shot":
                System.IO.Directory.CreateDirectory(Out);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(Out, arg + ".png"));
                break;
            case "toolshot":
                // toolshot:CueTableWindow|name = a screenshot of a tool window (its own viewport)
                string[] tool = arg.Split('|');
                var window = GetTree().Root.FindChild(tool[0], true, false) as Window
                             ?? throw new InvalidOperationException($"no window '{tool[0]}'");
                System.IO.Directory.CreateDirectory(Out);
                window.GetTexture().GetImage().SavePng(System.IO.Path.Combine(Out, tool[1] + ".png"));
                break;
            case "cell":
                // cell:row|column = chooses a cell of the cue table (rows from 0; columns: 0 cue .. 4 delay, 5.. heads)
                string[] rc = arg.Split('|');
                var tree = GetTree().Root.FindChild("Cues", true, false) as Tree ?? throw new InvalidOperationException("no cue table");
                var cueRow = tree.GetRoot().GetChild(int.Parse(rc[0]));
                int column = rc[1].StartsWith('#')
                    ? Enumerable.Range(0, tree.Columns).First(c => tree.GetColumnTitle(c) == rc[1][1..])
                    : int.Parse(rc[1]);
                cueRow.Select(column);
                tree.EmitSignal(Tree.SignalName.CellSelected);
                _wait = 10;
                break;
            case "open":
                All<FileDialog>().First(d => d.Title == "Import MagicQ show").EmitSignal(FileDialog.SignalName.FileSelected, arg);
                _wait = 30;
                break;
            case "copy":
                string[] parts = arg.Split('|');
                string target = System.IO.Path.Combine(ShowFolder, parts[1]);
                System.IO.File.Copy(parts[0], target, true);
                if (System.IO.File.Exists(System.IO.Path.ChangeExtension(parts[0], ".xhw")))
                    System.IO.File.Copy(System.IO.Path.ChangeExtension(parts[0], ".xhw"), System.IO.Path.ChangeExtension(target, ".xhw"), true);
                break;
            case "copyto":
                // copyto:from|res://Projects/name: a file into the app's folders (e.g. old project files)
                string[] fromTo = arg.Split('|');
                string to = ProjectSettings.GlobalizePath(fromTo[1]);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
                System.IO.File.Copy(fromTo[0], to, true);
                System.IO.File.SetLastWriteTimeUtc(to, System.IO.File.GetLastWriteTimeUtc(fromTo[0]));
                break;
            case "press":
                var button = All<BaseButton>().FirstOrDefault(b => b.IsVisibleInTree() && Text(b).StartsWith(arg))
                             ?? throw new InvalidOperationException($"no visible button '{arg}'");
                // A click on a toggle button flips it (toggled) and presses it (pressed), like a person's click.
                if (button.ToggleMode)
                    button.ButtonPressed = !button.ButtonPressed;
                button.EmitSignal(BaseButton.SignalName.Pressed);
                _wait = 10;
                break;
            case "menu":
                // menu:File|Open project|Kruimels: a menu bar menu's (or menu button's) item, through submenus
                string[] path = arg.Split('|');
                PopupMenu popup = BarMenu(path[0])
                                  ?? All<MenuButton>().FirstOrDefault(m => m.IsVisibleInTree() && m.Text.StartsWith(path[0]))?.GetPopup()
                                  ?? throw new InvalidOperationException($"no menu '{path[0]}'");
                popup.EmitSignal(Window.SignalName.AboutToPopup);
                for (int i = 1; i < path.Length; i++)
                {
                    int index = Enumerable.Range(0, popup.ItemCount).FirstOrDefault(n => popup.GetItemText(n).StartsWith(path[i]), -1);
                    if (index < 0)
                        throw new InvalidOperationException($"no item '{path[i]}' in [{string.Join(", ", Enumerable.Range(0, popup.ItemCount).Select(popup.GetItemText))}]");
                    if (popup.GetItemSubmenuNode(index) is { } sub)
                        popup = sub;
                    else
                        popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(index));
                }
                _wait = 20;
                break;
            case "showmenu":
                // showmenu:File opens a menu of the bar (for a screenshot)
                var shown = BarMenu(arg) ?? throw new InvalidOperationException($"no menu '{arg}'");
                var bar = (MenuBar)shown.GetParent();
                int menuIndex = bar.GetChildren().OfType<PopupMenu>().ToList().IndexOf(shown);
                shown.EmitSignal(Window.SignalName.AboutToPopup);
                // Under its title: the titles are about the same width.
                shown.Popup(new Rect2I(new Vector2I(8 + menuIndex * 80, (int)bar.Size.Y), Vector2I.Zero));
                _wait = 10;
                break;
            case "key":
                // key:Space / key:Ctrl+S: a real key press and release (through the key map like a person's)
                var binding = KeyBinding.Parse(arg) ?? throw new InvalidOperationException($"'{arg}' is not a key");
                var down = (InputEventKey)binding.ToEvent();
                down.Pressed = true;
                Input.ParseInputEvent(down);
                var up = (InputEventKey)down.Duplicate();
                up.Pressed = false;
                Input.ParseInputEvent(up);
                _wait = 10;
                break;
            case "dialog":
                // dialog:text: types into the visible dialog's line edit and confirms it ("dialog:" = confirm only)
                var dialog = All<ConfirmationDialog>().FirstOrDefault(d => d.Visible)
                             ?? throw new InvalidOperationException("no visible dialog");
                if (arg.Length > 0)
                    dialog.FindChildren("*", nameof(LineEdit), true, false).OfType<LineEdit>().First().Text = arg;
                GD.Print($"  dialog '{dialog.Title}'");
                dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
                _wait = 30;
                break;
            case "tab":
                var tabs = All<TabContainer>().First();
                tabs.CurrentTab = Enumerable.Range(0, tabs.GetTabCount()).First(i => tabs.GetTabTitle(i) == arg);
                break;
            case "dump":
                foreach (var c in All<Control>().Where(c => c.IsVisibleInTree() && c.GetGlobalRect().Position.X < 400))
                    if (c is Label { Text.Length: > 0 } or Button { Text.Length: > 0 } && c.GetWindow() == GetWindow())
                        GD.Print($"  {c.GetClass()} '{Text(c)}'{(c.TooltipText.Length > 0 ? $"  [tip: {c.TooltipText.Replace("\n", " | ")}]" : "")}");
                GD.Print($"  window title '{GetWindow().Title}', focus {GetViewport().GuiGetFocusOwner()?.GetPath()}");
                break;
            case "file":
                // file:path answers the open file dialog (Godot's own: native dialogs are switched off here)
                var fileDialog = All<FileDialog>().FirstOrDefault(d => d.Visible)
                                 ?? throw new InvalidOperationException("no file dialog open");
                fileDialog.Hide();
                fileDialog.EmitSignal(FileDialog.SignalName.FileSelected, arg);
                _wait = 20;
                break;
            case "select":
            case "selectadd":
                // select:<text> = the first visible list row starting with the text (a click); selectadd = Ctrl + click;
                // fixture table rows: their text is "head  name  type  gel  level"
                if (_scene.FindChildren("*", "", true, false).OfType<FixtureTable>().FirstOrDefault(t => t.IsVisibleInTree() &&
                        Enumerable.Range(0, t.RowCount).Any(i => t.RowText(i).StartsWith(arg))) is { } table)
                {
                    table.ClickRow(Enumerable.Range(0, table.RowCount).First(i => table.RowText(i).StartsWith(arg)), verb == "selectadd");
                    _wait = 10;
                    break;
                }
                var list = All<ItemList>().First(l => l.IsVisibleInTree() &&
                                                      Enumerable.Range(0, l.ItemCount).Any(i => l.GetItemText(i).StartsWith(arg)));
                int row = Enumerable.Range(0, list.ItemCount).First(i => list.GetItemText(i).StartsWith(arg));
                list.Select(row, verb == "select");
                if (list.SelectMode == ItemList.SelectModeEnum.Multi)
                    list.EmitSignal(ItemList.SignalName.MultiSelected, row, true);
                else
                    list.EmitSignal(ItemList.SignalName.ItemSelected, row);
                _wait = 10;
                break;
            case "rclick":
                // rclick:<text> = right click on the first visible list row starting with the text
                if (_scene.FindChildren("*", "", true, false).OfType<FixtureTable>().FirstOrDefault(t => t.IsVisibleInTree() &&
                        Enumerable.Range(0, t.RowCount).Any(i => t.RowText(i).StartsWith(arg))) is { } rtable)
                {
                    rtable.RightClickRow(Enumerable.Range(0, rtable.RowCount).First(i => rtable.RowText(i).StartsWith(arg)));
                    _wait = 10;
                    break;
                }
                var rlist = All<ItemList>().First(l => l.IsVisibleInTree() &&
                                                       Enumerable.Range(0, l.ItemCount).Any(i => l.GetItemText(i).StartsWith(arg)));
                int rrow = Enumerable.Range(0, rlist.ItemCount).First(i => rlist.GetItemText(i).StartsWith(arg));
                rlist.EmitSignal(ItemList.SignalName.ItemClicked, rrow, Vector2.Zero, (int)MouseButton.Right);
                _wait = 10;
                break;
            case "ctx":
                // ctx:<item text> = that item of the open context menu
                var ctx = FindChildren("*", nameof(PopupMenu), true, false).OfType<PopupMenu>().FirstOrDefault(m => m.Visible)
                          ?? throw new InvalidOperationException("no context menu open");
                int item = Enumerable.Range(0, ctx.ItemCount).FirstOrDefault(i => ctx.GetItemText(i).StartsWith(arg), -1);
                if (item < 0)
                    throw new InvalidOperationException($"no item '{arg}' in [{string.Join(", ", Enumerable.Range(0, ctx.ItemCount).Select(ctx.GetItemText))}]");
                GD.Print($"  menu [{string.Join(" | ", Enumerable.Range(0, ctx.ItemCount).Select(ctx.GetItemText))}]");
                ctx.Hide();
                ctx.EmitSignal(PopupMenu.SignalName.IdPressed, ctx.GetItemId(item));
                _wait = 20;
                break;
            case "field":
            case "submit":
                // field:<label>|<text> types into the field next to that label; submit:<label> presses Enter there
                string[] fieldArgs = arg.Split('|');
                var edit = All<LineEdit>().FirstOrDefault(e => e.IsVisibleInTree() && e.GetParent() is HBoxContainer row2
                               && row2.GetChildren().OfType<Label>().Any(l => l.Text == fieldArgs[0]))
                           ?? throw new InvalidOperationException($"no field '{fieldArgs[0]}'");
                if (verb == "field")
                {
                    edit.Text = fieldArgs[1];
                    edit.EmitSignal(LineEdit.SignalName.TextChanged, fieldArgs[1]);
                }
                else
                    edit.EmitSignal(LineEdit.SignalName.TextSubmitted, edit.Text);
                _wait = 10;
                break;
            case "spin":
                // spin:<label>|<value> sets the spin box next to that label (an inspector row)
                string[] spinArgs = arg.Split('|');
                var spin = All<SpinBox>().FirstOrDefault(s => s.IsVisibleInTree() && s.GetParent() is HBoxContainer spinRow
                               && spinRow.GetChildren().OfType<Label>().Any(l => l.Text == spinArgs[0]))
                           ?? throw new InvalidOperationException($"no spin box '{spinArgs[0]}'");
                spin.Value = double.Parse(spinArgs[1], System.Globalization.CultureInfo.InvariantCulture);
                _wait = 10;
                break;
            case "magicqsave":
                // magicqsave:<shw> = re-import this MagicQ show (the list of changes is asked)
                ((FixturePanel)_panel).Reimport(arg);
                _wait = 40;
                break;
            case "close":
                // the window's close button
                _panel.Notification((int)NotificationWMCloseRequest);
                _wait = 10;
                break;
            case "prompt":
                // prompt:Save / prompt:Don't save / prompt:Cancel answers the visible confirmation dialog
                var prompt = All<ConfirmationDialog>().FirstOrDefault(d => d.Visible)
                             ?? throw new InvalidOperationException("no prompt open");
                GD.Print($"  prompt '{prompt.DialogText.Replace("\n", " ")}'");
                // Press the dialog's button with that text, as a person would (OK, Cancel and added buttons).
                var promptButton = prompt.FindChildren("*", nameof(Button), true, false).OfType<Button>()
                                       .FirstOrDefault(b => b.Text == arg && b.IsVisibleInTree())
                                   ?? throw new InvalidOperationException($"no button '{arg}' in the prompt");
                promptButton.EmitSignal(BaseButton.SignalName.Pressed);
                _wait = 20;
                break;
            case "state":
                var files = _scene.GetNode<ShowBuilder>("Show").Files;
                GD.Print($"  project {files.ProjectName} ({files.ProjectFolder}, saved {files.ProjectSaved})\n  show {files.ShowFile}\n" +
                         $"  mechanics {files.MechanicsFile}\n  blocking {files.BlockingFile}\n  dialogs: " +
                         string.Join(", ", All<FileDialog>().Select(d => d.Title)));
                break;
            case "files":
                foreach (string root in new[] { ShowFolder, ProjectSettings.GlobalizePath("res://Projects") })
                    if (System.IO.Directory.Exists(root))
                        foreach (string f in System.IO.Directory.GetFiles(root, "*", System.IO.SearchOption.AllDirectories))
                            GD.Print($"  {f}");
                break;
            case "quit":
                _steps.Clear();
                GetTree().Quit();
                break;
            default:
                throw new InvalidOperationException($"unknown step '{verb}'");
        }
    }

    /// <summary>The menu bar's menu with this title, or null.</summary>
    private PopupMenu BarMenu(string title) =>
        All<MenuBar>().SelectMany(m => m.GetChildren().OfType<PopupMenu>()).FirstOrDefault(p => p.Name == title);

    /// <summary>The text of a label or button.</summary>
    private static string Text(Control c) => c switch
    {
        Label l => l.Text,
        Button b => b.Text,
        _ => "",
    };

    /// <summary>All nodes of a type in the show scene (incl. the panel's).</summary>
    private T[] All<T>() where T : Node => _scene.FindChildren("*", typeof(T).Name, true, false).OfType<T>().ToArray();
}
