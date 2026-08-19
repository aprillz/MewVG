using System.Linq;

using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// Reproduction coverage for plan Phase 1.1 (NOT fixed): Fill() followed by Stroke()
/// on the same path should behave identically to calling Stroke() alone (this is the
/// standard NanoVG "fill then outline" pattern). ExpandFill rewrites the path cache
/// in place, so the subsequent Stroke() reuses a corrupted cache instead of the
/// original flattened contour.
///
/// These tests assert the CORRECT (fixed) behavior and are expected to FAIL against
/// the current implementation. Do not "fix" by weakening the assertions - the goal
/// is to pin down and report the observed divergence.
/// </summary>
public class FillThenStrokeReproTests
{
    private static int TotalStrokeVerts(FakeRenderer.StrokeCallData call) => call.Paths.Sum(p => p.NStroke);

    [Fact]
    public void CaseA_AntialiasOff_ClosedRect_StrokeAfterFillShouldMatchStrokeAlone()
    {
        // Baseline: Stroke() alone, AA off, closed rect.
        var baselineRenderer = new FakeRenderer();
        var baselineContext = new NVGContext(baselineRenderer, edgeAntiAlias: false);
        baselineContext.BeginFrame(200, 200, 1.0f);
        baselineContext.BeginPath();
        baselineContext.Rect(10, 10, 50, 50);
        baselineContext.StrokeColor(NVGcolor.RGBA(0, 0, 0, 255));
        baselineContext.StrokeWidth(4);
        baselineContext.Stroke();

        Assert.Single(baselineRenderer.StrokeCalls);
        var baselineStrokeVerts = TotalStrokeVerts(baselineRenderer.StrokeCalls[0]);
        Assert.True(baselineStrokeVerts > 0, "sanity check: stroke-alone must emit geometry");

        // Repro: Fill() then Stroke() on the same closed rect, AA off.
        var renderer = new FakeRenderer();
        var context = new NVGContext(renderer, edgeAntiAlias: false);
        context.BeginFrame(200, 200, 1.0f);
        context.BeginPath();
        context.Rect(10, 10, 50, 50);
        context.FillColor(NVGcolor.RGBA(255, 0, 0, 255));
        context.Fill();
        context.StrokeColor(NVGcolor.RGBA(0, 0, 0, 255));
        context.StrokeWidth(4);
        context.Stroke();

        Assert.Single(renderer.StrokeCalls);
        var strokeAfterFillVerts = TotalStrokeVerts(renderer.StrokeCalls[0]);

        // Expected (correct) behavior: identical stroke geometry regardless of a
        // preceding Fill(). Currently fails - see class doc comment.
        Assert.Equal(baselineStrokeVerts, strokeAfterFillVerts);
    }

    [Fact]
    public void CaseB_AntialiasOn_OpenPolyline_StrokeAfterFillShouldMatchStrokeAlone()
    {
        // Baseline: Stroke() alone, AA on, open 2-segment polyline (no ClosePath).
        var baselineRenderer = new FakeRenderer();
        var baselineContext = new NVGContext(baselineRenderer, edgeAntiAlias: true);
        baselineContext.BeginFrame(200, 200, 1.0f);
        baselineContext.BeginPath();
        baselineContext.MoveTo(10, 10);
        baselineContext.LineTo(60, 10);
        baselineContext.LineTo(60, 60);
        baselineContext.StrokeColor(NVGcolor.RGBA(0, 0, 0, 255));
        baselineContext.StrokeWidth(4);
        baselineContext.Stroke();

        Assert.Single(baselineRenderer.StrokeCalls);
        var baselineCall = baselineRenderer.StrokeCalls[0];
        var baselineClosedFlags = baselineCall.Paths.Select(p => p.Closed).ToArray();
        var baselineStrokeVerts = TotalStrokeVerts(baselineCall);

        // Repro: Fill() then Stroke() on the same open polyline, AA on.
        var renderer = new FakeRenderer();
        var context = new NVGContext(renderer, edgeAntiAlias: true);
        context.BeginFrame(200, 200, 1.0f);
        context.BeginPath();
        context.MoveTo(10, 10);
        context.LineTo(60, 10);
        context.LineTo(60, 60);
        context.FillColor(NVGcolor.RGBA(255, 0, 0, 255));
        context.Fill();
        context.StrokeColor(NVGcolor.RGBA(0, 0, 0, 255));
        context.StrokeWidth(4);
        context.Stroke();

        Assert.Single(renderer.StrokeCalls);
        var call = renderer.StrokeCalls[0];
        var closedFlags = call.Paths.Select(p => p.Closed).ToArray();
        var strokeVerts = TotalStrokeVerts(call);

        // Expected (correct) behavior: the open polyline keeps Closed=false and the
        // same stroke vertex count whether or not Fill() ran first. Currently fails -
        // ExpandFill's fastSingleConvex path forces Closed=true on the cached contour
        // (to close the fill fan), and Stroke() reuses that corrupted cache. See class
        // doc comment.
        Assert.Equal(baselineClosedFlags, closedFlags);
        Assert.Equal(baselineStrokeVerts, strokeVerts);
    }
}
