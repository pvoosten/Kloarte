using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The cue table of a native show (decided 14 Oct 2026): a row per cue (number, text, in, out, delay) and a column per
/// head with its intensity in that cue: white = set in this cue, blue = from a palette, grey = tracked from a cue before;
/// a dot = the cue sets other values of the head (position, colour, beam). Cells are edited in place (a number, empty =
/// track, "@palette"); the chosen cell's head is edited in full on the right (<see cref="LookEditor"/>). Choosing a row
/// plays that cue. Right click on a row: new cue after it, copy, delete. A tool in its own window (<see cref="ToolWindow"/>).
/// </summary>
public partial class CueTableView : ViewBase
{
    /// <summary>The fixed columns before the heads.</summary>
    private static readonly string[] Fixed = { "Cue", "Text", "In", "Out", "Delay" };

    /// <summary>The table.</summary>
    private Tree _tree;

    /// <summary>The editor of the chosen cell's head.</summary>
    private VBoxContainer _editor;

    /// <summary>Group filter: every head, or the heads of one group.</summary>
    private OptionButton _group;

    /// <summary>Only the heads some cue sets.</summary>
    private CheckBox _used;

    /// <summary>Messages (to the window's status bar).</summary>
    private readonly StatusText _status = new();

    /// <summary>The heads of the columns, in column order.</summary>
    private readonly List<int> _heads = new();

    /// <summary>The chosen cell: cue index and head (0 = a cue column).</summary>
    private (int Cue, int Head) _chosen = (-1, 0);

    /// <summary>True while the table is filled (its signals are not edits).</summary>
    private bool _filling;

    /// <summary>The row menu.</summary>
    private PopupMenu _menu;

    /// <inheritdoc />
    public override string Title => "Cue table";

