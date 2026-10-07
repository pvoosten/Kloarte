using Godot;

// Builds a simple theatre venue from parameters.
// Usage: add a Node3D to your scene, attach this script, build (Alt+B), reload the scene.
// Change the values in the Inspector and click "Rebuild venue".
//
// Coordinates (metres):
//   origin = downstage centre, on the stage floor
//   +X = audience's right, +Y = up, -Z = upstage, +Z = towards the audience
[Tool]
public partial class VenueBuilder : Node3D
{
    [ExportGroup("Stage")]
    [Export] public float StageWidth = 12f;
    [Export] public float StageDepth = 10f;
    [Export] public float StageHeight = 1.0f;      // stage floor above auditorium floor
    [Export] public float GridHeight = 8f;         // underside of the grid / stage house ceiling

    [ExportGroup("Proscenium")]
    [Export] public float ProsceniumOpening = 9f;
    [Export] public float ProsceniumHeight = 6f;
    [Export] public float ApronDepth = 0.5f;       // stage in front of the proscenium

    [ExportGroup("Masking")]
    [Export] public int LegPairs = 3;
    [Export] public float LegWidth = 1.5f;
    [Export] public float LegOpening = 9f;         // distance between left and right legs
    [Export] public float BorderHeight = 2f;
    [Export] public float BorderTrim = 5.5f;       // bottom edge of the borders above the stage floor
    [Export] public bool Cyc = true;

    [ExportGroup("Auditorium")]
    [Export] public float AuditoriumDepth = 15f;

    // Godot 4.4+: shows a button in the Inspector. On older versions remove these two lines
    // and reload the scene to rebuild.
    [ExportToolButton("Rebuild venue")]
    public Callable RebuildButton => Callable.From(Build);

    private StandardMaterial3D _molton, _floor, _cyc, _wall, _hall;

    public override void _Ready() => Build();

    private void Build()
    {
        // Remove previously generated geometry (generated nodes are not saved in the scene).
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _molton = Mat(new Color(0.02f, 0.02f, 0.02f), 1.00f);
        _floor  = Mat(new Color(0.08f, 0.08f, 0.08f), 0.55f);
        _cyc    = Mat(new Color(0.85f, 0.85f, 0.85f), 0.95f);
        _wall   = Mat(new Color(0.15f, 0.15f, 0.15f), 0.90f);
        _hall   = Mat(new Color(0.25f, 0.08f, 0.08f), 0.90f);

        float halfOuter = StageWidth / 2f + 1f;   // stage house walls a bit outside the stage
        float wallT = 0.3f;                        // wall thickness
        float clothT = 0.02f;                      // cloth thickness

        // --- Floors ---
        AddBox("StageFloor",
            new Vector3(0, -StageHeight / 2f, -StageDepth / 2f),
            new Vector3(StageWidth, StageHeight, StageDepth), _floor);

        AddBox("AuditoriumFloor",
            new Vector3(0, -StageHeight - 0.05f, AuditoriumDepth / 2f),
            new Vector3(halfOuter * 2f, 0.1f, AuditoriumDepth), _hall);

        // --- Stage house ---
        float houseH = GridHeight + StageHeight;
        float houseCy = houseH / 2f - StageHeight;

        AddBox("BackWall",
            new Vector3(0, houseCy, -StageDepth - wallT / 2f),
            new Vector3(halfOuter * 2f, houseH, wallT), _wall);

        foreach (int side in new[] { -1, 1 })
        {
            AddBox(side < 0 ? "SideWall_L" : "SideWall_R",
                new Vector3(side * (halfOuter + wallT / 2f), houseCy, -StageDepth / 2f),
                new Vector3(wallT, houseH, StageDepth + 2f * wallT), _wall);
        }

        AddBox("Grid",
            new Vector3(0, GridHeight + wallT / 2f, -StageDepth / 2f),
            new Vector3(halfOuter * 2f, wallT, StageDepth), _wall);

        // --- Proscenium (wall with an opening) ---
        float pz = -ApronDepth + wallT / 2f;
        float sideW = halfOuter - ProsceniumOpening / 2f;
        foreach (int side in new[] { -1, 1 })
        {
            AddBox(side < 0 ? "Proscenium_L" : "Proscenium_R",
                new Vector3(side * (ProsceniumOpening / 2f + sideW / 2f), GridHeight / 2f, pz),
                new Vector3(sideW, GridHeight, wallT), _wall);
        }
        AddBox("Proscenium_Top",
            new Vector3(0, (ProsceniumHeight + GridHeight) / 2f, pz),
            new Vector3(halfOuter * 2f, GridHeight - ProsceniumHeight, wallT), _wall);

        // --- Legs and borders ---
        float playDepth = StageDepth - ApronDepth;
        for (int i = 0; i < LegPairs; i++)
        {
            float z = -ApronDepth - (i + 1) * playDepth / (LegPairs + 1);
            float legH = BorderTrim + BorderHeight;

            foreach (int side in new[] { -1, 1 })
            {
                AddBox($"Leg{i + 1}_{(side < 0 ? "L" : "R")}",
                    new Vector3(side * (LegOpening / 2f + LegWidth / 2f), legH / 2f, z),
                    new Vector3(LegWidth, legH, clothT), _molton);
            }

            AddBox($"Border{i + 1}",
                new Vector3(0, BorderTrim + BorderHeight / 2f, z + 0.15f),
                new Vector3(LegOpening + 2f * LegWidth, BorderHeight, clothT), _molton);
        }

        // --- Cyclorama ---
        if (Cyc)
        {
            float cycH = BorderTrim + BorderHeight;
            AddBox("Cyc",
                new Vector3(0, cycH / 2f, -StageDepth + 0.6f),
                new Vector3(LegOpening + 2f * LegWidth, cycH, clothT), _cyc);
        }
    }

    private void AddBox(string name, Vector3 center, Vector3 size, Material mat)
    {
        AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = mat,
            Position = center,
            // Thin cloths must block light from both sides, otherwise light leaks through.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided,
        });
    }

    private static StandardMaterial3D Mat(Color albedo, float roughness) => new()
    {
        AlbedoColor = albedo,
        Roughness = roughness,
        Metallic = 0f,
    };
}
