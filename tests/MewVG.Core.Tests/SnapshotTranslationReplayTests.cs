using Aprillz.MewVG;
using Aprillz.MewVG.Tess;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// A frozen fill's device snapshot is keyed on the linear transform only: panning replays the
/// stored geometry shifted by the translation delta instead of resolving and tessellating again,
/// and every replay shifts from the stored snapshot so rounding never accumulates across ticks.
/// </summary>
public sealed class SnapshotTranslationReplayTests
{
    private static FrozenFillCache EmitRing(TestMewVGContext vg)
    {
        // The FewPoints ring: boundaries interact so the fill is winding-resolved and stores a
        // device snapshot, while the point count stays far below the mask floor.
        vg.BeginPath();
        vg.Rect(10, 10, 100, 50);
        vg.Rect(20, 10, 80, 40);
        vg.FillRule(MewVGFillRule.EvenOdd);
        vg.FillColor(0, 0, 0);
        return vg.BuildFillCache(MewVGFillRule.EvenOdd);
    }

    [Fact]
    public void Translation_ReplaysTheSnapshotShifted()
    {
        var renderer = new FakeRenderer();
        using var vg = new TestMewVGContext(renderer);

        vg.BeginFrame(600, 600, 1f);
        var cache = EmitRing(vg);
        vg.FillFromCache(cache, MewVGFillRule.EvenOdd);
        Assert.True(cache.SnapshotValid);
        float storedTx = cache.SnapshotXform[4];

        vg.Translate(3, 7);
        vg.FillFromCache(cache, MewVGFillRule.EvenOdd);
        vg.EndFrame();

        Assert.Empty(renderer.MaskFillCalls);
        Assert.Equal(2, renderer.FillCalls.Count);
        var first = renderer.FillCalls[0];
        var second = renderer.FillCalls[1];
        AssertShifted(first, second, 3, 7);

        // A recompute would store a new snapshot under the translated transform; a replay
        // leaves the stored one in place.
        Assert.Equal(storedTx, cache.SnapshotXform[4]);
    }

    [Fact]
    public void RepeatedPans_ShiftFromTheStoredSnapshot()
    {
        var renderer = new FakeRenderer();
        using var vg = new TestMewVGContext(renderer);

        vg.BeginFrame(600, 600, 1f);
        var cache = EmitRing(vg);
        vg.FillFromCache(cache, MewVGFillRule.EvenOdd);
        vg.Translate(3, 0);
        vg.FillFromCache(cache, MewVGFillRule.EvenOdd);
        vg.Translate(3, 0);
        vg.FillFromCache(cache, MewVGFillRule.EvenOdd);
        vg.EndFrame();

        // The third draw sits at +6 from the original: shifting from the stored snapshot each
        // time, not from the previous replay's output.
        AssertShifted(renderer.FillCalls[0], renderer.FillCalls[2], 6, 0);
    }

    [Fact]
    public void ScaleChange_FallsBackAndStoresANewSnapshot()
    {
        var renderer = new FakeRenderer();
        using var vg = new TestMewVGContext(renderer);

        vg.BeginFrame(600, 600, 1f);
        var cache = EmitRing(vg);
        vg.FillFromCache(cache, MewVGFillRule.EvenOdd);
        float storedScale = cache.SnapshotXform[0];

        vg.Scale(0.5f, 0.5f);
        vg.FillFromCache(cache, MewVGFillRule.EvenOdd);
        vg.EndFrame();

        Assert.Equal(2, renderer.FillCalls.Count);
        Assert.NotEqual(storedScale, cache.SnapshotXform[0]);
    }

    /// <summary>Both calls submit whole backing arrays; only the vertex ranges the paths
    /// reference are meaningful, so the comparison walks those ranges.</summary>
    private static void AssertShifted(FakeRenderer.Call before, FakeRenderer.Call after, float dx, float dy)
    {
        Assert.Equal(before.Paths.Length, after.Paths.Length);
        for (var p = 0; p < before.Paths.Length; p++)
        {
            var pathBefore = before.Paths[p];
            var pathAfter = after.Paths[p];
            Assert.Equal(pathBefore.NFill, pathAfter.NFill);
            Assert.Equal(pathBefore.NStroke, pathAfter.NStroke);
            for (var i = 0; i < pathBefore.NFill; i++)
            {
                Assert.Equal(before.Verts[pathBefore.FillOffset + i].X + dx, after.Verts[pathAfter.FillOffset + i].X);
                Assert.Equal(before.Verts[pathBefore.FillOffset + i].Y + dy, after.Verts[pathAfter.FillOffset + i].Y);
            }
            for (var i = 0; i < pathBefore.NStroke; i++)
            {
                Assert.Equal(before.Verts[pathBefore.StrokeOffset + i].X + dx, after.Verts[pathAfter.StrokeOffset + i].X);
                Assert.Equal(before.Verts[pathBefore.StrokeOffset + i].Y + dy, after.Verts[pathAfter.StrokeOffset + i].Y);
            }
        }
    }
}
