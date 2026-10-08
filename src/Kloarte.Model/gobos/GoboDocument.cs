using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Kloarte.Model;

/// <summary>
/// A gobo as a drawing (gobos/&lt;name&gt;.gobo.json): a base (steel = all black, open = a white disc) and strokes
/// (lines and arcs) painted in order, each cutting (white, light passes) or blocking (black). Rasterised to a square
/// black/white image (gobos/&lt;name&gt;.png, white = light) with anti-aliased edges; outside the gobo disc is always black.
/// Image top = top of the beam (as the beam mask expects). No Godot dependencies.
/// </summary>
public sealed class GoboDocument
{
    /// <summary>Name (file name without extension).</summary>
    public string Name { get; set; } = "gobo";

    /// <summary>True: the disc starts open (white) and strokes are usually steel; false: steel with cut strokes.</summary>
    public bool OpenBase { get; set; }

    /// <summary>The strokes, painted in order.</summary>
    public List<GoboShape> Shapes { get; set; } = new();

    /// <summary>Loads a gobo drawing.</summary>
    public static GoboDocument Load(string path) =>
        JsonSerializer.Deserialize<GoboDocument>(File.ReadAllText(path), Json) ?? new GoboDocument();

    /// <summary>Saves the drawing (JSON).</summary>
    public void Save(string path) => File.WriteAllText(path, ToJson());

    /// <summary>JSON text (also used for undo snapshots).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>From JSON text.</summary>
    public static GoboDocument FromJson(string json) => JsonSerializer.Deserialize<GoboDocument>(json, Json) ?? new GoboDocument();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Greyscale image (row 0 = top), `size` x `size` bytes, 255 = light passes. Each pixel: base, then every stroke
    /// covers it with an anti-aliased edge (one pixel wide); outside the gobo disc black.
    /// </summary>
    public byte[] Rasterize(int size)
    {
        var pixels = new byte[size * size];
        float px = 2f / size;   // gobo units per pixel
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                var p = new Vector2((i + 0.5f) * px - 1f, 1f - (j + 0.5f) * px);
                float r = p.Length();
                float v = OpenBase ? 1f : 0f;
                foreach (var s in Shapes)
                {
                    float coverage = Math.Clamp((s.Width / 2f - s.Distance(p)) / px + 0.5f, 0f, 1f);
                    if (coverage > 0f)
                        v = v + ((s.Cut ? 1f : 0f) - v) * coverage;
                }
                float disc = Math.Clamp((1f - r) / px + 0.5f, 0f, 1f);
                pixels[j * size + i] = (byte)Math.Round(v * disc * 255f);
            }
        return pixels;
    }

    /// <summary>Share of the disc that lets light through (0..1), e.g. for tests.</summary>
    public static float OpenShare(byte[] pixels, int size)
    {
        double sum = 0;
        int n = 0;
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = (i + 0.5f) / size * 2f - 1f, y = 1f - (j + 0.5f) / size * 2f;
                if (x * x + y * y > 1f)
                    continue;
                sum += pixels[j * size + i] / 255.0;
                n++;
            }
        return n == 0 ? 0 : (float)(sum / n);
    }

    /// <summary>A starter drawing: a window cross (two lines) in an open frame arc.</summary>
    public static GoboDocument Example() => new()
    {
        Name = "example",
        Shapes =
        {
            GoboShape.Arc(Vector2.Zero, 0.75f, 0f, 360f, 0.25f, true),
            GoboShape.Line(new Vector2(-0.6f, 0f), new Vector2(0.6f, 0f), 0.08f, false),
            GoboShape.Line(new Vector2(0f, -0.6f), new Vector2(0f, 0.6f), 0.08f, false),
        },
    };
}
