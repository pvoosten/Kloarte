using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, right-click menus (UI review 9; decided with the user):
/// <list type="bullet">
/// <item>lamp (3D view: a right click without dragging, plan, fixture list): Select same group, Patch / gel... (a dialog:
///   head and DMX of one lamp, gel of the selection), Solo, Aim (the next click on the stage aims the selection, Esc
///   cancels), Remove (undoable);</item>
/// <item>actor (Actors list): Rename..., Remove, Clear movement in this cue;</item>
/// <item>cue (Playback list): Go to, Insert BO cue after it, Insert cue without changes after it.</item>
/// </list>
/// The timeline has its own menu (steps, holds; <see cref="TimelineView"/>).
/// </summary>
public partial class FixturePanel
{
    /// <summary>The menu shown (one at a time; rebuilt for every right click).</summary>
    private PopupMenu _contextMenu;

    /// <summary>What the menu's items do, by id.</summary>
    private readonly Dictionary<int, Action> _contextActions = new();

    /// <summary>The next click on the stage aims the selected lamps ("Aim" in the lamp menu).</summary>
    private bool _aimOnce;

    /// <summary>A new, empty context menu at the mouse.</summary>
    private PopupMenu NewContextMenu()
    {
        if (_contextMenu == null)
        {
            _contextMenu = new PopupMenu { Name = "ContextMenu" };
            _contextMenu.IdPressed += id =>
            {
                if (_contextActions.TryGetValue((int)id, out var action))
                    action();
            };
            AddChild(_contextMenu);
        }
        foreach (var child in _contextMenu.GetChildren().OfType<PopupMenu>())
        {
            _contextMenu.RemoveChild(child);
            child.QueueFree();
        }
        _contextMenu.Clear();
        _contextActions.Clear();
        return _contextMenu;
    }

    /// <summary>Adds an item that runs `action`.</summary>
    private void MenuItem(PopupMenu menu, string text, Action action, bool disabled = false)
    {
        int id = _contextActions.Count + 1;
        _contextActions[id] = action;
        menu.AddItem(text, id);
        menu.SetItemDisabled(menu.GetItemIndex(id), disabled);
    }

    /// <summary>Shows a menu at a viewport position.</summary>
    private static void PopupAt(PopupMenu menu, Vector2 at)
    {
        menu.ResetSize();
        menu.Popup(new Rect2I((Vector2I)at, Vector2I.Zero));
    }

    /// <summary>
    /// The lamp menu for `lamp` at `at` (viewport position): a lamp outside the selection becomes the selection, else the
    /// whole selection is meant.
    /// </summary>
    internal void ShowLampMenu(ProfileFixture lamp, Vector2 at)
    {
        var builder = BuilderOf(lamp);
        if (builder == null || !IsInstanceValid(lamp))
            return;
        if (!Selection.Fixtures.Contains(lamp))
            Selection.Select(lamp);
        var lamps = Selection.Fixtures.Where(IsInstanceValid).ToList();
        string what = lamps.Count == 1 ? $"head {lamp.HeadNumber}" : $"{lamps.Count} lamps";
        var menu = NewContextMenu();

        var groups = lamp.GetGroups().Select(g => g.ToString()).Where(g => g.StartsWith("mq:")).ToList();
        foreach (string group in groups)
        {
            var members = GetTree().GetNodesInGroup(group).OfType<ProfileFixture>().Where(IsInstanceValid).ToList();
            MenuItem(menu, $"Select group {group[3..]} ({members.Count})", () => Selection.Select(members));
        }
        if (groups.Count == 0)
            MenuItem(menu, "Select same group (in no MagicQ group)", () => { }, disabled: true);
        menu.AddSeparator();
        MenuItem(menu, $"Patch / gel... ({what})", () => ShowPatchDialog(lamps, builder));
        MenuItem(menu, lamp.HeadNumber > 0 ? $"Rename head {lamp.HeadNumber}..." : "Rename head... (not patched)",
            () => RenameHead(lamp, builder), disabled: lamp.HeadNumber <= 0);
        MenuItem(menu, _solo.ButtonPressed && _selected == lamp ? "Solo off" : "Solo", () =>
        {
            Selection.Select(lamp);
            _solo.ButtonPressed = !(_solo.ButtonPressed && _selected == lamp);
        });
        MenuItem(menu, "Aim: click on the stage", () =>
        {
            _aimOnce = true;
            _status.Text = $"Click on the stage (3D view) to aim {what}; {KeyMap.KeysText("deselect")} cancels";
        });
        menu.AddSeparator();
        MenuItem(menu, lamps.Count == 1 ? "Remove lamp" : $"Remove {lamps.Count} lamps", () => RemoveLamps(lamps, builder));
        PopupAt(menu, at);
    }

