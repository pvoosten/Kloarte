using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// A set piece on stage (<see cref="SceneryPiece"/>): its meshes and material, built from the model. The node's origin
/// is the piece's position (centre of its bottom), turned by its rotation about the vertical axis. Playback sets
/// whether it is on stage (<see cref="Node3D.Visible"/>) and how far a split curtain is open (<see cref="Apply"/>);
/// moving a curtain only rebuilds its cloth mesh (a few hundred vertices). Opaque pieces are on
/// <see cref="RenderLayers.Occluders"/> (they cut haze beams), gauze is not.
/// </summary>
public partial class SetPiece : Node3D
{
    /// <summary>The meta tag on the node with the piece's id.</summary>
    public const string PieceMeta = "pieceId";

    /// <summary>The piece's id in the blocking.</summary>
    public string PieceId { get; private set; } = "";

    /// <summary>The shape.</summary>
    public SceneryShape Shape { get; private set; }

    /// <summary>The material preset.</summary>
    public SceneryMaterial Material { get; private set; }

    /// <summary>True when the piece cuts haze beams (everything but gauze).</summary>
    public bool Occludes => Material != SceneryMaterial.Gauze;

    /// <summary>How far a split curtain is open now (0..100 %); 0 for other pieces.</summary>
    public float Open { get; private set; }

    /// <summary>The piece's sizes as built (a copy of the model's piece).</summary>
    private SceneryPiece _piece;

    /// <summary>A curtain's cloth mesh (rebuilt when it opens or closes); null for other shapes.</summary>
    private ArrayMesh _cloth;

    /// <summary>Builds the node of a piece (meshes, material, layers, transform); on stage, a curtain at its own openness.</summary>
    public static SetPiece Create(SceneryPiece piece)
    {
        var node = new SetPiece
        {
            Name = ("Piece " + (string.IsNullOrEmpty(piece.Name) ? piece.Id : piece.Name)).Replace("/", "-").Replace(":", "-"),
            PieceId = piece.Id,
            Shape = piece.Shape,
            Material = piece.Material,
            _piece = piece.Copy(),
        };
        node.SetMeta(PieceMeta, piece.Id);
        float[] p = piece.Position is { Length: 3 } ? piece.Position : new[] { 0f, 0f, 0f };
        node.Transform = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(piece.Rotation)), new Vector3(p[0], p[1], p[2]));
        var material = SetPieceMaterial.For(piece);
        switch (piece.Shape)
        {
            case SceneryShape.Pyramid:
                node.AddMesh("Pyramid", SetPieceMeshes.Pyramid(Size(piece.Width), Size(piece.Depth), Size(piece.Height)),
                    Vector3.Zero, material);
                break;
            case SceneryShape.Table:
                node.AddTable(material);
                break;
            case SceneryShape.Curtain:
                node._cloth = new ArrayMesh();
                node.AddMesh("Cloth", node._cloth, Vector3.Zero, material);
                node.Open = piece.Opens ? Mathf.Clamp(piece.Open, 0f, 100f) : 0f;
                node.BuildCloth();
                break;
            default:
                node.AddMesh("Block", new BoxMesh { Size = new Vector3(Size(piece.Width), Size(piece.Height), Size(piece.Depth)) },
                    new Vector3(0, Size(piece.Height) / 2f, 0), material);
                break;
        }
        return node;
    }

    /// <summary>
    /// Puts the piece in the state of a moment of the timeline: on stage or not, a split curtain's openness. True when
    /// something changed.
    /// </summary>
    public bool Apply(SceneryFrame frame)
    {
        bool changed = Visible != frame.Visible;
        Visible = frame.Visible;
        return SetOpen(frame.Open) || changed;
    }

    /// <summary>Opens a split curtain to `percent` (0..100); true when it moved. Other pieces ignore it.</summary>
    public bool SetOpen(float percent)
    {
        if (_cloth == null || !_piece.Opens)
            return false;
        percent = Mathf.Clamp(percent, 0f, 100f);
        if (percent == Open)
            return false;   // exact: a near miss (99.99999 % from the end of an ease) must still reach the target
        Open = percent;
        BuildCloth();
        return true;
    }

    /// <summary>A size that is never zero (Godot refuses empty boxes).</summary>
    private static float Size(float metres) => Mathf.Max(metres, 0.01f);

    /// <summary>A table: the top slab with its upper side at the height, and a square leg under each corner.</summary>
    private void AddTable(Material material)
    {
        float w = Size(_piece.Width), d = Size(_piece.Depth), h = Size(_piece.Height);
        float top = Mathf.Clamp(_piece.TopThickness, 0.005f, h), leg = Mathf.Clamp(_piece.LegThickness, 0.005f, Mathf.Min(w, d) / 2f);
        AddMesh("Top", new BoxMesh { Size = new Vector3(w, top, d) }, new Vector3(0, h - top / 2f, 0), material);
        float legHeight = h - top;
        if (legHeight <= 0.001f)
            return;
        var legMesh = new BoxMesh { Size = new Vector3(leg, legHeight, leg) };
        float x = w / 2f - leg / 2f, z = d / 2f - leg / 2f;
        int i = 0;
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            AddMesh($"Leg{++i}", legMesh, new Vector3(sx * x, legHeight / 2f, sz * z), material);
    }

    /// <summary>Adds a mesh child at a local offset with the piece's material, shadows and render layers.</summary>
    private void AddMesh(string name, Mesh mesh, Vector3 offset, Material material)
    {
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = offset,
            MaterialOverride = material,
            CastShadow = Occludes ? GeometryInstance3D.ShadowCastingSetting.DoubleSided : GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = Occludes ? RenderLayers.Default | RenderLayers.Occluders : RenderLayers.Default,
        });
    }

    /// <summary>
    /// The curtain's cloth at its openness: one cloth, or two halves each gathered towards its outer edge (visible width
    /// x (1 - 0.85 open)). The cloth keeps its length: the pleats (as many as when closed, so they come closer) deepen
    /// as it bunches.
    /// </summary>
    private void BuildCloth()
    {
        float width = Size(_piece.Width), height = Size(_piece.Height), fullness = Mathf.Max(_piece.Fullness, 0f) / 100f;
        _cloth.ClearSurfaces();
        if (!_piece.Opens)
        {
            SetPieceMeshes.Cloth(_cloth, -width / 2f, width, height, Periods(width), Amplitude(width, width, fullness));
            return;
        }
        float half = width / 2f, visible = half * (1f - 0.85f * Open / 100f);
        float amplitude = Amplitude(half, visible, fullness);
        int periods = Periods(half);
        SetPieceMeshes.Cloth(_cloth, -half, visible, height, periods, amplitude, startAtOuterEdge: true);
        SetPieceMeshes.Cloth(_cloth, half - visible, visible, height, periods, amplitude, startAtOuterEdge: false);
    }

    /// <summary>The number of pleats (sine periods) of a cloth this wide when it hangs closed.</summary>
    private static int Periods(float width) => Mathf.Max(1, Mathf.RoundToInt(width / SceneryPiece.PleatSpacing));

    /// <summary>
    /// The pleats' amplitude (m) of a cloth `closed` wide when hung flat-out, now gathered into `visible` (its pleats
    /// as many as when closed): its length (1 + fullness) x closed spread over `visible`.
    /// </summary>
    private static float Amplitude(float closed, float visible, float fullness)
    {
        float ratio = (1f + fullness) * closed / visible;
        if (ratio <= 1.0001f)
            return 0f;
        float period = visible / Periods(closed);
        return SceneryPiece.PleatAmplitude(ratio - 1f) * period / SceneryPiece.PleatSpacing;
    }
}
