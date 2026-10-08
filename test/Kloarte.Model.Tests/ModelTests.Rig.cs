using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Kloarte.MagicQ;
using Kloarte.Model;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Xunit;

namespace Kloarte.Tests;

/// <summary>ModelTests: tests from ModelTests.Rig.cs (converted from the Godot model tests).</summary>
public partial class ModelTests
{
    /// <summary>The plan as a PDF.</summary>
    [Fact]
    public void PdfTest()
    {
        var data = new PlanData { Show = "Test show", Venue = "Venue é", StageEdge = 2f, StageBack = -9f, StageWidth = 15f, CycWidth = 15f };
        data.Supports.Add(new PlanSupport { Id = 1, Name = "LT 11", Start = new Vector2(-7.5f, -5.5f), End = new Vector2(7.5f, -5.5f), Height = 6f });
        data.Lamps.Add(new PlanLamp { VisId = 1, Position = new Vector2(0, -5.5f), Focus = new Vector2(0, -2), Kind = FixtureKind.Profile,
                                      ModelName = "ADB DS104", Gel = new Rgb(0.2f, 0.4f, 1f), GelCode = "L201", GelName = "Full C.T. Blue", Head = 150, Address = 150 });
        data.Lamps.Add(new PlanLamp { VisId = 2, Position = new Vector2(2, -5.5f), Kind = FixtureKind.Par, ModelName = "PAR64", Head = 151, Address = 151 });
        string path = Path.Combine(_out, "plan.pdf");
        int scale = PlanPdf.Export(data, path, a3: true);
        var bytes = File.ReadAllBytes(path);
        string text = Encoding.Latin1.GetString(bytes);
        Check(text.StartsWith("%PDF-1.4") && text.TrimEnd().EndsWith("%%EOF"), "PDF header and trailer");
        // Every xref offset points at "n 0 obj".
        int xref = int.Parse(text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 10)..].Split('\n')[0].Trim());
        var lines = text[xref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ")).ToList();
        Check(lines.Count > 0 && lines.Select((l, i) => text.Substring(int.Parse(l[..10]), $"{i + 1} 0 obj".Length) == $"{i + 1} 0 obj").All(x => x),
              $"xref: {lines.Count} objects at the right offsets");
        Check(text.Contains("/MediaBox [0 0 1190.55 841.89]"), "A3 landscape");
        Check(text.Contains("(150) Tj") && text.Contains("(L201) Tj") && text.Contains("Full C.T. Blue") && text.Contains("ADB DS104")
              && text.Contains($"scale 1:{scale}"), $"labels, legend and title block (1:{scale})");
    }

    /// <summary>The rig: trusses, snapping, truss heights, stands, aiming, the stage frame.</summary>
    [Fact]
    public void RigTest()
    {
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var rig = new Rig(show);
        var lt11 = rig.Trusses.First(t => t.Name == "LT 11");
        Check(Vector3.Distance(lt11.Start, new Vector3(-7.5f, 6f, 4.2f)) < 0.01f && Math.Abs(lt11.Length - 15f) < 0.01f
              && Vector3.Distance(lt11.Direction, Vector3.UnitX) < 1e-4f, "LT 11 runs 15 m along +X at 6 m");
        Check(Vector3.Distance(Rig.Rotate(Vector3.UnitX, 0, 90, 0), -Vector3.UnitZ) < 1e-4f
              && Vector3.Distance(Rig.Rotate(Vector3.UnitX, 0, 0, 90), Vector3.UnitY) < 1e-4f, "MagicVis rotations (Y-X-Z)");

        // Stage frame: RT2025_031 has a proscenium object at z 9.75 and the house cyc truss (T 22).
        var venue = Kloarte.Model.Venue.Load(AppPaths.File("res://venue/ccdebrouckere.venue.yaml"));
        var frame = StageFrame.Fit(show.Vis, venue, new StageFitOptions());
        Check(Math.Abs(frame.FrontZ - 9.75f) < 0.01f && frame.FloorY == 0f && !frame.OwnPodium && frame.CycTruss != ""
              && frame.BackStageZ < -8f && Vector3.Distance(frame.FromStage(frame.ToStage(1f, 2f, 3f)), new Vector3(1f, 2f, 3f)) < 1e-5f,
              $"stage frame: front z {frame.FrontZ}, back {frame.BackStageZ:0.00} (cyc on {frame.CycTruss})");
        var bare = StageFrame.Fit(Array.Empty<IRigItem>(), null, new StageFitOptions { StageFrontZ = 5f, BackdropZ = -1f });
        Check(bare.FrontZ == 5f && bare.BackZ == -1f && bare.HallY == -1f, "stage frame without show objects or venue");

        var snap = rig.Snap(new Vector3(1f, 5f, 4.3f), 0.5f);
        Check(snap?.Support.Name == "LT 11" && Math.Abs(snap.Value.Position.Z - 4.2f) < 0.01f
              && Math.Abs(snap.Value.Position.Y - (6f - Rig.DefaultHang)) < 0.01f, $"snaps onto {snap?.Support.Name}");
        Check(rig.Snap(new Vector3(0f, 5f, 13.5f), 0.5f) == null, "no snap beyond the snap distance");

        // Height: all lamps on the truss move by the same amount, others don't.
        var lamps = rig.LampsOn(lt11.Id).ToList();
        var other = show.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture && v.ParentId != lt11.Id);
        float otherY = other.Y;
        var before = lamps.Select(l => l.Y).ToList();
        var moved = rig.SetHeight(lt11.Id, 7.5f);
        Check(moved.Count == lamps.Count && lamps.Count > 0 && lamps.Select((l, i) => l.Y - before[i]).All(d => Math.Abs(d - 1.5f) < 0.011f)
              && Math.Abs(rig.Find(lt11.Id).Height - 7.5f) < 0.01f && other.Y == otherY,
              $"LT 11 to 7.5 m: its {lamps.Count} lamps +1.5 m, the rest unchanged");

        // Aiming: the rotation for a focus point puts the beam through it.
        var pos = new Vector3(2, 7, 3);
        var focus = new Vector3(-1, 0, 0.5f);
        var (rx, ry) = Rig.AimRotation(pos, focus);
        var dir = Rig.BeamDirection(rx, ry, 0);
        var t = (focus.Y - pos.Y) / dir.Y;
        Check(Vector3.Distance(pos + dir * t, focus) < 0.01f, "aim rotation hits the focus point (< 1 cm)");

        // Stands.
        var stand = rig.AddStand("Manfrotto 111U", 3f, 9f, 0f, 2.5f);
        Check(stand.IsStand && Math.Abs(stand.Height - 2.5f) < 0.01f && rig.Stands.Any(s => s.Id == stand.Id), "stand added");
        var standSnap = rig.Snap(new Vector3(3.2f, 1f, 9.1f), 0.5f);
        Check(standSnap?.Support.Id == stand.Id && Math.Abs(standSnap.Value.Position.Y - (2.5f + Rig.StandMount)) < 0.01f, "snaps onto the stand top");
        rig.SetHeight(stand.Id, 1.8f);
        Check(Math.Abs(rig.Find(stand.Id).Height - 1.8f) < 0.01f, "stand height 1.8 m");
        string path = Path.Combine(_out, "rig.shw");
        show.Save(path);
        var re = new Rig(MagicQShow.Load(path));
        Check(re.Stands.Any(s => s.Name == stand.Name && Math.Abs(s.Height - 1.8f) < 0.01f)
              && Math.Abs(re.Trusses.First(x => x.Name == "LT 11").Height - 7.5f) < 0.01f, "stand and truss height survive save + reload");
        re.Remove(re.Stands.First(s => s.Name == stand.Name).Id);
        Check(!re.Stands.Any(s => s.Name == stand.Name), "stand removed");
    }

    /// <summary>
    /// Where a lamp can move: along its truss only (the other axes fixed, the drop and offset kept, stopping at the ends),
    /// up and down a stand, freely without a support; a diagonal truss couples X and Z.
    /// </summary>
    [Fact]
    public void LampMotionTest()
    {
        var show = MagicQShow.Load(Show("RT2025_031.shw"));
        var rig = new Rig(show);
        var lt11 = rig.Trusses.First(t => t.Name == "LT 11");
        var lamp = rig.LampsOn(lt11.Id).First();
        var before = new Vector3(lamp.X, lamp.Y, lamp.Z);

        // On a truss that runs along X: X is free, height and depth are fixed, with their reasons.
        var motion = rig.MotionOf(lamp);
        Check(motion.OnTruss && motion.IsFree(MotionAxis.X) && !motion.IsFree(MotionAxis.Y) && !motion.IsFree(MotionAxis.Z),
              "a lamp on LT 11: only X is free");
        Check(motion.WhyFixed(MotionAxis.Y).Contains("Truss heights") && motion.WhyFixed(MotionAxis.Z).Contains("along LT 11")
              && motion.WhyFixed(MotionAxis.X) == "", "the reasons for the fixed axes");
        var moved = motion.Move(MotionAxis.X, before.X + 0.5f, out bool stopped)!.Value;
        Check(!stopped && Vector3.Distance(moved, before + new Vector3(0.5f, 0, 0)) < 1e-4f, "X + 0.5 m slides along the truss, drop and depth kept");
        Check(motion.Move(MotionAxis.Y, before.Y + 1f, out _) == null && motion.Move(MotionAxis.Z, before.Z + 1f, out _) == null,
              "a value for a fixed axis is refused");
        var end = motion.Move(MotionAxis.X, lt11.End.X + 5f, out stopped)!.Value;
        var start = motion.Move(MotionAxis.X, lt11.Start.X - 5f, out bool stoppedAtStart)!.Value;
        Check(stopped && stoppedAtStart && Math.Abs(end.X - lt11.End.X) < 1e-4f && Math.Abs(start.X - lt11.Start.X) < 1e-4f
              && Math.Abs(end.Y - before.Y) < 1e-4f && Math.Abs(end.Z - before.Z) < 1e-4f, "stops at the ends of the truss (15 m)");

        // A truss across the stage (along Z): Z is the free axis.
        var across = rig.AddStand("test", 0f, 0f, 6f, 9f);
        across.Item.RotZ = 0;
        across.Item.RotY = 90;   // local +X now points to -Z
        across.Item.SizeA = 6f;
        var acrossLamp = rig.AddLamp(lamp, 0f, 5.67f, -2f, across.Id, 0, 0);
        var onAcross = rig.MotionOf(acrossLamp);
        Check(onAcross.OnTruss && onAcross.IsFree(MotionAxis.Z) && !onAcross.IsFree(MotionAxis.X) && !onAcross.IsFree(MotionAxis.Y),
              "a truss along Z: only Z is free");

        // A diagonal truss (45 degrees in plan): moving X moves Z with it.
        across.Item.RotY = 45;
        var diagonal = rig.MotionOf(rig.AddLamp(lamp, 0f, 5.67f, 0f, across.Id, 0, 0));
        var along = diagonal.Move(MotionAxis.X, 1f, out _)!.Value;
        Check(diagonal.IsFree(MotionAxis.X) && diagonal.IsFree(MotionAxis.Z) && !diagonal.IsFree(MotionAxis.Y)
              && Math.Abs(along.X - 1f) < 1e-4f && Math.Abs(along.Z + 1f) < 1e-4f, "a diagonal truss: X and Z follow each other, height fixed");

        // A stand: only the height; it stops at the floor and a little above the top.
        var stand = rig.AddStand("Manfrotto 111U", 3f, 9f, 0f, 2.5f);
        var standLamp = rig.AddLamp(lamp, 3f, 2.5f + Rig.StandMount, 9f, stand.Id, 0, 0);
        var onStand = rig.MotionOf(standLamp);
        Check(!onStand.OnTruss && onStand.Support!.IsStand && onStand.IsFree(MotionAxis.Y) && !onStand.IsFree(MotionAxis.X) && !onStand.IsFree(MotionAxis.Z)
              && onStand.WhyFixed(MotionAxis.X).Contains("Move the stand"), "a lamp on a stand: only the height");
        var low = onStand.Move(MotionAxis.Y, -3f, out stopped)!.Value;
        var high = onStand.Move(MotionAxis.Y, 9f, out bool stoppedHigh)!.Value;
        Check(stopped && stoppedHigh && Math.Abs(low.Y) < 1e-4f && Math.Abs(high.Y - (2.5f + Rig.StandMount)) < 1e-4f
              && Math.Abs(low.X - 3f) < 1e-4f && Math.Abs(high.Z - 9f) < 1e-4f, "up and down the stand, between the floor and its top");

        // A lamp without a support: everything is free, as before.
        standLamp.ParentId = 0;
        var loose = rig.MotionOf(standLamp);
        var free = loose.Move(MotionAxis.Z, -4f, out stopped)!.Value;
        Check(loose.Support == null && !loose.OnTruss && Enum.GetValues<MotionAxis>().All(loose.IsFree) && !stopped
              && Math.Abs(free.Z + 4f) < 1e-4f && Math.Abs(free.X - 3f) < 1e-4f, "no support: all three axes free");
        Check(Vector3.Distance(new Vector3(lamp.X, lamp.Y, lamp.Z), before) < 1e-6f, "reading the rule moves nothing");
    }
}
