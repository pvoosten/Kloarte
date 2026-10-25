using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// Builds a show from the ShowModel: MagicQ .shw (+ .xhw), the mechanics JSON and the venue.
/// Scenery comes from the MagicVis objects; every MagicVis fixture (a lamp) becomes a ProfileFixture aimed by its
/// MagicVis rotation (or the hand-focus point in the mechanics). Each lamp has a catalogue model (fixture type, light
/// output, which settings exist): from its saved mechanics, else FixtureConfig (by address / personality / head name),
/// else the default. "Save mechanics" writes the current set-up of all lamps (also used by the FixturePanel).
///
/// MagicVis axes match ours (+X audience right, +Y up, +Z towards the audience); only the origin differs:
/// our origin is the proscenium line on the stage floor (the forestage reaches Venue.Forestage further downstage).
/// With AutoVenue it comes from the show: the proscenium object (z), else a box named "podium"/"stage" (front edge and
/// top), else StageFrontZ. Generated nodes are not saved.
///
/// Partial class: ShowBuilder.Venue.cs (venue, scenery), ShowBuilder.Rigging.cs (rig, patch), ShowBuilder.Playback.cs
/// (cues, actors).
/// </summary>
[Tool]
public partial class ShowBuilder : Node3D, IStage
{
    /// <summary>
    /// Makes the application's services for a show scene (set by the host at start; null in the editor, where the
    /// scene is not built).
    /// </summary>
    public static System.Func<IStage, IShowServices> ServicesFactory { get; set; }

    /// <summary>The application's services for this scene (null in the editor).</summary>
    public IShowServices Services
    {
        get
        {
            if (_services == null && ServicesFactory != null)
            {
                _services = ServicesFactory(this);
                (_services.Files.ShowFile, _services.Files.MechanicsFile, _services.Files.BlockingFile) =
                    (_showFile, _mechanicsFile, _blockingFile);
            }
            return _services;
        }
    }

    /// <summary>Backing field of <see cref="Services"/>.</summary>
    private IShowServices _services;

    /// <summary>The exported file settings until the services exist.</summary>
    private string _showFile = "", _mechanicsFile = "", _blockingFile = "";

    /// <summary>The open show: files, model, loading and saving.</summary>
    public IShowFiles Files => Services.Files;

    /// <summary>The MagicQ show (.shw; the .xhw next to it is read too).</summary>
    [Export(PropertyHint.GlobalFile, "*.shw")]
    public string ShowFile
    {
        get => _services?.Files.ShowFile ?? _showFile;
        set
        {
            _showFile = value;
            if (_services != null)
                _services.Files.ShowFile = value;
        }
    }
    /// <summary>
    /// When the scene is run directly (not inside a test), open the show MagicQ PC had loaded last (status.dat, else
    /// lastshow.shw) instead of <see cref="ShowFile"/>.
    /// </summary>
    [Export] public bool OpenMagicQLastShow = true;
    /// <summary>The open project's name (file dialogs use it).</summary>
    public string ProjectName => Files.ProjectName;
    /// <summary>Mechanics JSON of the show.</summary>
    [Export(PropertyHint.File, "*.json")]
    public string MechanicsFile
    {
        get => _services?.Files.MechanicsFile ?? _mechanicsFile;
        set
        {
            _mechanicsFile = value;
            if (_services != null)
                _services.Files.MechanicsFile = value;
        }
    }
    /// <summary>Fixture types per head name / personality / address (shows/*.fixtures.json).</summary>
    [Export(PropertyHint.File, "*.json")] public string FixtureConfig = "";
    /// <summary>Blocking file; empty = the project's blocking.json.</summary>
    [Export]
    public string BlockingFile
    {
        get => _services?.Files.BlockingFile ?? _blockingFile;
        set
        {
            _blockingFile = value;
            if (_services != null)
                _services.Files.BlockingFile = value;
        }
    }
    /// <summary>
    /// A file of the open project's folder, e.g. "mechanics.json" (&lt;projects&gt;/&lt;project&gt;/;
    /// <see cref="AppPaths.Projects"/>: res://Projects, not in git; Documents\Kloarte in an exported build).
    /// </summary>
    public string ProjectFile(string name) => Files.ProjectFile(name);
    /// <summary>Venue file (stage, cyc, hoists, stands, auditorium), YAML.</summary>
    [Export(PropertyHint.File, "*.yaml")] public string VenueFile = "res://venue/ccdebrouckere.venue.yaml";
    /// <summary>fallback when AutoVenue finds no proscenium / podium</summary>
    [Export] public float StageFrontZ = 9.75f;
    /// <summary>Fit the venue to the show (proscenium / podium objects).</summary>
    [Export] public bool AutoVenue = true;
    /// <summary>Intensity of every lamp before a cue is played.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float PreviewIntensity = 0.15f;
    /// <summary>Lamps cast shadows.</summary>
    [Export] public bool FixtureShadows = true;

