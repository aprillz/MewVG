using System.Numerics;

using Aprillz.MewVG;
using Aprillz.MewVG.Tess;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// The coverage-mask fill path (agent/coverage-mask-fill/plan.md): a fill with many
/// interacting contours is rasterized on the CPU instead of resolved and tessellated, and
/// the mask has to match what the tessellator's resolution would have filled.
/// </summary>
public class CoverageMaskFillTests
{
    private const int TILE_SIDE = 60;
    private const float PITCH = 8f;

    // Rounded tiles whose radius is half the pitch touch their neighbours at single points,
    // the arrangement that drives the tessellator into its worst case.
    private static List<Vector2[]> TangentTiles(int side, float pitch)
    {
        var contours = new List<Vector2[]>(side * side);
        var radius = pitch * 0.5f;
        for (var index = 0; index < side * side; index++)
        {
            if (((index % side) * 7 + (index / side) * 13) % 3 == 0)
            {
                continue;
            }

            var x = 4f + (index % side) * pitch;
            var y = 4f + (index / side) * pitch;
            var points = new List<Vector2>(12);
            (float cx, float cy, float start)[] corners =
            {
                (x + pitch - radius, y + radius, -MathF.PI / 2),
                (x + pitch - radius, y + pitch - radius, 0),
                (x + radius, y + pitch - radius, MathF.PI / 2),
                (x + radius, y + radius, MathF.PI),
            };
            foreach (var (cx, cy, start) in corners)
            {
                for (var step = 0; step <= 2; step++)
                {
                    var angle = start + (MathF.PI / 2) * step / 2;
                    points.Add(new Vector2(cx + radius * MathF.Cos(angle), cy + radius * MathF.Sin(angle)));
                }
            }
            contours.Add(points.ToArray());
        }

        return contours;
    }

    private static NVGpoint[] ToPoints(Vector2[] contour)
    {
        var points = new NVGpoint[contour.Length];
        for (var i = 0; i < contour.Length; i++)
        {
            points[i].X = contour[i].X;
            points[i].Y = contour[i].Y;
        }
        return points;
    }

    private static byte[] RasterizeDirect(List<Vector2[]> contours, int size, TessWindingRule rule)
    {
        var rasterizer = new CoverageRasterizer();
        foreach (var contour in contours)
        {
            rasterizer.AddContour(ToPoints(contour), 0f, 0f);
        }
        rasterizer.Rasterize(size, size, rule);
        return rasterizer.Mask.AsSpan(0, size * size).ToArray();
    }

    private static byte[] RasterizeResolved(List<Vector2[]> contours, int size, TessWindingRule rule)
    {
        var tess = new Tessellator();
        foreach (var contour in contours)
        {
            tess.AddContour(contour);
        }
        var boundary = tess.Tessellate(rule, TessElementType.BoundaryContours, 3);
        Assert.Equal(TessStatus.Ok, boundary.Status);

        // The resolved boundary has no overlaps left, so any rule fills it identically.
        var rasterizer = new CoverageRasterizer();
        for (var i = 0; i < boundary.Indices.Length; i += 2)
        {
            var loop = boundary.Vertices.AsSpan(boundary.Indices[i], boundary.Indices[i + 1]).ToArray();
            rasterizer.AddContour(ToPoints(loop), 0f, 0f);
        }
        rasterizer.Rasterize(size, size, TessWindingRule.NonZero);
        return rasterizer.Mask.AsSpan(0, size * size).ToArray();
    }

    [Theory]
    [InlineData(TessWindingRule.Odd)]
    [InlineData(TessWindingRule.NonZero)]
    public void Mask_MatchesTessellatorResolution(TessWindingRule rule)
    {
        var contours = TangentTiles(TILE_SIDE, PITCH);
        var size = (int)(TILE_SIDE * PITCH) + 8;

        var direct = RasterizeDirect(contours, size, rule);
        var resolved = RasterizeResolved(contours, size, rule);

        var differing = 0;
        for (var i = 0; i < direct.Length; i++)
        {
            if (Math.Abs(direct[i] - resolved[i]) > 1)
            {
                differing++;
            }
        }
        Assert.Equal(0, differing);
        Assert.Contains(direct, value => value == 255);
    }

    private static void EmitTiles(MewVGContext vg, List<Vector2[]> contours)
    {
        vg.BeginPath();
        foreach (var contour in contours)
        {
            vg.MoveTo(contour[0].X, contour[0].Y);
            for (var i = 1; i < contour.Length; i++)
            {
                vg.LineTo(contour[i].X, contour[i].Y);
            }
            vg.ClosePath();
        }
    }

