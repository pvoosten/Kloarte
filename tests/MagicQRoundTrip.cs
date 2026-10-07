using System;
using System.IO;
using System.Linq;
using Godot;

// Checks the MagicQ reader/writer against real show files (read only; output goes to tests/output/roundtrip).
// 1. Every .shw/.xhw read and written back unchanged is byte-identical.
// 2. EncodeNumber(DecodeNumber(s)) == s for every MagicVis number.
// 3. An edit through the typed views changes only that field and survives a reload.
// 4. Regenerated xhw headers equal MagicQ's own headers.
// Run:  Godot_console.exe --headless --path . res://tests/magicq_roundtrip.tscn
public partial class MagicQRoundTrip : Node
{
    [Export] public string ShowFolder = "C:/Users/pvoos_nnvpp5d/OneDrive/Documents/MagicQ/show";

    public override void _Ready()
    {
        string outDir = ProjectSettings.GlobalizePath("res://tests/output/roundtrip");
        Directory.CreateDirectory(outDir);
        int files = 0, failures = 0, numbers = 0;

        foreach (string shw in Directory.EnumerateFiles(ShowFolder, "*.shw", SearchOption.AllDirectories))
        {
            files++;
            string copy = Path.Combine(outDir, Path.GetFileName(shw));
            try
            {
                var show = MagicQShow.Load(shw);
                show.Save(copy);
                failures += Compare(shw, copy);
                string xhw = Path.ChangeExtension(shw, ".xhw");
                if (File.Exists(xhw))
                    failures += Compare(xhw, Path.ChangeExtension(copy, ".xhw"));

                foreach (var item in show.Vis)
                    for (int i = 13; i < 20; i++)
                    {
                        string s = item.Record.GetString(i);
                        numbers++;
                        string again = MagicQShow.EncodeNumber(MagicQShow.DecodeNumber(s) ?? float.NaN);
                        if (again != s)
                        {
                            failures++;
                            GD.PrintErr($"number mismatch in {shw}: '{s}' -> {MagicQShow.DecodeNumber(s)} -> '{again}'");
                        }
                    }
            }
            catch (Exception e)
            {
                failures++;
                GD.PrintErr($"{shw}: {e.Message}");
            }
        }
        GD.Print($"Round trip: {files} shows, {numbers} MagicVis numbers checked, {failures} failures");

        failures += EditTest(outDir);
        failures += XhwHeaderTest(outDir);
        failures += MechanicsTest(outDir);
        GD.Print(failures == 0 ? "ALL OK" : $"FAILURES: {failures}");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private int EditTest(string outDir)
    {
        string src = Directory.EnumerateFiles(ShowFolder, "RT2025_031.shw", SearchOption.AllDirectories).First();
        string dst = Path.Combine(outDir, "edited.shw");
        var show = MagicQShow.Load(src);
        var fixture = show.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture);
        float oldX = fixture.X;
        fixture.X = -3.25f;
        fixture.RotX = 45f;
        show.Patch[0].HeadName = "Renamed";
        show.Save(dst);

        var reloaded = MagicQShow.Load(dst);
        var f2 = reloaded.Vis.First(v => v.Kind == MagicQShow.VisKind.Fixture);
        int diffLines = File.ReadAllLines(src).Zip(File.ReadAllLines(dst)).Count(p => p.First != p.Second);
        bool ok = Math.Abs(f2.X + 3.25f) < 0.001f && Math.Abs(f2.RotX - 45f) < 0.001f
                  && reloaded.Patch[0].HeadName == "Renamed" && diffLines == 2;
        GD.Print($"Edit test: x {oldX} -> {f2.X}, rotX -> {f2.RotX}, name -> {reloaded.Patch[0].HeadName}, " +
                 $"{diffLines} lines differ: {(ok ? "OK" : "FAIL")}");
        return ok ? 0 : 1;
    }

    private int XhwHeaderTest(string outDir)
    {
        string src = Directory.EnumerateFiles(ShowFolder, "RT2025_031.xhw", SearchOption.AllDirectories).First();
        string dst = Path.Combine(outDir, "regenerated.xhw");
        var archive = XhwArchive.Load(src);
        foreach (var e in archive.Entries)
            e.SetData(e.Data);   // drops the original header -> regenerated on save
        archive.Save(dst);
        int result = Compare(src, dst);
        GD.Print($"Xhw header regeneration ({archive.Entries.Count} entries): {(result == 0 ? "OK" : "FAIL")}");
        return result;
    }

    private int MechanicsTest(string outDir)
    {
        string src = Directory.EnumerateFiles(ShowFolder, "RT2025_031.shw", SearchOption.AllDirectories).First();
        string path = Path.Combine(outDir, "mechanics.json");
        var model = ShowModel.Load(src);
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
        var m = ShowModel.Load(src, path).Mechanics[211];
        bool ok = m.Kind == "profile" && m.Shutters.Top.Depth == 0.4f && m.Shutters.Rotation == 5 && m.Iris == 0.7f
                  && m.Gobo.Rotation == 30 && m.Barndoors.Left == 0.5f && m.FocusPoint[2] == -3f;
        GD.Print($"Mechanics JSON round trip: {(ok ? "OK" : "FAIL")}");
        return ok ? 0 : 1;
    }

    private static int Compare(string a, string b)
    {
        byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
        if (x.AsSpan().SequenceEqual(y))
            return 0;
        int i = 0;
        while (i < Math.Min(x.Length, y.Length) && x[i] == y[i])
            i++;
        GD.PrintErr($"DIFF {a}: sizes {x.Length}/{y.Length}, first difference at byte {i}");
        return 1;
    }
}