    /// <summary>Add the venue (stage, cyc, auditorium) around the show.</summary>
    [ExportGroup("Assumed venue (when the venue file has no value)")]
    [Export] public bool AddVenue = true;
    /// <summary>MagicVis z of a black back wall when there is no cyc</summary>
    [Export] public float BackdropZ = -0.6f;
    /// <summary>auditorium floor below the stage floor</summary>
    [Export] public float AuditoriumDrop = 1.0f;

    /// <summary>Inspector button: rebuild the show.</summary>
    [ExportToolButton("Rebuild show")]
    public Callable RebuildButton => Callable.From(Rebuild);

    /// <summary>Inspector button: save the mechanics.</summary>
    [ExportToolButton("Save mechanics")]
    public Callable SaveButton => Callable.From(() => { SaveMechanics(); });

    /// <summary>The loaded show model (null before a show is loaded or in the editor).</summary>
    public IShowModel Model => _services?.Files.Model;
    /// <summary>The loaded show document, as the scene and the views see it (model interfaces only).</summary>
    public IShowDocument Document => Model?.Show;

    /// <summary>The round trip with MagicQ PC (the application's).</summary>
    public IConsoleCommands ConsoleLink => Services.Console;
    /// <summary>The console's colour table (gels).</summary>
    public IGelTable Gels => Files.Gels;
    /// <summary>The fixture catalogue.</summary>
    public FixtureCatalog Catalog => Files.Catalog;
    /// <summary>The venue.</summary>
    public Venue Venue { get; private set; }


    private sealed class FixtureConfigFile
    {
        public string Default { get; set; } = "pc";
        /// <summary>head name -&gt; model id or kind</summary>
        public Dictionary<string, string> ByName { get; set; } = new();
        /// <summary>personality name contains key</summary>
        public Dictionary<string, string> ByPersonality { get; set; } = new();
        public Dictionary<string, string> ByAddress { get; set; } = new();
    }

    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <inheritdoc />
    public override void _Ready()
    {
        if (OpenMagicQLastShow && !Engine.IsEditorHint() && Owner != null && Owner == GetTree().CurrentScene
            && Services?.Files.OpenConsoleLastShow() == true)
            GD.Print($"ShowBuilder: MagicQ's last show {ShowFile} (project {ProjectName})");
        if (!Engine.IsEditorHint())
            ConfigureShadowAtlas(GetViewport());   // a place in the shadow atlas for every lamp
        Build();
    }

    /// <summary>
    /// Loads another show at runtime (the .xhw next to it is picked up automatically) with its project's mechanics
    /// (<see cref="IShowFiles.Open"/>).
    /// </summary>
    public void OpenShow(string shwPath)
    {
        Files.Open(shwPath);
        Build();
    }

    /// <summary>Opens a project by name and builds its show (<see cref="IShowFiles.OpenProject"/>).</summary>
    public void OpenProject(string name)
    {
        Files.OpenProject(name);
        Build();
    }

    /// <summary>Opens the project in a folder and builds its show (<see cref="IShowFiles.OpenProjectFolder"/>).</summary>
    public void OpenProjectFolder(string folder)
    {
        Files.OpenProjectFolder(folder);
        Build();
    }

    /// <summary>Makes a new project, opens it and builds its show (<see cref="IShowFiles.NewProject"/>).</summary>
    public void NewProject(string name, string fromShow = null)
    {
        Files.NewProject(name, fromShow);
        Build();
    }

    /// <summary>Builds the show again from its files.</summary>
    public void Rebuild() => Build();

    /// <summary>
    /// Undoes the last edit (<see cref="IShowFiles.Undo"/>) and builds the scene from the model. Returns the edit's
    /// name, or null when there was nothing to undo.
    /// </summary>
    public string Undo() => FromHistory(Files.Undo);

