using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// Image quads go to the renderer as one triangle list in frame units, with texcoords normalised to the image and
/// the colour carrying the global alpha.
/// </summary>
public sealed class ImageQuadTests
{
    [Fact]
    public void Quads_BecomeOneTriangleListUnderTheTransform()
    {
        var renderer = new FakeRenderer();
        using (var vg = new TestMewVGContext(renderer) { ImageSizeForTests = (64, 32) })
        {
            vg.BeginFrame(600, 600, 1f);
            vg.Translate(10, 20);
            vg.Scale(2, 2);
            vg.GlobalAlpha(0.5f);
            vg.FillImageQuads(7,
            [
                new MewVGImageQuad(1, 2, 3, 4, 16, 8, 3, 4),
                new MewVGImageQuad(5, 6, 1, 1, 0, 0, 64, 32),
            ], MewVGColor.RGBAf(1, 0, 0, 0.8f));
            vg.EndFrame();
        }

        var call = Assert.Single(renderer.TrianglesCalls);
        Assert.Equal(7, call.Image);
        Assert.Equal(0.4f, call.Color.A, 5);
        Assert.Equal(12, call.Verts.Length);

        // The first quad's corners: (1,2)-(4,6) in user space, (12,24)-(18,32) in frame units.
        Assert.Equal(new NVGvertex(12, 24, 16f / 64, 8f / 32), call.Verts[0]);
        Assert.Equal(new NVGvertex(18, 24, 19f / 64, 8f / 32), call.Verts[1]);
        Assert.Equal(new NVGvertex(18, 32, 19f / 64, 12f / 32), call.Verts[2]);
        Assert.Equal(new NVGvertex(12, 32, 16f / 64, 12f / 32), call.Verts[5]);

        // The second quad samples the whole image.
        Assert.Equal(new NVGvertex(20, 32, 0, 0), call.Verts[6]);
        Assert.Equal(new NVGvertex(22, 34, 1, 1), call.Verts[8]);
    }

    [Fact]
    public void UnknownImage_DrawsNothing()
    {
        var renderer = new FakeRenderer();
        using (var vg = new TestMewVGContext(renderer))
        {
            vg.BeginFrame(600, 600, 1f);
            vg.FillImageQuads(7, [new MewVGImageQuad(1, 2, 3, 4, 0, 0, 3, 4)], MewVGColor.RGBAf(1, 1, 1, 1));
            vg.EndFrame();
        }

        Assert.Empty(renderer.TrianglesCalls);
    }
}
