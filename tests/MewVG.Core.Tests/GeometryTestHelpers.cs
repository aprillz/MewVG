using System.Numerics;

using Aprillz.MewVG;

namespace MewVG.Core.Tests;

internal static class GeometryTestHelpers
{
    /// <summary>Barycentric sign test; true if <paramref name="point"/> lies on or inside the triangle.</summary>
    public static bool TriangleContainsPoint(Vector2 a, Vector2 b, Vector2 c, Vector2 point)
    {
        var d1 = Sign(point, a, b);
        var d2 = Sign(point, b, c);
        var d3 = Sign(point, c, a);

        var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;

        return !(hasNegative && hasPositive);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);

    /// <summary>Scans every path's tessellated fill-body triangles (NFill/FillOffset, triangle list) for one containing the point.</summary>
    public static bool AnyFillTriangleContains(NVGpathData[] paths, NVGvertex[] verts, float x, float y)
    {
        var point = new Vector2(x, y);
        foreach (var path in paths)
        {
            if (path.NFill <= 0)
            {
                continue;
            }

            for (var i = path.FillOffset; i + 3 <= path.FillOffset + path.NFill; i += 3)
            {
                var a = new Vector2(verts[i].X, verts[i].Y);
                var b = new Vector2(verts[i + 1].X, verts[i + 1].Y);
                var c = new Vector2(verts[i + 2].X, verts[i + 2].Y);
                if (TriangleContainsPoint(a, b, c, point))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static float MaxFillCoverageAtPoint(NVGpathData[] paths, NVGvertex[] verts, float x, float y)
    {
        var point = new Vector2(x, y);
        var maximum = 0f;
        foreach (var path in paths)
        {
            for (var i = path.FillOffset; i + 3 <= path.FillOffset + path.NFill; i += 3)
            {
                var a = new Vector2(verts[i].X, verts[i].Y);
                var b = new Vector2(verts[i + 1].X, verts[i + 1].Y);
                var c = new Vector2(verts[i + 2].X, verts[i + 2].Y);
                var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                if (MathF.Abs(denominator) <= 1e-6f)
                {
                    continue;
                }

                var wa = ((b.Y - c.Y) * (point.X - c.X) + (c.X - b.X) * (point.Y - c.Y)) / denominator;
                var wb = ((c.Y - a.Y) * (point.X - c.X) + (a.X - c.X) * (point.Y - c.Y)) / denominator;
                var wc = 1f - wa - wb;
                if (wa < 0f || wb < 0f || wc < 0f)
                {
                    continue;
                }

                var u = verts[i].U * wa + verts[i + 1].U * wb + verts[i + 2].U * wc;
                maximum = MathF.Max(maximum, Math.Clamp(u + 0.5f, 0f, 1f));
            }
        }

        return maximum;
    }
}
