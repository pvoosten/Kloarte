using System.Collections.Generic;
using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// Simple mannequin (about 1.76 m standing) built from primitive meshes, for MagicVis "People" objects.
/// Origin between the feet on the floor, facing +Z (towards the audience at zero rotation).
/// Builds: male (broad shoulders) or female (narrower shoulders, wider hips, skirt); standing or seated on a stool.
/// </summary>
public static partial class Mannequin
{

    /// <summary>
    /// MagicVis people object codes (H field 20). GUESS from the shows: 60003 is used for a "Drummer" (seated).
    /// Correct here when verified in MagicVis.
    /// </summary>
    public static readonly Dictionary<string, (bool Female, Pose Pose)> MagicVisCodes = new()
    {
        ["60001"] = (false, Pose.Standing),
        ["60002"] = (true, Pose.Standing),
        ["60003"] = (false, Pose.Seated),
        ["60004"] = (true, Pose.Seated),
    };

    private const float SeatHeight = 0.46f;

    /// <summary>Builds a primitive mannequin (1.76 m standing).</summary>
    public static Node3D Create(string name, bool female, Pose pose, Material skin, Material clothes, Material furniture)
    {
        var root = new Node3D { Name = name };
        float shoulder = female ? 0.19f : 0.22f;   // half shoulder width
        float hip = female ? 0.12f : 0.10f;        // half distance between the legs
        bool seated = pose == Pose.Seated;
        float dy = seated ? SeatHeight + 0.06f - 0.98f : 0f;   // upper body drop: pelvis from 0.98 to just above the seat

        // Legs and feet.
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * hip;
            if (seated)
            {
                Add(root, new CapsuleMesh { Radius = 0.08f, Height = 0.5f }, clothes,
                    new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 2f), new Vector3(x, SeatHeight + 0.06f, 0.2f)));
                Capsule(root, female ? skin : clothes, new Vector3(x, 0.27f, 0.42f), 0.055f, 0.5f);
                Box(root, clothes, new Vector3(x, 0.035f, 0.47f), new Vector3(0.1f, 0.07f, 0.26f));
            }
            else
            {
                Box(root, clothes, new Vector3(x, 0.035f, 0.05f), new Vector3(0.1f, 0.07f, 0.26f));
                Capsule(root, female ? skin : clothes, new Vector3(x, 0.3f, 0), 0.055f, 0.5f);
                if (!female)
                    Capsule(root, clothes, new Vector3(x, 0.74f, 0), 0.08f, 0.5f);
            }
        }
        if (female && !seated)
        {
            // Skirt from the waist to just above the knee.
            Add(root, new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.27f, Height = 0.5f }, clothes,
                new Transform3D(Basis.Identity.Scaled(new Vector3(1f, 1f, 0.75f)), new Vector3(0, 0.77f, 0)));
        }
        if (seated)
            Box(root, furniture, new Vector3(0, SeatHeight / 2f, 0.02f), new Vector3(0.42f, SeatHeight, 0.4f));

        // Pelvis, torso, shoulders, neck, head.
        var up = new Vector3(0, dy, 0);
        Add(root, new CapsuleMesh { Radius = female ? 0.17f : 0.15f, Height = 0.36f }, clothes,
            new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2f).Scaled(new Vector3(1f, 1f, 0.6f)), up + new Vector3(0, 0.98f, 0)));
        // Torso and shoulders by name: the actor dresses them in its shirt (Actor.SetShirt).
        Add(root, new CapsuleMesh { Radius = 0.16f, Height = 0.56f }, clothes,
            new Transform3D(Basis.Identity.Scaled(new Vector3(female ? 0.95f : 1.1f, 1f, 0.6f)), up + new Vector3(0, 1.24f, 0)), TorsoName);
        Add(root, new CapsuleMesh { Radius = 0.065f, Height = shoulder * 2f + 0.13f }, clothes,
            new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2f), up + new Vector3(0, 1.44f, 0)), ShouldersName);
        Capsule(root, skin, up + new Vector3(0, 1.53f, 0), 0.05f, 0.14f);
        Add(root, new SphereMesh { Radius = 0.105f, Height = 0.24f }, skin,
            new Transform3D(Basis.Identity.Scaled(new Vector3(0.9f, 1f, 1f)), up + new Vector3(0, 1.65f, 0.01f)));

        // Arms: hanging slightly outwards when standing; forearms forward onto the thighs when seated.
        foreach (int side in new[] { -1, 1 })
        {
            var tilt = new Basis(Vector3.Forward, side * Mathf.DegToRad(7f));
            float x = side * (shoulder + 0.02f);
            Add(root, new CapsuleMesh { Radius = 0.048f, Height = 0.32f }, clothes,
                new Transform3D(tilt, up + new Vector3(x, 1.29f, 0)));
            if (seated)
            {
                var forward = new Basis(Vector3.Right, Mathf.DegToRad(70f));
                Add(root, new CapsuleMesh { Radius = 0.042f, Height = 0.3f }, female ? skin : clothes,
                    new Transform3D(forward, up + new Vector3(x + side * 0.02f, 1.1f, 0.13f)));
                Add(root, new SphereMesh { Radius = 0.045f, Height = 0.11f }, skin,
                    new Transform3D(Basis.Identity, up + new Vector3(x + side * 0.02f, 1.06f, 0.28f)));
            }
            else
            {
                Add(root, new CapsuleMesh { Radius = 0.042f, Height = 0.3f }, female ? skin : clothes,
                    new Transform3D(tilt, new Vector3(x + side * 0.03f, 1.02f, 0.01f)));
                Add(root, new SphereMesh { Radius = 0.045f, Height = 0.11f }, skin,
                    new Transform3D(Basis.Identity, new Vector3(x + side * 0.045f, 0.83f, 0.01f)));
            }
        }
        return root;
    }

    private static void Capsule(Node3D parent, Material mat, Vector3 pos, float radius, float height) =>
        Add(parent, new CapsuleMesh { Radius = radius, Height = height }, mat, new Transform3D(Basis.Identity, pos));

    private static void Box(Node3D parent, Material mat, Vector3 pos, Vector3 size) =>
        Add(parent, new BoxMesh { Size = size }, mat, new Transform3D(Basis.Identity, pos));

    private static void Add(Node3D parent, Mesh mesh, Material mat, Transform3D xform, string name = null)
    {
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Transform = xform };
        if (name != null)
            instance.Name = name;
        parent.AddChild(instance);
    }

    /// <summary>Node name of the torso (a capsule: its texture's u goes round from the front (+Z) to the right).</summary>
    public const string TorsoName = "Torso";

    /// <summary>Node name of the shoulders.</summary>
    public const string ShouldersName = "Shoulders";
}
