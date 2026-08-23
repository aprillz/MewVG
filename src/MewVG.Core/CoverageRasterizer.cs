using Aprillz.MewVG.Tess;

namespace Aprillz.MewVG;

/// <summary>
/// Scanline coverage rasterizer for flattened device-space contours: sub-scanline sampling
/// with exact horizontal span coverage, any fill rule, into an 8-bit coverage mask.
/// </summary>
/// <remarks>
/// Costs scale with mask area plus edge count, not with planar-arrangement work, which is
/// what makes this path cheap for fills with many interacting contours (see
/// agent/coverage-mask-fill/plan.md). Spans accumulate through difference arrays so a row
/// costs O(spans) plus one prefix pass over its pixels. The active edge list keeps its
/// current-x order between sub-scanlines, so the per-line insertion sort only moves edges
/// that crossed each other.
/// </remarks>
internal sealed class CoverageRasterizer
{
    private const int SUB_SCANLINES = 4;
    private const float SUB_WEIGHT = 1f / SUB_SCANLINES;

    private struct Edge
    {
        public float X0, Y0, X1, Y1, DxDy, CurrentX;
        public int Direction;
    }

    private Edge[] _edges = new Edge[1024];
    private int _edgeCount;
    private int[] _order = Array.Empty<int>();
    private float[] _orderKeys = Array.Empty<float>();
    private int[] _active = new int[256];
    private float[] _area = Array.Empty<float>();
    private float[] _coverDelta = Array.Empty<float>();

    /// <summary>Coverage mask of the last <see cref="Rasterize"/>, row-major, <see cref="Width"/> x <see cref="Height"/>.</summary>
    public byte[] Mask { get; private set; } = Array.Empty<byte>();

    public int Width { get; private set; }

    public int Height { get; private set; }

    public void Clear() => _edgeCount = 0;

    /// <summary>Adds a closed contour, translating it so that the mask origin lands at (0, 0).</summary>
    public void AddContour(ReadOnlySpan<NVGpoint> points, float originX, float originY)
    {
        for (var i = 0; i < points.Length; i++)
        {
            ref readonly var a = ref points[i];
            ref readonly var b = ref points[(i + 1) % points.Length];
            if (a.Y == b.Y)
            {
                continue;
            }

            if (_edgeCount == _edges.Length)
            {
                Array.Resize(ref _edges, _edges.Length * 2);
            }

            ref var edge = ref _edges[_edgeCount++];
            if (a.Y < b.Y)
            {
                edge.X0 = a.X - originX; edge.Y0 = a.Y - originY;
                edge.X1 = b.X - originX; edge.Y1 = b.Y - originY;
                edge.Direction = 1;
            }
            else
            {
                edge.X0 = b.X - originX; edge.Y0 = b.Y - originY;
                edge.X1 = a.X - originX; edge.Y1 = a.Y - originY;
                edge.Direction = -1;
            }
            edge.DxDy = (edge.X1 - edge.X0) / (edge.Y1 - edge.Y0);
        }
    }

    /// <summary>Rasterizes the added contours into a <paramref name="width"/> x <paramref name="height"/> mask.</summary>
    public void Rasterize(int width, int height, TessWindingRule windingRule)
    {
        Width = width;
        Height = height;
        var pixelCount = width * height;
        if (Mask.Length < pixelCount)
        {
            Mask = new byte[pixelCount];
        }
        else
        {
            Array.Clear(Mask, 0, pixelCount);
        }

        if (_area.Length < width + 2)
        {
            _area = new float[width + 2];
            _coverDelta = new float[width + 2];
        }
        if (_order.Length < _edgeCount)
        {
            _order = new int[_edgeCount];
            _orderKeys = new float[_edgeCount];
        }
        if (_active.Length < _edgeCount)
        {
            _active = new int[_edgeCount];
        }

        // Edges sorted by top y feed the active list incrementally.
        for (var i = 0; i < _edgeCount; i++)
        {
            _order[i] = i;
            _orderKeys[i] = _edges[i].Y0;
        }
        Array.Sort(_orderKeys, _order, 0, _edgeCount);

        var evenOdd = windingRule == TessWindingRule.Odd;
        var next = 0;
        var activeCount = 0;

        for (var row = 0; row < height; row++)
        {
            var rowTouched = false;
            for (var sub = 0; sub < SUB_SCANLINES; sub++)
            {
                var y = row + (sub + 0.5f) * SUB_WEIGHT;

                // Retire edges that ended above this line, admit edges that start on or above it.
                var kept = 0;
                for (var i = 0; i < activeCount; i++)
                {
                    if (_edges[_active[i]].Y1 > y)
                    {
                        _active[kept++] = _active[i];
                    }
                }
                activeCount = kept;
                while (next < _edgeCount && _orderKeys[next] <= y)
                {
                    var edgeIndex = _order[next++];
                    if (_edges[edgeIndex].Y1 > y)
                    {
                        _active[activeCount++] = edgeIndex;
                    }
                }
                if (activeCount < 2)
                {
                    continue;
                }

                for (var i = 0; i < activeCount; i++)
                {
                    ref var edge = ref _edges[_active[i]];
                    edge.CurrentX = edge.X0 + (y - edge.Y0) * edge.DxDy;
                }
                SortActiveByX(activeCount);

                var winding = 0;
                for (var i = 0; i < activeCount - 1; i++)
                {
                    ref readonly var edge = ref _edges[_active[i]];
                    winding += evenOdd ? 1 : edge.Direction;
                    var inside = evenOdd ? (winding & 1) != 0 : winding != 0;
                    if (!inside)
                    {
                        continue;
                    }

                    var spanStart = Math.Clamp(edge.CurrentX, 0f, width);
                    var spanEnd = Math.Clamp(_edges[_active[i + 1]].CurrentX, 0f, width);
                    if (spanEnd <= spanStart)
                    {
                        continue;
                    }

                    rowTouched = true;
                    var startPixel = (int)spanStart;
                    var endPixel = (int)spanEnd;
                    if (startPixel == endPixel)
                    {
                        _area[startPixel] += (spanEnd - spanStart) * SUB_WEIGHT;
                    }
                    else
                    {
                        _area[startPixel] += (startPixel + 1 - spanStart) * SUB_WEIGHT;
                        _coverDelta[startPixel + 1] += SUB_WEIGHT;
                        _coverDelta[endPixel] -= SUB_WEIGHT;
                        if (endPixel < width)
                        {
                            _area[endPixel] += (spanEnd - endPixel) * SUB_WEIGHT;
                        }
                    }
                }
            }

            if (!rowTouched)
            {
                continue;
            }

            var accumulated = 0f;
            var rowOffset = row * width;
            for (var x = 0; x < width; x++)
            {
                accumulated += _coverDelta[x];
                var coverage = accumulated + _area[x];
                if (coverage > 0f)
                {
                    Mask[rowOffset + x] = (byte)Math.Min(255, (int)(coverage * 255f + 0.5f));
                }
            }
            Array.Clear(_area, 0, width + 2);
            Array.Clear(_coverDelta, 0, width + 2);
        }
    }

    private void SortActiveByX(int count)
    {
        for (var i = 1; i < count; i++)
        {
            var edgeIndex = _active[i];
            var x = _edges[edgeIndex].CurrentX;
            var j = i - 1;
            while (j >= 0 && _edges[_active[j]].CurrentX > x)
            {
                _active[j + 1] = _active[j];
                j--;
            }
            _active[j + 1] = edgeIndex;
        }
    }
}
