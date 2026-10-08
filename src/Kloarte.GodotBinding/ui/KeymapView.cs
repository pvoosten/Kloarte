using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The key map: every bindable function per category, with up to two keys (or mouse buttons) each. Click a key button
/// and press the new key (Esc cancels); a key already used where both actions can be active is reported, with the choice
/// to swap. x clears a key, Reset restores an action's default keys; "Reset all" the whole default key map. Changes are
/// saved immediately (only the actions that differ from their default, in user://keymap.json). A tool in its own window
/// (<see cref="ToolWindow"/>).
/// </summary>
public partial class KeymapView : ViewBase
{
    private VBoxContainer _rows;
    private LineEdit _search;
    /// <summary>Messages (to the window's status bar).</summary>
    private readonly StatusText _status = new();
    private ConfirmationDialog _confirm;
    private (string Id, int Index)? _capturing;
    private (string Id, int Index, KeyBinding Binding, AppAction Other)? _pending;
    private readonly Dictionary<(string, int), Button> _buttons = new();

    /// <inheritdoc />
    public override string Title => "Key map";

    /// <summary>Builds the table.</summary>
    public override void _Ready()
    {
        KeyMap.EnsureLoaded();
        var bg = new Panel();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var box = new VBoxContainer();
        box.SetAnchorsPreset(LayoutPreset.FullRect);
        box.OffsetLeft = 8;
        box.OffsetRight = -8;
        box.OffsetTop = 6;
        AddChild(box);

        var top = new HBoxContainer();
        _search = new LineEdit { PlaceholderText = "Search actions or keys", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        _search.TextChanged += _ => Fill();
        var resetAll = new Button { Text = "Reset all", TooltipText = "Every key back to its default" };
        resetAll.Pressed += () => Confirm.Ask(this, "Reset all keys",
            "Reset every key to its default? Your own key bindings are lost.", "Reset all", () =>
            {
                KeyMap.ResetAll();
                _status.Text = "Default key map restored";
                Fill();
            });
        top.AddChild(_search);
        top.AddChild(resetAll);
        box.AddChild(top);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_rows);
        box.AddChild(scroll);

        _confirm = new ConfirmationDialog { OkButtonText = "Swap", CancelButtonText = "Cancel" };
        _confirm.Confirmed += Swap;
        _confirm.Canceled += () =>
        {
            _pending = null;
            _status.Text = "Not changed";
        };
        AddChild(_confirm);
        Fill();
    }

    /// <inheritdoc />
    protected override void OnRoleChanged()
    {
        if (_rows != null)
            _rows.GetParent().GetParent<Control>().Visible = true;
    }

    /// <summary>Rebuilds the rows (filtered by the search text).</summary>
    private void Fill()
    {
        foreach (Node c in _rows.GetChildren())
            c.QueueFree();
        _buttons.Clear();
        string filter = _search.Text.Trim().ToLowerInvariant();
        foreach (var group in KeyMap.Actions.GroupBy(a => a.Category))
        {
            var actions = group.Where(a => filter == "" || a.Name.ToLowerInvariant().Contains(filter)
                                           || KeyMap.Bindings(a.Id).Any(b => b.ToString().ToLowerInvariant().Contains(filter))).ToList();
            if (actions.Count == 0)
                continue;
            var header = new Label { Text = group.Key };
            header.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.4f));
            _rows.AddChild(header);
            foreach (var a in actions)
                _rows.AddChild(Row(a));
        }
    }

    private Control Row(AppAction a)
    {
        var row = new HBoxContainer();
        var name = new Label { Text = a.Name, CustomMinimumSize = new Vector2(260, 0), ClipText = true };
        if (KeyMap.IsChanged(a.Id))
            name.Text += "  *";
        row.AddChild(name);
        row.AddChild(new Label
        {
            Text = a.Context == KeyMap.Global ? "everywhere" : $"in {a.Context}",
            CustomMinimumSize = new Vector2(100, 0),
            Modulate = new Color(1, 1, 1, 0.55f),
        });
        var bindings = KeyMap.Bindings(a.Id);
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            var button = new Button
            {
                Text = i < bindings.Count ? bindings[i].ToString() : "-",
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = "Click, then press the new key (Esc cancels)",
            };
            button.Pressed += () => BeginCapture(a.Id, Mathf.Min(index, KeyMap.Bindings(a.Id).Count));
            _buttons[(a.Id, i)] = button;
            row.AddChild(button);
            var clear = new Button { Text = "x", TooltipText = "Remove this key", Disabled = i >= bindings.Count };
            clear.Pressed += () =>
            {
                KeyMap.Bind(a.Id, index, null);
                Fill();
            };
            row.AddChild(clear);
        }
        var reset = new Button { Text = "Reset", Disabled = !KeyMap.IsChanged(a.Id) };
        reset.Pressed += () =>
        {
            KeyMap.Reset(a.Id);
            Fill();
        };
        row.AddChild(reset);
        return row;
    }

    /// <summary>Waits for the next key / mouse button for an action's binding slot.</summary>
    public void BeginCapture(string id, int index)
    {
        _capturing = (id, index);
        _status.Text = $"Press the new key for '{KeyMap.Find(id).Name}' (Esc cancels)";
        if (_buttons.TryGetValue((id, Mathf.Min(index, 1)), out var b))
            b.Text = "press a key...";
    }

    /// <summary>Takes the captured key before anything else reacts to it.</summary>
    public override void _Input(InputEvent e)
    {
        if (_capturing == null || !e.IsPressed() || e.IsEcho())
            return;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb && GetGlobalRect().HasPoint(mb.GlobalPosition))
            return;   // clicking around in the table
        GetViewport().SetInputAsHandled();
        var (id, index) = _capturing.Value;
        _capturing = null;
        if (e is InputEventKey { PhysicalKeycode: Key.Escape })
        {
            _status.Text = "Cancelled";
            Fill();
            return;
        }
        var binding = KeyBinding.FromEvent(e);
        if (binding == null)
            return;
        Offer(id, index, binding);
    }

    /// <summary>Binds, or asks to swap when another action that can be active at the same time uses the key.</summary>
    public void Offer(string id, int index, KeyBinding binding)
    {
        var other = KeyMap.Conflicts(id, binding).FirstOrDefault();
        if (other == null)
        {
            KeyMap.Bind(id, index, binding);
            _status.Text = $"'{KeyMap.Find(id).Name}' = {binding}";
            Fill();
            return;
        }
        _pending = (id, index, binding, other);
        _confirm.DialogText = $"{binding} is already used by '{other.Name}' ({other.Category}).\n" +
                              $"Swap: '{other.Name}' gets the key '{KeyMap.Find(id).Name}' had.";
        _confirm.PopupCentered();
    }

    /// <summary>Gives this action the key and the other action this action's old key (or nothing).</summary>
    public void Swap()
    {
        if (_pending == null)
            return;
        var (id, index, binding, other) = _pending.Value;
        _pending = null;
        _confirm.Hide();
        var mine = KeyMap.Bindings(id);
        var old = index < mine.Count ? mine[index] : null;
        var theirs = KeyMap.Bindings(other.Id).ToList();
        int at = theirs.IndexOf(binding);
        KeyMap.Bind(other.Id, at, old);
        KeyMap.Bind(id, index, binding);
        _status.Text = $"'{KeyMap.Find(id).Name}' = {binding}; '{other.Name}' = {old?.ToString() ?? "(none)"}";
        Fill();
    }

    /// <summary>True while waiting for a key.</summary>
    private bool Capturing => _capturing != null;
}
