using System.Numerics;

namespace Aprillz.MewVG.Tess;

/// <summary>
/// C# counterpart of libtess2 winding rules.
/// Designed for NativeAOT-safe usage (no native interop).
/// </summary>
public enum TessWindingRule
{
    Odd = 0,
    NonZero = 1,
    Positive = 2,
    Negative = 3,
    AbsGeqTwo = 4
}

/// <summary>
/// C# counterpart of libtess2 element output modes.
/// </summary>
public enum TessElementType
{
    Polygons = 0,
    ConnectedPolygons = 1,
    BoundaryContours = 2
}

public enum TessStatus
{
    Ok = 0,
    InvalidInput = 1,
    OutOfMemory = 2,
    NotImplemented = 3
}

/// <summary>
/// Flattened contour used by the managed tessellator.
/// </summary>
public readonly record struct TessContour(ReadOnlyMemory<Vector2> Points);

/// <summary>
/// Triangulation output. Indices are triangle list (3*n).
/// </summary>
public sealed class TessResult
{
    public TessStatus Status { get; init; } = TessStatus.Ok;

    public Vector2[] Vertices { get; init; } = Array.Empty<Vector2>();

    public int[] Indices { get; init; } = Array.Empty<int>();
}

