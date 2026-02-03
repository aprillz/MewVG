// NanoVG OpenGL API wrapper

namespace Aprillz.MewVG;

/// <summary>
/// Main NanoVG API for OpenGL rendering (GL3 core profile)
/// </summary>
public sealed class NanoVGGL : NanoVG
{
    private readonly GLNVGContext _gl;

    public static void Initialize(Func<string, nint> getProcAddress) => GL.Initialize(getProcAddress);

    public NanoVGGL(NVGcreateFlags flags = NVGcreateFlags.NVG_ANTIALIAS | NVGcreateFlags.NVG_STENCIL_STROKES)
        : base(CreateRenderer(flags, out var gl), (flags & NVGcreateFlags.NVG_ANTIALIAS) != 0)
    {
        _gl = gl;
    }

    #region Images

    public override int CreateImageRGBA(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data)
        => _gl.CreateTexture(NVGtextureType.RGBA, width, height, imageFlags, data);

    public override int CreateImageAlpha(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data)
        => _gl.CreateTexture(NVGtextureType.Alpha, width, height, imageFlags, data);

    public override bool UpdateImage(int image, ReadOnlySpan<byte> data)
    {
        if (!_gl.GetTextureSize(image, out var width, out var height))
        {
            return false;
        }

        return _gl.UpdateTexture(image, 0, 0, width, height, data);
    }

    public override bool ImageSize(int image, out int width, out int height) => _gl.GetTextureSize(image, out width, out height);

    public override void DeleteImage(int image) => _gl.DeleteTexture(image);

    public override int CreateImageFromHandle(int textureId, int width, int height, NVGimageFlags flags)
        => _gl.CreateImageFromHandle(textureId, width, height, flags);

    public override int ImageHandle(int image) => _gl.GetImageHandle(image);

    #endregion

    protected override void DisposeBackend() => _gl.Dispose();

    private static GLNVGContext CreateRenderer(NVGcreateFlags flags, out GLNVGContext gl)
    {
        // Creating the GL backend requires that:
        // 1) NanoVGGL.Initialize(...) has already been called, and
        // 2) a current OpenGL context exists on the calling thread.
        // Without these, Silk.NET will throw (often surfacing as 0xe0434352).
        try
        {
            GL.EnsureLoaded();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "OpenGL is not ready. Call `NanoVGGL.Initialize(...)` after creating a window and making its GL context current (e.g., after `glfw.MakeContextCurrent(window)`).",
                ex);
        }

        gl = new GLNVGContext(flags);
        return gl;
    }
}