    /// <summary>Builds the table, the filters and the editor.</summary>
    public override void _Ready()
    {
        var bg = new Panel();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var split = new HSplitContainer { SplitOffsets = new[] { -360 } };
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        split.OffsetLeft = 6;
        split.OffsetRight = -6;
        split.OffsetTop = 6;
        AddChild(split);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var top = new HBoxContainer();
        _group = new OptionButton { TooltipText = "Show the heads of one group" };
        _group.ItemSelected += _ => Fill();
        _used = new CheckBox { Text = "Only heads in cues", ButtonPressed = true, TooltipText = "Hide the heads no cue sets" };
        _used.Toggled += _ => Fill();
        var add = new Button { Text = "+ Cue", TooltipText = "A new cue after the chosen one (no changes: the look tracks)" };
        add.Pressed += () => NewCue(_chosen.Cue, copy: false);
        top.AddChild(_group);
        top.AddChild(_used);
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        top.AddChild(add);
        left.AddChild(top);
        _tree = new Tree
        {
            Name = "Cues",
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Single,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _tree.CellSelected += OnCellSelected;
        _tree.ItemEdited += OnItemEdited;
        _tree.ItemMouseSelected += (_, button) =>
        {
            if (button == (long)MouseButton.Right && _tree.GetSelected() is { } item)
                ShowMenu(item.GetMeta("cue").AsInt32());
        };
        left.AddChild(_tree);
        split.AddChild(left);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(340, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _editor = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_editor);
        split.AddChild(scroll);

        _menu = new PopupMenu();
        _menu.AddItem("New cue after it", 0);
        _menu.AddItem("Copy after it", 1);
        _menu.AddSeparator();
        _menu.AddItem("Delete cue", 2);
        _menu.IdPressed += OnMenu;
        AddChild(_menu);

        CueEditing.Edited += OnEdited;
        CueEditing.CueChanged += OnCueChanged;
        TreeExiting += () =>
        {
            CueEditing.Edited -= OnEdited;
            CueEditing.CueChanged -= OnCueChanged;
        };
        FillGroups();
        Fill();
    }

    /// <inheritdoc />
    protected override void OnShowChanged()
    {
        if (_tree == null)
            return;
        FillGroups();
        Fill();
    }

    /// <summary>The show changed: the table again (the editor too, unless the change came from it).</summary>
    private void OnEdited()
    {
        if (IsInstanceValid(this) && IsVisibleInTree())
            CallDeferred(MethodName.Fill);
    }

    /// <summary>The playback went to another cue: its row is shown.</summary>
    private void OnCueChanged()
    {
        if (!IsInstanceValid(this) || _tree == null || !IsVisibleInTree())
            return;
        var row = Rows().FirstOrDefault(r => r.GetMeta("cue").AsInt32() == CueEditing.Cue);
        if (row != null)
            _tree.ScrollToItem(row);
    }

    /// <summary>The native show, or null.</summary>
    private DesignShow Design => CueEditing.Design(Builder);

    /// <summary>The rows of the table.</summary>
    private IEnumerable<TreeItem> Rows() => _tree.GetRoot()?.GetChildren() ?? Enumerable.Empty<TreeItem>();

    /// <summary>The group filter's items.</summary>
    private void FillGroups()
    {
        if (_group == null)
            return;
        _group.Clear();
        _group.AddItem("All heads");
        foreach (var group in Design?.Rig.Groups ?? new List<DesignGroup>())
        {
            _group.AddItem(group.Name);
            _group.SetItemMetadata(_group.ItemCount - 1, group.Number);
        }
    }

    /// <summary>Fills the table (and the editor of the chosen cell).</summary>
    private void Fill()
    {
        if (_tree == null)
            return;
        _filling = true;
        _tree.Clear();
        var design = Design;
        if (design == null)
        {
            _tree.Columns = 1;
            _tree.SetColumnTitle(0, "A native project shows its cues here");
            _filling = false;
            return;
        }
        var heads = design.Rig.Heads.OrderBy(h => h.Number).Select(h => h.Number);
        if (_group.Selected > 0)
        {
            int number = _group.GetItemMetadata(_group.Selected).AsInt32();
            var members = design.Rig.Groups.FirstOrDefault(g => g.Number == number)?.Heads ?? Array.Empty<int>();
            heads = heads.Where(members.Contains);
        }
        if (_used.ButtonPressed)
        {
            var used = design.CueOrder.SelectMany(c => c.Heads.Keys).Select(DesignShow.HeadOfKey).Where(h => h != null).Select(h => h.Value).ToHashSet();
            heads = heads.Where(used.Contains);
        }
        _heads.Clear();
        _heads.AddRange(heads);
        _tree.Columns = Fixed.Length + _heads.Count;
        for (int c = 0; c < Fixed.Length; c++)
        {
            _tree.SetColumnTitle(c, Fixed[c]);
            _tree.SetColumnExpand(c, false);
            _tree.SetColumnCustomMinimumWidth(c, c == 1 ? 140 : 52);
        }
        for (int i = 0; i < _heads.Count; i++)
        {
            int c = Fixed.Length + i;
            var head = design.Head(_heads[i]);
            _tree.SetColumnTitle(c, _heads[i].ToString(CultureInfo.InvariantCulture));
            _tree.SetColumnTitleTooltipText(c, $"Head {head.Number} {head.Name} ({head.Type})");
            _tree.SetColumnExpand(c, false);
            _tree.SetColumnCustomMinimumWidth(c, 44);
        }
        var root = _tree.CreateItem();
        // Tracking: the intensity each head has so far, walking the cues once.
        var tracked = new Dictionary<int, LookValue>();
        for (int index = 0; index < design.CueOrder.Count; index++)
        {
            var cue = design.CueOrder[index];
            var row = _tree.CreateItem(root);
            row.SetMeta("cue", index);
            Cell(row, 0, cue.Cue, true, Colors.White, "Cue number (cues play in number order)");
            Cell(row, 1, cue.Text ?? "", true, Colors.White, "Cue text");
            Cell(row, 2, Seconds(cue.FadeIn), true, Colors.White, "In: seconds levels that go up take");
            Cell(row, 3, Seconds(cue.FadeOut), true, Colors.White, "Out: seconds levels that go down take");
            Cell(row, 4, Seconds(cue.Delay), true, Colors.White, "Delay before the fades (Kloarte's playback only)");
            for (int i = 0; i < _heads.Count; i++)
            {
                int head = _heads[i];
                var values = cue.Of(head);
                var own = values?.GetValueOrDefault("intensity");
                bool others = values != null && values.Keys.Any(k => k != "intensity");
                if (own != null)
                    tracked[head] = own;
                var shown = own ?? tracked.GetValueOrDefault(head);
                var layout = design.LayoutOf(head);
                var channel = layout?.Intensity;
                var resolved = shown == null ? null : DesignEdits.Resolved(design, head, "intensity", shown);
                string text = resolved != null && channel?.NumberOf(resolved) is { } percent ? LookEditor.Text(percent) : "";
                if (others)
                    text += "•";
                var color = own == null ? new Color(0.55f, 0.55f, 0.55f) : own.IsPalette ? new Color(0.55f, 0.8f, 1f) : Colors.White;
                string tip = own != null ? (own.IsPalette ? $"From palette {own.Palette}" : "Set in this cue")
                    : shown != null ? "Tracked from a cue before" : "Not set yet";
                if (others)
                    tip += "; the cue sets other values of this head too";
                Cell(row, Fixed.Length + i, text, channel != null, color, tip);
            }
            if (index == _chosen.Cue)
                row.Select(_chosen.Head == 0 ? 0 : Fixed.Length + Math.Max(0, _heads.IndexOf(_chosen.Head)));
        }
        _filling = false;
        FillEditor();
    }

    /// <summary>A cell's text, colour, tooltip and whether it can be edited.</summary>
    private static void Cell(TreeItem row, int column, string text, bool editable, Color color, string tooltip)
    {
        row.SetText(column, text);
        row.SetEditable(column, editable);
        row.SetCustomColor(column, color);
        row.SetTooltipText(column, tooltip);
    }

    /// <summary>Seconds as a cell shows them ("" for none).</summary>
    private static string Seconds(double? value) => value is { } v && v > 0 ? v.ToString("0.###", CultureInfo.InvariantCulture) : "";

    /// <summary>A cell was chosen: its cue plays, its head is edited on the right.</summary>
    private void OnCellSelected()
    {
        if (_filling || _tree.GetSelected() is not { } row)
            return;
        int cue = row.GetMeta("cue").AsInt32();
        int column = _tree.GetSelectedColumn();
        _chosen = (cue, column >= Fixed.Length && column - Fixed.Length < _heads.Count ? _heads[column - Fixed.Length] : 0);
        if (cue != CueEditing.Cue)
            CueEditing.Jump(cue);
        FillEditor();
    }

    /// <summary>The editor of the chosen cell: the head's values in the cue, or the cue's own fields.</summary>
    private void FillEditor()
    {
        if (_editor == null)
            return;
        foreach (var child in _editor.GetChildren())
            child.QueueFree();
        var design = Design;
        if (design == null || _chosen.Cue < 0 || _chosen.Cue >= design.CueOrder.Count)
        {
            _editor.AddChild(new Label { Text = "Choose a cell: a head in a cue", Modulate = new Color(0.7f, 0.7f, 0.7f) });
            return;
        }
        var cue = design.CueOrder[_chosen.Cue];
        _editor.AddChild(new Label { Text = $"Cue {cue.Cue} {cue.Text}", Modulate = new Color(1f, 0.75f, 0.4f) });
        if (_chosen.Head == 0)
        {
            _editor.AddChild(new Label
            {
                Text = $"{cue.Heads.Count} head{(cue.Heads.Count == 1 ? "" : "s")} set in this cue. Choose a head's cell to edit its values.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(0.7f, 0.7f, 0.7f),
            });
            return;
        }
        var head = design.Head(_chosen.Head);
        _editor.AddChild(new Label { Text = $"Head {head.Number} {head.Name}", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _editor.AddChild(LookEditor.ForCue(Builder, _chosen.Cue, new[] { _chosen.Head }, () => CallDeferred(MethodName.FillEditor)));
    }

    /// <summary>A cell was edited in place: the cue's field, or the head's intensity ("" = track, "@name" = a palette).</summary>
    private void OnItemEdited()
    {
        if (_filling || _tree.GetEdited() is not { } row)
            return;
        var design = Design;
        int index = row.GetMeta("cue").AsInt32();
        int column = _tree.GetEditedColumn();
        if (design == null || index >= design.CueOrder.Count)
            return;
        var cue = design.CueOrder[index];
        string text = row.GetText(column).Trim();
        string problem = null;
        switch (column)
        {
            case 0:
                problem = Renumber(design, cue, text);
                break;
            case 1:
                CueEditing.Edit(Builder, $"Text of cue {cue.Cue}", _ => cue.Text = text.Length == 0 ? null : text);
                break;
            case 2:
            case 3:
            case 4:
                if (text.Length > 0 && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    problem = $"'{text}' is not a number of seconds";
                    break;
                }
                double? seconds = text.Length == 0 ? null : Math.Max(0, double.Parse(text, CultureInfo.InvariantCulture));
                CueEditing.Edit(Builder, $"{Fixed[column]} of cue {cue.Cue}", _ =>
                {
                    if (column == 2) cue.FadeIn = seconds;
                    else if (column == 3) cue.FadeOut = seconds;
                    else cue.Delay = seconds;
                });
                break;
            default:
                int head = _heads[column - Fixed.Length];
                LookValue value = null;
                if (text.Length > 0)
                {
                    try
                    {
                        value = LookValue.Parse(text.TrimEnd('•'));
                    }
                    catch (FormatException e)
                    {
                        problem = e.Message;
                        break;
                    }
                }
                CueEditing.Edit(Builder, $"Intensity of head {head} in cue {cue.Cue}", _ => DesignEdits.Set(cue.Heads, head, "intensity", value));
                break;
        }
        _status.Text = problem ?? "";
        CallDeferred(MethodName.Fill);
    }

    /// <summary>Gives a cue another number (any free number: the cues are kept in number order). Returns why not, or null.</summary>
    private string Renumber(DesignShow design, DesignCue cue, string text)
    {
        if (CueNumbers.Parse(text) is not { } number)
            return $"'{text}' is not a cue number";
        if (design.CueOrder.Any(c => c != cue && CueNumbers.Same(c.Number, number)))
            return $"cue {CueNumbers.Text(number)} exists already";
        CueEditing.Edit(Builder, $"Renumber cue {cue.Cue}", _ => cue.Cue = CueNumbers.Text(number));
        _chosen = (design.CueOrder.IndexOf(cue), _chosen.Head);
        return null;
    }

    /// <summary>The row menu of a cue.</summary>
    private void ShowMenu(int cue)
    {
        _chosen = (cue, _chosen.Head);
        _menu.Position = (Vector2I)GetGlobalMousePosition() + GetWindow().Position;
        _menu.Popup();
    }

    /// <summary>A row menu item.</summary>
    private void OnMenu(long id)
    {
        switch (id)
        {
            case 0:
                NewCue(_chosen.Cue, copy: false);
                break;
            case 1:
                NewCue(_chosen.Cue, copy: true);
                break;
            case 2:
                DeleteCue(_chosen.Cue);
                break;
        }
    }

    /// <summary>A new cue after cue `after` (-1: before the first): empty (the look tracks) or a copy of it.</summary>
    private void NewCue(int after, bool copy)
    {
        var design = Design;
        if (design == null)
            return;
        if (DesignEdits.NumberAfter(design, after) is not { } number)
        {
            _status.Text = "No free cue number after it (3 decimals)";
            return;
        }
        var source = after >= 0 && after < design.CueOrder.Count ? design.CueOrder[after] : null;
        var cue = copy && source != null ? source.Copy() : new DesignCue();
        cue.Cue = CueNumbers.Text(number);
        cue.Id = design.NextCueId;
        CueEditing.Edit(Builder, $"New cue {cue.Cue}", d => d.CueOrder.Add(cue));
        _chosen = (design.CueOrder.IndexOf(cue), 0);
        CueEditing.Jump(_chosen.Cue);
        _status.Text = $"Cue {cue.Cue} {(copy ? "copied" : "added")}";
    }

    /// <summary>Deletes a cue (one undo step); later cues track from the cue before it.</summary>
    private void DeleteCue(int index)
    {
        var design = Design;
        if (design == null || index < 0 || index >= design.CueOrder.Count)
            return;
        var cue = design.CueOrder[index];
        CueEditing.Edit(Builder, $"Delete cue {cue.Cue}", d => d.CueOrder.Remove(cue));
        _chosen = (Math.Min(index, design.CueOrder.Count - 1), 0);
        _status.Text = $"Deleted cue {cue.Cue} (undo brings it back)";
    }
}
