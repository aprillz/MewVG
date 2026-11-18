using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// A path drawn again at the same scale but another position replays its stored geometry shifted; the result must
/// match what expanding the path afresh at that position gives, and a path that differs in shape or in how it is
/// expanded must never take another's geometry.
/// </summary>
public sealed class TranslatedPathReplayTests
{
    private const float TOLERANCE = 1e-3f;

    [Fact]
    public void RepeatedFill_MatchesAFreshFillAtTheSamePosition()
    {
        var repeated = Draw((vg, x, y) => FillRounded(vg, x, y, 48, 32, 8), (10, 10), (40, 20), (70, 35), (300, 120));
        var fresh = Draw((vg, x, y) => FillRounded(vg, x, y, 48, 32, 8), (300, 120));

        AssertSameGeometry(fresh.FillCalls[0], repeated.FillCalls[3]);
    }

    [Fact]
    public void RepeatedStroke_MatchesAFreshStrokeAtTheSamePosition()
    {
        var repeated = Draw((vg, x, y) => StrokeRounded(vg, x, y, 48, 32, 8, 2), (10, 10), (40, 20), (70, 35), (300, 120));
        var fresh = Draw((vg, x, y) => StrokeRounded(vg, x, y, 48, 32, 8, 2), (300, 120));

        Assert.Equal(fresh.StrokeCalls[0].Paths.Length, repeated.StrokeCalls[3].Paths.Length);
        AssertSameVerts(fresh.StrokeCalls[0].Paths, fresh.StrokeCalls[0].Verts, repeated.StrokeCalls[3].Paths, repeated.StrokeCalls[3].Verts);
    }

    [Fact]
    public void SameShapeExpandedOtherwise_IsNotReplayed()
    {
        // The shape is seen often enough to be stored, then drawn without antialiasing and with a wider stroke.
        var renderer = Draw((vg, x, y) =>
        {
            FillRounded(vg, x, y, 48, 32, 8);
            StrokeRounded(vg, x, y, 48, 32, 8, 2);
        }, (10, 10), (40, 20), (70, 35));
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);
            vg.ShapeAntiAlias(false);
            FillRounded(vg, 300, 120, 48, 32, 8);
            vg.ShapeAntiAlias(true);
            StrokeRounded(vg, 300, 120, 48, 32, 8, 6);
            vg.EndFrame();
        }

        var fresh = new FakeRenderer();
        using (var vg = new TestMewVGContext(fresh))
        {
            vg.BeginFrame(600, 600, 1f);
            vg.ShapeAntiAlias(false);
            FillRounded(vg, 300, 120, 48, 32, 8);
            vg.ShapeAntiAlias(true);
            StrokeRounded(vg, 300, 120, 48, 32, 8, 6);
            vg.EndFrame();
        }

        AssertSameGeometry(fresh.FillCalls[0], renderer.FillCalls[^1]);
        AssertSameVerts(fresh.StrokeCalls[0].Paths, fresh.StrokeCalls[0].Verts, renderer.StrokeCalls[^1].Paths, renderer.StrokeCalls[^1].Verts);
    }

    [Fact]
    public void OtherShapeAtTheSameAnchor_IsNotReplayed()
    {
        var renderer = Draw((vg, x, y) => FillRounded(vg, x, y, 48, 32, 8), (10, 10), (40, 20), (70, 35));
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);
            FillRounded(vg, 70, 35, 60, 32, 8);
            vg.EndFrame();
        }

        var fresh = Draw((vg, x, y) => FillRounded(vg, x, y, 60, 32, 8), (70, 35));

        AssertSameGeometry(fresh.FillCalls[0], renderer.FillCalls[^1]);
    }

    private static FakeRenderer Draw(Action<TestMewVGContext, float, float> draw, params (float X, float Y)[] positions)
    {
        var renderer = new FakeRenderer();
        using var vg = new TestMewVGContext(renderer);
        vg.BeginFrame(600, 600, 1f);
        foreach (var (x, y) in positions)
        {
            draw(vg, x, y);
        }

        vg.EndFrame();
        return renderer;
    }

    private static void FillRounded(TestMewVGContext vg, float x, float y, float width, float height, float radius)
    {
        vg.BeginPath();
        vg.RoundedRect(x, y, width, height, radius);
        vg.FillColor(0, 0, 0);
        vg.Fill();
    }

    private static void StrokeRounded(TestMewVGContext vg, float x, float y, float width, float height, float radius, float strokeWidth)
    {
        vg.BeginPath();
        vg.RoundedRect(x, y, width, height, radius);
        vg.StrokeColor(0, 0, 0);
        vg.StrokeWidth(strokeWidth);
        vg.Stroke();
    }

    private static void AssertSameGeometry(FakeRenderer.Call expected, FakeRenderer.Call actual)
    {
        Assert.Equal(expected.Fringe, actual.Fringe);
        Assert.Equal(expected.Paths.Length, actual.Paths.Length);
        AssertSameVerts(expected.Paths, expected.Verts, actual.Paths, actual.Verts);
    }

    private static void AssertSameVerts(NVGpathData[] expectedPaths, NVGvertex[] expectedVerts, NVGpathData[] actualPaths, NVGvertex[] actualVerts)
    {
        for (var index = 0; index < expectedPaths.Length; index++)
        {
            var expected = expectedPaths[index];
            var actual = actualPaths[index];
            Assert.Equal(expected.NFill, actual.NFill);
            Assert.Equal(expected.NStroke, actual.NStroke);
            Assert.Equal(expected.Convex, actual.Convex);
            AssertClose(expectedVerts.AsSpan(expected.FillOffset, expected.NFill), actualVerts.AsSpan(actual.FillOffset, actual.NFill));
            AssertClose(expectedVerts.AsSpan(expected.StrokeOffset, expected.NStroke), actualVerts.AsSpan(actual.StrokeOffset, actual.NStroke));
        }
    }

    private static void AssertClose(ReadOnlySpan<NVGvertex> expected, ReadOnlySpan<NVGvertex> actual)
    {
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.InRange(actual[index].X - expected[index].X, -TOLERANCE, TOLERANCE);
            Assert.InRange(actual[index].Y - expected[index].Y, -TOLERANCE, TOLERANCE);
            Assert.Equal(expected[index].U, actual[index].U);
            Assert.Equal(expected[index].V, actual[index].V);
        }
    }
}
