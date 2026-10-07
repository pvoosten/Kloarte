using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

// Builds a show from the ShowModel: MagicQ .shw (+ .xhw) and the fixture mechanics JSON.
// Scenery comes from the MagicVis objects; every MagicVis fixture becomes a ProfileFixture aimed by its
// MagicVis rotation (or the hand-focus point in the mechanics). Fixtures without saved mechanics get defaults
// from the kind presets (FixtureConfig, matched by head name). "Save mechanics" writes the current set-up of all
// fixtures (also used by the FixturePanel).
//
// MagicVis axes match ours (+X audience right, +Y up, +Z towards the audience); only the origin differs:
// our origin is the downstage edge on the stage floor. With AutoVenue that comes from the show: the proscenium object
// (z), else a box named "podium"/"stage" (front edge and top), else StageFrontZ. Generated nodes are not saved.
[Tool]
public partial class ShowBuilder : Node3D
{
    [Export(PropertyHint.GlobalFile, "*.shw")] public string ShowFile = "";
    [Export(PropertyHint.File, "*.json")] public string MechanicsFile = "";
    [Export(PropertyHint.File, "*.json")] public string FixtureConfig = "";
    [Export] public float StageFrontZ = 9.75f;       // fallback when AutoVenue finds no proscenium / podium
    [Export] public bool AutoVenue = true;
    [Export(PropertyHint.Range, "0,1,0.01")] public float PreviewIntensity = 0.15f;
    [Export] public bool FixtureShadows = true;

    [ExportGroup("Assumed venue (not in the show file)")]
    [Export] public bool AddVenue = true;
    [Export] public float BackdropZ = -0.6f;        // MagicVis z of a black back wall / cyc for the horizon lights
    [Export] public float AuditoriumDrop = 1.0f;    // auditorium floor below the stage floor

    [ExportToolButton("Rebuild show")]
    public Callable RebuildButton => Callable.From(Build);

    [ExportToolButton("Save mechanics")]
    public Callable SaveButton => Callable.From(() => { SaveMechanics(); });

    public ShowModel Model { get; private set; }
    public MagicQColours Gels { get; private set; }

    // Fallback copy of MagicQ's colour table, used when the show folder has no heads/allcols.csv.
    private const string GelTableFallback = "res://data/allcols.csv";

    private StandardMaterial3D _molton, _floor, _truss, _people, _skin, _setPiece, _cyc;

    private sealed class FixtureConfigFile
    {
        public Dictionary<string, FixtureMechanics> Kinds { get; set; } = new();
        public string Default { get; set; } = "pc";
        public Dictionary<string, string> ByName { get; set; } = new();
        public Dictionary<string, string> ByPersonality { get; set; } = new();   // personality name contains key
        public Dictionary<string, string> ByAddress { get; set; } = new();
    }

    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public override void _Ready() => Build();

    // Loads another show at runtime (the .xhw next to it is picked up automatically). Its mechanics are kept
    // per show in res://shows/<show name>.mechanics.json.
    public void OpenShow(string shwPath)
    {
        ShowFile = shwPath;
        MechanicsFile = $"res://shows/{System.IO.Path.GetFileNameWithoutExtension(shwPath)}.mechanics.json";
        Build();
    }

    public void Rebuild() => Build();

    private void Build()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        if (string.IsNullOrEmpty(ShowFile) || !System.IO.File.Exists(ShowFile))
        {
            GD.PushWarning($"ShowBuilder: show file not found: '{ShowFile}'");
            return;
        }

        _visOf.Clear();
        _actors.Clear();
        Model = ShowModel.Load(ShowFile, MechanicsPath, BlockingPath);
        var show = Model.MagicQ;
        var config = LoadConfig();
        string gelTable = MagicQColours.FindFor(ShowFile) ?? ProjectSettings.GlobalizePath(GelTableFallback);
        Gels = System.IO.File.Exists(gelTable) ? MagicQColours.Load(gelTable) : null;
        CreateMaterials();
        FitVenue(show);

        var scenery = new Node3D { Name = "Scenery" };
        var fixtures = new Node3D { Name = "Fixtures" };
        AddChild(scenery);
        AddChild(fixtures);

        if (AddVenue)
            BuildVenue(scenery, show);

