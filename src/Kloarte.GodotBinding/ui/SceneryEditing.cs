using System;
using System.Linq;
using Godot;
using Kloarte.Model;
using SysVec3 = System.Numerics.Vector3;

namespace Kloarte.GodotBinding;

/// <summary>
/// What the views share about editing the project's set pieces (<see cref="Blocking.Scenery"/>): adding, duplicating,
/// removing, changing a piece and what a cue changes of it, each a named undo step (the edit is recorded when it has
/// settled, so a whole drag is one step); <see cref="Edited"/> tells the panel, which shows the result on stage. Which
/// piece is under a click in a 3D view comes from the model's geometry (<see cref="SceneryGeometry"/>), not from the
/// scene's nodes.
/// </summary>
internal static class SceneryEditing
{
    /// <summary>
    /// A piece was edited: its id (null: several, or one removed) and whether the piece itself changed (true: the scene
    /// shows it again) or only what a cue does with it (false: the playback shows the cue again).
    /// </summary>
    public static event Action<string, bool> Edited;

    /// <summary>
    /// The state of a piece at the playhead (set by the panel from the playback's timeline), or null when there is no
    /// timeline yet.
    /// </summary>
    public static Func<string, SceneryFrame?> Now { get; set; }

    /// <summary>How far apart new pieces are put, so they don't all stand on the same spot (m).</summary>
    private const float NewPieceStep = 0.6f;

    /// <summary>The name a shape is shown with ("Block", "Curtain").</summary>
    public static string ShapeName(SceneryShape shape) => shape.ToString();

    /// <summary>The state of a piece at the playhead: in or out, how far a curtain is open (before playback: in, its own opening).</summary>
    public static SceneryFrame FrameOf(SceneryPiece piece) => Now?.Invoke(piece.Id) ?? new SceneryFrame(true, piece.Open);

    /// <summary>The project's blocking (null without a show).</summary>
    private static Blocking BlockingOf(ShowBuilder builder) => builder?.Model?.Blocking;

    /// <summary>
    /// Adds a piece of a shape with its default sizes (a curtain across the stage upstage, other pieces centre stage, each
    /// new one a little to the side of the last) and returns it; null without a show.
    /// </summary>
    public static SceneryPiece Add(ShowBuilder builder, SceneryShape shape)
    {
        var blocking = BlockingOf(builder);
        if (blocking == null)
            return null;
        int n = blocking.Scenery.Count(p => p.Shape == shape) + 1;
        string name = $"{ShapeName(shape)} {n}";
        var piece = SceneryPiece.New(ActorIds.For(name, blocking.Scenery.Select(p => p.Id)), name, shape);
        float side = ((blocking.Scenery.Count % 5) - 2) * NewPieceStep;
        piece.Position = shape == SceneryShape.Curtain ? new[] { 0f, 0f, -4f - n * 0.5f } : new[] { side, 0f, -2f };
        builder.Files.NameNextEdit($"Add {name}");
        blocking.Scenery.Add(piece);
        Edited?.Invoke(piece.Id, true);
        return piece;
    }

    /// <summary>
    /// Adds a copy of a piece (with what the cues do with it) next to it, named "... copy"; returns it, or null when
    /// there is no such piece.
    /// </summary>
    public static SceneryPiece Duplicate(ShowBuilder builder, string pieceId)
    {
        var blocking = BlockingOf(builder);
        if (blocking?.FindPiece(pieceId) is not { } source)
            return null;
        var copy = source.Copy();
        copy.Name = $"{source.Name} copy";
        copy.Id = ActorIds.For(copy.Name, blocking.Scenery.Select(p => p.Id));
        copy.Position[0] += NewPieceStep;
        builder.Files.NameNextEdit($"Duplicate {source.Name}");
        blocking.Scenery.Insert(blocking.Scenery.IndexOf(source) + 1, copy);
        foreach (var (cue, pieces) in blocking.SceneryCues.ToList())
            if (pieces.TryGetValue(pieceId, out var change))
                pieces[copy.Id] = new SceneryChange { Visible = change.Visible, Open = change.Open, Time = change.Time };
        Edited?.Invoke(copy.Id, true);
        return copy;
    }

    /// <summary>Removes a piece and what the cues do with it; returns its name, or null when there is no such piece.</summary>
    public static string Remove(ShowBuilder builder, string pieceId)
    {
        var blocking = BlockingOf(builder);
        if (blocking?.FindPiece(pieceId) is not { } piece)
            return null;
        if (Selection.Piece == pieceId)
            Selection.Clear();
        builder.Files.NameNextEdit($"Remove {piece.Name}");
        blocking.RemovePiece(pieceId);
        Edited?.Invoke(null, true);
        return piece.Name;
    }

    /// <summary>Changes a piece (its place, sizes, colour...): one undo step named `name`.</summary>
    public static void Edit(ShowBuilder builder, string name, string pieceId, Action<SceneryPiece> change)
    {
        if (BlockingOf(builder)?.FindPiece(pieceId) is not { } piece)
            return;
        builder.Files.NameNextEdit(name);
        change(piece);
        Edited?.Invoke(pieceId, true);
    }

    /// <summary>
    /// Changes what a cue does with a piece (`change` gets the cue's entry, made when there is none): one undo step named
    /// `name`; an entry left empty is removed.
    /// </summary>
    public static void EditCue(ShowBuilder builder, string name, int cueId, string pieceId, Action<SceneryChange> change)
    {
        var blocking = BlockingOf(builder);
        if (blocking?.FindPiece(pieceId) == null || cueId < 0)
            return;
        builder.Files.NameNextEdit(name);
        change(blocking.SceneryChangeOf(cueId, pieceId, create: true));
        if (blocking.SceneryChangeOf(cueId, pieceId) is { IsEmpty: true })
            blocking.ClearSceneryChange(cueId, pieceId);
        Edited?.Invoke(pieceId, false);
    }

    /// <summary>Takes away what a cue does with a piece (it tracks from the cues before): one undo step.</summary>
    public static void ClearCue(ShowBuilder builder, string name, int cueId, string pieceId)
    {
        var blocking = BlockingOf(builder);
        if (blocking?.SceneryChangeOf(cueId, pieceId) == null)
            return;
        builder.Files.NameNextEdit(name);
        blocking.ClearSceneryChange(cueId, pieceId);
        Edited?.Invoke(pieceId, false);
    }

    /// <summary>
    /// The piece on stage at the playhead that a ray (world coordinates, e.g. a click in a 3D view) hits first, and where
    /// (stage coordinates); null when it hits none.
    /// </summary>
    public static (SceneryPiece Piece, Vector3 Point)? Hit(ShowBuilder builder, Vector3 origin, Vector3 dir)
    {
        var blocking = BlockingOf(builder);
        if (blocking == null)
            return null;
        var o = builder.ToLocal(origin);
        var d = builder.GlobalBasis.Inverse() * dir;
        (SceneryPiece Piece, float T)? best = null;
        foreach (var piece in blocking.Scenery)
        {
            var frame = FrameOf(piece);
            if (!frame.Visible)
                continue;
            if (SceneryGeometry.RayHit(piece, new SysVec3(o.X, o.Y, o.Z), new SysVec3(d.X, d.Y, d.Z), frame.Open) is { } t
                && (best == null || t < best.Value.T))
                best = (piece, t);
        }
        return best is { } b ? (b.Piece, o + d * b.T) : null;
    }
}