    /// <summary>Builds the scene from the model again, after a file of the project changed the model (no load).</summary>
    public void RebuildFromModel() => Build(fromModel: true);

    /// <summary>Redoes the last undone edit (see <see cref="Undo"/>).</summary>
    public string Redo() => FromHistory(Files.Redo);

    /// <summary>Undo or redo with the lamps' set-up in the model first, then the scene built from the model.</summary>
    private string FromHistory(System.Func<string> step)
    {
        UpdateModelMechanics();
        string name = step();
        if (name != null)
            Build(fromModel: true);
        return name;
    }

    /// <summary>Records an undo entry when the model changed (the lamps' set-up copied in first); its name or null.</summary>
    public string Checkpoint()
    {
        UpdateModelMechanics();
        return Files.Checkpoint();
    }

    /// <summary>
    /// Builds the scene: from the files (load), or from the model as it is (undo / redo: nothing is loaded, and the state
    /// is not taken as saved).
    /// </summary>
    private void Build(bool fromModel = false)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _visOf.Clear();
        _movers.Clear();
        _fixtureOf.Clear();
        _intensityAddress.Clear();
        _actors.Clear();
        if (Services == null)
            return;   // in the editor: the host's services don't exist there
        if (!Files.CanLoad)
        {
            if (!string.IsNullOrEmpty(ShowFile))   // no show at all = starting without a project
                GD.PushWarning($"ShowBuilder: show file not found: '{ShowFile}'");
            return;
        }

        IShowDocument show = (fromModel ? Files.Model : Files.Load()).Show;
        GoboLibrary.ProjectFolder = Files.ProjectFolder;   // the project's own gobos (gobos/<name>.png)
        int saved = Model.Mechanics.Count;
        var config = LoadConfig();
        Files.DefaultVenueFile = VenueFile;   // Save puts a copy of it into a project that has no venue.yaml
        Venue = LoadVenue();
        CreateMaterials();
        FitVenue(show);

        var scenery = new Node3D { Name = "Scenery" };
        var fixtures = new Node3D { Name = "Fixtures" };
        AddChild(scenery);
        AddChild(fixtures);

        if (AddVenue)
            BuildVenue(scenery, show);

        int placed = 0;
        foreach (var item in show.RigItems)
        {
            if (!item.IsFixture)
                AddObject(scenery, item);
            else
            {
                AddFixture(fixtures, item, show, config);
                placed++;
            }
        }

        BuildActors(show);
        AssignGroups();