        var byHead = new Dictionary<int, List<Node>>();
        int placed = 0;
        foreach (var item in show.Vis)
        {
            if (item.Kind == MagicQShow.VisKind.Object)
                AddObject(scenery, item);
            else
            {
                var fixture = AddFixture(fixtures, item, show, config);
                if (!byHead.TryGetValue(item.HeadNumber, out var list))
                    byHead[item.HeadNumber] = list = new List<Node>();
                list.Add(fixture);
                placed++;
            }
        }

        BuildActors(show);

        // MagicQ groups become Godot node groups "mq:<name>" (e.g. GetTree().GetNodesInGroup("mq:Dimmer")).
        foreach (var group in show.Groups)
            foreach (int head in group.Heads)
                if (byHead.TryGetValue(head, out var nodes))
                    foreach (var node in nodes)
                        node.AddToGroup("mq:" + group.Name);

        GD.Print($"ShowBuilder: {placed} fixtures, {show.Vis.Count - placed} scenery objects, " +
                 $"{show.Groups.Count} groups, {show.Patch.Count} patched channels, " +
                 $"{Model.Mechanics.Count} fixtures with saved mechanics, " +
                 $"{fixtures.GetChildren().Count(n => n.HasMeta("beamFromMagicQ"))} beam angles from the patch, from {ShowFile}");
    }

    private string MechanicsPath =>
        string.IsNullOrEmpty(MechanicsFile) ? null : ProjectSettings.GlobalizePath(MechanicsFile);

    // Copies the current set-up of every fixture into the model and writes the mechanics JSON.
    public string SaveMechanics()
    {
        if (Model == null || MechanicsPath == null)
            return "No show loaded or no MechanicsFile set";
        foreach (var fixture in GetNode("Fixtures").GetChildren().OfType<ProfileFixture>())
            Model.Mechanics[fixture.DmxAddress] = ReadMechanics(fixture);
        Model.SaveMechanics(MechanicsPath);
        string msg = $"Saved {Model.Mechanics.Count} fixtures to {MechanicsFile}";
        GD.Print(msg);
        return msg;
    }

    // Writes the MagicQ part back to a .shw (+ .xhw next to it). Don't pass the original file unless intended.
    public void ExportMagicQ(string shwPath) => Model?.SaveMagicQ(shwPath);

    // --- Rigging: which object a head hangs on, and where (stored in the MagicVis part of the .shw) ---

    private readonly Dictionary<ProfileFixture, MagicQShow.VisItem> _visOf = new();

    // Default drop from the truss centre to the fixture position (MagicVis rigs have 6.00 -> 5.67).
    private const float DefaultHang = 0.33f;

    // MagicVis objects a head can hang on: trusses/pipes first, then other named objects.
    public IEnumerable<(int Id, string Name)> RigObjects =>
        Model?.MagicQ.Vis.Where(v => v.Kind == MagicQShow.VisKind.Object && v.ObjectType != 7)
             .OrderBy(v => v.ObjectType == 2 ? 0 : 1)
             .Select(v => (v.Id, string.IsNullOrEmpty(v.Name) ? $"Object {v.Id}" : v.Name))
        ?? Enumerable.Empty<(int, string)>();

    public int GetParent(ProfileFixture f) => _visOf.TryGetValue(f, out var v) ? v.ParentId : 0;

    // Hangs the head on another object. On a truss/pipe the head is moved onto it: nearest point along the truss,
    // keeping its current drop below the truss.
    public void SetParent(ProfileFixture f, int parentId)
    {
        if (!_visOf.TryGetValue(f, out var vis))
            return;
        var oldParent = FindObject(vis.ParentId);
        var newParent = FindObject(parentId);
        vis.ParentId = parentId;
        if (newParent is not { ObjectType: 2 })
            return;

        float hang = oldParent is { ObjectType: 2 } ? ToStage(oldParent.X, oldParent.Y, oldParent.Z).Y - f.Position.Y : DefaultHang;
        var (start, dir, length) = TrussLine(newParent);
        float t = Mathf.Clamp((f.Position - start).Dot(dir), 0f, length);
        MoveFixture(f, start + dir * t + Vector3.Down * hang);
    }

    // Moves the head (stage coordinates). It stays focused on its focus point, like re-hanging and re-focusing a lamp;
    // the MagicVis rotation is updated to match (roll = 0).
    public void MoveFixture(ProfileFixture f, Vector3 stagePos)
    {
        if (!_visOf.TryGetValue(f, out var vis))
            return;
        f.Position = stagePos;
        f.FocusPoint = f.FocusPoint;   // re-aim at the same focus point
        vis.X = stagePos.X;
        vis.Y = stagePos.Y + _floorY;
        vis.Z = stagePos.Z + _frontZ;

        // MagicVis: beam = Ry(ry) * Rx(rx) * down  =>  dir = (-sin rx sin ry, -cos rx, -sin rx cos ry).
        Vector3 d = (f.FocusPoint - stagePos).Normalized();
        if (d.IsZeroApprox())
            return;
        float rx = Mathf.Acos(Mathf.Clamp(-d.Y, -1f, 1f));
        float ry = Mathf.Abs(Mathf.Sin(rx)) < 1e-4f ? 0f : Mathf.Atan2(-d.X, -d.Z);
        vis.RotX = Mathf.RadToDeg(rx);
        vis.RotY = Mathf.RadToDeg(ry);
        vis.RotZ = 0f;
    }

    // Fixtures recorded in a cue (S record id), with their level 0..255 for single-channel heads (dimmers), else -1.
    // A cue lists DMX addresses; they are mapped to heads through the patch (L field 10).
    public Dictionary<ProfileFixture, int> FixturesInCue(int cueId)
    {
        var result = new Dictionary<ProfileFixture, int>();
        var cue = Model?.MagicQ.Cues.FirstOrDefault(c => c.Id == cueId);
        if (cue == null)
            return result;
        var headOf = new Dictionary<int, int>();
        foreach (var ch in Model.MagicQ.Patch)
            headOf[ch.Address] = ch.HeadNumber;
        var channelsOfHead = Model.MagicQ.Patch.GroupBy(p => p.HeadNumber).ToDictionary(g => g.Key, g => g.Count());

        var levelOfHead = new Dictionary<int, int>();
        foreach (var (address, value) in cue.Channels)
            if (headOf.TryGetValue(address, out int head))
                levelOfHead[head] = channelsOfHead.GetValueOrDefault(head) == 1 ? value & 0xff : -1;

        foreach (var fixture in _visOf.Keys)
            if (headOf.TryGetValue(fixture.DmxAddress, out int head) && levelOfHead.TryGetValue(head, out int level))
                result[fixture] = level;
        return result;
    }

    // Changes the beam angle of a head in the MagicQ patch (whole degrees, saved with Save show as) and on the lamp.
    public void SetBeamAngle(ProfileFixture f, float degrees)
    {
        f.FieldAngle = degrees;
        var channel = Model?.MagicQ.Patch.FirstOrDefault(p => p.Address == f.DmxAddress);
        if (channel == null)
            return;
        channel.BeamAngle = Mathf.Clamp(Mathf.RoundToInt(degrees), 1, 255);
        f.SetMeta("beamFromMagicQ", true);
    }

    // Plays a cue stack up to and including step `upTo` (index in cue-number order): MagicQ tracks, so the cues of
    // steps 0..upTo are accumulated. Every fixture's intensity is set from its intensity channel (dimmers: their only
    // channel; moving heads: the personality's dimmer attribute). Returns the number of fixtures above 0.
    public int ApplyCueStack(MagicQShow.CueStack stack, int upTo)
    {
        int lit = 0;
        foreach (var (fixture, level) in CueLevels(stack, upTo))
        {
            fixture.Intensity = level;
            if (level > 0f)
                lit++;
        }
        foreach (var (id, actor) in _actors)
            actor.Apply(ActorStateAt(id, stack, upTo));
        return lit;
    }

    // Intensity (0..1) of every fixture after steps 0..upTo of the stack (tracking). upTo = -1: all off.
    public Dictionary<ProfileFixture, float> CueLevels(MagicQShow.CueStack stack, int upTo)
    {
        var cues = Model.MagicQ.Cues.ToDictionary(c => c.Id);
        var state = new Dictionary<int, int>();
        var steps = stack.Steps;
        for (int i = 0; i <= upTo && i < steps.Count; i++)
            if (cues.TryGetValue(steps[i].CueId, out var cue))
                foreach (var (address, value) in cue.Channels)
                    state[address] = value & 0xff;

        var levels = new Dictionary<ProfileFixture, float>();
        foreach (var fixture in _visOf.Keys)
        {
            int channel = IntensityAddress(fixture);
            levels[fixture] = channel > 0 && state.TryGetValue(channel, out int v) ? v / 255f : 0f;
        }
        return levels;
    }

    // --- Actors (blocking) ---

    private readonly Dictionary<string, Actor> _actors = new();
    public IReadOnlyDictionary<string, Actor> Actors => _actors;

    private string BlockingPath => ProjectSettings.GlobalizePath(
        $"res://shows/{System.IO.Path.GetFileNameWithoutExtension(ShowFile)}.blocking.json");

    // Actor state after steps 0..upTo of the stack (upTo = -1: the initial state).
    public ActorState ActorStateAt(string actorId, MagicQShow.CueStack stack, int upTo) =>
        Model.Blocking.StateAfter(actorId, stack == null ? Enumerable.Empty<int>()
            : stack.Steps.Take(upTo + 1).Select(s => s.CueId));

    // Without saved blocking the MagicVis people become the actors (initial position, facing, standing/seated).
    private void BuildActors(MagicQShow show)
    {
        var node = new Node3D { Name = "Actors" };
        AddChild(node);
        var blocking = Model.Blocking;
        if (blocking.Actors.Count == 0)
            foreach (var item in show.Vis.Where(v => v.Kind == MagicQShow.VisKind.Object && v.ObjectType == 6))
            {
                var (female, pose) = Mannequin.MagicVisCodes.GetValueOrDefault(item.ObjectCode, (false, Mannequin.Pose.Standing));
                var p = ToStage(item.X, item.Y, item.Z);
                blocking.Actors.Add(new ActorDef
                {
                    Id = $"vis{item.Id:x}",
                    Name = $"{item.Name} #{blocking.Actors.Count + 1}",
                    Female = female,
                    Initial = new ActorState
                    {
                        Visible = true,
                        Pose = pose == Mannequin.Pose.Seated ? ActorPose.Seated : ActorPose.Standing,
                        Position = new[] { p.X, p.Y, p.Z },
                        Facing = item.RotY,
                    },
                });
            }
        foreach (var def in blocking.Actors)
            AddActorNode(node, def);
    }

    private Actor AddActorNode(Node parent, ActorDef def)
    {
        var actor = Actor.Create(def, _skin, _people, _molton);
        actor.Apply(def.Initial);
        parent.AddChild(actor);
        _actors[def.Id] = actor;
        return actor;
    }

    public Actor AddActor(string name, bool female, Vector3 position)
    {
        var def = new ActorDef
        {
            Id = System.Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Female = female,
            Initial = new ActorState { Visible = true, Pose = ActorPose.Standing, Position = new[] { position.X, position.Y, position.Z }, Facing = 0f },
        };
        Model.Blocking.Actors.Add(def);
        return AddActorNode(GetNode("Actors"), def);
    }

    public void RemoveActor(string actorId)
    {
        Model.Blocking.RemoveActor(actorId);
        if (_actors.Remove(actorId, out var actor))
            actor.QueueFree();
    }

    public string SaveBlocking()
    {
        if (Model == null)
            return "No show loaded";
        Model.SaveBlocking(BlockingPath);
        return $"Saved blocking ({Model.Blocking.Actors.Count} actors) to {System.IO.Path.GetFileName(BlockingPath)}";
    }

    private int IntensityAddress(ProfileFixture f)
    {
        var show = Model.MagicQ;
        var ch = show.Patch.FirstOrDefault(p => p.Address == f.DmxAddress);
        if (ch == null)
            return 0;
        var personality = show.Personalities.FirstOrDefault(p => p.Id == ch.Personality);
        int offset = personality?.IntensityChannel ?? 0;
        return offset < 0 ? 0 : f.DmxAddress + offset;
    }

    // Writes the show (incl. rigging changes) as a MagicQ .shw + .xhw.
    public string SaveShowAs(string shwPath)
    {
        if (Model == null)
            return "No show loaded";
        Model.SaveMagicQ(shwPath);
        return $"Saved MagicQ show {System.IO.Path.GetFileName(shwPath)} (+ .xhw)";
    }

    private MagicQShow.VisItem FindObject(int id) =>
        id == 0 ? null : Model.MagicQ.Vis.FirstOrDefault(v => v.Kind == MagicQShow.VisKind.Object && v.Id == id);

    // A truss runs along its local +X from its position, Size A metres long.
    private (Vector3 Start, Vector3 Dir, float Length) TrussLine(MagicQShow.VisItem truss)
    {
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(truss.RotX), Mathf.DegToRad(truss.RotY),
                                                Mathf.DegToRad(truss.RotZ)), EulerOrder.Yxz);
        return (ToStage(truss.X, truss.Y, truss.Z), (basis * Vector3.Right).Normalized(), truss.SizeA);
    }

    // --- Fixtures ---

    private Node AddFixture(Node parent, MagicQShow.VisItem item, MagicQShow show, FixtureConfigFile config)
    {
        string name = show.HeadName(item.Address);
        if (!Model.Mechanics.TryGetValue(item.Address, out var mech))
        {
            // Real heads (not generic dimmers) are typed by their MagicQ personality, e.g. Robe_RobinT1Profile_Mode1.
            string personality = show.Personalities.FirstOrDefault(p => p.Id == item.Personality)?.Name ?? "";
            string kind = config.ByAddress.GetValueOrDefault(item.Address.ToString())
                          ?? config.ByPersonality.FirstOrDefault(kv => personality.Contains(kv.Key, System.StringComparison.OrdinalIgnoreCase)).Value
                          ?? config.ByName.GetValueOrDefault(name)
                          ?? config.Default;
            mech = Clone(config.Kinds.GetValueOrDefault(kind) ?? new FixtureMechanics());
            mech.Kind = kind;
        }

        // MagicVis fixtures point down (-Y) at zero rotation; Euler order Y-X-Z (pan, then tilt).
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(item.RotX), Mathf.DegToRad(item.RotY),
                                                Mathf.DegToRad(item.RotZ)), EulerOrder.Yxz);
        Vector3 pos = ToStage(item.X, item.Y, item.Z);
        Vector3 dir = basis * Vector3.Down;
        float t = dir.Y < -0.05f ? pos.Y / -dir.Y : 12f;     // hit the stage floor, or 12 m out for horizontal beams

        int universe = (item.Address - 1) / 512 + 1, channel = (item.Address - 1) % 512 + 1;
        var fixture = new ProfileFixture
        {
            Name = $"{universe}.{channel:000} {name}".Replace("/", "-"),
            Position = pos,
            FocusPoint = mech.FocusPoint is { Length: 3 } fp ? new Vector3(fp[0], fp[1], fp[2]) : pos + dir * t,
            Intensity = PreviewIntensity,
            CastShadows = FixtureShadows,
            HeadNumber = item.HeadNumber,
            DmxAddress = item.Address,
        };
        ApplyMechanics(fixture, mech);
        var patchChannel = show.Patch.FirstOrDefault(p => p.Address == item.Address);
        ApplyGel(fixture, patchChannel?.GelOrNone ?? 0);
        // Beam angle from the MagicQ patch wins over mechanics / presets (MagicQ is the source of truth).
        if (patchChannel is { BeamAngle: > 0 })
        {
            fixture.FieldAngle = patchChannel.BeamAngle;
            fixture.SetMeta("beamFromMagicQ", true);
        }
        fixture.SetMeta("shape", patchChannel?.Shape ?? 0);
        _visOf[fixture] = item;
        parent.AddChild(fixture);
        return fixture;
    }

    // The gel comes from the MagicQ patch (not from the mechanics): normalised gel colour x its brightness.
    private void ApplyGel(ProfileFixture f, int gelId)
    {
        var gel = Gels?[gelId];
        f.SetMeta("gel", gel == null || gel.IsNeutral ? "" : gel.Name);
        if (gel == null || gel.IsNeutral)
            return;
        float max = Mathf.Max(gel.R, Mathf.Max(gel.G, gel.B));
        f.GelColor = max <= 0f
            ? Colors.Black
            : new Color(gel.R / max, gel.G / max, gel.B / max) * gel.Brightness;
    }

    private static void ApplyMechanics(ProfileFixture f, FixtureMechanics m)
    {
        f.SetMeta("kind", m.Kind);
        f.FieldAngle = m.FieldAngle;
        f.Softness = m.Softness;
        f.FieldFalloff = m.FieldFalloff;
        f.Energy = m.Energy;
        f.LampColor = new Color(m.Color);
        var s = m.Shutters ?? new Shutters();
        (f.TopDepth, f.TopAngle) = (s.Top.Depth, s.Top.Angle);
        (f.RightDepth, f.RightAngle) = (s.Right.Depth, s.Right.Angle);
        (f.BottomDepth, f.BottomAngle) = (s.Bottom.Depth, s.Bottom.Angle);
        (f.LeftDepth, f.LeftAngle) = (s.Left.Depth, s.Left.Angle);
        f.ShutterRotation = s.Rotation;
        f.Iris = m.Iris;
        f.Gobo = m.Gobo != null && ResourceLoader.Exists(m.Gobo.Path) ? GD.Load<Texture2D>(m.Gobo.Path) : null;
        f.GoboRotation = m.Gobo?.Rotation ?? 0f;
        f.GoboSize = m.Gobo?.Size ?? 1f;
        // Barndoors are stored in the model but not rendered yet.
    }

    private static FixtureMechanics ReadMechanics(ProfileFixture f)
    {
        var m = new FixtureMechanics
        {
            Kind = f.HasMeta("kind") ? f.GetMeta("kind").AsString() : "pc",
            FieldAngle = f.FieldAngle,
            Softness = f.Softness,
            FieldFalloff = f.FieldFalloff,
            Energy = f.Energy,
            Color = "#" + f.LampColor.ToHtml(false),
            Iris = f.Iris,
            FocusPoint = new[] { f.FocusPoint.X, f.FocusPoint.Y, f.FocusPoint.Z },
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
        return m;
    }

    private static FixtureMechanics Clone(FixtureMechanics m) =>
        JsonSerializer.Deserialize<FixtureMechanics>(JsonSerializer.Serialize(m));

    private FixtureConfigFile LoadConfig()
    {
        if (string.IsNullOrEmpty(FixtureConfig) || !FileAccess.FileExists(FixtureConfig))
            return new FixtureConfigFile();
        return JsonSerializer.Deserialize<FixtureConfigFile>(FileAccess.GetFileAsString(FixtureConfig), ConfigJson)
               ?? new FixtureConfigFile();
    }

    // --- Scenery ---

    private void AddObject(Node parent, MagicQShow.VisItem item)
    {
        var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(item.RotX), Mathf.DegToRad(item.RotY),
                                                Mathf.DegToRad(item.RotZ)), EulerOrder.Yxz);
        Vector3 pos = ToStage(item.X, item.Y, item.Z);
        float[] s = { item.SizeA, item.SizeB, item.SizeC };
        string name = string.IsNullOrEmpty(item.Name) ? $"Object{item.Id}" : item.Name;

        switch (item.ObjectType)
        {
            case 2:   // truss / pipe: Size = (length, ?, ?), runs along local +X from the position
                AddBox(parent, name, new Transform3D(basis, pos + basis * new Vector3(s[0] / 2f, 0, 0)),
                    new Vector3(s[0], 0.05f, 0.05f), _truss);
                break;
            case 1:   // proscenium beam: Size = (width, height, depth), centred
                AddBox(parent, name, new Transform3D(basis, pos), new Vector3(s[0], s[1], s[2]), _molton);
                break;
            case 4:   // box / flat: Size = (depth, width, height), bottom-centre at the position
            case 9:   // border (fries): same layout
                AddBox(parent, name, new Transform3D(basis, pos + basis * new Vector3(0, s[2] / 2f, 0)),
                    new Vector3(Mathf.Max(s[1], 0.01f), Mathf.Max(s[2], 0.01f), Mathf.Max(s[0], 0.01f)),
                    item.ObjectType == 9 || name.StartsWith("Manteau") ? _molton : _setPiece);
                break;
            case 6:   // person: becomes an actor (see BuildActors), not static scenery
                break;
            case 8:   // custom model (.mvm, format unknown): placeholder box
                AddBox(parent, $"{name} ({item.Model})", new Transform3D(basis, pos + new Vector3(0, 0.35f, 0)),
                    new Vector3(2f, 0.7f, 1f), _setPiece);
                break;
            default:  // 7 = podium (probably the stage floor, provided by BuildVenue)
                break;
        }
    }

    // MagicVis position of our origin, and where the back wall goes (MagicVis z).
    private float _frontZ, _floorY, _backZ, _hallY;
    private bool _ownPodium;

    private void FitVenue(MagicQShow show)
    {
        var objects = show.Vis.Where(v => v.Kind == MagicQShow.VisKind.Object).ToList();
        var proscenium = objects.FirstOrDefault(o => o.ObjectType == 1);
        var podium = objects.FirstOrDefault(o => o.ObjectType == 4 && System.Text.RegularExpressions.Regex.IsMatch(
            o.Name, "podium|stage|scene|toneel", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        _frontZ = StageFrontZ;
        _floorY = 0f;
        _ownPodium = false;
        if (AutoVenue && proscenium != null)
            _frontZ = proscenium.Z;
        else if (AutoVenue && podium != null)
        {
            _frontZ = podium.Z + podium.SizeA / 2f;      // box: size = depth, width, height; centred in x/z
            _floorY = podium.Y + podium.SizeC;
            _ownPodium = true;
        }
        _hallY = _ownPodium ? 0f : -AuditoriumDrop;       // MagicVis y of the auditorium floor, relative to the stage
        _backZ = AutoVenue && show.Vis.Count > 0 ? Mathf.Min(show.Vis.Min(v => v.Z) - 0.5f, BackdropZ) : BackdropZ;
        GD.Print($"ShowBuilder venue: stage front at MagicVis z={_frontZ}, stage floor y={_floorY}, back wall z={_backZ}" +
                 (_ownPodium ? " (show has its own podium)" : ""));
    }

    private void BuildVenue(Node parent, MagicQShow show)
    {
        float back = _backZ - _frontZ;
        float hall = _ownPodium ? -_floorY : _hallY;
        if (!_ownPodium)
            AddBox(parent, "StageFloor", Transform3D.Identity.Translated(new Vector3(0, -0.5f, back / 2f)),
                new Vector3(18f, 1f, -back), _floor);
        AddBox(parent, "AuditoriumFloor", Transform3D.Identity.Translated(new Vector3(0, hall - 0.05f, (back + 18f) / 2f)),
            new Vector3(20f, 0.1f, 18f - back), _floor);
        AddBox(parent, "Backdrop", Transform3D.Identity.Translated(new Vector3(0, 4f, back - 0.05f)),
            new Vector3(18f, 8f - hall, 0.02f), _cyc);
    }

    private Vector3 ToStage(float x, float y, float z) => new(x, y - _floorY, z - _frontZ);

    private static void AddBox(Node parent, string name, Transform3D xform, Vector3 size, Material mat)
    {
        parent.AddChild(new MeshInstance3D
        {
            Name = name.Replace("/", "-"),
            Mesh = new BoxMesh { Size = size },
            Transform = xform,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided,
        });
    }

    private void CreateMaterials()
    {
        _molton = Mat(new Color(0.02f, 0.02f, 0.02f), 1.0f);
        _floor = Mat(new Color(0.08f, 0.08f, 0.08f), 0.55f);
        _truss = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.6f, 0.62f), Metallic = 0.8f, Roughness = 0.35f };
        _people = Mat(new Color(0.3f, 0.3f, 0.32f), 0.85f);   // clothes
        _skin = Mat(new Color(0.62f, 0.45f, 0.37f), 0.6f);
        _setPiece = Mat(new Color(0.3f, 0.25f, 0.2f), 0.8f);
        _cyc = Mat(new Color(0.1f, 0.1f, 0.1f), 0.95f);
    }

    private static StandardMaterial3D Mat(Color albedo, float roughness) => new()
    {
        AlbedoColor = albedo,
        Roughness = roughness,
    };
}
