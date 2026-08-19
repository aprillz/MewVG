using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// Regression coverage for plan Phase 1.3: NanoVG.Rect (public facade) must emit
/// the exact same point order/winding as the internal NVGContext.Rect it wraps.
/// </summary>
public class RectWindingTests
{
    [Fact]
    public void FacadeRect_MatchesInternalContextRect_FillVertices()
    {
        var internalRenderer = new FakeRenderer();
        var internalContext = new NVGContext(internalRenderer, edgeAntiAlias: true);
        internalContext.BeginFrame(200, 200, 1.0f);
        internalContext.BeginPath();
        internalContext.Rect(10, 20, 30, 40);
        internalContext.FillColor(NVGcolor.RGBA(255, 0, 0, 255));
        internalContext.Fill();

        var facadeRenderer = new FakeRenderer();
        using var facade = new TestNanoVG(facadeRenderer, edgeAntiAlias: true);
        facade.BeginFrame(200, 200, 1.0f);
        facade.BeginPath();
        facade.Rect(10, 20, 30, 40);
        facade.FillColor(255, 0, 0, 255);
        facade.Fill();

        Assert.Single(internalRenderer.FillCalls);
        Assert.Single(facadeRenderer.FillCalls);

        var internalCall = internalRenderer.FillCalls[0];
        var facadeCall = facadeRenderer.FillCalls[0];

        Assert.Equal(internalCall.Paths.Length, facadeCall.Paths.Length);
        Assert.Equal(internalCall.Verts.Length, facadeCall.Verts.Length);

        for (var i = 0; i < internalCall.Verts.Length; i++)
        {
            Assert.Equal(internalCall.Verts[i].X, facadeCall.Verts[i].X, 3);
            Assert.Equal(internalCall.Verts[i].Y, facadeCall.Verts[i].Y, 3);
            Assert.Equal(internalCall.Verts[i].U, facadeCall.Verts[i].U, 3);
            Assert.Equal(internalCall.Verts[i].V, facadeCall.Verts[i].V, 3);
        }
    }

    [Fact]
    public void OuterRectWithReversedInnerRect_NonZeroFill_CreatesHole()
    {
        var renderer = new FakeRenderer();
        var context = new NVGContext(renderer, edgeAntiAlias: true);
        context.BeginFrame(200, 200, 1.0f);
        context.BeginPath();

        // Outer rect via the public facade order: (x,y) -> (x,y+h) -> (x+w,y+h) -> (x+w,y).
        context.Rect(0, 0, 100, 100);

        // Inner rect wound in mirror order relative to Rect(): (x,y) -> (x+w,y) -> (x+w,y+h) -> (x,y+h).
        // Opposite geometric orientation from the outer contour, so NonZero fill treats it as a hole.
        context.MoveTo(25, 25);
        context.LineTo(75, 25);
        context.LineTo(75, 75);
        context.LineTo(25, 75);
        context.ClosePath();

        context.FillColor(NVGcolor.RGBA(0, 128, 255, 255));
        context.FillRule(NVGfillRule.NonZero);
        context.Fill();

        Assert.Single(renderer.FillCalls);
        var call = renderer.FillCalls[0];

        // Center of the inner rect must be uncovered (the hole)...
        Assert.False(GeometryTestHelpers.AnyFillTriangleContains(call.Paths, call.Verts, 50, 50));

        // ...while the band between inner and outer rects is covered.
        Assert.True(GeometryTestHelpers.AnyFillTriangleContains(call.Paths, call.Verts, 10, 10));
        Assert.True(GeometryTestHelpers.AnyFillTriangleContains(call.Paths, call.Verts, 90, 90));
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    public void RoundedBorderWithZeroTop_DoesNotPutOpaqueBodyInTheTaper(float devicePixelRatio)
    {
        var renderer = new FakeRenderer();
        var context = new NVGContext(renderer, edgeAntiAlias: true);
        context.BeginFrame(220, 120, devicePixelRatio);
        context.BeginPath();

        AppendRoundedRect(context, 0, 0, 190, 100, 10, 10, 10, 10, clockwise: true);
        // Thickness (3, 0, 2, 1): the inner and outer top arcs meet at (180, 0).
        AppendRoundedRect(context, 3, 0, 185, 99, 7, 10, 8, 9, clockwise: false);

        context.FillColor(NVGcolor.RGBA(80, 141, 254, 255));
        context.FillRule(NVGfillRule.NonZero);
        context.Fill();

        var call = Assert.Single(renderer.FillCalls);
        Assert.False(GeometryTestHelpers.AnyFillTriangleContains(call.Paths, call.Verts, 100, 50));
        Assert.InRange(
            GeometryTestHelpers.MaxFillCoverageAtPoint(call.Paths, call.Verts, 181.5f, 0.5f),
            0f,
            0.75f);

        var taperVertices = call.Paths
            .Where(static path => path.NStroke > 0)
            .SelectMany(path => call.Verts.Skip(path.StrokeOffset).Take(path.NStroke))
            .Where(static vertex => vertex.X is >= 180 and <= 185 && vertex.Y is >= -0.5f and <= 2f)
            .ToArray();
        Assert.NotEmpty(taperVertices);
        Assert.DoesNotContain(taperVertices, static vertex => vertex.U >= 0.499f);
    }

    private static void AppendRoundedRect(
        NVGContext context,
        float x,
        float y,
        float width,
        float height,
        float leftRadius,
        float topRadius,
        float rightRadius,
        float bottomRadius,
        bool clockwise)
    {
        const float k = 0.55228475f;
        var right = x + width;
        var bottom = y + height;
        if (clockwise)
        {
            context.MoveTo(x + leftRadius, y);
            context.LineTo(right - rightRadius, y);
            context.BezierTo(right - rightRadius + rightRadius * k, y, right, y + topRadius - topRadius * k, right, y + topRadius);
            context.LineTo(right, bottom - bottomRadius);
            context.BezierTo(right, bottom - bottomRadius + bottomRadius * k, right - rightRadius + rightRadius * k, bottom, right - rightRadius, bottom);
            context.LineTo(x + leftRadius, bottom);
            context.BezierTo(x + leftRadius - leftRadius * k, bottom, x, bottom - bottomRadius + bottomRadius * k, x, bottom - bottomRadius);
            context.LineTo(x, y + topRadius);
            context.BezierTo(x, y + topRadius - topRadius * k, x + leftRadius - leftRadius * k, y, x + leftRadius, y);
        }
        else
        {
            context.MoveTo(x + leftRadius, y);
            context.BezierTo(x + leftRadius - leftRadius * k, y, x, y + topRadius - topRadius * k, x, y + topRadius);
            context.LineTo(x, bottom - bottomRadius);
            context.BezierTo(x, bottom - bottomRadius + bottomRadius * k, x + leftRadius - leftRadius * k, bottom, x + leftRadius, bottom);
            context.LineTo(right - rightRadius, bottom);
            context.BezierTo(right - rightRadius + rightRadius * k, bottom, right, bottom - bottomRadius + bottomRadius * k, right, bottom - bottomRadius);
            context.LineTo(right, y + topRadius);
            context.BezierTo(right, y + topRadius - topRadius * k, right - rightRadius + rightRadius * k, y, right - rightRadius, y);
            context.LineTo(x + leftRadius, y);
        }
        context.ClosePath();
    }
}
