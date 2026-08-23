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
    /// Returns true if this cache no longer matches the current draw request, meaning it
    /// should be rebuilt via <see cref="NanoVG.BuildFillCache"/>. A cache is stale when the
    /// bezier flatten tolerance changed (e.g. DPI changed), the winding rule used to build
    /// the cached tessellation differs from the one requested now, or the current transform
    /// scale exceeds the scale the cache was tessellated for (the baked-in bezier flattening
    /// and fringe inset would be too coarse for a larger scale).
    /// </summary>
    public bool IsStale(float currentTessTol, Tess.TessWindingRule currentWindingRule, float currentScale)
    {
        const float scaleEpsilon = 1.001f;
        return TessTol != currentTessTol
            || WindingRule != currentWindingRule
            || currentScale > BuildScale * scaleEpsilon;
    }

    /// <summary>
    /// Partial staleness check comparing only the tessellation tolerance; prefer the
    /// 3-argument overload for a full check (winding rule and scale changes too).
    /// </summary>
    public bool IsStale(float currentTessTol) => TessTol != currentTessTol;

    // Invalidation keys: rebuild when DPI (_tessTol), winding rule, or scale change.
    internal float TessTol;
    internal Tess.TessWindingRule WindingRule;
    internal float BuildScale;

    // Device-space snapshot of the last winding-resolved ExpandFill output.
    // Boundary resolution runs in device space and bypasses the object-space
    // tessellation reuse, so repeat draws with an unchanged transform replay
    // this snapshot instead of resolving and tessellating again.
    internal bool SnapshotValid;
    internal float[] SnapshotXform = new float[6];
    internal float SnapshotFringe;
    internal Tess.TessWindingRule SnapshotWindingRule;
    internal NVGpathData[] SnapshotPaths = Array.Empty<NVGpathData>();
    internal NVGvertex[] SnapshotVerts = Array.Empty<NVGvertex>();
    internal int SnapshotNPaths;
    internal int SnapshotNVerts;
    internal float[] SnapshotBounds = new float[4];

    // Coverage-mask path (see NVGContext.TryRasterizeCoverageMask): the mask is device-space,
    // so it is keyed by the transform, fringe and winding rule it was built for, exactly like
    // the tessellation snapshot above. Only CPU pixels live here; the renderer that draws the
    // cache owns any device copy and keys it on (this cache, MaskVersion), so a cache shared
    // between windows never carries one context's texture into another. LastUsedMask feeds
    // the hysteresis that keeps a geometry on one path while its area-to-point ratio hovers
    // around the switch threshold.
    internal bool MaskValid;
    internal bool LastUsedMask;
    internal byte[] MaskPixels = Array.Empty<byte>();
    internal int MaskWidth;
    internal int MaskHeight;
    // Bumped on every rasterization; a renderer re-uploads when the version it holds differs.
    internal int MaskVersion;
    // Device-pixel placement of the mask, and the unclipped device-pixel bounds of the
    // geometry it was cut from: the mask is reusable only while everything visible of the
    // geometry lies inside it.
    internal float MaskOriginX;
    internal float MaskOriginY;
    internal float[] MaskGeometryPx = new float[4];
    internal float[] MaskXform = new float[6];
    internal float MaskFringe;
    internal Tess.TessWindingRule MaskWindingRule;
    internal float[] MaskBounds = new float[4];
}
