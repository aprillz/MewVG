// Aprillz NanoVG .NET - Public API
// Metal NanoVG .NET by Olli Wang
// Original: https://github.com/niclasolofsson/MetalNanoVG

using System.Runtime.InteropServices;

using Aprillz.MewVG.Interop;

namespace Aprillz.MewVG;

/// <summary>
/// Main NanoVG API for Metal rendering
/// </summary>
public sealed class NanoVGMetal : NanoVG
{
    private readonly MNVGcontext _context;

    /// <summary>
    /// Creates a new NanoVG context with Metal backend
    /// </summary>
    /// <param name="device">Metal device (id&lt;MTLDevice&gt;)</param>
    /// <param name="flags">Creation flags</param>
    public NanoVGMetal(IntPtr device, NVGcreateFlags flags = NVGcreateFlags.Antialias | NVGcreateFlags.StencilStrokes)
        : base(CreateRenderer(device, flags, out var context), (flags & NVGcreateFlags.Antialias) != 0)
    {
        _context = context;
    }

    /// <summary>
    /// Gets the underlying Metal context
    /// </summary>
    public MNVGcontext Context => _context;

    /// <summary>
    /// Gets or sets the pixel format for rendering
    /// </summary>
    public MTLPixelFormat PixelFormat
    {
        get => _context.PixelFormat;
        set => _context.PixelFormat = value;
    }

    /// <summary>
    /// Gets or sets the stencil format
    /// </summary>
    public MTLPixelFormat StencilFormat
    {
        get => _context.StencilFormat;
        set => _context.StencilFormat = value;
    }

    #region Frame Management

    /// <summary>
    /// Sets the render encoder for Metal command encoding
    /// </summary>
    /// <param name="renderEncoder">Metal render command encoder</param>
    /// <param name="commandBuffer">Metal command buffer</param>
    public void SetRenderEncoder(IntPtr renderEncoder, IntPtr commandBuffer) => _context.SetRenderEncoder(renderEncoder, commandBuffer);

    /// <summary>
    /// Signals that the GPU has completed rendering the frame
    /// Call this in your command buffer completion handler
    /// </summary>
    public void FrameCompleted() => _context.FrameCompleted();

    #endregion

    #region Images

    /// <summary>
    /// Creates an image from RGBA data
    /// </summary>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="imageFlags">Image flags</param>
    /// <param name="data">RGBA pixel data</param>
    /// <returns>Image handle, or 0 on failure</returns>
    public override int CreateImageRGBA(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data) => _context.CreateTexture((int)NVGtexture.RGBA, width, height, (int)imageFlags, data);

    /// <summary>
    /// Creates an image from alpha data
    /// </summary>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="imageFlags">Image flags</param>
    /// <param name="data">Alpha pixel data</param>
    /// <returns>Image handle, or 0 on failure</returns>
    public override int CreateImageAlpha(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data) => _context.CreateTexture((int)NVGtexture.Alpha, width, height, (int)imageFlags, data);

    /// <summary>
    /// Updates image data
    /// </summary>
    public override bool UpdateImage(int image, ReadOnlySpan<byte> data)
    {
        if (!_context.GetTextureSize(image, out var width, out var height))
        {
            return false;
        }

        return _context.UpdateTexture(image, 0, 0, width, height, data);
    }

    /// <summary>
    /// Gets image size
    /// </summary>
    public override bool ImageSize(int image, out int width, out int height) => _context.GetTextureSize(image, out width, out height);

    /// <summary>
    /// Deletes an image
    /// </summary>
    public override void DeleteImage(int image) => _context.DeleteTexture(image);

    public override int CreateImageFromHandle(int textureId, int width, int height, NVGimageFlags flags)
        => throw new NotSupportedException("Metal backend does not support creating images from external texture handles.");

    public override int ImageHandle(int image)
        => throw new NotSupportedException("Metal backend does not expose texture handles.");

    #endregion

    #region Utility Functions

    /// <summary>
    /// Converts degrees to radians
    /// </summary>
    public static float DegToRad(float deg) => deg * MathF.PI / 180.0f;

    /// <summary>
    /// Converts radians to degrees
    /// </summary>
    public static float RadToDeg(float rad) => rad * 180.0f / MathF.PI;

    /// <summary>
    /// Creates a color from HSL values
    /// </summary>
    /// <param name="h">Hue (0-1)</param>
    /// <param name="s">Saturation (0-1)</param>
    /// <param name="l">Lightness (0-1)</param>
    public static NVGcolor HSL(float h, float s, float l) => NVGcolor.HSL(h, s, l);

    /// <summary>
    /// Creates a color from HSLA values
    /// </summary>
    /// <param name="h">Hue (0-1)</param>
    /// <param name="s">Saturation (0-1)</param>
    /// <param name="l">Lightness (0-1)</param>
    /// <param name="a">Alpha (0-1)</param>
    public static NVGcolor HSLA(float h, float s, float l, float a)
    {
        a = Math.Clamp(a, 0.0f, 1.0f);
        return NVGcolor.HSLA(h, s, l, (byte)(a * 255.0f));
    }

    /// <summary>
    /// Creates a color from RGB values (0-255)
    /// </summary>
    public static NVGcolor RGB(byte r, byte g, byte b) => NVGcolor.RGB(r, g, b);

    /// <summary>
    /// Creates a color from RGBA values (0-255)
    /// </summary>
    public static NVGcolor RGBA(byte r, byte g, byte b, byte a) => NVGcolor.RGBA(r, g, b, a);

    /// <summary>
    /// Creates a color from RGB float values (0.0-1.0)
    /// </summary>
    public static NVGcolor RGBf(float r, float g, float b) => NVGcolor.RGBf(r, g, b);

    /// <summary>
    /// Creates a color from RGBA float values (0.0-1.0)
    /// </summary>
    public static NVGcolor RGBAf(float r, float g, float b, float a) => NVGcolor.RGBAf(r, g, b, a);

    /// <summary>
    /// Linearly interpolates between two colors
    /// </summary>
    public static NVGcolor LerpRGBA(NVGcolor c0, NVGcolor c1, float u) => NVGcolor.Lerp(c0, c1, u);

    #endregion

    protected override void DisposeBackend() => _context.Dispose();

    private static MNVGcontext CreateRenderer(IntPtr device, NVGcreateFlags flags, out MNVGcontext context)
    {
        context = new MNVGcontext(device, flags);
        return context;
    }
}

/// <summary>
/// Extension methods for Metal device creation
/// </summary>
public static partial class MetalDevice
{
    [LibraryImport("/System/Library/Frameworks/Metal.framework/Metal", EntryPoint = "MTLCreateSystemDefaultDevice")]
    private static partial IntPtr MTLCreateSystemDefaultDevice();

    /// <summary>
    /// Creates the system default Metal device
    /// </summary>
    public static IntPtr CreateSystemDefaultDevice() => MTLCreateSystemDefaultDevice();
}
