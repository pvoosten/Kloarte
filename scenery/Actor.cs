using Godot;

// An actor on stage: a mannequin that can stand, sit (on a stool) or lie on its back, appear/disappear, and is
// moved/turned by the CuePlayer. Position = between the feet (standing) on the floor; Facing 0 = towards the
// audience (+Z). Lying: on the back, head upstage of the position at Facing 0.
public partial class Actor : Node3D
{
    public string ActorId { get; private set; } = "";
    public bool Female { get; private set; }

    private Node3D _standing, _seated, _lying;
    private string _pose = ActorPose.Standing;

    public static Actor Create(ActorDef def, Material skin, Material clothes, Material furniture)
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
        actor.Pose = ActorPose.Standing;
        actor.SetHeight(def.EffectiveHeight);
        return actor;
    }

    // The mannequins are 1.76 m tall standing; the whole figure (incl. stool) scales with the actor's height.
    public void SetHeight(float metres)
    {
        float k = metres / ActorDef.DefaultMaleHeight;
        foreach (var child in new[] { _standing, _seated, _lying })
            child.Scale = new Vector3(k, k, k);
    }

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

    public float Facing
    {
        get => Mathf.RadToDeg(Rotation.Y);
        set => Rotation = new Vector3(0, Mathf.DegToRad(value), 0);
    }

    public void Apply(ActorState state)
    {
        Visible = state.Visible ?? true;
        Pose = state.Pose;
        if (state.Position is { Length: 3 } p)
            Position = new Vector3(p[0], p[1], p[2]);
        Facing = state.Facing ?? 0f;
    }
}
