using System.Numerics;

using LibTessDotNet;

namespace Aprillz.MewVG.Tess;

/// <summary>
/// NativeAOT-safe tessellator backed by vendored LibTessDotNet
/// (full libtess2 algorithm port in C#).
/// </summary>
public sealed class Tessellator
{
    private const float MaxInput = 1 << 23;
    private const float MinInput = -MaxInput;

    private LibTessDotNet.Tess _tess = new();
    private ContourVertex[] _contourBuffer = Array.Empty<ContourVertex>();
    private bool _hasContours;
    private bool _inputValid = true;

    public void Clear()
    {
        // Tess can be reused; each Tessellate() clears mesh state internally.
        _hasContours = false;
        _inputValid = true;
    }

    public void AddContour(ReadOnlySpan<Vector2> points)
    {
        if (points.Length < 3)
        {
            return;
        }

        EnsureContourCapacity(points.Length);
        var contour = _contourBuffer.AsSpan(0, points.Length);

        for (int i = 0; i < points.Length; i++)
        {
            var p = points[i];
            if (!IsValidInput(p))
            {
                _inputValid = false;
                return;
            }

            contour[i].Position = new Vec3(p.X, p.Y, 0);
            contour[i].Data = default!;
        }

        _tess.AddContour(contour, ContourOrientation.Original);
        _hasContours = true;
    }

    public TessResult Tessellate(
        TessWindingRule windingRule,
        TessElementType elementType = TessElementType.Polygons,
        int polySize = 3)
    {
        var status = RunTessellation(windingRule, elementType, polySize);
        if (status != TessStatus.Ok || !_hasContours)
        {
            return new TessResult { Status = status };
        }

        var outVerts = new Vector2[_tess.VertexCount];
        CopyVertices(outVerts);

        var outIndices = FilterUndefIndices(_tess.Elements, _tess.ElementCount, elementType, polySize);

        return new TessResult
        {
            Status = TessStatus.Ok,
            Vertices = outVerts,
            Indices = outIndices
        };
    }

    internal TessBufferedResult Tessellate(
        TessWindingRule windingRule,
        TessResultBuffer resultBuffer,
        TessElementType elementType = TessElementType.Polygons,
        int polySize = 3)
    {
        ArgumentNullException.ThrowIfNull(resultBuffer);

        var status = RunTessellation(windingRule, elementType, polySize);
        if (status != TessStatus.Ok || !_hasContours)
        {
            resultBuffer.Clear();
            return new TessBufferedResult(status, resultBuffer.Vertices, resultBuffer.Indices);
        }

        var outVerts = resultBuffer.PrepareVertices(_tess.VertexCount);
        CopyVertices(outVerts);

        int indexCount = CountDefinedIndices(_tess.Elements, _tess.ElementCount, elementType, polySize);
        var outIndices = resultBuffer.PrepareIndices(indexCount);
        CopyDefinedIndices(_tess.Elements, _tess.ElementCount, elementType, polySize, outIndices);

        return new TessBufferedResult(TessStatus.Ok, resultBuffer.Vertices, resultBuffer.Indices);
    }

    private void EnsureContourCapacity(int count)
    {
        if (_contourBuffer.Length < count)
        {
            _contourBuffer = new ContourVertex[count];
        }
    }

    private TessStatus RunTessellation(TessWindingRule windingRule, TessElementType elementType, int polySize)
    {
        if (!_hasContours)
        {
            return TessStatus.Ok;
        }

        if (!_inputValid)
        {
            return TessStatus.InvalidInput;
        }

        try
        {
            _tess.Tessellate(
                MapWinding(windingRule),
                MapElementType(elementType),
                polySize);
        }
        catch
        {
            return TessStatus.InvalidInput;
        }

        if (_tess.Vertices is null || _tess.Elements is null)
        {
            return TessStatus.InvalidInput;
        }

        return TessStatus.Ok;
    }

    private void CopyVertices(Span<Vector2> destination)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            var p = _tess.Vertices[i].Position;
            destination[i] = new Vector2((float)p.X, (float)p.Y);
        }
    }

    private static int[] FilterUndefIndices(int[] elements, int elementCount, TessElementType elementType, int polySize)
    {
        int kept = CountDefinedIndices(elements, elementCount, elementType, polySize);
        if (kept == 0)
        {
            return Array.Empty<int>();
        }

        var result = new int[kept];
        CopyDefinedIndices(elements, elementCount, elementType, polySize, result);
        return result;
    }

    private static int CountDefinedIndices(ReadOnlySpan<int> elements, int elementCount, TessElementType elementType, int polySize)
    {
        int maxCount = GetElementScanCount(elements.Length, elementCount, elementType, polySize);
        int kept = 0;
        for (int i = 0; i < maxCount; i++)
        {
            if (elements[i] != LibTessDotNet.Tess.Undef)
            {
                kept++;
            }
        }

        return kept;
    }

    private static void CopyDefinedIndices(ReadOnlySpan<int> elements, int elementCount, TessElementType elementType, int polySize, Span<int> destination)
    {
        int maxCount = GetElementScanCount(elements.Length, elementCount, elementType, polySize);
        int dst = 0;
        for (int i = 0; i < maxCount; i++)
        {
            int idx = elements[i];
            if (idx != LibTessDotNet.Tess.Undef)
            {
                destination[dst++] = idx;
            }
        }
    }

    private static int GetElementScanCount(int elementLength, int elementCount, TessElementType elementType, int polySize)
    {
        if (elementLength == 0)
        {
            return 0;
        }

        if (elementType != TessElementType.Polygons)
        {
            return elementLength;
        }

        int stride = Math.Max(3, polySize);
        return Math.Min(elementLength, elementCount * stride);
    }

    private static WindingRule MapWinding(TessWindingRule windingRule) => windingRule switch
    {
        TessWindingRule.Odd => WindingRule.EvenOdd,
        TessWindingRule.NonZero => WindingRule.NonZero,
        TessWindingRule.Positive => WindingRule.Positive,
        TessWindingRule.Negative => WindingRule.Negative,
        TessWindingRule.AbsGeqTwo => WindingRule.AbsGeqTwo,
        _ => WindingRule.NonZero
    };

    private static ElementType MapElementType(TessElementType elementType) => elementType switch
    {
        TessElementType.Polygons => ElementType.Polygons,
        TessElementType.ConnectedPolygons => ElementType.ConnectedPolygons,
        TessElementType.BoundaryContours => ElementType.BoundaryContours,
        _ => ElementType.Polygons
    };

    private static bool IsValidInput(Vector2 p)
    {
        if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y))
        {
            return false;
        }

        if (p.X < MinInput || p.X > MaxInput || p.Y < MinInput || p.Y > MaxInput)
        {
            return false;
        }

        return true;
    }
}
