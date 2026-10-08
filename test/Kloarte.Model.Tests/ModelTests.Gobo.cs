using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>ModelTests: tests from ModelTests.Gobo.cs (converted from the Godot model tests).</summary>
public partial class ModelTests
{
    /// <summary>Gobo documents: shapes, JSON, rasterising.</summary>
    [Fact]
    public void GoboTest()
    {
        var doc = new GoboDocument { Name = "test" };
        doc.Shapes.Add(GoboShape.Line(new Vector2(-0.8f, 0f), new Vector2(0.8f, 0f), 0.1f, true));
        doc.Shapes.Add(GoboShape.Arc(Vector2.Zero, 0.5f, 0f, 180f, 0.05f, true));
        string path = Path.Combine(_out, "test.gobo.json");
        doc.Save(path);
        var re = GoboDocument.Load(path);
        Check(re.Shapes.Count == 2 && re.Shapes[1].Kind == "arc" && Math.Abs(re.Shapes[1].End - 180f) < 1e-4f && re.Shapes[0].Cut,
              "gobo drawing JSON round trip");

        const int n = 128;
        var px = re.Rasterize(n);
        byte At(float x, float y) => px[(int)((1f - y) / 2f * n) * n + (int)((x + 1f) / 2f * n)];
        Check(At(0.3f, 0.01f) > 240 && At(0.3f, -0.4f) < 15 && At(0.99f, 0.99f) == 0, "line cut is white, steel black, outside the disc black");
        Check(At(0f, 0.5f) > 200 && At(0f, -0.5f) < 15, "upper half arc (0..180°) cut, lower half not: top of the image = top of the beam");
        float share = GoboDocument.OpenShare(px, n);
        float expected = (1.6f * 0.1f + MathF.PI * 0.5f * 0.05f) / MathF.PI;
        Check(Math.Abs(share - expected) < 0.01f, $"open share {share:0.000} (expected ~{expected:0.000})");
        var open = new GoboDocument { OpenBase = true };
        open.Shapes.Add(GoboShape.Line(new Vector2(-1, 0), new Vector2(1, 0), 0.2f, false));
        Check(GoboDocument.OpenShare(open.Rasterize(n), n) < 0.9f && open.Rasterize(n)[n / 4 * n + n / 2] > 240, "open base with a steel bar");
        Check(new GoboShape { Kind = "arc", Centre = new[] { 0f, 0f }, Radius = 0.5f, Start = 350f, End = 370f }.Distance(new Vector2(0.5f, 0)) < 1e-4f,
              "arcs through 0° (wrap-around)");
    }
}
