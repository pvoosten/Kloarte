using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kloarte.MagicQ;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// The MagicQ reader / writer against every show of the MagicQ show folder (read only; output to
/// test/output/roundtrip): lossless round trip, MagicVis numbers, edits through the typed views, xhw headers,
/// mechanics JSON. Converted from res://tests/magicq_roundtrip.tscn.
/// </summary>
public sealed class MagicQRoundTripTests : TestBase
{
    /// <summary>Output to test/output/roundtrip.</summary>
    public MagicQRoundTripTests() : base("roundtrip") { }

    /// <summary>Every .shw / .xhw read and written back unchanged is byte-identical; every MagicVis number round-trips.</summary>
    [Fact]
    public void EveryShowRoundTrips()
    {
        var failures = new List<string>();
        int files = 0, numbers = 0;
        foreach (string shw in Directory.EnumerateFiles(ShowFolder, "*.shw", SearchOption.AllDirectories))
        {
            files++;
            string copy = Path.Combine(_out, Path.GetFileName(shw));
            try
            {
                var show = MagicQShow.Load(shw);
                show.Save(copy);
                Compare(shw, copy, failures);
                string xhw = Path.ChangeExtension(shw, ".xhw");
                if (File.Exists(xhw))
                    Compare(xhw, Path.ChangeExtension(copy, ".xhw"), failures);
                foreach (var item in show.Vis)
                    for (int i = 13; i < 20; i++)
                    {
                        string s = item.Record.GetString(i);
                        numbers++;
                        string again = MagicQShow.EncodeNumber(MagicQShow.DecodeNumber(s) ?? float.NaN);
                        if (again != s)
                            failures.Add($"number mismatch in {shw}: '{s}' -> {MagicQShow.DecodeNumber(s)} -> '{again}'");
                    }
            }
            catch (Exception e)
            {
                failures.Add($"{shw}: {e.Message}");
            }
        }
        Console.WriteLine($"Round trip: {files} shows, {numbers} MagicVis numbers checked, {failures.Count} failures");
        Check(files > 0, "the MagicQ show folder has shows");
        Check(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    /// <summary>An edit through the typed views changes only that field and survives a reload.</summary>
    [Fact]
    public void EditChangesOnlyThatField()
    {
        string src = Show("RT2025_031.shw");
        string dst = Path.Combine(_out, "edited.shw");
        var show = MagicQShow.Load(src);
        var fixture = show.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture);
        fixture.X = -3.25f;
        fixture.RotX = 45f;
        show.Patch[0].HeadName = "Renamed";
        show.Save(dst);

        var reloaded = MagicQShow.Load(dst);
        var f2 = reloaded.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture);
        int diffLines = File.ReadAllLines(src).Zip(File.ReadAllLines(dst)).Count(p => p.First != p.Second);
        Check(Math.Abs(f2.X + 3.25f) < 0.001f && Math.Abs(f2.RotX - 45f) < 0.001f && reloaded.Patch[0].HeadName == "Renamed"
              && diffLines == 2, $"x {f2.X}, rotX {f2.RotX}, name {reloaded.Patch[0].HeadName}, {diffLines} lines differ");
    }

    /// <summary>Regenerated xhw headers equal MagicQ's own headers.</summary>
    [Fact]
    public void XhwHeadersRegenerate()
    {
        string src = Show("RT2025_031.xhw");
        string dst = Path.Combine(_out, "regenerated.xhw");
        var archive = XhwArchive.Load(src);
        foreach (var e in archive.Entries)
            e.SetData(e.Data);   // drops the original header -> regenerated on save
        archive.Save(dst);
        var failures = new List<string>();
        Compare(src, dst, failures);
        Check(failures.Count == 0, $"xhw header regeneration ({archive.Entries.Count} entries): " + string.Join("; ", failures));
    }

    /// <summary>The mechanics JSON round trip.</summary>
    [Fact]
    public void MechanicsJsonRoundTrips()
    {
        string src = Show("RT2025_031.shw");
        string path = Path.Combine(_out, "mechanics.json");
        var model = ShowModel<MagicQShow>.Load(MagicQShow.Load(src), src);
        model.Mechanics[211] = new FixtureMechanics
        {
            Kind = "profile",
            FieldAngle = 26,
            Shutters = new Shutters { Top = new() { Depth = 0.4f, Angle = 10 }, Rotation = 5 },
            Iris = 0.7f,
            Gobo = new Gobo { Path = "res://gobos/window.png", Rotation = 30 },
            Barndoors = new Barndoors { Left = 0.5f },
            FocusPoint = new[] { 1f, 0f, -3f },
        };
        model.SaveMechanics(path);
        var m = ShowModel<MagicQShow>.Load(MagicQShow.Load(src), src, path).Mechanics[211];
        Check(m.Kind == "profile" && m.Shutters.Top.Depth == 0.4f && m.Shutters.Rotation == 5 && m.Iris == 0.7f
              && m.Gobo.Rotation == 30 && m.Barndoors.Left == 0.5f && m.FocusPoint[2] == -3f, "mechanics JSON round trip");
    }

    /// <summary>Adds a failure when the two files differ (sizes and the first differing byte).</summary>
    private static void Compare(string a, string b, List<string> failures)
    {
        byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
        if (x.AsSpan().SequenceEqual(y))
            return;
        int i = 0;
        while (i < Math.Min(x.Length, y.Length) && x[i] == y[i])
            i++;
        failures.Add($"DIFF {a}: sizes {x.Length}/{y.Length}, first difference at byte {i}");
    }
}
