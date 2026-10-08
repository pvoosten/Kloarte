using System.Linq;
using Godot;
using Kloarte.Model;

namespace Kloarte.GodotBinding;

/// <summary>
/// An actor on stage: a mannequin that can stand, sit (on a stool) or lie on its back, appear/disappear, and is
/// moved/turned by the CuePlayer. Position = between the feet (standing) on the floor; Facing 0 = towards the
/// audience (+Z). Lying: on the back, head upstage of the position at Facing 0.
/// </summary>
public partial class Actor : Node3D
{
    /// <summary>Blocking actor id.</summary>
    public string ActorId { get; private set; } = "";
    /// <summary>Woman (else man) mannequin.</summary>
    public bool Female { get; private set; }

    private Node3D _standing, _seated, _lying;

    /// <summary>
    /// The shirt (user: the name is part of the mannequin, a texture on it, lit like the body; a colour per actor; the
    /// name as large as fits): a texture rendered once per name / colour into this viewport — the shirt colour with
    /// the name on the chest and on the back — on the torso of every figure; the shoulders in the plain colour.
    /// </summary>
    private SubViewport _shirt;

    /// <summary>The shirt's background and the name labels (front twice: it lies across the texture's seam).</summary>
    private ColorRect _shirtColour;
    private Label[] _shirtNames;

    /// <summary>The torso's material (the shirt texture) and the shoulders' (the plain shirt colour).</summary>
    private StandardMaterial3D _torsoMaterial, _shoulderMaterial;

    /// <summary>The actor's name and shirt colour ("rrggbb") as on the shirt now.</summary>
    private string _shirtName = "", _shirtHex = ShirtColors.Palette[0];

    /// <summary>
    /// The shirt texture: u goes once round the torso (u 0 = front, +Z; increasing to the figure's left = the viewer's
    /// right seen from the front), v from the top; square pixels on the torso.
    /// </summary>
    private static readonly Vector2I ShirtSize = new(1024, 832);

    /// <summary>The name's box on the chest / back: width (about a third of the way round) and height (px).</summary>
    private static readonly Vector2 NameBox = new(380, 190);

    /// <summary>Centre of the name on the texture's v (chest height, on the torso's straight part).</summary>
    private const float NameCentreY = 347;

    /// <summary>Largest letters (font size, px).</summary>
    private const int NameMaxFont = 140;

    private string _pose = ActorPose.Standing;

