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

        _tess.AddContour(new ArraySegment<ContourVertex>(_contourBuffer, 0, points.Length), ContourOrientation.Original);
        _hasContours = true;
    }

    public TessResult Tessellate(
        TessWindingRule windingRule,
        TessElementType elementType = TessElementType.Polygons,
        int polySize = 3)
    {
        if (!_hasContours)
        {
            return new TessResult
            {
                Status = TessStatus.Ok,
                Vertices = Array.Empty<Vector2>(),
                Indices = Array.Empty<int>()
            };
        }

        if (!_inputValid)
        {
            return new TessResult { Status = TessStatus.InvalidInput };
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
            return new TessResult { Status = TessStatus.InvalidInput };
        }

        if (_tess.Vertices is null || _tess.Elements is null)
        {
            return new TessResult { Status = TessStatus.InvalidInput };
        }

        var outVerts = new Vector2[_tess.VertexCount];
        for (int i = 0; i < _tess.VertexCount; i++)
        {
            var p = _tess.Vertices[i].Position;
            outVerts[i] = new Vector2((float)p.X, (float)p.Y);
        }

        var outIndices = FilterUndefIndices(_tess.Elements, _tess.ElementCount, elementType, polySize);

        return new TessResult
        {
            Status = TessStatus.Ok,
            Vertices = outVerts,
            Indices = outIndices
        };
    }

    private void EnsureContourCapacity(int count)
    {
        if (_contourBuffer.Length < count)
        {
            _contourBuffer = new ContourVertex[count];
        }
    }

    private static int[] FilterUndefIndices(int[] elements, int elementCount, TessElementType elementType, int polySize)
    {
        if (elements.Length == 0)
        {
            return Array.Empty<int>();
        }

        if (elementType == TessElementType.Polygons)
        {
            int stride = Math.Max(3, polySize);
            int maxCount = Math.Min(elements.Length, elementCount * stride);
            int kept = 0;
            for (int i = 0; i < maxCount; i++)
            {
                if (elements[i] != LibTessDotNet.Tess.Undef)
                {
                    kept++;
                }
            }

            if (kept == 0)
            {
                return Array.Empty<int>();
            }

            var result = new int[kept];
            int dst = 0;
            for (int i = 0; i < maxCount; i++)
            {
                int idx = elements[i];
                if (idx != LibTessDotNet.Tess.Undef)
                {
                    result[dst++] = idx;
                }
            }

            return result;
        }

        int keptOther = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i] != LibTessDotNet.Tess.Undef)
            {
                keptOther++;
            }
        }

        if (keptOther == 0)
        {
            return Array.Empty<int>();
        }

        var resultOther = new int[keptOther];
        int dstOther = 0;
        for (int i = 0; i < elements.Length; i++)
        {
            int idx = elements[i];
            if (idx != LibTessDotNet.Tess.Undef)
            {
                resultOther[dstOther++] = idx;
            }
        }

        return resultOther;
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
