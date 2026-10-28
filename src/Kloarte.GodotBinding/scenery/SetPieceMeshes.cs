using Godot;

namespace Kloarte.GodotBinding;

/// <summary>
/// The meshes of set pieces that Godot has no primitive for: the pyramid and a curtain's pleated cloth. Godot's front
/// faces are wound clockwise (seen from the side the normal points to).
/// </summary>
internal static class SetPieceMeshes
{
    /// <summary>Segments per pleat (sine period) of a curtain: smooth enough at any gathering.</summary>
    private const int SegmentsPerPleat = 16;

    /// <summary>
    /// A pyramid on a `width` (x) by `depth` (z) base centred on the origin, its apex `height` above the centre: four
    /// triangular sides and the base, flat shaded.
    /// </summary>
    public static ArrayMesh Pyramid(float width, float depth, float height)
    {
        float x = width / 2f, z = depth / 2f;
        var apex = new Vector3(0, height, 0);
        Vector3 a = new(-x, 0, -z), b = new(x, 0, -z), c = new(x, 0, z), d = new(-x, 0, z);
        var inside = new Vector3(0, height / 4f, 0);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (p, q) in new[] { (a, b), (b, c), (c, d), (d, a) })
            Triangle(st, p, q, apex, inside);
        Triangle(st, a, b, c, inside);
        Triangle(st, a, c, d, inside);
        return st.Commit();
    }

    /// <summary>
    /// Adds a triangle facing away from `inside` (wound clockwise seen from outside) with its flat normal.
    /// </summary>
    private static void Triangle(SurfaceTool st, Vector3 p, Vector3 q, Vector3 r, Vector3 inside)
    {
        var outward = (p + q + r) / 3f - inside;
        var normal = (q - p).Cross(r - p);   // counter-clockwise seen from where this points
        if (normal.Dot(outward) > 0f)
            (q, r) = (r, q);   // make it clockwise seen from outside
        else
            normal = -normal;
        normal = normal.Normalized();
        foreach (var v in new[] { p, q, r })
        {
            st.SetNormal(normal);
            st.AddVertex(v);
        }
    }

    /// <summary>
    /// Adds a surface to `mesh`: a hanging cloth from x = `x0` to `x0 + width`, bottom edge at y = 0, `height` high, with
    /// `periods` sine pleats along z of `amplitude` (flat when 0), centred on z = 0. The sine starts at the outer edge:
    /// x0 (`startAtOuterEdge`) or x0 + width. Front = +Z; the material shows both sides.
    /// </summary>
    public static void Cloth(ArrayMesh mesh, float x0, float width, float height, int periods, float amplitude,
        bool startAtOuterEdge = true)
    {
        int columns = amplitude > 0f ? periods * SegmentsPerPleat + 1 : 2;
        var vertices = new Vector3[columns * 2];
        var normals = new Vector3[columns * 2];
        var uvs = new Vector2[columns * 2];
        float k = Mathf.Tau * periods / width;
        for (int i = 0; i < columns; i++)
        {
            float u = i / (float)(columns - 1);
            float x = x0 + u * width;
            float phase = k * (startAtOuterEdge ? x - x0 : x0 + width - x);
            float z = amplitude * Mathf.Sin(phase);
            float slope = amplitude * k * Mathf.Cos(phase) * (startAtOuterEdge ? 1f : -1f);   // dz / dx
            var normal = new Vector3(-slope, 0, 1).Normalized();
            vertices[2 * i] = new Vector3(x, 0, z);
            vertices[2 * i + 1] = new Vector3(x, height, z);
            normals[2 * i] = normals[2 * i + 1] = normal;
            uvs[2 * i] = new Vector2(u, 1);
            uvs[2 * i + 1] = new Vector2(u, 0);
        }
        var indices = new int[(columns - 1) * 6];
        for (int i = 0; i < columns - 1; i++)
        {
            int b0 = 2 * i, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            // Clockwise seen from the front (+Z): bottom-left, top-left, top-right / bottom-left, top-right, bottom-right.
            indices[6 * i] = b0;
            indices[6 * i + 1] = t0;
            indices[6 * i + 2] = t1;
            indices[6 * i + 3] = b0;
            indices[6 * i + 4] = t1;
            indices[6 * i + 5] = b1;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    }
}
