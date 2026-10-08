using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The fixture list as a table (UI review 22): columns Head / Name / Type / Gel / Level (%), a click on a column title
/// sorts by it (again: the other way round; the rows come in DMX order until then). Rows are selected as a whole:
/// click, Ctrl + click adds / takes out, Shift + click a range; right click asks for the lamp menu. Details (DMX, full
/// type and gel names) in the row's tooltip; a lamp whose file has an error is red.
/// </summary>
public partial class FixtureTable : Tree
{
    /// <summary>Column titles.</summary>
    private static readonly string[] Titles = { "Head", "Name", "Type", "Gel", "Level" };

    /// <summary>Column of the level (refreshed while the stage changes).</summary>
    private const int LevelColumn = 4;

    /// <summary>The rows as given (DMX order) and as shown (sorted).</summary>
    private List<FixtureRow> _rows = new(), _shown = new();

    /// <summary>The tree items of <see cref="_shown"/>, in the same order.</summary>
    private readonly List<TreeItem> _items = new();

    /// <summary>Sort column (-1 = DMX order) and direction.</summary>
    private int _sortColumn = -1;
    private bool _descending;

    /// <summary>The level of a lamp in % (-1 = none), for the level column and its sort.</summary>
    private Func<ProfileFixture, int> _level = _ => -1;

    /// <summary>Row of the last plain or Ctrl click (the start of a Shift range).</summary>
    private int _anchor = -1;

    /// <summary>The selected rows changed by a click: the lamps of the selected rows.</summary>
    internal event Action<IReadOnlyList<ProfileFixture>> RowsSelected;

    /// <summary>A row was right clicked: its lamp and the mouse position in the window.</summary>
    internal event Action<ProfileFixture, Vector2> RowMenuRequested;

    /// <summary>The lamps in the order shown.</summary>
    internal IReadOnlyList<ProfileFixture> Shown => _shown.Select(r => r.Fixture).ToList();

    /// <summary>Number of rows (tests).</summary>
    internal int RowCount => _shown.Count;

