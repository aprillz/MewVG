using System.Numerics;

using Aprillz.MewVG.Tess;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// A run whose input crosses the pool-drop threshold takes a different Clear() path than a
/// small one, so these pin the results either path produces.
/// </summary>
public class TessellatorPoolReuseTests
{
    // Enough tiles to push the input past Tessellator's pool-drop threshold.
    private const int LARGE_TILE_COUNT = 6000;

    private static Vector2[][] BuildTiles(int count, float pitch)
    {
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        var contours = new Vector2[count][];
        for (int i = 0; i < count; i++)
        {
            float x = (i % side) * pitch;
            float y = (i / side) * pitch;
            float size = pitch * 0.5f;
            contours[i] = new[]
            {
                new Vector2(x, y),
                new Vector2(x + size, y),
                new Vector2(x + size, y + size),
                new Vector2(x, y + size),
            };
        }

        return contours;
    }

    private static (Vector2[] Vertices, int[] Indices) Run(Tessellator tess, Vector2[][] contours)
    {
        tess.Clear();
        foreach (var contour in contours)
        {
            tess.AddContour(contour);
        }

        var result = tess.Tessellate(TessWindingRule.NonZero);
        Assert.Equal(TessStatus.Ok, result.Status);
        return (result.Vertices, result.Indices);
    }

    [Fact]
    public void LargeInputRepeatedOnSameInstance_ProducesIdenticalOutput()
    {
        var contours = BuildTiles(LARGE_TILE_COUNT, 10f);
        var tess = new Tessellator();

        var first = Run(tess, contours);
        var second = Run(tess, contours);
        var third = Run(tess, contours);

        Assert.Equal(first.Vertices, second.Vertices);
        Assert.Equal(first.Indices, second.Indices);
        Assert.Equal(first.Vertices, third.Vertices);
        Assert.Equal(first.Indices, third.Indices);
    }

    [Fact]
    public void SmallInputAfterLargeInput_MatchesFreshInstance()
    {
        var large = BuildTiles(LARGE_TILE_COUNT, 10f);
        var small = BuildTiles(4, 10f);

        var reused = new Tessellator();
        Run(reused, large);
        var afterLarge = Run(reused, small);

        var expected = Run(new Tessellator(), small);

        Assert.Equal(expected.Vertices, afterLarge.Vertices);
        Assert.Equal(expected.Indices, afterLarge.Indices);
    }
}