    [Fact]
    public void ManyInteractingContours_TakeTheMaskPath()
    {
        var renderer = new FakeRenderer();
        using var vg = new TestMewVGContext(renderer);
        var contours = TangentTiles(TILE_SIDE, PITCH);

        vg.BeginFrame(600, 600, 1f);
        EmitTiles(vg, contours);
        vg.FillRule(MewVGFillRule.EvenOdd);
        vg.FillColor(0, 0, 0);
        vg.Fill();
        vg.EndFrame();

        Assert.Empty(renderer.FillCalls);
        var call = Assert.Single(renderer.MaskFillCalls);
        Assert.Equal(call.MaskWidth, (int)(call.Bounds[2] - call.Bounds[0]));
        Assert.Equal(call.MaskHeight, (int)(call.Bounds[3] - call.Bounds[1]));
        Assert.Contains(call.Coverage, value => value == 255);

        // Nothing caches an unfrozen fill, so the renderer gets a one-draw mask.
        Assert.Null(call.CacheKey);
    }

    [Fact]
    public void FewPoints_KeepTheTessellationPath()
    {
        var renderer = new FakeRenderer();
        using var vg = new TestMewVGContext(renderer);

        // A rounded border ring with a zero-width top: boundaries interact, but the point
        // count is far below the mask floor.
        vg.BeginFrame(600, 600, 1f);
        vg.BeginPath();
        vg.Rect(10, 10, 100, 50);
        vg.Rect(20, 10, 80, 40);
        vg.FillRule(MewVGFillRule.EvenOdd);
        vg.FillColor(0, 0, 0);
        vg.Fill();
        vg.EndFrame();

        Assert.Empty(renderer.MaskFillCalls);
        Assert.Single(renderer.FillCalls);
    }

    [Fact]
    public void FrozenFill_ReusesTheMaskUntilTheTransformChanges()
    {
        var renderer = new FakeRenderer();
        using var vg = new TestMewVGContext(renderer);
        var contours = TangentTiles(TILE_SIDE, PITCH);

        vg.BeginFrame(600, 600, 1f);
        EmitTiles(vg, contours);
        var cache = vg.BuildFillCache(TessWindingRule.Odd);
        vg.FillColor(0, 0, 0);
        vg.FillFromCache(cache, TessWindingRule.Odd);
        vg.FillFromCache(cache, TessWindingRule.Odd);
        vg.Translate(3, 0);
        vg.FillFromCache(cache, TessWindingRule.Odd);
        vg.EndFrame();

        Assert.Equal(3, renderer.MaskFillCalls.Count);
        // Same cache and version twice: the renderer may keep its upload. The translated draw
        // rasterizes again and carries a new version.
        Assert.Same(cache, renderer.MaskFillCalls[0].CacheKey);
        Assert.Same(cache, renderer.MaskFillCalls[1].CacheKey);
        Assert.Equal(renderer.MaskFillCalls[0].CacheVersion, renderer.MaskFillCalls[1].CacheVersion);
        Assert.NotEqual(renderer.MaskFillCalls[1].CacheVersion, renderer.MaskFillCalls[2].CacheVersion);
        Assert.Equal(renderer.MaskFillCalls[0].MaskOriginX + 3, renderer.MaskFillCalls[2].MaskOriginX);
        Assert.Empty(renderer.FillCalls);
    }

    [Fact]
    public void FrozenFill_RebuildsTheMaskWhenTheScissorWidens()
    {
        var renderer = new FakeRenderer();
        using var vg = new TestMewVGContext(renderer);
        var contours = TangentTiles(TILE_SIDE, PITCH);

        // Built under a scissor that cuts the geometry: the mask covers only the visible part.
        vg.BeginFrame(600, 600, 1f);
        EmitTiles(vg, contours);
        var cache = vg.BuildFillCache(TessWindingRule.Odd);
        vg.FillColor(0, 0, 0);
        vg.Scissor(0, 0, 150, 150);
        vg.FillFromCache(cache, TessWindingRule.Odd);
        var clipped = Assert.Single(renderer.MaskFillCalls);

        // The same transform with a wider scissor exposes more of the geometry than the cut mask
        // holds, so the mask is rasterized again rather than reused.
        vg.ResetScissor();
        vg.Scissor(0, 0, 400, 400);
        vg.FillFromCache(cache, TessWindingRule.Odd);
        vg.EndFrame();

        Assert.Equal(2, renderer.MaskFillCalls.Count);
        var widened = renderer.MaskFillCalls[1];
        Assert.NotEqual(clipped.CacheVersion, widened.CacheVersion);
        Assert.True(widened.MaskWidth > clipped.MaskWidth, $"mask width {widened.MaskWidth} should exceed {clipped.MaskWidth}");
        Assert.True(widened.MaskHeight > clipped.MaskHeight, $"mask height {widened.MaskHeight} should exceed {clipped.MaskHeight}");
        Assert.Empty(renderer.FillCalls);
    }
}
