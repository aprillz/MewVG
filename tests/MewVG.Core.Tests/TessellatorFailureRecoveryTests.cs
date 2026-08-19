using System.Numerics;

using Aprillz.MewVG.Tess;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// Regression coverage for plan Phase 1.4: a tessellation run that fails must not
/// leave mesh/contour state that contaminates the next run.
/// </summary>
public class TessellatorFailureRecoveryTests
{
    private static readonly Vector2[] Square0 = { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
    private static readonly Vector2[] Square100 = { new(100, 100), new(110, 100), new(110, 110), new(100, 110) };

    [Fact]
    public void InvalidContourAfterValidOne_ReturnsInvalidInput()
    {
        var tess = new Tessellator();

        // A prior valid contour is required to observe the InvalidInput path: if the
        // very first AddContour call hits a NaN/Inf point, _hasContours never becomes
        // true and RunTessellation short-circuits to TessStatus.Ok with empty output
        // (see Tessellator.RunTessellation's `if (!_hasContours) return Ok;` guard).
        tess.AddContour(Square0);
        tess.AddContour(new[] { new Vector2(0, 0), new Vector2(float.NaN, 0), new Vector2(10, 10) });

        var result = tess.Tessellate(TessWindingRule.NonZero);

        Assert.Equal(TessStatus.InvalidInput, result.Status);
    }

    [Fact]
    public void InvalidContourWithInfinity_ReturnsInvalidInput()
    {
        var tess = new Tessellator();

        tess.AddContour(Square0);
        tess.AddContour(new[] { new Vector2(0, 0), new Vector2(float.PositiveInfinity, 0), new Vector2(10, 10) });

        var result = tess.Tessellate(TessWindingRule.NonZero);

        Assert.Equal(TessStatus.InvalidInput, result.Status);
    }

    [Fact]
    public void ClearAfterInvalidInput_RecoversCleanTessellation_NoContamination()
    {
        var tess = new Tessellator();

        tess.AddContour(Square0);
        tess.AddContour(new[] { new Vector2(0, 0), new Vector2(float.NaN, 0), new Vector2(10, 10) });
        var badResult = tess.Tessellate(TessWindingRule.NonZero);
        Assert.Equal(TessStatus.InvalidInput, badResult.Status);

        tess.Clear();
        tess.AddContour(Square100);
        var goodResult = tess.Tessellate(TessWindingRule.NonZero);

        Assert.Equal(TessStatus.Ok, goodResult.Status);

        // A single convex quad tessellates to exactly 2 triangles (4 vertices, 6 indices).
        // If the failed run's contours had lingered in the mesh, this would tessellate
        // more than one quad's worth of geometry.
        Assert.Equal(4, goodResult.Vertices.Length);
        Assert.Equal(6, goodResult.Indices.Length);

        foreach (var vertex in goodResult.Vertices)
        {
            Assert.InRange(vertex.X, 100, 110);
            Assert.InRange(vertex.Y, 100, 110);
        }
    }

    [Fact]
    public void ConsecutiveSuccessfulRunsWithoutClear_AreIndependent()
    {
        var tess = new Tessellator();

        tess.AddContour(Square0);
        var first = tess.Tessellate(TessWindingRule.NonZero);
        Assert.Equal(TessStatus.Ok, first.Status);
        Assert.Equal(4, first.Vertices.Length);
        Assert.Equal(6, first.Indices.Length);
        foreach (var vertex in first.Vertices)
        {
            Assert.InRange(vertex.X, 0, 10);
            Assert.InRange(vertex.Y, 0, 10);
        }

        // No Clear() between runs - only a fresh AddContour for a disjoint square.
        // The prior run completed (Tess.Tessellate returns its mesh to the pool on
        // success), so the next AddContour should start from a clean mesh.
        tess.AddContour(Square100);
        var second = tess.Tessellate(TessWindingRule.NonZero);
        Assert.Equal(TessStatus.Ok, second.Status);
        Assert.Equal(4, second.Vertices.Length);
        Assert.Equal(6, second.Indices.Length);
        foreach (var vertex in second.Vertices)
        {
            Assert.InRange(vertex.X, 100, 110);
            Assert.InRange(vertex.Y, 100, 110);
        }
    }
}
