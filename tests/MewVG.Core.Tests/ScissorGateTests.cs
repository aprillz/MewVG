using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// A draw takes a program without the scissor only when the scissor leaves every device pixel the draw can
/// cover at full strength: a scissor with a soft edge is at full strength half a fringe inside it, one with no
/// fringe (a shape drawn without antialiasing) up to its edge.
/// </summary>
public class ScissorGateTests
{
    private const float VIEW_WIDTH = 1024f;
    private const float VIEW_HEIGHT = 600f;

    [Fact]
    public void ViewSizedScissor_LeavesAShapeWithoutFringeWhole()
    {
        var scissor = Scissor(0f, 0f, VIEW_WIDTH, VIEW_HEIGHT);

        Assert.False(Cuts(ref scissor, 100f, 100f, 124f, 124f, fringe: 0f));
        Assert.False(Cuts(ref scissor, 0f, 0f, VIEW_WIDTH, VIEW_HEIGHT, fringe: 0f));
    }

    [Fact]
    public void ViewSizedScissor_LeavesAnInteriorShapeWithFringeWhole()
    {
        var scissor = Scissor(0f, 0f, VIEW_WIDTH, VIEW_HEIGHT);

        Assert.False(Cuts(ref scissor, 98f, 98f, 126f, 126f, fringe: 1f));
    }

    [Fact]
    public void SoftEdge_CutsTheOutermostPixelsInsideIt()
    {
        // With a fringe the scissor fades across its edge, so the pixel just inside it is not at full strength.
        var scissor = Scissor(0f, 0f, VIEW_WIDTH, VIEW_HEIGHT);

        Assert.True(Cuts(ref scissor, 0f, 100f, 24f, 124f, fringe: 1f));
    }

    [Fact]
    public void ShapeCrossingTheScissor_IsCut()
    {
        var scissor = Scissor(200f, 200f, 100f, 100f);

        Assert.True(Cuts(ref scissor, 180f, 220f, 230f, 240f, fringe: 0f));
        Assert.True(Cuts(ref scissor, 220f, 290f, 240f, 320f, fringe: 1f));
        Assert.False(Cuts(ref scissor, 220f, 220f, 240f, 240f, fringe: 0f));
    }

    [Fact]
    public void HardEdgeOnAPixelCentre_IsCut()
    {
        // The scissor's right edge falls on the centre of pixel 250, where a hard edge is neither in nor out.
        var scissor = Scissor(200f, 200f, 50.5f, 100f);

        Assert.True(Cuts(ref scissor, 240f, 220f, 251f, 240f, fringe: 0f));
        Assert.False(Cuts(ref scissor, 240f, 220f, 250f, 240f, fringe: 0f));
    }

    [Fact]
    public void FractionalPixelRatio_MeasuresDevicePixels()
    {
        // At 125% the view-sized scissor still holds every device pixel, and a scissor edge at 80.2 view units
        // (100.25 device pixels) cuts the device pixel centred at 100.5.
        var view = Scissor(0f, 0f, VIEW_WIDTH, VIEW_HEIGHT);
        Assert.False(Cuts(ref view, 100f, 100f, 124f, 124f, fringe: 0f, pixelRatio: 1.25f));

        var narrow = Scissor(0f, 0f, 80.2f, VIEW_HEIGHT);
        Assert.True(Cuts(ref narrow, 70f, 100f, 81f, 124f, fringe: 0f, pixelRatio: 1.25f));
        Assert.False(Cuts(ref narrow, 70f, 100f, 80f, 124f, fringe: 0f, pixelRatio: 1.25f));
    }

    [Fact]
    public void RotatedScissor_IsAlwaysKept()
    {
        var scissor = Scissor(0f, 0f, VIEW_WIDTH, VIEW_HEIGHT);
        scissor.Xform[1] = 0.1f;

        Assert.True(Cuts(ref scissor, 100f, 100f, 124f, 124f, fringe: 0f));
    }

    [Fact]
    public void NoScissor_NeverCuts()
    {
        var scissor = new NVGscissorState();

        Assert.False(Cuts(ref scissor, -10f, -10f, VIEW_WIDTH + 10f, VIEW_HEIGHT + 10f, fringe: 1f));
    }

    private static bool Cuts(ref NVGscissorState scissor, float minX, float minY, float maxX, float maxY, float fringe, float pixelRatio = 1f)
        => NVGScissorCoverage.Cuts(ref scissor, minX, minY, maxX, maxY, fringe, VIEW_WIDTH, VIEW_HEIGHT, pixelRatio);

    private static NVGscissorState Scissor(float x, float y, float width, float height)
    {
        var scissor = new NVGscissorState();
        scissor.Xform[0] = 1f;
        scissor.Xform[3] = 1f;
        scissor.Xform[4] = x + (width * 0.5f);
        scissor.Xform[5] = y + (height * 0.5f);
        scissor.Extent[0] = width * 0.5f;
        scissor.Extent[1] = height * 0.5f;
        return scissor;
    }
}