    /// <inheritdoc />
    public override void _Ready()
    {
        Columns = Titles.Length;
        HideRoot = true;
        ColumnTitlesVisible = true;
        SelectMode = SelectModeEnum.Multi;
        for (int c = 0; c < Titles.Length; c++)
        {
            SetColumnTitle(c, Titles[c]);
            SetColumnClipContent(c, true);
            SetColumnExpand(c, c == 1);
            SetColumnTitleAlignment(c, c is 0 or LevelColumn ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        }
        SetColumnCustomMinimumWidth(1, 60);
        AddThemeConstantOverride("h_separation", 2);
        ColumnTitleClicked += (column, button) =>
        {
            if (button == (long)MouseButton.Left)
                SortBy((int)column);
        };
        // Keyboard moves (up / down) select cells: make that the row.
        MultiSelected += (_, _, _) => CallDeferred(MethodName.RowsFromCells);
    }

    /// <summary>Shows these rows (DMX order) with the level of each lamp in % (-1 = none); the sort and the selected lamps
    /// stay.</summary>
    internal void SetRows(List<FixtureRow> rows, Func<ProfileFixture, int> level, IEnumerable<ProfileFixture> selected)
    {
        _rows = rows;
        _level = level;
        Fill(selected.ToHashSet());
    }

    /// <summary>Sorts by a column; the same column again reverses the order.</summary>
    internal void SortBy(int column)
    {
        _descending = column == _sortColumn && !_descending;
        _sortColumn = column;
        Fill(SelectedRows().Select(r => r.Fixture).ToHashSet());
    }

    /// <summary>Selects the rows of these lamps (no event).</summary>
    internal void ShowSelected(IEnumerable<ProfileFixture> lamps)
    {
        var set = lamps.ToHashSet();
        for (int i = 0; i < _items.Count; i++)
            SetRowSelected(i, set.Contains(_shown[i].Fixture));
        QueueRedraw();
    }

    /// <summary>Scrolls so the row of this lamp is visible.</summary>
    internal void ScrollToLamp(ProfileFixture lamp)
    {
        int i = _shown.FindIndex(r => r.Fixture == lamp);
        if (i >= 0)
            ScrollToItem(_items[i]);
    }

    /// <summary>Refreshes the level column; the order stays.</summary>
    internal void UpdateLevels()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            string text = LevelText(_level(_shown[i].Fixture));
            if (_items[i].GetText(LevelColumn) != text)
                _items[i].SetText(LevelColumn, text);
        }
    }

    /// <summary>A row's cells joined by two spaces ("150  Binnen T  C103  L201  70%"; tests, explore).</summary>
    internal string RowText(int row) =>
        string.Join("  ", Enumerable.Range(0, Titles.Length).Select(c => _items[row].GetText(c)).Where(t => t.Length > 0));

    /// <summary>A click on a row as the mouse does it (`add` = Ctrl + click; tests, explore).</summary>
    internal void ClickRow(int row, bool add = false) => Click(row, add, false);

    /// <summary>A right click on a row (tests, explore).</summary>
    internal void RightClickRow(int row) => RowMenuRequested?.Invoke(_shown[row].Fixture, GetGlobalRect().GetCenter());

    /// <inheritdoc />
    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } mouse
            || mouse.ButtonIndex is not (MouseButton.Left or MouseButton.Right)
            || GetItemAtPosition(mouse.Position) is not { } item)
            return;
        int row = _items.IndexOf(item);
        if (row < 0)
            return;
        AcceptEvent();
        GrabFocus();
        if (mouse.ButtonIndex == MouseButton.Right)
        {
            if (!IsRowSelected(row))
                Click(row, false, false);
            RowMenuRequested?.Invoke(_shown[row].Fixture, GetViewport().GetMousePosition());
            return;
        }
        Click(row, mouse.CtrlPressed || mouse.MetaPressed, mouse.ShiftPressed);
    }

    /// <summary>Selects rows as a click does: only this row, Ctrl toggles it, Shift selects the range from the anchor.</summary>
    private void Click(int row, bool toggle, bool range)
    {
        if (range && _anchor >= 0 && _anchor < _items.Count)
        {
            int from = Math.Min(_anchor, row), to = Math.Max(_anchor, row);
            for (int i = 0; i < _items.Count; i++)
                SetRowSelected(i, i >= from && i <= to);
        }
        else if (toggle)
        {
            SetRowSelected(row, !IsRowSelected(row));
            _anchor = row;
        }
        else
        {
            for (int i = 0; i < _items.Count; i++)
                SetRowSelected(i, i == row);
            _anchor = row;
        }
        QueueRedraw();
        RowsSelected?.Invoke(SelectedRows().Select(r => r.Fixture).ToList());
    }

    /// <summary>After a keyboard move: a row with a selected cell is selected as a whole.</summary>
    private void RowsFromCells()
    {
        var before = SelectedRows().Select(r => r.Fixture).ToList();
        for (int i = 0; i < _items.Count; i++)
            SetRowSelected(i, Enumerable.Range(0, Titles.Length).Any(c => _items[i].IsSelected(c)));
        var after = SelectedRows().Select(r => r.Fixture).ToList();
        if (!before.SequenceEqual(after) || after.Count == 1)
            RowsSelected?.Invoke(after);
    }

    /// <summary>Rebuilds the items in the sort order, selecting these lamps.</summary>
    private void Fill(HashSet<ProfileFixture> selected)
    {
        Clear();
        _items.Clear();
        _shown = Sorted(_rows).ToList();
        var root = CreateItem();
        foreach (var row in _shown)
        {
            var item = CreateItem(root);
            item.SetText(0, row.Head > 0 ? row.Head.ToString() : "");
            item.SetText(1, row.Broken ? "⚠ " + row.Name : row.Name);
            item.SetText(2, row.Type);
            item.SetText(3, row.Gel);
            item.SetText(LevelColumn, LevelText(_level(row.Fixture)));
            item.SetTextAlignment(0, HorizontalAlignment.Right);
            item.SetTextAlignment(LevelColumn, HorizontalAlignment.Right);
            for (int c = 0; c < Titles.Length; c++)
            {
                item.SetTooltipText(c, row.Tooltip);
                if (row.Broken)
                    item.SetCustomColor(c, new Color(1f, 0.45f, 0.4f));
            }
            _items.Add(item);
        }
        for (int c = 0; c < Titles.Length; c++)
            SetColumnTitle(c, Titles[c] + (c == _sortColumn ? _descending ? " ▼" : " ▲" : ""));
        FitColumns();
        ShowSelected(selected);
    }

    /// <summary>The columns other than the name are as wide as their title or widest cell; the name takes the rest.</summary>
    private void FitColumns()
    {
        var font = GetThemeFont("font");
        int size = GetThemeFontSize("font_size");
        var titleFont = GetThemeFont("title_button_font");
        int titleSize = GetThemeFontSize("title_button_font_size");
        foreach (int c in new[] { 0, 2, 3, LevelColumn })
        {
            float width = titleFont.GetStringSize(GetColumnTitle(c), HorizontalAlignment.Left, -1, titleSize).X;
            foreach (var item in _items)
                width = Mathf.Max(width, font.GetStringSize(item.GetText(c), HorizontalAlignment.Left, -1, size).X);
            if (c == LevelColumn)
                width = Mathf.Max(width, font.GetStringSize("100%", HorizontalAlignment.Left, -1, size).X);
            SetColumnCustomMinimumWidth(c, Mathf.CeilToInt(width) + 2 * GetThemeConstant("inner_item_margin_left") + 6);
        }
    }

    /// <summary>The rows in the chosen order (ties in DMX order).</summary>
    private IEnumerable<FixtureRow> Sorted(List<FixtureRow> rows)
    {
        if (_sortColumn < 0)
            return rows;
        var indexed = rows.Select((r, i) => (Row: r, Index: i));
        Func<(FixtureRow Row, int Index), IComparable> key = _sortColumn switch
        {
            0 => x => x.Row.Head <= 0 ? int.MaxValue : x.Row.Head,
            1 => x => x.Row.Name.ToLowerInvariant(),
            2 => x => x.Row.Type.ToLowerInvariant(),
            3 => x => x.Row.Gel.Length == 0 ? "￿" : x.Row.Gel,
            _ => x => _level(x.Row.Fixture),
        };
        var ordered = _descending ? indexed.OrderByDescending(key) : indexed.OrderBy(key);
        return ordered.ThenBy(x => x.Index).Select(x => x.Row);
    }

    /// <summary>The level as shown ("70%"), "" for none.</summary>
    private static string LevelText(int level) => level < 0 ? "" : $"{level}%";

    /// <summary>Selects or deselects all cells of a row.</summary>
    private void SetRowSelected(int row, bool on)
    {
        for (int c = 0; c < Titles.Length; c++)
        {
            if (on && !_items[row].IsSelected(c))
                _items[row].Select(c);
            else if (!on && _items[row].IsSelected(c))
                _items[row].Deselect(c);
        }
    }

    /// <summary>True when the row is selected.</summary>
    private bool IsRowSelected(int row) => _items[row].IsSelected(0);

    /// <summary>The selected rows in the order shown.</summary>
    private IEnumerable<FixtureRow> SelectedRows() =>
        _shown.Where((_, i) => i < _items.Count && IsRowSelected(i));
}