    /// <summary>
    /// Asks for a head's new name (MagicQ keeps it per head, at most 15 characters, so every lamp on the head gets it);
    /// undoable.
    /// </summary>
    private void RenameHead(ProfileFixture lamp, ShowBuilder builder)
    {
        string current = lamp.HasMeta("headName") ? lamp.GetMeta("headName").AsString() : "";
        var dialog = new ConfirmationDialog { Title = $"Rename head {lamp.HeadNumber}", OkButtonText = "Rename" };
        var name = new LineEdit
        {
            Text = current,
            MaxLength = HeadNames.MaxLength,
            CustomMinimumSize = new Vector2(300, 0),
            SelectAllOnFocus = true,
            TooltipText = "The head's name in the patch (MagicQ's limit: 15 characters). Every lamp on the head gets it",
        };
        dialog.AddChild(name);
        void Apply()
        {
            string text = name.Text.Trim();
            dialog.Hide();
            dialog.QueueFree();
            if (text.Length == 0 || text == current || !IsInstanceValid(lamp))
                return;
            _status.Text = builder.RenameHead(lamp, text) + " " + KeyMap.Hint("Undo", "edit_undo");
            FillList();
            CallDeferred(MethodName.BuildParams);
        }
        dialog.Confirmed += Apply;
        name.TextSubmitted += _ => Apply();
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
        name.GrabFocus();
    }

    /// <summary>Removes lamps from the show (undoable; the head's patch goes too when nothing else uses it).</summary>
    private void RemoveLamps(List<ProfileFixture> lamps, ShowBuilder builder)
    {
        Deselect();
        _status.Text = string.Join("; ", lamps.Where(IsInstanceValid).Select(builder.RemoveLamp)) + " " +
                       KeyMap.Hint("Undo", "edit_undo");
        CallDeferred(MethodName.Rescan);
    }

