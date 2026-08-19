using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

public class SharpConcaveCornerTests
{
    // Regression: a non-resolved fill (single simple contour, no interacting
    // boundaries) with a sharp concave corner reached the notch-pair probe,
    // which indexed empty probe-bounds spans and threw IndexOutOfRange.
    [Fact]
    public void SharpConcaveCorner_WithoutResolution_DoesNotThrow()
    {
        var renderer = new FakeRenderer();
        var context = new NVGContext(renderer, edgeAntiAlias: true);
        context.BeginFrame(220, 120, 1.0f);
        context.BeginPath();

        // Rectangles with a tiny sharp notch (interior angle ~25 degrees, sides
        // ~1.8px) cut into one edge: sharp enough for a bevel join, but short
        // enough that the interaction gate's fold detection stays silent, so
        // the fill is NOT winding-resolved. Both orientations, so the concave
        // turn matches either fringe direction.
        context.MoveTo(10f, 10f);
        context.LineTo(90f, 10f);
        context.LineTo(90f, 100f);
        context.LineTo(52.39f, 100f);
        context.LineTo(52f, 98.24f);
        context.LineTo(51.61f, 100f);
        context.LineTo(10f, 100f);
        context.ClosePath();

        context.MoveTo(110f, 10f);
        context.LineTo(110f, 100f);
        context.LineTo(151.61f, 100f);
        context.LineTo(152f, 98.24f);
        context.LineTo(152.39f, 100f);
        context.LineTo(190f, 100f);
        context.LineTo(190f, 10f);
        context.ClosePath();

        context.FillColor(NVGcolor.RGBA(80, 141, 254, 255));
        context.FillRule(NVGfillRule.NonZero);
        context.Fill();

        var call = Assert.Single(renderer.FillCalls);
        Assert.True(call.Paths.Length > 0);
    }
}
