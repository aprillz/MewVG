using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// A path that is exactly one rounded rectangle, axis-aligned under a uniform scale with circular corners and a
/// solid paint, goes to the renderer whole; anything else is expanded as a path.
/// </summary>
public sealed class RoundedRectRecognitionTests
{
    [Fact]
    public void LoneRoundedRect_GoesWholeInFrameUnits()
    {
        var renderer = new FakeRenderer { AcceptsRoundedRects = true };
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);
            vg.Translate(10, 20);
            vg.Scale(2, 2);
            vg.BeginPath();
            vg.RoundedRectVarying(5, 6, 40, 30, 4, 5, 6, 7);
            vg.FillColor(0, 0, 0);
            vg.Fill();
            vg.EndFrame();
        }

        Assert.Empty(renderer.FillCalls);
        var shape = Assert.Single(renderer.RoundedRectCalls);
        Assert.Equal(new NVGRoundedRect(10 + (25 * 2), 20 + (21 * 2), 40, 30, 8, 10, 12, 14, -1f), shape);
    }

    [Fact]
    public void Outline_CarriesHalfTheStrokeWidth()
    {
        var renderer = new FakeRenderer { AcceptsRoundedRects = true };
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);
            vg.BeginPath();
            vg.RoundedRect(10, 10, 48, 32, 8);
            vg.StrokeColor(0, 0, 0);
            vg.StrokeWidth(3);
            vg.Stroke();
            vg.EndFrame();
        }

        Assert.Empty(renderer.StrokeCalls);
        Assert.Equal(1.5f, Assert.Single(renderer.RoundedRectCalls).StrokeHalfWidth);
    }

    [Fact]
    public void MirroredTransform_SwapsTheCornersItMirrors()
    {
        var renderer = new FakeRenderer { AcceptsRoundedRects = true };
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);
            vg.Translate(300, 0);
            vg.Scale(-1, 1);
            vg.BeginPath();
            vg.RoundedRectVarying(10, 10, 40, 30, 1, 2, 3, 4);
            vg.FillColor(0, 0, 0);
            vg.Fill();
            vg.EndFrame();
        }

        var shape = Assert.Single(renderer.RoundedRectCalls);
        Assert.Equal((2f, 1f, 4f, 3f), (shape.TopLeft, shape.TopRight, shape.BottomRight, shape.BottomLeft));
    }

    [Fact]
    public void ShapesTheRendererCannotDrawWhole_AreExpanded()
    {
        var renderer = new FakeRenderer { AcceptsRoundedRects = true };
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);

            // Turned.
            vg.Save();
            vg.Rotate(0.3f);
            FillRounded(vg, 8);
            vg.Restore();

            // Stretched unevenly, which makes the corners elliptical.
            vg.Save();
            vg.Scale(2, 1);
            FillRounded(vg, 8);
            vg.Restore();

            // A radius past half the shorter side.
            FillRounded(vg, 20);

            // More than the rectangle in the path.
            vg.BeginPath();
            vg.RoundedRect(10, 10, 48, 32, 8);
            vg.Rect(100, 100, 10, 10);
            vg.FillColor(0, 0, 0);
            vg.Fill();

            // A gradient paint.
            vg.BeginPath();
            vg.RoundedRect(10, 10, 48, 32, 8);
            vg.FillPaint(vg.LinearGradient(10, 10, 58, 10, MewVGColor.RGBA(0, 0, 0, 255), MewVGColor.RGBA(255, 255, 255, 255)));
            vg.Fill();

            // A square corner outlined with round joins.
            vg.BeginPath();
            vg.RoundedRectVarying(10, 10, 48, 32, 8, 0, 8, 8);
            vg.LineJoin(MewVGLineJoin.Round);
            vg.StrokeColor(0, 0, 0);
            vg.Stroke();
            vg.EndFrame();
        }

        Assert.Empty(renderer.RoundedRectCalls);
        Assert.Equal(5, renderer.FillCalls.Count);
        Assert.Single(renderer.StrokeCalls);
    }

    [Fact]
    public void SquareCornerOutlinedWithMiters_GoesWhole()
    {
        var renderer = new FakeRenderer { AcceptsRoundedRects = true };
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);
            vg.BeginPath();
            vg.RoundedRectVarying(10, 10, 48, 32, 8, 0, 8, 8);
            vg.LineJoin(MewVGLineJoin.Miter);
            vg.StrokeColor(0, 0, 0);
            vg.Stroke();
            vg.EndFrame();
        }

        Assert.Equal(0f, Assert.Single(renderer.RoundedRectCalls).TopRight);
    }

    [Fact]
    public void RendererDecliningTheShape_GetsThePath()
    {
        var renderer = new FakeRenderer();
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);
            FillRounded(vg, 8);
            vg.EndFrame();
        }

        Assert.Single(renderer.FillCalls);
    }

    private static void FillRounded(TestMewVGContext vg, float radius)
    {
        vg.BeginPath();
        vg.RoundedRect(10, 10, 48, 32, radius);
        vg.FillColor(0, 0, 0);
        vg.Fill();
    }
}
