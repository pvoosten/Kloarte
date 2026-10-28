using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Kloarte.Model;
using Xunit;

namespace Kloarte.Tests;

/// <summary>
/// The set pieces' geometry from the model (<see cref="SceneryGeometry"/>), used by the plan, the PDF and the picking in
/// 3D: rotation counter-clockwise seen from above, the footprint, an open split curtain as two gathered halves, a ray
/// against the piece's box; and how the plan draws pieces (<see cref="PlanPieces"/>).
/// </summary>
public sealed class SceneryGeometryTests
{
    /// <summary>A 2 x 1 m block of 1 m high at (1, 0, -3), turned 90°.</summary>
    private static SceneryPiece Turned()
    {
        var piece = SceneryPiece.New("b", "Block", SceneryShape.Block);
        (piece.Width, piece.Depth, piece.Height) = (2f, 1f, 1f);
        piece.Position = new[] { 1f, 0f, -3f };
        piece.Rotation = 90f;
        return piece;
    }

    /// <summary>Turned 90° counter-clockwise from above, the piece's +x points upstage (-z); ToLocal undoes ToPlan.</summary>
    [Fact]
    public void RotationIsCounterClockwiseFromAbove()
    {
        var piece = Turned();
        var end = SceneryGeometry.ToPlan(piece, 1f, 0f);
        Assert.Equal(1f, end.X, 3);
        Assert.Equal(-4f, end.Y, 3);
        var back = SceneryGeometry.ToLocal(piece, new Vector2(0.3f, -2.2f));
        var again = SceneryGeometry.ToPlan(piece, back.X, back.Y);
        Assert.Equal(0.3f, again.X, 3);
        Assert.Equal(-2.2f, again.Y, 3);
        Assert.Equal(90f, SceneryGeometry.AngleTo(piece, end), 3);
        Assert.Equal(-170f, SceneryGeometry.Wrap(190f), 3);
    }

    /// <summary>The turned block covers 1 m along x and 2 m along z.</summary>
    [Fact]
    public void FootprintAndContains()
    {
        var piece = Turned();
        var f = SceneryGeometry.Footprint(piece);
        Assert.Equal(0.5f, f.Min(p => p.X), 3);
        Assert.Equal(1.5f, f.Max(p => p.X), 3);
        Assert.Equal(-4f, f.Min(p => p.Y), 3);
        Assert.Equal(-2f, f.Max(p => p.Y), 3);
        Assert.True(SceneryGeometry.Contains(piece, new Vector2(1.2f, -3.8f), 0f));
        Assert.False(SceneryGeometry.Contains(piece, new Vector2(1.8f, -3f), 0f));
    }

    /// <summary>A split curtain opens to two halves gathered at the sides; a closed or unsplit one is one span.</summary>
    [Fact]
    public void OpenCurtainIsTwoHalves()
    {
        var curtain = SceneryPiece.New("c", "Curtain", SceneryShape.Curtain);   // 6 m wide
        Assert.Single(SceneryGeometry.Spans(curtain, 100f));
        curtain.Split = true;
        var closed = SceneryGeometry.Spans(curtain, 0f);
        Assert.Equal((-3f, 0f), closed[0]);
        Assert.Equal((0f, 3f), closed[1]);
        var open = SceneryGeometry.Spans(curtain, 100f);
        Assert.Equal(-3f + 3f * SceneryGeometry.GatheredWidth, open[0].To, 3);
        Assert.False(SceneryGeometry.Contains(curtain, new Vector2(0f, 0f), 100f));
        Assert.True(SceneryGeometry.Contains(curtain, new Vector2(0f, 0f), 0f));
        Assert.True(SceneryGeometry.HalfDepth(curtain) > SceneryGeometry.MinThickness / 2f);   // the pleats
    }

