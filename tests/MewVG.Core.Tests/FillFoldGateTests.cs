using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// A fill is winding-resolved only where its boundary interacts with itself. Two edges that meet at an
/// ordinary corner do not, however long they are; a spike thinner than the fringe and a contour that
/// crosses itself do.
/// </summary>
public class FillFoldGateTests
{
    [Fact]
    public void ConcavePolygonWithOrdinaryCorners_IsNotResolved()
    {
        // An L: six long edges, one corner turning the other way.
        Assert.True(FillBodyIsDirect((10f, 10f), (110f, 10f), (110f, 50f), (50f, 50f), (50f, 110f), (10f, 110f)));
    }

    [Fact]
    public void StarWithWideTips_IsNotResolved()
    {
        Assert.True(FillBodyIsDirect(Star(outerRadius: 50f, innerRadius: 30f)));
    }

    [Fact]
    public void SpikeThinnerThanTheFringe_IsResolved()
    {
        // A square with a spike 60 long and 4 wide at its base: the two edges of the spike run within a
        // fringe of each other for a quarter of their length.
        Assert.False(FillBodyIsDirect((10f, 10f), (60f, 10f), (60f, 33f), (120f, 35f), (60f, 37f), (60f, 60f), (10f, 60f)));
    }

    [Fact]
    public void ContourCrossingItself_IsResolved()
    {
        Assert.False(FillBodyIsDirect((10f, 10f), (110f, 110f), (110f, 10f), (10f, 110f)));
    }

    [Fact]
    public void RegularPolygonWithManyCorners_IsConvex()
    {
        var points = new (float X, float Y)[24];
        for (var index = 0; index < points.Length; index++)
        {
            var angle = MathF.PI * 2f * index / points.Length;
            points[index] = (80f + (50f * MathF.Cos(angle)), 80f + (50f * MathF.Sin(angle)));
        }

        Assert.True(FillBodyIsDirect(points));
    }

    [Fact]
    public void PentagramGoingRoundTwice_IsNotConvex()
    {
        // Every corner turns the same way, but the edges go round twice.
        var points = new (float X, float Y)[5];
        for (var index = 0; index < points.Length; index++)
        {
            var angle = (MathF.PI * 4f * index / 5f) - (MathF.PI / 2f);
            points[index] = (80f + (50f * MathF.Cos(angle)), 80f + (50f * MathF.Sin(angle)));
        }

        Assert.False(FillBodyIsDirect(points));
    }

    private static (float X, float Y)[] Star(float outerRadius, float innerRadius)
    {
        var points = new (float X, float Y)[10];
        for (var index = 0; index < points.Length; index++)
        {
            var radius = (index & 1) == 0 ? outerRadius : innerRadius;
            var angle = (MathF.PI * index / 5f) - (MathF.PI / 2f);
            points[index] = (80f + (radius * MathF.Cos(angle)), 80f + (radius * MathF.Sin(angle)));
        }

        return points;
    }

    /// <summary>True when the fill body reached the renderer without winding resolution.</summary>
    private static bool FillBodyIsDirect(params (float X, float Y)[] contour)
    {
        var renderer = new FakeRenderer();
        var context = new NVGContext(renderer);
        context.BeginFrame(200, 200, 1.0f);
        context.BeginPath();
        context.MoveTo(contour[0].X, contour[0].Y);
        for (var index = 1; index < contour.Length; index++)
        {
            context.LineTo(contour[index].X, contour[index].Y);
        }

        context.ClosePath();
        context.FillColor(MewVGColor.RGBA(80, 141, 254, 255));
        context.Fill();

        var call = Assert.Single(renderer.FillCalls);
        foreach (var path in call.Paths)
        {
            if (path.NFill > 0)
            {
                return path.Convex;
            }
        }

        throw new Xunit.Sdk.XunitException("The fill has no body.");
    }
}
