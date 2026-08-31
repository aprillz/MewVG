// NanoVG OpenGL API wrapper

namespace Aprillz.MewVG;

/// <summary>
/// Main NanoVG API for OpenGL rendering. The GL API surface (desktop GL3 core or GLES3/WebGL2)
/// is selected once per process via <see cref="Initialize"/>.
/// </summary>
public sealed class NanoVGGL : NanoVG
{
    private readonly GLNVGContext _gl;

    public static void Initialize(Func<string, nint> getProcAddress, NanoVGGLProfile profile = NanoVGGLProfile.Gl3Core)
        => GL.Initialize(getProcAddress, profile);

    public NanoVGGL()
        : base(CreateRenderer(out var gl))
    {
        _gl = gl;
    }

    #region Images

    public override int CreateImageRGBA(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data)
        => _gl.CreateTexture(NVGtextureType.RGBA, width, height, imageFlags, data);

    public override int CreateImageBGRA(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data)
    {
        // Without native BGRA upload the base implementation swaps to RGBA on the CPU.
        if (GL.SupportsBgraUpload)
        {
            return _gl.CreateTexture(NVGtextureType.BGRA, width, height, imageFlags, data);
        }
        else
        {
            return base.CreateImageBGRA(width, height, imageFlags, data);
        }
    }

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

    public override bool UpdateImageBGRA(int image, ReadOnlySpan<byte> data)
    {
        // GL UpdateTexture dispatches on the stored NVGtextureType set at creation. With native
        // BGRA upload the image was created BGRA so the raw bytes pass through; otherwise it was
        // created RGBA (see CreateImageBGRA) and the base implementation swaps on the CPU first.
        if (GL.SupportsBgraUpload)
        {
            return UpdateImage(image, data);
        }
        else
        {
            return base.UpdateImageBGRA(image, data);
        }
    }

    public override bool ImageSize(int image, out int width, out int height) => _gl.GetTextureSize(image, out width, out height);

    public override void DeleteImage(int image) => _gl.DeleteTexture(image);

    public override int CreateImageFromHandle(int textureId, int width, int height, NVGimageFlags flags)
        => _gl.CreateImageFromHandle(textureId, width, height, flags);

    public override int ImageHandle(int image) => _gl.GetImageHandle(image);

    #endregion

    protected override void DisposeBackend() => _gl.Dispose();

    private static GLNVGContext CreateRenderer(out GLNVGContext gl)
    {
        // Creating the GL backend requires that:
        // 1) NanoVGGL.Initialize(...) has already been called, and
        // 2) a current OpenGL context exists on the calling thread.
        // Without these, GLNative's loaded function pointers are unset and calls will crash.
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

        gl = new GLNVGContext();
        return gl;
    }
}