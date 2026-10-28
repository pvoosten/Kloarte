using System.Collections.Generic;
using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// ShowBuilder: the set pieces of the blocking (<see cref="Blocking.Scenery"/>) as <see cref="SetPiece"/> nodes under
/// the "Scenery" node, each tagged with its id (<see cref="SetPiece.PieceMeta"/>). The UI and the MCP tools rebuild all
/// of them (<see cref="RebuildScenery"/>) or one (<see cref="RebuildPiece"/>) after an edit; playback puts them in and
/// out and opens curtains (<see cref="CuePlayer"/>).
/// </summary>
public partial class ShowBuilder
{
    /// <summary>The set pieces on stage by piece id.</summary>
    private readonly Dictionary<string, SetPiece> _setPieces = new();

    /// <summary>The set pieces on stage by piece id.</summary>
    internal IReadOnlyDictionary<string, SetPiece> SetPieces => _setPieces;

    /// <summary>
    /// Raised when the geometry that cuts haze beams (<see cref="RenderLayers.Occluders"/>) changed: the scene or the
    /// scenery was built, a piece was rebuilt, went out or came in, a curtain moved.
    /// </summary>
    internal event System.Action OccludersChanged;

    /// <summary>
    /// Raised after <see cref="RebuildScenery"/> / <see cref="RebuildPiece"/>: the new nodes are in their state before
    /// the first cue, the player puts them back in the playhead's state.
    /// </summary>
    internal event System.Action SceneryRebuilt;

    /// <summary>The set piece node of a piece id, or null.</summary>
    internal SetPiece SetPieceOf(string pieceId) =>
        pieceId != null && _setPieces.TryGetValue(pieceId, out var node) && IsInstanceValid(node) ? node : null;

    /// <summary>Builds all set pieces again from the model (after an edit of the scenery).</summary>
    public void RebuildScenery()
    {
        if (GetNodeOrNull<Node3D>("Scenery") is not { } parent || Model == null)
            return;
        foreach (var node in _setPieces.Values.Where(IsInstanceValid))
            RemovePieceNode(node);
        _setPieces.Clear();
        BuildScenery(parent);
        SceneryRebuilt?.Invoke();
        NotifyOccludersChanged();
    }

    /// <summary>
    /// Builds one set piece again from the model (after an edit of it); removes its node when the model has no piece of
    /// that id any more, adds one for a new piece.
    /// </summary>
    public void RebuildPiece(string pieceId)
    {
        if (GetNodeOrNull<Node3D>("Scenery") is not { } parent || Model == null || pieceId == null)
            return;
        if (_setPieces.Remove(pieceId, out var old) && IsInstanceValid(old))
            RemovePieceNode(old);
        if (Model.Blocking.FindPiece(pieceId) is { } piece)
            AddPiece(parent, piece);
        SceneryRebuilt?.Invoke();
        NotifyOccludersChanged();
    }

    /// <summary>
    /// The occluding geometry changed (playback: a piece went in or out, a curtain moved; a build): every lamp's beam
    /// occlusion map is out of date (<see cref="ProfileFixture.InvalidateOcclusion"/>), then <see cref="OccludersChanged"/>.
    /// </summary>
    internal void NotifyOccludersChanged()
    {
        foreach (var fixture in Fixtures)
            fixture.InvalidateOcclusion();
        OccludersChanged?.Invoke();
    }

    /// <summary>Adds a node for every set piece of the model.</summary>
    private void BuildScenery(Node3D parent)
    {
        foreach (var piece in Model.Blocking.Scenery)
            AddPiece(parent, piece);
    }

    /// <summary>Adds a piece's node (a later piece with the same id replaces an earlier one's).</summary>
    private void AddPiece(Node3D parent, SceneryPiece piece)
    {
        if (string.IsNullOrEmpty(piece?.Id))
            return;
        if (_setPieces.Remove(piece.Id, out var old) && IsInstanceValid(old))
            RemovePieceNode(old);
        var node = SetPiece.Create(piece);
        parent.AddChild(node);
        _setPieces[piece.Id] = node;
    }

    /// <summary>Takes a piece's node out of the scene now (so its name is free) and frees it at the end of the frame.</summary>
    private static void RemovePieceNode(SetPiece node)
    {
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    /// <summary>Puts the set pieces in their state after steps 0..upTo of a stack (tracking; no times).</summary>
    private void ApplySceneryAfter(ICueStack stack, int upTo)
    {
        var cues = stack == null ? new List<int>() : stack.Steps.Take(upTo + 1).Select(s => s.CueId).ToList();
        bool changed = false;
        foreach (var (id, node) in _setPieces)
            if (IsInstanceValid(node) && node.Apply(Model.Blocking.SceneryAfter(id, cues)) && node.Occludes)
                changed = true;
        if (changed)
            NotifyOccludersChanged();
    }
}
