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
}
