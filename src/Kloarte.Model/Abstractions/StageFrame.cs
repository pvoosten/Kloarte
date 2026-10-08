using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Kloarte.Model;

/// <summary>
/// Where the show sits in the venue: the rig (MagicVis) position of our origin (the proscenium line on the stage
/// floor), the back wall / cyc and the auditorium floor; and the conversion between rig and stage coordinates. Rig
/// and stage axes are the same (+X audience right, +Y up, +Z towards the audience); only the origin differs.
/// </summary>
public sealed class StageFrame
{
    /// <summary>Rig z of the proscenium line (our z = 0).</summary>
    public float FrontZ { get; init; }

    /// <summary>Rig y of the stage floor (our y = 0).</summary>
    public float FloorY { get; init; }

    /// <summary>Rig z of the back wall / cyc.</summary>
    public float BackZ { get; init; }

    /// <summary>Stage y of the auditorium floor (the first row).</summary>
    public float HallY { get; init; }

    /// <summary>True when the show models its own podium (then the venue's stage and auditorium are left out).</summary>
    public bool OwnPodium { get; init; }

    /// <summary>Name of the truss the cyc hangs on, when the show has it ("" otherwise).</summary>
    public string CycTruss { get; init; } = "";

    /// <summary>Stage z of the back wall / cyc.</summary>
    public float BackStageZ => BackZ - FrontZ;

    /// <summary>Rig position -&gt; stage coordinates.</summary>
    public Vector3 ToStage(float x, float y, float z) => new(x, y - FloorY, z - FrontZ);

    /// <summary>Stage coordinates -&gt; rig position.</summary>
    public Vector3 FromStage(Vector3 p) => new(p.X, p.Y + FloorY, p.Z + FrontZ);

    /// <summary>
    /// Fits the show into the venue: the origin at the show's proscenium object (z), else at the front edge and top of
    /// a box named podium / stage / scene / toneel (the show's own podium), else at <see cref="StageFitOptions.StageFrontZ"/>.
    /// The back wall is on the venue's cyc truss if the show has it, else at the venue's cyc, else behind the most
    /// upstage item.
    /// </summary>
    public static StageFrame Fit(IReadOnlyList<IRigItem> items, Venue venue, StageFitOptions options)
    {
        var objects = items.Where(i => !i.IsFixture).ToList();
        var proscenium = objects.FirstOrDefault(o => o.ObjectKind == RigObjectKind.Proscenium);
        var podium = objects.FirstOrDefault(o => o.ObjectKind == RigObjectKind.Box
                                                 && Regex.IsMatch(o.Name ?? "", "podium|stage|scene|toneel", RegexOptions.IgnoreCase));
        float frontZ = options.StageFrontZ, floorY = 0f;
        bool ownPodium = false;
        if (options.AutoVenue && proscenium != null)
            frontZ = proscenium.Z;
        else if (options.AutoVenue && podium != null)
        {
            frontZ = podium.Z + podium.SizeA / 2f;      // box: size = depth, width, height; centred in x/z
            floorY = podium.Y + podium.SizeC;
            ownPodium = true;
        }
        float hallY = ownPodium ? 0f : -(venue?.Auditorium?.FirstRowDrop ?? options.AuditoriumDrop);

        var cycTruss = venue?.Cyc?.Truss == null ? null
            : objects.FirstOrDefault(o => o.ObjectKind == RigObjectKind.Pipe && Normalize(o.Name) == Normalize(venue.Cyc.Truss));
        float backZ;
        if (!ownPodium && cycTruss != null)
            backZ = cycTruss.Z + venue.Cyc.Offset;
        else if (!ownPodium && venue?.Cyc != null && venue.Cyc.Truss == null)
            backZ = venue.Cyc.Z + frontZ;
        else
            backZ = options.AutoVenue && items.Count > 0 ? Math.Min(items.Min(v => v.Z) - 0.5f, options.BackdropZ) : options.BackdropZ;
        return new StageFrame
        {
            FrontZ = frontZ,
            FloorY = floorY,
            BackZ = backZ,
            HallY = hallY,
            OwnPodium = ownPodium,
            CycTruss = cycTruss?.Name ?? "",
        };
    }

    /// <summary>Name compare key: no spaces, upper case.</summary>
    private static string Normalize(string s) => (s ?? "").Replace(" ", "").ToUpperInvariant();
}
