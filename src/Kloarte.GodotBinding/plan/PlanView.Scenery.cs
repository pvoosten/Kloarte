using System.Globalization;
using System.Linq;
using Godot;
using Kloarte.Model;
using SysVec2 = System.Numerics.Vector2;

namespace Kloarte.GodotBinding;

/// <summary>
/// PlanView: the project's set pieces in the plan. A click on a piece selects it; dragging moves it in 5 cm steps (hold
/// the "place freely" key, <c>plan_free</c>, to move it freely); dragging with the "turn" key held (<c>plan_rotate</c>)
/// turns it about its centre in 15° steps (freely with <c>plan_free</c> too). The plan key <c>plan_delete</c> removes the
/// selected piece. Edits go through <see cref="SceneryEditing"/> (a whole drag is one undo step).
/// </summary>
public partial class PlanView
{
    /// <summary>The piece being dragged (id), or null.</summary>
    private string _dragPiece;

    /// <summary>The drag turns the piece (else it moves it).</summary>
    private bool _pieceTurning;

    /// <summary>Where the piece's centre was relative to the mouse (plan m) when the move started.</summary>
    private SysVec2 _pieceGrab;

    /// <summary>The piece's rotation and the mouse's angle about its centre when the turn started (degrees).</summary>
    private float _pieceStartRotation, _pieceStartAngle;

    /// <summary>Grid of a moved piece (m) and steps of a turned one (degrees), unless placed freely.</summary>
    private const float PieceGrid = 0.05f, TurnStep = 15f;

    /// <summary>The piece under a view position (the selected one first, then the one drawn last), or null.</summary>
    private PlanPiece PieceAt(Vector2 viewPos)
    {
        if (_data == null)
            return null;
        var p = ToPlan(viewPos);
        var hits = _data.Pieces.Where(x => SceneryGeometry.Contains(x.Piece, p, x.Open)).ToList();
        return hits.FirstOrDefault(x => x.Selected) ?? hits.LastOrDefault();
    }

    /// <summary>Selects a piece and starts dragging it (moving, or turning while the turn key is held).</summary>
    private void GrabPiece(PlanPiece piece, Vector2 viewPos)
    {
        Selection.SelectPiece(piece.Piece.Id);
        if (Builder != null)
            Builder.SelectedSupport = 0;
        _dragPiece = piece.Piece.Id;
        StartPieceDrag(piece.Piece, viewPos);
    }

    /// <summary>Takes the references of a move or a turn from where the piece and the mouse are now.</summary>
    private void StartPieceDrag(SceneryPiece piece, Vector2 viewPos)
    {
        var at = ToPlan(viewPos);
        _pieceTurning = KeyMap.Held("plan_rotate", GetTree().Root);
        _pieceGrab = new SysVec2(piece.Position[0], piece.Position[2]) - at;
        _pieceStartRotation = piece.Rotation;
        _pieceStartAngle = SceneryGeometry.AngleTo(piece, at);
    }

    /// <summary>The mouse moved while dragging a piece: it moves or turns (the turn key can be pressed or let go during the drag).</summary>
    private void DragPiece(Vector2 viewPos)
    {
        if (Builder?.Model?.Blocking.FindPiece(_dragPiece) is not { } piece)
            return;
        if (!_dragging && viewPos.DistanceTo(_pressAt) <= 4f)
            return;
        _dragging = true;
        var root = GetTree().Root;
        if (KeyMap.Held("plan_rotate", root) != _pieceTurning)
            StartPieceDrag(piece, viewPos);
        bool free = KeyMap.Held("plan_free", root);
        var at = ToPlan(viewPos);
        if (_pieceTurning)
        {
            float rotation = _pieceStartRotation + SceneryGeometry.AngleTo(piece, at) - _pieceStartAngle;
            if (!free)
                rotation = Mathf.Round(rotation / TurnStep) * TurnStep;
            rotation = SceneryGeometry.Wrap(rotation);
            if (!Mathf.IsEqualApprox(rotation, piece.Rotation))
                SceneryEditing.Edit(Builder, $"Turn {piece.Name}", piece.Id, p => p.Rotation = rotation);
            _status.Text = $"{piece.Name}: {rotation.ToString("0", CultureInfo.InvariantCulture)}°";
        }
        else
        {
            var centre = at + _pieceGrab;
            if (!free)
                centre = new SysVec2(Mathf.Round(centre.X / PieceGrid) * PieceGrid, Mathf.Round(centre.Y / PieceGrid) * PieceGrid);
            if (!Mathf.IsEqualApprox(centre.X, piece.Position[0]) || !Mathf.IsEqualApprox(centre.Y, piece.Position[2]))
                SceneryEditing.Edit(Builder, $"Move {piece.Name}", piece.Id, p => p.Position = new[] { centre.X, p.Position[1], centre.Y });
            _status.Text = $"{piece.Name}: x {centre.X.ToString("0.00", CultureInfo.InvariantCulture)} m, z {centre.Y.ToString("0.00", CultureInfo.InvariantCulture)} m";
        }
        Refresh();
    }

    /// <summary>Removes the selected set piece.</summary>
    private void RemoveSelectedPiece()
    {
        string name = SceneryEditing.Remove(Builder, Selection.Piece);
        _status.Text = name == null ? "Select a set piece first" : $"Removed {name} {KeyMap.Hint("Undo", "edit_undo")}";
        Refresh();
    }
}
