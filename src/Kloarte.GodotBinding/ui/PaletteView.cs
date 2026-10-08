using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// The palettes of a native show (decided 14 Oct 2026: per project, combined: one palette can hold intensity, colour,
/// position and beam values; position as pan / tilt angles). Left the palettes; right the chosen palette's values per head
/// (<see cref="LookEditor"/>). "From the cue" makes a palette of the values the current cue gives the selected lamps'
/// heads and lets the cue refer to it; editing a palette changes every cue that uses it. Deleting a palette puts its
/// values into the cues that used it. A tool in its own window (<see cref="ToolWindow"/>).
/// </summary>
public partial class PaletteView : ViewBase
{
    /// <summary>The palettes.</summary>
    private ItemList _list;

    /// <summary>The chosen palette's name and heads.</summary>
    private VBoxContainer _detail;

    /// <summary>The name of a new palette / the chosen one's new name.</summary>
    private LineEdit _name;

    /// <summary>Messages (to the window's status bar).</summary>
    private readonly StatusText _status = new();

    /// <summary>The chosen palette's name, or null.</summary>
    private string _chosen;

    /// <inheritdoc />
    public override string Title => "Palettes";

    /// <summary>Builds the list, its buttons and the detail.</summary>
    public override void _Ready()
    {
        var bg = new Panel();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var split = new HSplitContainer { SplitOffsets = new[] { 220 } };
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        split.OffsetLeft = 6;
        split.OffsetRight = -6;
        split.OffsetTop = 6;
        AddChild(split);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) };
        _list = new ItemList { Name = "Palettes", SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.ItemSelected += i =>
        {
            _chosen = _list.GetItemText((int)i);
            FillDetail();
        };
        _name = new LineEdit { PlaceholderText = "palette name", ClearButtonEnabled = true };
        var fromCue = new Button { Text = "From the cue", TooltipText = "A palette (this name) of the values the current cue gives the selected lamps' heads; the cue then refers to it" };
        fromCue.Pressed += FromCue;
        var rename = new Button { Text = "Rename", TooltipText = "Give the chosen palette this name (the cues follow)" };
        rename.Pressed += Rename;
        var delete = new Button { Text = "Delete", TooltipText = "Delete the chosen palette: the cues that used it keep its values" };
        delete.Pressed += Delete;
        left.AddChild(_list);
        left.AddChild(_name);
        var buttons = new HBoxContainer();
        buttons.AddChild(fromCue);
        buttons.AddChild(rename);
        buttons.AddChild(delete);
        left.AddChild(buttons);
        split.AddChild(left);

        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_detail);
        split.AddChild(scroll);

        CueEditing.Edited += OnEdited;
        TreeExiting += () => CueEditing.Edited -= OnEdited;
        Fill();
    }

    /// <inheritdoc />
    protected override void OnShowChanged()
    {
        if (_list != null)
            Fill();
    }

    /// <summary>The show changed: the list and the detail again.</summary>
    private void OnEdited()
    {
        if (IsInstanceValid(this) && IsVisibleInTree())
            CallDeferred(MethodName.Fill);
    }

    /// <summary>The native show, or null.</summary>
    private DesignShow Design => CueEditing.Design(Builder);

    /// <summary>Fills the list (keeping the chosen palette) and the detail.</summary>
    private void Fill()
    {
        if (_list == null)
            return;
        _list.Clear();
        foreach (var palette in Design?.Palettes ?? new List<DesignPalette>())
        {
            _list.AddItem(palette.Name);
            _list.SetItemTooltip(_list.ItemCount - 1, $"{palette.Heads.Count} head{(palette.Heads.Count == 1 ? "" : "s")}, " +
                $"used in {Design.CueOrder.Count(c => c.Heads.Values.Any(h => h.Values.Any(v => v.Palette == palette.Name)))} cues");
            if (palette.Name == _chosen)
                _list.Select(_list.ItemCount - 1);
        }
        FillDetail();
    }

    /// <summary>The chosen palette's heads and their values.</summary>
    private void FillDetail()
    {
        if (_detail == null)
            return;
        foreach (var child in _detail.GetChildren())
            child.QueueFree();
        var palette = _chosen == null ? null : Design?.Palette(_chosen);
        if (palette == null)
        {
            _detail.AddChild(new Label
            {
                Text = Design == null ? "A native project has palettes" : "Choose a palette, or make one: select lamps, choose a cue, type a name, From the cue",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                Modulate = new Color(0.7f, 0.7f, 0.7f),
            });
            return;
        }
        _name.Text = palette.Name;
        _detail.AddChild(new Label { Text = palette.Name, Modulate = new Color(1f, 0.75f, 0.4f) });
        foreach (string key in palette.Heads.Keys.OrderBy(k => DesignShow.HeadOfKey(k) ?? 0).ToList())
        {
            if (DesignShow.HeadOfKey(key) is not { } head)
                continue;
            var fold = new Button { Text = $"Head {head} {Design.Head(head)?.Name}", ToggleMode = true, ButtonPressed = palette.Heads.Count <= 3, Flat = true, Alignment = HorizontalAlignment.Left };
            var look = LookEditor.ForPalette(Builder, palette, head, () => CallDeferred(MethodName.FillDetail));
            look.Visible = fold.ButtonPressed;
            fold.Toggled += on => look.Visible = on;
            _detail.AddChild(fold);
            _detail.AddChild(look);
        }
    }

    /// <summary>
    /// A palette of the values the current cue gives the selected lamps' heads (tracked values too; palette references
    /// resolved); new, or the values merged into the palette of that name. The cue refers to it for those values.
    /// </summary>
    private void FromCue()
    {
        var design = Design;
        string name = _name.Text.Trim();
        int index = CueEditing.Cue;
        var heads = Selection.Fixtures.Where(IsInstanceValid).Select(f => f.HeadNumber).Where(h => h > 0).Distinct().ToList();
        if (design == null || name.Length == 0 || index < 0 || heads.Count == 0)
        {
            _status.Text = "Select lamps, choose a cue and type a name first";
            return;
        }
        var cue = design.CueOrder[index];
        int count = 0;
        CueEditing.Edit(Builder, $"Palette {name} from cue {cue.Cue}", d =>
        {
            var palette = d.Palette(name);
            if (palette == null)
                d.Palettes.Add(palette = new DesignPalette { Name = name });
            foreach (int head in heads)
                foreach (var channel in d.LayoutOf(head)?.Channels ?? new List<HeadChannel>())
                {
                    var (value, _) = DesignEdits.Tracked(d, index, head, channel.Key);
                    var resolved = DesignEdits.Resolved(d, head, channel.Key, value);
                    if (resolved == null || resolved.IsPalette || value.Palette == name)
                        continue;
                    DesignEdits.Set(palette.Heads, head, channel.Key, resolved);
                    DesignEdits.Set(cue.Heads, head, channel.Key, LookValue.OfPalette(name));
                    count++;
                }
        });
        _chosen = name;
        _status.Text = $"Palette {name}: {count} values of {heads.Count} head{(heads.Count == 1 ? "" : "s")}; cue {cue.Cue} refers to it";
    }

    /// <summary>Renames the chosen palette (and the cues' references to it).</summary>
    private void Rename()
    {
        var design = Design;
        string name = _name.Text.Trim();
        if (design?.Palette(_chosen ?? "") is not { } palette || name.Length == 0 || name == palette.Name)
            return;
        if (design.Palette(name) != null)
        {
            _status.Text = $"There is a palette {name} already";
            return;
        }
        string old = palette.Name;
        CueEditing.Edit(Builder, $"Rename palette {old}", d =>
        {
            palette.Name = name;
            foreach (var cue in d.CueOrder)
                foreach (var values in cue.Heads.Values)
                    foreach (string key in values.Where(v => v.Value.Palette == old).Select(v => v.Key).ToList())
                        values[key] = LookValue.OfPalette(name);
        });
        _chosen = name;
    }

    /// <summary>Deletes the chosen palette; the cues that referred to it get its values.</summary>
    private void Delete()
    {
        var design = Design;
        if (design?.Palette(_chosen ?? "") is not { } palette)
            return;
        CueEditing.Edit(Builder, $"Delete palette {palette.Name}", d =>
        {
            foreach (var cue in d.CueOrder)
                foreach (var (head, values) in cue.Heads)
                    foreach (string key in values.Where(v => v.Value.Palette == palette.Name).Select(v => v.Key).ToList())
                    {
                        var value = DesignShow.HeadOfKey(head) is { } h ? palette.Of(h)?.GetValueOrDefault(key) : null;
                        if (value != null)
                            values[key] = value;
                        else
                            values.Remove(key);
                    }
            d.Palettes.Remove(palette);
        });
        _status.Text = $"Deleted palette {palette.Name}; its cues keep its values (undo brings it back)";
        _chosen = null;
    }
}