    /// <summary>
    /// Patch / gel for the selection: head and DMX of one lamp (the plan's patch rules) and the gel of all their heads
    /// (applied at once, like the settings' gel field).
    /// </summary>
    private void ShowPatchDialog(List<ProfileFixture> lamps, ShowBuilder builder)
    {
        var dialog = new ConfirmationDialog
        {
            Title = lamps.Count == 1 ? $"Patch / gel - head {lamps[0].HeadNumber}" : $"Gel - {lamps.Count} lamps",
            OkButtonText = lamps.Count == 1 ? "Apply patch" : "Close",
        };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        SpinBox head = null;
        LineEdit dmx = null;
        if (lamps.Count == 1)
        {
            var row = new HBoxContainer();
            // Own label: as the spin box's prefix "Head" was cut to "ead" (explored 11 Oct 2026).
            head = new SpinBox { MinValue = 1, MaxValue = 9999, Value = Math.Max(1, lamps[0].HeadNumber), CustomMinimumSize = new Vector2(100, 0) };
            dmx = new LineEdit
            {
                Text = DmxAddress.Format(lamps[0].DmxAddress),
                PlaceholderText = "1-150",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = "DMX address as universe-channel (1-150) or absolute (150)",
            };
            row.AddChild(new Label { Text = "Head" });
            row.AddChild(head);
            row.AddChild(new Label { Text = "DMX" });
            row.AddChild(dmx);
            box.AddChild(row);
        }
        GelField(box, lamps, builder, () => CallDeferred(MethodName.BuildParams));
        var message = new Label
        {
            Modulate = new Color(0.7f, 0.7f, 0.7f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(360, 0),
            Text = "The gel is set per head as you pick it; the patch with Apply.",
        };
        box.AddChild(message);
        dialog.AddChild(box);
        dialog.Confirmed += () =>
        {
            if (head != null && IsInstanceValid(lamps[0]) && DmxAddress.Parse(dmx.Text) is { } address
                && ((int)head.Value != lamps[0].HeadNumber || address != lamps[0].DmxAddress))
            {
                string error = builder.ApplyLampPatch(lamps[0], (int)head.Value, address, out string done);
                _status.Text = error ?? done + " " + KeyMap.Hint("Save", "file_save");
                Selection.Touch();
                Rescan();
            }
            else if (head != null && DmxAddress.Parse(dmx.Text) == null)
                _status.Text = $"'{dmx.Text}' is not a DMX address (e.g. 1-150 or 150)";
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>The actor menu: rename, remove, clear its movement in the current cue.</summary>
    private void ShowActorMenu(string actorId, Vector2 at)
    {
        var builder = CurrentBuilder;
        var actor = builder?.Model?.Blocking.Find(actorId);
        if (actor == null)
            return;
        SelectActor(actorId);
        var menu = NewContextMenu();
        MenuItem(menu, $"Rename {actor.Name}...", () => RenameActor(actorId));
        MenuItem(menu, $"Remove {actor.Name}", RemoveActor);
        int cue = _player.CueId;
        bool moves = cue >= 0 && builder.Model.Blocking.Steps(cue, actorId).Count > 0;
        MenuItem(menu, "Clear movement in this cue", () =>
        {
            builder.Files.NameNextEdit($"Clear {actor.Name}'s movement");
            builder.Model.Blocking.ClearCue(cue, actorId);
            _player.Invalidate();
            _actorStep = 0;
            CallDeferred(MethodName.BuildActorParams);
        }, disabled: !moves);
        PopupAt(menu, at);
    }

    /// <summary>Asks for an actor's new name.</summary>
    private void RenameActor(string actorId)
    {
        var builder = CurrentBuilder;
        var actor = builder?.Model?.Blocking.Find(actorId);
        if (actor == null)
            return;
        var dialog = new ConfirmationDialog { Title = "Rename actor", OkButtonText = "Rename" };
        var name = new LineEdit { Text = actor.Name, CustomMinimumSize = new Vector2(300, 0), SelectAllOnFocus = true };
        dialog.AddChild(name);
        void Apply()
        {
            string text = name.Text.Trim();
            if (text.Length > 0 && text != actor.Name)
            {
                builder.Files.NameNextEdit($"Rename {actor.Name}");
                actor.Name = text;
                if (builder.Actors.TryGetValue(actorId, out var node))
                    node.SetLabel(text);
                RefreshActors();
                BuildActorParams();
            }
            dialog.Hide();
            dialog.QueueFree();
        }
        dialog.Confirmed += Apply;
        name.TextSubmitted += _ => Apply();
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
        name.GrabFocus();
    }

    /// <summary>The cue menu of the Playback list: go to it, insert a BO / no-change cue after it.</summary>
    private void ShowCueMenu(int step, Vector2 at)
    {
        var stack = _player.Stack;
        if (stack == null || step < 0 || step >= stack.Steps.Count)
            return;
        string cue = $"{stack.Steps[step].CueNumberText} {stack.Steps[step].Text}".Trim();
        var menu = NewContextMenu();
        MenuItem(menu, $"Go to cue {cue}", () => _player.Jump(step));
        MenuItem(menu, "Insert BO cue after it", () => InsertCueAfter(step, blackout: true));
        MenuItem(menu, "Insert cue without changes after it", () => InsertCueAfter(step, blackout: false));
        PopupAt(menu, at);
    }

    /// <summary>Inserts a cue after step `step` (the number between it and the next one).</summary>
    private void InsertCueAfter(int step, bool blackout)
    {
        _player.Jump(step);
        _newCueNumber.GetLineEdit().ReleaseFocus();
        SuggestCueNumber();
        InsertCue(blackout);
    }
}
