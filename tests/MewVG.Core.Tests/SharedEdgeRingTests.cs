using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// A ring whose hole shares three edges with its outer contour (a border drawn on one side only)
/// resolves to a single convex strip, and that strip keeps an opaque anti-aliasing fringe.
/// </summary>
public class SharedEdgeRingTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void TopOnlyRing_FringeKeepsFullCoverage(float thickness)
    {
        var renderer = new FakeRenderer();
        var context = new NVGContext(renderer);
        context.BeginFrame(200, 200, 1.0f);
        context.BeginPath();

        // Outer contour clockwise on screen, the hole counter-clockwise, sharing the left,
        // right and bottom edges.
        context.MoveTo(8, 8);
        context.LineTo(112, 8);
        context.LineTo(112, 72);
        context.LineTo(8, 72);
        context.ClosePath();
        context.MoveTo(8, 8 + thickness);
        context.LineTo(8, 72);
        context.LineTo(112, 72);
        context.LineTo(112, 8 + thickness);
        context.ClosePath();

        context.FillColor(MewVGColor.RGBA(200, 40, 60, 255));
        context.FillRule(MewVGFillRule.NonZero);
        context.Fill();

        var call = Assert.Single(renderer.FillCalls);
        var fringe = call.Paths.Where(path => path.NStroke > 0)
            .SelectMany(path => call.Verts.Skip(path.StrokeOffset).Take(path.NStroke))
            .ToArray();

        Assert.NotEmpty(fringe);

        // The fringe's inner edge carries full coverage (u = 0.5); a transparent fringe has
        // every vertex at the outer value.
        Assert.Contains(fringe, vertex => vertex.U >= 0.49f);
    }
}
