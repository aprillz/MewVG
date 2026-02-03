// OpenGL framebuffer utilities (ported from nanovg_gl_utils.h)

namespace Aprillz.MewVG;

public sealed class GLNVGFramebuffer : IDisposable
{
    private static int _defaultFbo = -1;

    private readonly NanoVGGL _ctx;
    private bool _disposed;

    public int Framebuffer { get; private set; }

    public int Renderbuffer { get; private set; }

    public int Texture { get; private set; }

    public int Image { get; private set; }

    private GLNVGFramebuffer(NanoVGGL ctx)
    {
        _ctx = ctx;
    }

    public static GLNVGFramebuffer? Create(NanoVGGL ctx, int width, int height, NVGimageFlags imageFlags)
    {
        GL.EnsureLoaded();
        var fb = new GLNVGFramebuffer(ctx);

        var defaultFbo = GL.GetInteger(GetPName.FramebufferBinding);
        var defaultRbo = GL.GetInteger(GetPName.RenderbufferBinding);

        fb.Image = ctx.CreateImageRGBA(width, height, imageFlags | NVGimageFlags.FlipY | NVGimageFlags.Premultiplied, ReadOnlySpan<byte>.Empty);
        fb.Texture = ctx.ImageHandle(fb.Image);

        fb.Framebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fb.Framebuffer);

        fb.Renderbuffer = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, fb.Renderbuffer);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.StencilIndex8, width, height);

        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, fb.Texture, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.StencilAttachment, RenderbufferTarget.Renderbuffer, fb.Renderbuffer);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width, height);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, fb.Texture, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, fb.Renderbuffer);

            status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, defaultFbo);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, defaultRbo);
                fb.Dispose();
                return null;
            }
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, defaultFbo);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, defaultRbo);
        return fb;
    }

    public void Bind()
    {
        GL.EnsureLoaded();
        if (_defaultFbo == -1)
        {
            _defaultFbo = GL.GetInteger(GetPName.FramebufferBinding);
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer != 0 ? Framebuffer : _defaultFbo);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Framebuffer != 0)
        {
            GL.DeleteFramebuffer(Framebuffer);
        }

        if (Renderbuffer != 0)
        {
            GL.DeleteRenderbuffer(Renderbuffer);
        }

        if (Image >= 0)
        {
            _ctx.DeleteImage(Image);
        }

        Framebuffer = 0;
        Renderbuffer = 0;
        Texture = 0;
        Image = -1;
    }
}