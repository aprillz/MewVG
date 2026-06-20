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

/// <summary>
/// Reusable storage for tessellation output.
/// </summary>
internal sealed class TessResultBuffer
{
    private Vector2[] _vertices = Array.Empty<Vector2>();
    private int[] _indices = Array.Empty<int>();

    public int VertexCount { get; private set; }

    public int IndexCount { get; private set; }

    public ReadOnlyMemory<Vector2> Vertices => _vertices.AsMemory(0, VertexCount);

    public ReadOnlyMemory<int> Indices => _indices.AsMemory(0, IndexCount);

    public void Clear()
    {
        VertexCount = 0;
        IndexCount = 0;
    }

    internal Span<Vector2> PrepareVertices(int count)
    {
        if (_vertices.Length < count)
        {
            _vertices = new Vector2[count];
        }

        VertexCount = count;
        return _vertices.AsSpan(0, count);
    }

    internal Span<int> PrepareIndices(int count)
    {
        if (_indices.Length < count)
        {
            _indices = new int[count];
        }

        IndexCount = count;
        return _indices.AsSpan(0, count);
    }
}

/// <summary>
/// Tessellation output view backed by a caller-owned <see cref="TessResultBuffer"/>.
/// </summary>
internal readonly record struct TessBufferedResult(
    TessStatus Status,
    ReadOnlyMemory<Vector2> Vertices,
    ReadOnlyMemory<int> Indices);