    /// <summary>Creates the actor node with its standing, seated and lying figures.</summary>
    public static Actor Create(ActorDef def, Material skin, Material clothes, Material furniture, string shirtColor = null)
    {
        var actor = new Actor { Name = def.Name.Replace("/", "-"), ActorId = def.Id, Female = def.Female };
        actor._standing = Mannequin.Create("Standing", def.Female, Mannequin.Pose.Standing, skin, clothes, furniture);
        actor._seated = Mannequin.Create("Seated", def.Female, Mannequin.Pose.Seated, skin, clothes, furniture);
        actor._lying = Mannequin.Create("Lying", def.Female, Mannequin.Pose.Standing, skin, clothes, furniture);
        // On the back: the body's up axis goes upstage (-Z), the face points up; lifted by the body's thickness.
        actor._lying.Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2f), new Vector3(0, 0.12f, 0.1f));
        actor.AddChild(actor._standing);
        actor.AddChild(actor._seated);
        actor.AddChild(actor._lying);
        actor.MakeShirt();
        actor.SetShirt(def.Name, shirtColor ?? actor._shirtHex);
        actor.Pose = ActorPose.Standing;
        actor.SetHeight(def.EffectiveHeight);
        return actor;
    }

    /// <summary>
    /// The mannequins are 1.76 m tall standing; the whole figure (incl. stool) scales with the actor's height.
    /// </summary>
    public void SetHeight(float metres)
    {
        float k = metres / ActorDef.DefaultMaleHeight;
        foreach (var child in new[] { _standing, _seated, _lying })
            child.Scale = new Vector3(k, k, k);
    }

    /// <summary>The shirt's viewport and materials, put on the torso and shoulders of every figure.</summary>
    private void MakeShirt()
    {
        _shirt = new SubViewport
        {
            Name = "Shirt",
            Size = ShirtSize,
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Disable3D = true,
        };
        _shirtColour = new ColorRect { Size = ShirtSize };
        _shirt.AddChild(_shirtColour);
        // Front centred on u 0 = both edges of the texture, back on u 0.5.
        _shirtNames = new[] { 0f, ShirtSize.X, ShirtSize.X / 2f }.Select(centre => new Label
        {
            Position = new Vector2(centre - NameBox.X / 2f, NameCentreY - NameBox.Y / 2f),
            Size = NameBox,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = new LabelSettings(),
        }).ToArray();
        foreach (var label in _shirtNames)
            _shirt.AddChild(label);
        AddChild(_shirt);
        _torsoMaterial = new StandardMaterial3D { AlbedoTexture = _shirt.GetTexture(), Roughness = 0.9f };
        _shoulderMaterial = new StandardMaterial3D { Roughness = 0.9f };
        foreach (var figure in new[] { _standing, _seated, _lying })
        {
            if (figure.GetNodeOrNull<MeshInstance3D>(Mannequin.TorsoName) is { } torso)
                torso.MaterialOverride = _torsoMaterial;
            if (figure.GetNodeOrNull<MeshInstance3D>(Mannequin.ShouldersName) is { } shoulders)
                shoulders.MaterialOverride = _shoulderMaterial;
        }
    }

    /// <summary>
    /// Prints the name on a shirt of this colour ("rrggbb"): white letters on a dark shirt, black on a light one, as
    /// large as fits on the chest (on two lines when that makes them larger); the texture is rendered once.
    /// </summary>
    public void SetShirt(string name, string colorHex)
    {
        if (name.Length > 0)
            Name = name.Replace("/", "-");   // a plain shirt (an export for another person) keeps the node's name
        _shirtName = name;
        _shirtHex = ShirtColors.Parse(colorHex) is null ? _shirtHex : colorHex;
        var (r, g, b) = ShirtColors.Parse(_shirtHex).Value;
        var colour = new Color(r, g, b);
        _shirtColour.Color = colour;
        _shoulderMaterial.AlbedoColor = colour;
        var font = ThemeDB.FallbackFont;
        string text = Fit(name, font, out int size);
        foreach (var label in _shirtNames)
        {
            label.Text = text;
            label.LabelSettings.FontSize = size;
            label.LabelSettings.FontColor = ShirtColors.DarkLetters(_shirtHex) ? new Color(0.05f, 0.05f, 0.05f) : Colors.White;
        }
        _shirt.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }

    /// <summary>Another name on the same shirt (renamed).</summary>
    public void SetLabel(string name) => SetShirt(name, _shirtHex);

    /// <summary>The same name on a shirt of another colour.</summary>
    public void SetShirtColor(string colorHex) => SetShirt(_shirtName, colorHex);

    /// <summary>
    /// The text as printed (one line, or two split at the space nearest the middle when that allows larger letters) and
    /// its font size: as large as fits in <see cref="NameBox"/>, at most <see cref="NameMaxFont"/>.
    /// </summary>
    private static string Fit(string name, Font font, out int size)
    {
        int SizeFor(string text)
        {
            var lines = text.Split('\n');
            float widest = lines.Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, 100).X);
            float tall = font.GetHeight(100) * lines.Length;
            float k = Mathf.Min(NameBox.X / Mathf.Max(1f, widest), NameBox.Y / Mathf.Max(1f, tall));
            return Mathf.Clamp((int)(100 * k), 8, NameMaxFont);
        }
        string best = name;
        size = SizeFor(name);
        int middle = name.Length / 2;
        int space = name.IndexOf(' ') < 0 ? -1
            : Enumerable.Range(0, name.Length).Where(i => name[i] == ' ').OrderBy(i => Mathf.Abs(i - middle)).First();
        if (space > 0)
        {
            string twoLines = name[..space] + "\n" + name[(space + 1)..];
            if (SizeFor(twoLines) > size)
            {
                best = twoLines;
                size = SizeFor(twoLines);
            }
        }
        return best;
    }

    /// <summary>Current pose (ActorPose).</summary>
    public string Pose
    {
        get => _pose;
        set
        {
            _pose = value ?? ActorPose.Standing;
            _standing.Visible = _pose == ActorPose.Standing;
            _seated.Visible = _pose == ActorPose.Seated;
            _lying.Visible = _pose == ActorPose.Lying;
        }
    }

    /// <summary>Facing (degrees about Y; 0 = towards the audience).</summary>
    public float Facing
    {
        get => Mathf.RadToDeg(Rotation.Y);
        set => Rotation = new Vector3(0, Mathf.DegToRad(value), 0);
    }

    /// <summary>Takes a timeline state (position, facing, pose, visible).</summary>
    public void Apply(ActorFrame frame)
    {
        Visible = frame.Visible;
        Pose = frame.Pose;
        Position = new Vector3(frame.Position.X, frame.Position.Y, frame.Position.Z);
        Facing = frame.Facing;
    }

    /// <summary>Takes a (tracked) blocking state.</summary>
    public void Apply(ActorState state)
    {
        Visible = state.Visible ?? true;
        Pose = state.Pose;
        if (state.Position is { Length: 3 } p)
            Position = new Vector3(p[0], p[1], p[2]);
        Facing = state.Facing ?? 0f;
    }
}