        GD.Print($"ShowBuilder: {placed} fixtures, {show.RigItems.Count - placed} scenery objects, " +
                 $"{show.Groups.Count} groups, " +
                 $"{saved} fixtures with saved mechanics, " +
                 $"{fixtures.GetChildren().Count(n => n.HasMeta("beamFromMagicQ"))} beam angles from the patch, from {ShowFile}");
        // What the build filled in (default mechanics of lamps without saved ones) is not an edit.
        UpdateModelMechanics();
        if (fromModel)
            Files.ResyncHistory();
        else
            Files.AcceptAsSaved();
    }

    /// <summary>Saves the whole project (<see cref="IShowFiles.SaveProject"/>) with the lamps' current set-up.</summary>
    public string SaveProject()
    {
        UpdateModelMechanics();
        string msg = Files.SaveProject();
        GD.Print(msg);
        return msg;
    }

    /// <summary>The parts with unsaved changes (the lamps' set-up copied into the model first).</summary>
    public System.Collections.Generic.IReadOnlyList<string> UnsavedChanges()
    {
        UpdateModelMechanics();
        return Files.UnsavedChanges();
    }

    /// <summary>
    /// MagicQ groups become Godot node groups "mq:&lt;name&gt;" (e.g. GetTree().GetNodesInGroup("mq:Dimmer")).
    /// </summary>
    private void AssignGroups()
    {
        foreach (var f in _visOf.Keys)
            foreach (var g in f.GetGroups().Select(g => g.ToString()).Where(g => g.StartsWith("mq:")).ToList())
                f.RemoveFromGroup(g);
        var byHead = _visOf.Keys.GroupBy(f => f.HeadNumber).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var group in Document.Groups)
            foreach (int head in group.Heads)
                if (byHead.TryGetValue(head, out var nodes))
                    foreach (var node in nodes)
                        node.AddToGroup("mq:" + group.Name);
    }

    /// <summary>Copies the current set-up of every lamp into the model and writes the mechanics JSON.</summary>
    public string SaveMechanics()
    {
        UpdateModelMechanics();
        string msg = Files.SaveMechanics();
        GD.Print(msg);
        return msg;
    }

    /// <summary>Copies the current set-up of every lamp into the model's mechanics.</summary>
    private void UpdateModelMechanics()
    {
        if (Model == null)
            return;
        foreach (var (fixture, vis) in _visOf)
            Model.Mechanics[vis.Id] = ReadMechanics(fixture);
    }

    /// <summary>
    /// Writes everything needed to render the show elsewhere (the movie process) into a folder: show.shw (+ .xhw),
    /// mechanics.json and blocking.json, incl. changes that are not saved yet. Returns the .shw path.
    /// </summary>
    public string SaveForRender(string folder)
    {
        UpdateModelMechanics();
        return Files.SaveForRender(folder);
    }

    /// <summary>Writes the show (incl. rigging, patch and palette changes) as a MagicQ .shw + .xhw.</summary>
    public string SaveShowAs(string shwPath) => Files.SaveShowAs(shwPath);

    // --- Lamps ---

    private readonly Dictionary<ProfileFixture, IRigItem> _visOf = new();
    /// <summary>MagicVis id -&gt; lamp</summary>
    private readonly Dictionary<int, ProfileFixture> _fixtureOf = new();

    /// <summary>All lamps.</summary>
    public IEnumerable<ProfileFixture> Fixtures => _visOf.Keys.Where(IsInstanceValid);
    /// <summary>The MagicVis item of a lamp, or null.</summary>
    public IRigItem VisOf(ProfileFixture f) => f != null && _visOf.TryGetValue(f, out var v) ? v : null;
    /// <summary>Size of the positional shadow atlas (px).</summary>
    private const int ShadowAtlasSize = 4096;

    /// <summary>
    /// Sets up a viewport's positional shadow atlas for a rig (Godot finding 7): every lamp is a spot light with a
    /// projector, and one that gets no place in the atlas gives no light at all (finding 3), so Godot's default of
    /// 4 + 4 + 16 + 64 = 88 places left all lamps after the 88th dark (<c>shadow_budget</c>: 182 lamps, 94 dark). Here
    /// 4 + 16 + 64 + 256 = 340 places in a 4096 atlas: the lamps that cover most of the picture get the larger maps.
    /// Every viewport that renders the stage needs it (each has its own atlas): the window, the 3D views, the exports.
    /// </summary>
    public static void ConfigureShadowAtlas(Viewport viewport)
    {
        viewport.PositionalShadowAtlasSize = ShadowAtlasSize;
        viewport.SetPositionalShadowAtlasQuadrantSubdiv(0, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4);
        viewport.SetPositionalShadowAtlasQuadrantSubdiv(1, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv16);
        viewport.SetPositionalShadowAtlasQuadrantSubdiv(2, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv64);
        viewport.SetPositionalShadowAtlasQuadrantSubdiv(3, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv256);
    }

    /// <summary>The lamp of a MagicVis id, or null.</summary>
    public ProfileFixture FixtureOf(int visId) => _fixtureOf.GetValueOrDefault(visId);

    /// <summary>The catalogue model of a lamp, or null.</summary>
    public FixtureModel ModelOf(ProfileFixture f) =>
        f != null && f.HasMeta("model") ? Catalog?.Find(f.GetMeta("model").AsString()) : null;

    private void AddFixture(Node parent, IRigItem item, IShowDocument show, FixtureConfigFile config)
    {
        var patch = show.LampPatchOf(item);
        string name = patch.HeadName;
        string personality = patch.Personality;
        FixtureModel model;
        if (Model.Mechanics.TryGetValue(item.Id, out var mech))
            model = Catalog.Resolve(mech.Model) ?? Catalog.Resolve(mech.Kind);
        else
        {
            // Real heads (not generic dimmers) are typed by their MagicQ personality, e.g. Robe_RobinT1Profile_Mode1.
            string type = config.ByAddress.GetValueOrDefault(item.Address.ToString())
                          ?? config.ByPersonality.FirstOrDefault(kv => personality.Contains(kv.Key, System.StringComparison.OrdinalIgnoreCase)).Value
                          ?? config.ByName.GetValueOrDefault(name)
                          ?? config.Default;
            model = Catalog.Resolve(type);
            mech = FixtureMechanics.DefaultFor(model, type);
        }
        model ??= new FixtureModel { Id = "", Name = mech.Kind, Kind = mech.Kind, Lumens = 0 };

        // MagicVis fixtures point down (-Y) at zero rotation; Euler order Y-X-Z (pan, then tilt).
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(item.RotX), Mathf.DegToRad(item.RotY),
                                                Mathf.DegToRad(item.RotZ)), EulerOrder.Yxz);
        Vector3 pos = ToStage(item.X, item.Y, item.Z);
        Vector3 dir = basis * Vector3.Down;

        int universe = (item.Address - 1) / 512 + 1, channel = (item.Address - 1) % 512 + 1;
        var fixture = new ProfileFixture
        {
            Name = $"{universe}.{channel:000} {name} #{item.Id:x}".Replace("/", "-"),
            Position = pos,
            FocusPoint = mech.FocusPoint is { Length: 3 } fp ? new Vector3(fp[0], fp[1], fp[2]) : AimPoint(pos, dir),
            Intensity = PreviewIntensity,
            CastShadows = FixtureShadows,
            HeadNumber = item.HeadNumber,
            DmxAddress = item.Address,
        };
        ApplyMechanics(fixture, model, mech);
        ApplyGel(fixture, patch.GelId);
        // Beam angle from the MagicQ patch wins over mechanics / presets (MagicQ is the source of truth).
        if (patch.BeamAngle > 0)
        {
            fixture.FieldAngle = patch.BeamAngle;
            fixture.SetMeta("beamFromMagicQ", true);
        }
        fixture.SetMeta("headName", name);
        fixture.SetMeta("shape", patch.Shape);
        fixture.SetMeta("personality", personality);
        _visOf[fixture] = item;
        _fixtureOf[item.Id] = fixture;
        parent.AddChild(fixture);
        if (model.IsMoving)
            AttachMovingHead(fixture, model, item, mech, patch);
        Model.Mechanics[item.Id] = ReadMechanics(fixture);
        fixture.MechanicsChanged += OnLampMechanicsChanged;
    }

    /// <summary>
    /// The model's mechanics follow every change on a lamp (the model is always current; the application reads and
    /// edits it there). Moving heads are skipped: their DMX attributes change the lamp every frame (their mechanics
    /// are copied when saving, <see cref="UpdateModelMechanics"/>).
    /// </summary>
    private void OnLampMechanicsChanged(ProfileFixture f)
    {
        if (Model != null && !_movers.ContainsKey(f) && _visOf.TryGetValue(f, out var item))
            Model.Mechanics[item.Id] = ReadMechanics(f);
    }

    /// <summary>Where a beam along `dir` from `pos` lands: the stage floor, the cyc / back wall, or 12 m out.</summary>
    public Vector3 AimPoint(Vector3 pos, Vector3 dir)
    {
        float best = 12f;
        if (dir.Y < -0.02f)
            best = Mathf.Min(best, pos.Y / -dir.Y);
        if (dir.Z < -0.02f && Frame.BackStageZ < pos.Z)
            best = Mathf.Min(best, (pos.Z - Frame.BackStageZ) / -dir.Z);
        return pos + dir * best;
    }

    /// <summary>
    /// The gel comes from the MagicQ patch (not from the mechanics): normalised gel colour x its brightness.
    /// </summary>
    private void ApplyGel(ProfileFixture f, int gelId)
    {
        var gel = Gels?[gelId];
        f.SetMeta("gel", gel == null || gel.IsNeutral ? "" : gel.Name);
        f.SetMeta("gelId", gel == null || gel.IsNeutral ? 0 : gelId);
        f.GelColor = Colors.White;
        if (gel == null || gel.IsNeutral)
            return;
        float max = Mathf.Max(gel.R, Mathf.Max(gel.G, gel.B));
        f.GelColor = max <= 0f
            ? Colors.Black
            : new Color(gel.R / max, gel.G / max, gel.B / max) * gel.Brightness;
    }

    /// <summary>Gel display code: "L201", "R132", or "" for open white (from MagicQ's colour table id).</summary>
    public static string GelCode(int gelId) => GelCodes.Code(gelId);

    /// <summary>Sets the gel of the heads of these lamps (<see cref="IRigCommands.SetGel"/>); a status line.</summary>
    public string SetGel(IEnumerable<ProfileFixture> lamps, int gelId) =>
        RigCommands.SetGel(lamps.Select(VisOf).Where(v => v != null).Select(v => v.Id).ToList(), gelId);

    /// <summary>Names the head of this lamp (<see cref="IRigCommands.RenameHead"/>); a status line.</summary>
    public string RenameHead(ProfileFixture lamp, string name) =>
        VisOf(lamp) is { } vis ? RigCommands.RenameHead(vis.Id, name) : "Could not rename: no lamp";

    /// <summary>Applies the model's photometrics and the mechanics it supports.</summary>
    private void ApplyMechanics(ProfileFixture f, FixtureModel model, FixtureMechanics m)
    {
        f.SetMeta("model", model.Id);
        f.SetMeta("kind", model.Kind);
        f.Lumens = model.Lumens;
        f.PeakCandela = 0f;
        f.OvalRatio = 1f;
        f.OvalRotation = 0f;
        f.Flood = ProfileFixture.FloodMounting.None;
        f.Output = m.Output <= 0f ? 1f : m.Output;
        f.Energy = m.Energy;
        f.LampColor = new Color(string.IsNullOrEmpty(m.Color) ? model.Color : m.Color);
        f.FieldFalloff = model.Lumens > 0f || model.Lamps != null ? model.Falloff : m.FieldFalloff;

        if (model.Kind == FixtureKind.Par && model.FindLamp(m.Lamp) is { } lamp)
        {
            f.SetMeta("lamp", lamp.Id);
            f.PeakCandela = lamp.Candela;
            float wide = Mathf.Max(lamp.Beam[0], lamp.Beam[1]), narrow = Mathf.Min(lamp.Beam[0], lamp.Beam[1]);
            f.FieldAngle = wide;
            f.OvalRatio = narrow / wide;
            f.OvalRotation = m.OvalRotation;
        }
        else
            f.FieldAngle = model.HasZoom ? Mathf.Clamp(m.FieldAngle, model.Zoom[0], model.Zoom[1])
                         : model.Lumens > 0f ? model.FieldAngle : m.FieldAngle;
        if (model.Asymmetric)
            f.Flood = m.Mounting == "bottom" ? ProfileFixture.FloodMounting.Bottom : ProfileFixture.FloodMounting.Top;
        f.Softness = model.Focus || model.Lumens <= 0f && model.Lamps == null ? m.Softness : model.Softness;

        var s = model.Shutters || model.Id == "" ? m.Shutters ?? new Shutters() : new Shutters();
        (f.TopDepth, f.TopAngle) = (s.Top.Depth, s.Top.Angle);
        (f.RightDepth, f.RightAngle) = (s.Right.Depth, s.Right.Angle);
        (f.BottomDepth, f.BottomAngle) = (s.Bottom.Depth, s.Bottom.Angle);
        (f.LeftDepth, f.LeftAngle) = (s.Left.Depth, s.Left.Angle);
        f.ShutterRotation = s.Rotation;
        f.Iris = model.Iris || model.Id == "" ? m.Iris : 1f;
        bool gobo = model.GoboSize != null || model.Id == "";
        f.Gobo = gobo && m.Gobo != null ? GoboLibrary.Load(m.Gobo.Path) : null;
        f.GoboRotation = gobo ? m.Gobo?.Rotation ?? 0f : 0f;
        f.GoboSize = gobo ? m.Gobo?.Size ?? 1f : 1f;
        var b = model.Barndoors ? m.Barndoors ?? new Barndoors() : new Barndoors();
        (f.BarndoorTop, f.BarndoorRight, f.BarndoorBottom, f.BarndoorLeft, f.BarndoorRotation) =
            (b.Top, b.Right, b.Bottom, b.Left, b.Rotation);
        f.SetMeta("mounting", m.Mounting ?? "");
    }

    /// <summary>Changes a lamp's catalogue model (fixture type) and re-applies its mechanics.</summary>
    public void SetModel(ProfileFixture f, string modelId)
    {
        var model = Catalog?.Find(modelId);
        if (model == null || !_visOf.ContainsKey(f))
            return;
        var m = ReadMechanics(f);
        m.Model = model.Id;
        m.Kind = model.Kind;
        if (model.HasZoom)
            m.FieldAngle = Mathf.Clamp(m.FieldAngle, model.Zoom[0], model.Zoom[1]);
        else
            m.FieldAngle = model.FieldAngle;
        m.Lamp ??= model.DefaultLamp;
        m.Mounting = model.Kind == FixtureKind.Flood ? "top" : model.IsMoving ? "hanging" : null;
        m.Color = model.Color;
        ApplyMechanics(f, model, m);
        StoreMechanics(f);
    }

    /// <summary>Puts a lamp's current set-up into the model (after changes that only touch its metadata).</summary>
    private void StoreMechanics(ProfileFixture f)
    {
        if (Model != null && _visOf.TryGetValue(f, out var item))
            Model.Mechanics[item.Id] = ReadMechanics(f);
    }

    /// <summary>
    /// Changes a lamp's mechanics in the model (e.g. PAR lamp, mounting) and re-applies them to the lamp with its
    /// catalogue model.
    /// </summary>
    public void UpdateMechanics(ProfileFixture f, System.Action<FixtureMechanics> change)
    {
        var model = ModelOf(f);
        if (model == null)
            return;
        var m = ReadMechanics(f);
        change(m);
        ApplyMechanics(f, model, m);
        StoreMechanics(f);
    }

    /// <summary>The lamp's current mechanical set-up, as saved in the mechanics JSON.</summary>
    public FixtureMechanics ReadMechanics(ProfileFixture f)
    {
        var model = ModelOf(f);
        var m = new FixtureMechanics
        {
            Model = model?.Id,
            Kind = f.HasMeta("kind") ? f.GetMeta("kind").AsString() : "pc",
            FieldAngle = f.FieldAngle,
            Softness = f.Softness,
            FieldFalloff = f.FieldFalloff,
            Energy = f.Lumens > 0f || f.PeakCandela > 0f ? 400f : f.Energy,
            Output = f.Output,
            Color = "#" + f.LampColor.ToHtml(false),
            Iris = f.Iris,
            FocusPoint = new[] { f.FocusPoint.X, f.FocusPoint.Y, f.FocusPoint.Z },
            Lamp = f.HasMeta("lamp") ? f.GetMeta("lamp").AsString() : null,
            OvalRotation = f.OvalRotation,
            Mounting = f.HasMeta("mounting") && f.GetMeta("mounting").AsString() != "" ? f.GetMeta("mounting").AsString() : null,
        };
        if (f.TopDepth + f.RightDepth + f.BottomDepth + f.LeftDepth > 0f || f.ShutterRotation != 0f)
            m.Shutters = new Shutters
            {
                Top = new() { Depth = f.TopDepth, Angle = f.TopAngle },
                Right = new() { Depth = f.RightDepth, Angle = f.RightAngle },
                Bottom = new() { Depth = f.BottomDepth, Angle = f.BottomAngle },
                Left = new() { Depth = f.LeftDepth, Angle = f.LeftAngle },
                Rotation = f.ShutterRotation,
            };
        if (f.Gobo != null)
            m.Gobo = new Gobo { Path = f.Gobo.ResourcePath, Rotation = f.GoboRotation, Size = f.GoboSize };
        if (f.BarndoorTop + f.BarndoorRight + f.BarndoorBottom + f.BarndoorLeft > 0f || f.BarndoorRotation != 0f)
            m.Barndoors = new Barndoors
            {
                Top = f.BarndoorTop, Right = f.BarndoorRight, Bottom = f.BarndoorBottom, Left = f.BarndoorLeft,
                Rotation = f.BarndoorRotation,
            };
        return m;
    }

    private FixtureConfigFile LoadConfig()
    {
        if (string.IsNullOrEmpty(FixtureConfig) || !System.IO.File.Exists(AppPaths.File(FixtureConfig)))
            return new FixtureConfigFile();
        return JsonSerializer.Deserialize<FixtureConfigFile>(System.IO.File.ReadAllText(AppPaths.File(FixtureConfig)), ConfigJson)
               ?? new FixtureConfigFile();
    }
}
