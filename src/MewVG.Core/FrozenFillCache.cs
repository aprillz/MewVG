using System.Numerics;

namespace Aprillz.MewVG;

/// <summary>
/// Stores object-space tessellation data for a frozen PathGeometry.
/// Created by <see cref="NanoVG.BuildFillCache"/> and consumed by <see cref="NanoVG.FillFromCache"/>.
/// All contour/tessellation data is in object-space (identity transform).
/// </summary>
public sealed class FrozenFillCache
{
    // Object-space flattened contour points (bezier subdivided, normalized)
    internal NVGpoint[] ContourPoints = Array.Empty<NVGpoint>();
    internal NVGpathData[] ContourPaths = Array.Empty<NVGpathData>();
    internal int NContourPaths;
    internal int NContourPoints;

    // Whether this is a single convex contour (directConvexFill fast path)
    internal bool IsDirectConvex;

    // Object-space tessellation result (null for direct convex paths)
    internal Vector2[]? TessVertices;
    internal int[]? TessIndices;
    internal int TriangleCount;

    /// <summary>
    /// Returns true if this cache was built with a different tolerance than the current one
    /// (e.g. DPI changed), meaning it should be rebuilt.
    /// </summary>
    public bool IsStale(float currentTessTol) => TessTol != currentTessTol;

    // Invalidation key: rebuild when DPI changes (_tessTol changes)
    internal float TessTol;
}