    /// <summary>A ray from above hits the block's top; one beside it misses; through an open curtain's gap it misses.</summary>
    [Fact]
    public void RayHitsTheBox()
    {
        var piece = Turned();
        var t = SceneryGeometry.RayHit(piece, new Vector3(1f, 5f, -3.5f), new Vector3(0f, -1f, 0f), 0f);
        Assert.NotNull(t);
        Assert.Equal(4f, t.Value, 3);
        Assert.Null(SceneryGeometry.RayHit(piece, new Vector3(2f, 5f, -3f), new Vector3(0f, -1f, 0f), 0f));
        // From the audience, level, towards upstage.
        Assert.NotNull(SceneryGeometry.RayHit(piece, new Vector3(1f, 0.5f, 5f), new Vector3(0f, 0f, -1f), 0f));

        var curtain = SceneryPiece.New("c", "Curtain", SceneryShape.Curtain);
        curtain.Split = true;
        curtain.Position = new[] { 0f, 0f, -5f };
        var level = new Vector3(0f, 0f, -1f);
        Assert.NotNull(SceneryGeometry.RayHit(curtain, new Vector3(0f, 2f, 5f), level, 0f));
        Assert.Null(SceneryGeometry.RayHit(curtain, new Vector3(0f, 2f, 5f), level, 100f));
        Assert.NotNull(SceneryGeometry.RayHit(curtain, new Vector3(-2.9f, 2f, 5f), level, 100f));
    }

    /// <summary>
    /// The plan draws a piece that is in filled with its colour, one that is out as a dashed outline, a pyramid with its
    /// diagonals and an open split curtain as two lines in its colour.
    /// </summary>
    [Fact]
    public void PlanDrawsPieces()
    {
        var style = new PlanStyle { LabelSize = 10f };
        var block = Turned();
        block.Color = "ff0000";
        var canvas = new Recorder();
        PlanPieces.Draw(canvas, new PlanPiece { Piece = block }, style);
        Assert.Contains(canvas.Polygons, p => p.Fill == new Rgb(1f, 0f, 0f));
        Assert.Contains(canvas.Texts, t => t == "Block");

        canvas = new Recorder();
        PlanPieces.Draw(canvas, new PlanPiece { Piece = block, Visible = false }, style);
        Assert.Empty(canvas.Polygons);
        Assert.Contains(canvas.Lines, l => l.Dashed);

        var pyramid = SceneryPiece.New("p", "Pyramid", SceneryShape.Pyramid);
        canvas = new Recorder();
        PlanPieces.Draw(canvas, new PlanPiece { Piece = pyramid }, style);
        Assert.Equal(2, canvas.Lines.Count(l => l.Points == 2));

        var curtain = SceneryPiece.New("c", "Curtain", SceneryShape.Curtain);
        curtain.Split = true;
        canvas = new Recorder();
        PlanPieces.Draw(canvas, new PlanPiece { Piece = curtain, Open = 50f }, style);
        Assert.Equal(2, canvas.Lines.Count(l => l.Colour == PlanPieces.ColourOf(curtain) && l.Points > 2));

        var data = new PlanData();
        data.Pieces.Add(new PlanPiece { Piece = block });
        var (min, max) = PlanDrawing.Bounds(data, seats: false);
        Assert.True(min.Y < -4f && max.X > 1.5f);
    }

    /// <summary>A canvas that records what is drawn.</summary>
    private sealed class Recorder : IPlanCanvas
    {
        /// <summary>Filled / outlined polygons.</summary>
        public List<(Rgb? Fill, Rgb? Stroke)> Polygons { get; } = new();

        /// <summary>Polylines: their colour, number of points and whether dashed.</summary>
        public List<(Rgb Colour, int Points, bool Dashed)> Lines { get; } = new();

        /// <summary>Texts drawn.</summary>
        public List<string> Texts { get; } = new();

        /// <inheritdoc />
        public float UnitsPerMetre => 30f;

        /// <inheritdoc />
        public void Polygon(IReadOnlyList<Vector2> points, Rgb? fill, Rgb? stroke, float strokeWidth = 1) => Polygons.Add((fill, stroke));

        /// <inheritdoc />
        public void Polyline(IReadOnlyList<Vector2> points, Rgb stroke, float width = 1, bool dashed = false) =>
            Lines.Add((stroke, points.Count, dashed));

        /// <inheritdoc />
        public void Circle(Vector2 centre, float radius, Rgb? fill, Rgb? stroke, float strokeWidth = 1) { }

        /// <inheritdoc />
        public void Text(Vector2 position, string text, float size, Rgb color, TextAlign align = TextAlign.Left) => Texts.Add(text);

        /// <inheritdoc />
        public float TextWidth(string text, float size) => text.Length * size * 0.5f;
    }
}
