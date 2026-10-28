using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// FixturePanel, undo / redo (UI review 6): Ctrl+Z / Ctrl+Y (Edit menu, key map <c>edit_undo</c> / <c>edit_redo</c>).
/// Undo puts the show, mechanics and blocking back as they were before the last edit (<see cref="ShowBuilder.Undo"/>) and
/// the scene is rebuilt from the model; the selected lamps, the selected actor and the cue stay. In the gobo editor's
/// window the keys undo its drawing instead (<see cref="GoboEditorView.HandleKey"/>). Edits are recorded when they have settled (<see cref="PollUnsaved"/>).
/// </summary>
public partial class FixturePanel
{
    /// <summary>Undoes (or redoes) the last edit and shows the scene as it is then.</summary>
    private async void UndoEdit(bool redo)
    {
        var builder = CurrentBuilder;
        if (builder?.Model == null)
            return;
        var view = TakeView(builder);
        string name = redo ? builder.Redo() : builder.Undo();
        if (name == null)
        {
            _status.Text = redo ? "Nothing to redo" : "Nothing to undo";
            return;
        }
        await RestoreView(builder, view);
        _status.Text = redo
            ? $"Redone: {name} {KeyMap.Hint("Undo", "edit_undo")}"
            : $"Undone: {name} {KeyMap.Hint("Redo", "edit_redo")}";
    }

    /// <summary>What the view shows that a rebuild of the scene keeps: the selected lamps, the actor, the cue and step.</summary>
    /// <param name="Lamps">Rig item ids of the selected lamps.</param>
    /// <param name="Actor">The selected actor, or null.</param>
    /// <param name="Piece">The selected set piece, or null.</param>
    /// <param name="Stack">The cue stack's number, or null.</param>
    /// <param name="Step">The playback step.</param>
    /// <param name="Cue">The cue number, or null.</param>
    private readonly record struct ViewKeep(HashSet<int> Lamps, string Actor, string Piece, int? Stack, int Step, float? Cue);

    /// <summary>Takes the view (and clears the selection, which the rebuild frees) before the scene is rebuilt.</summary>
    private ViewKeep TakeView(ShowBuilder builder)
    {
        var lamps = Selection.Fixtures.Where(IsInstanceValid).Select(f => builder.VisOf(f)?.Id ?? -1).ToHashSet();
        var view = new ViewKeep(lamps, _selectedActor, _selectedPiece, _player.Stack?.Number, _player.Step, CurrentCueNumber());
        Deselect();
        return view;
    }

    /// <summary>After a rebuild of the scene: the nodes are rescanned and the view taken by <see cref="TakeView"/> is back.</summary>
    private async System.Threading.Tasks.Task RestoreView(ShowBuilder builder, ViewKeep view)
    {
        ulong started = Time.GetTicksMsec();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);   // the old nodes are freed at frame end
        Rescan();
        RestoreStack(view.Stack, view.Step, view.Cue);
        var again = builder.Fixtures.Where(f => view.Lamps.Contains(builder.VisOf(f)?.Id ?? -2)).ToList();
        if (again.Count > 0)
        {
            Select(again[0]);
            Selection.Select(again);
        }
        else if (view.Actor != null && builder.Model.Blocking.Find(view.Actor) != null)
            SelectActor(view.Actor);
        else if (view.Piece != null && builder.Model.Blocking.FindPiece(view.Piece) != null)
            Selection.SelectPiece(view.Piece);
        RefreshUnsaved();
        UpdateProjectBox();
        GD.Print($"Scene rebuilt ({Time.GetTicksMsec() - started} ms)");
    }

    /// <summary>The Edit menu's item texts: what Undo and Redo would do ("Undo Remove lamp").</summary>
    private void UpdateEditMenu(PopupMenu edit, int undoId, int redoId)
    {
        var files = CurrentBuilder?.Model != null ? CurrentBuilder.Files : null;
        if (files != null && Input.GetMouseButtonMask() == 0)
            CurrentBuilder.Checkpoint();   // an edit not recorded yet is the one Undo undoes
        string undo = files?.UndoName, redo = files?.RedoName;
        int u = edit.GetItemIndex(undoId), r = edit.GetItemIndex(redoId);
        edit.SetItemText(u, undo != null ? $"Undo {undo}" : "Undo");
        edit.SetItemDisabled(u, undo == null);
        edit.SetItemText(r, redo != null ? $"Redo {redo}" : "Redo");
        edit.SetItemDisabled(r, redo == null);
    }
}
