using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// ShowBuilder: venue (from the venue JSON: forestage, cyc, auditorium with seats) and scenery (MagicVis objects).
/// </summary>
public partial class ShowBuilder
{
    private StandardMaterial3D _molton, _floor, _truss, _people, _skin, _setPiece, _cyc, _seat, _wall;

    /// <summary>Where the show sits in the venue (rig position of our origin, back wall, auditorium floor).</summary>
    public StageFrame Frame { get; private set; } = new();

    /// <summary>Stage z of the stage edge (downstage end of the forestage).</summary>
    public float StageEdge => Frame.OwnPodium ? 0f : Venue?.Forestage ?? 0f;
    /// <summary>Stage z of the back wall / cyc.</summary>
    public float BackStageZ => Frame.BackStageZ;

    private Venue LoadVenue()
    {
        // A show can have its own venue file in its project (YAML, or the JSON of an old project); else the scene's
        // VenueFile. A venue file with an error is not applied: the scene keeps the venue it has (user: keep the last
        // good state; the project's venue.yaml is watched and can be broken while being edited).
        foreach (string path in new[] { ProjectFile("venue.yaml"), ProjectFile("venue.json"), VenueFile })
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(AppPaths.File(path)))
                continue;
            try
            {
                return Venue.Load(AppPaths.File(path));
            }
            catch (System.Exception e) when (e is System.IO.InvalidDataException or System.Text.Json.JsonException or System.IO.IOException)
            {
                GD.PushWarning($"ShowBuilder: venue {path} not read ({e.Message}); the venue stays as it was");
                if (Venue != null)
                    return Venue;
            }
        }
        return new Venue { Auditorium = new Auditorium { FirstRowDrop = AuditoriumDrop } };
    }

    private void FitVenue(IShowDocument show)
    {
        Frame = StageFrame.Fit(show.RigItems, Venue, new StageFitOptions
        {
            AutoVenue = AutoVenue, StageFrontZ = StageFrontZ, BackdropZ = BackdropZ, AuditoriumDrop = AuditoriumDrop,
        });
        GD.Print($"ShowBuilder venue: {Venue?.Name}: stage front at MagicVis z={Frame.FrontZ}, stage floor y={Frame.FloorY}, " +
                 $"back wall z={Frame.BackZ}{(Frame.CycTruss != "" ? $" (cyc on {Frame.CycTruss})" : "")}" +
                 (Frame.OwnPodium ? " (show has its own podium)" : ""));
    }

    private void BuildVenue(Node parent, IShowDocument show)
    {
        float back = Frame.BackStageZ;
        float edge = StageEdge;
        float width = Mathf.Max(Venue?.StageWidth ?? 18f, 15f) + 3f;
        if (!Frame.OwnPodium)
            AddBox(parent, "StageFloor", Transform3D.Identity.Translated(new Vector3(0, -0.5f, (back + edge) / 2f)),
                new Vector3(width, 1f, edge - back), _floor);

        // Cyc (if the venue has one) in front of a black back wall.
        var cyc = Venue?.Cyc;
        if (cyc != null && !Frame.OwnPodium)
        {
            var mat = Mat(new Color(cyc.Albedo, cyc.Albedo, cyc.Albedo * 0.97f), 0.95f);
            AddBox(parent, "Cyc", Transform3D.Identity.Translated(new Vector3(0, cyc.Height / 2f, back)),
                new Vector3(cyc.Width, cyc.Height, 0.02f), mat);
        }
        float hall = Frame.OwnPodium ? -Frame.FloorY : Frame.HallY;
        AddBox(parent, "BackWall", Transform3D.Identity.Translated(new Vector3(0, 4f, back - 0.6f)),
            new Vector3(width + 4f, 8f - hall + 1f, 0.05f), _cyc);

        var aud = Venue?.Auditorium;
        if (aud == null || aud.Rows.Count == 0 || Frame.OwnPodium)
        {
            AddBox(parent, "AuditoriumFloor", Transform3D.Identity.Translated(new Vector3(0, hall - 0.05f, (back + 18f) / 2f)),
                new Vector3(20f, 0.1f, 18f - back), _floor);
            return;
        }
        BuildAuditorium(parent, aud, edge);
    }

    /// <summary>
    /// Stepped rows (one platform per row, rising by the rake) with a block of seats per row and the stage front wall.
    /// </summary>
    private void BuildAuditorium(Node parent, Auditorium aud, float edge)
    {
        var hall = new Node3D { Name = "Auditorium" };
        parent.AddChild(hall);
        var rows = aud.Layout(edge);
        float firstFront = edge + aud.FirstRowDistance;
        // Floor in front of the first row and the stage front (wall from the hall floor up to the stage).
        AddBox(hall, "HallFront", Transform3D.Identity.Translated(new Vector3(0, -aud.FirstRowDrop - 0.05f, edge + aud.FirstRowDistance / 2f)),
            new Vector3(aud.Width + 4f, 0.1f, aud.FirstRowDistance), _floor);
        AddBox(hall, "StageFront", Transform3D.Identity.Translated(new Vector3(0, -aud.FirstRowDrop / 2f, edge + 0.02f)),
            new Vector3(Mathf.Max(Venue.StageWidth, 15f) + 3f, aud.FirstRowDrop, 0.04f), _molton);
        foreach (var row in rows)
        {
            // Platform from the hall floor up to this row's floor.
            float h = row.FloorY + aud.FirstRowDrop + 0.1f;
            AddBox(hall, $"Row {row.Name}", Transform3D.Identity.Translated(new Vector3(0, row.FloorY - h / 2f, row.Z)),
                new Vector3(aud.Width, h, aud.RowPitch), _floor);
            if (row.Seats.Count == 0)
                continue;
            float x0 = row.Seats.Min(s => s.X) - aud.SeatWidth / 2f, x1 = row.Seats.Max(s => s.X) + aud.SeatWidth / 2f;
            AddBox(hall, $"Seats {row.Name}", Transform3D.Identity.Translated(new Vector3((x0 + x1) / 2f, row.FloorY + 0.22f, row.Z + 0.1f)),
                new Vector3(x1 - x0, 0.44f, 0.45f), _seat);
            AddBox(hall, $"Backs {row.Name}", Transform3D.Identity.Translated(new Vector3((x0 + x1) / 2f, row.FloorY + 0.6f, row.Z + 0.33f)),
                new Vector3(x1 - x0, 0.75f, 0.08f), _seat);
        }
        var last = rows[^1];
        AddBox(hall, "HallBackWall", Transform3D.Identity.Translated(new Vector3(0, last.FloorY + 2f, last.Z + aud.RowPitch)),
            new Vector3(aud.Width + 4f, 5f, 0.05f), _wall);
        GD.Print($"ShowBuilder auditorium: {rows.Count} rows, {rows.Sum(r => r.Seats.Count)} seats, first row z={firstFront:0.00}");
    }

    private void AddObject(Node parent, IRigItem item)
    {
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(item.RotX), Mathf.DegToRad(item.RotY),
                                                Mathf.DegToRad(item.RotZ)), EulerOrder.Yxz);
        Vector3 pos = ToStage(item.X, item.Y, item.Z);
        float[] s = { item.SizeA, item.SizeB, item.SizeC };
        string name = string.IsNullOrEmpty(item.Name) ? $"Object{item.Id}" : item.Name;

        switch (item.ObjectKind)
        {
            case RigObjectKind.Pipe:   // Size = (length, ?, ?), runs along local +X from the position
                AddBox(parent, name, new Transform3D(basis, pos + basis * new Vector3(s[0] / 2f, 0, 0)),
                    new Vector3(s[0], 0.05f, 0.05f), _truss).SetMeta("vis", item.Id);
                break;
            case RigObjectKind.Proscenium:   // Size = (width, height, depth), centred
                AddBox(parent, name, new Transform3D(basis, pos), new Vector3(s[0], s[1], s[2]), _molton);
                break;
            case RigObjectKind.Box:      // box / flat: Size = (depth, width, height), bottom-centre at the position
            case RigObjectKind.Border:   // border (fries): same layout
                AddBox(parent, name, new Transform3D(basis, pos + basis * new Vector3(0, s[2] / 2f, 0)),
                    new Vector3(Mathf.Max(s[1], 0.01f), Mathf.Max(s[2], 0.01f), Mathf.Max(s[0], 0.01f)),
                    item.ObjectKind == RigObjectKind.Border || name.StartsWith("Manteau") ? _molton : _setPiece);
                break;
            case RigObjectKind.Person:   // becomes an actor (see BuildActors), not static scenery
                break;
            case RigObjectKind.Model:    // custom model (.mvm, format unknown): placeholder box
                AddBox(parent, $"{name} ({item.ModelName})", new Transform3D(basis, pos + new Vector3(0, 0.35f, 0)),
                    new Vector3(2f, 0.7f, 1f), _setPiece);
                break;
            default:  // podium (probably the stage floor, provided by BuildVenue)
                break;
        }
    }

    /// <summary>MagicVis position -&gt; stage coordinates.</summary>
    public Vector3 ToStage(float x, float y, float z) => Frame.ToStage(x, y, z).ToGodot();
    /// <summary>Stage coordinates -&gt; MagicVis position.</summary>
    public Vector3 FromStage(Vector3 p) => Frame.FromStage(p.ToSystem()).ToGodot();

    private static MeshInstance3D AddBox(Node parent, string name, Transform3D xform, Vector3 size, Material mat)
    {
        var box = new MeshInstance3D
        {
            Name = name.Replace("/", "-"),
            Mesh = new BoxMesh { Size = size },
            Transform = xform,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided,
        };
        parent.AddChild(box);
        return box;
    }

    private void CreateMaterials()
    {
        _molton = Mat(new Color(0.02f, 0.02f, 0.02f), 1.0f);
        _floor = Mat(new Color(0.08f, 0.08f, 0.08f), 0.55f);
        _truss = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.6f, 0.62f), Metallic = 0.8f, Roughness = 0.35f };
        _people = Mat(new Color(0.3f, 0.3f, 0.32f), 0.85f);   // clothes
        _skin = Mat(new Color(0.62f, 0.45f, 0.37f), 0.6f);
        _setPiece = Mat(new Color(0.3f, 0.25f, 0.2f), 0.8f);
        _cyc = Mat(new Color(0.02f, 0.02f, 0.02f), 0.95f);     // black back wall (molton)
        _seat = Mat(new Color(0.22f, 0.03f, 0.04f), 0.9f);     // red velvet
        _wall = Mat(new Color(0.05f, 0.05f, 0.06f), 0.9f);
    }

    private static StandardMaterial3D Mat(Color albedo, float roughness) => new()
    {
        AlbedoColor = albedo,
        Roughness = roughness,
    };
}
