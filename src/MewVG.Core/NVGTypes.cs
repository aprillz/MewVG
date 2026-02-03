// Copyright (c) 2017 Ollix (Original)
// Copyright (c) 2024 .NET Port
// MIT License

using System.Numerics;
using System.Runtime.InteropServices;

namespace Aprillz.MewVG;

/// <summary>
/// Create flags for NanoVG context.
/// </summary>
[Flags]
public enum NVGcreateFlags
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// Flag indicating if geometry based anti-aliasing is used (may not be needed when using MSAA).
    /// </summary>
    Antialias = 1 << 0,

    // Legacy alias
    NVG_ANTIALIAS = Antialias,

    /// <summary>
    /// Flag indicating if strokes should be drawn using stencil buffer.
    /// The rendering will be a little slower, but path overlaps (i.e. self-intersecting or sharp turns) will be drawn just once.
    /// </summary>
    StencilStrokes = 1 << 1,

    // Legacy alias
    NVG_STENCIL_STROKES = StencilStrokes,

    /// <summary>
    /// Flag indicating that additional debug checks are done.
    /// </summary>
    Debug = 1 << 2,

    /// <summary>
    /// Flag indicating if double buffering scheme is used.
    /// </summary>
    DoubleBuffer = 1 << 12,

    /// <summary>
    /// Flag indicating if triple buffering scheme is used.
    /// </summary>
    TripleBuffer = 1 << 13,
}

/// <summary>
/// Image flags for NanoVG.
/// </summary>
[Flags]
public enum NVGimageFlags
{
    /// <summary>
    /// No flags.
    /// </summary>
    None = 0,

    /// <summary>
    /// Generate mipmaps during creation of the image.
    /// </summary>
    GenerateMipmaps = 1 << 0,

    // Legacy alias
    NVG_IMAGE_GENERATE_MIPMAPS = GenerateMipmaps,

    /// <summary>
    /// Repeat image in X direction.
    /// </summary>
    RepeatX = 1 << 1,

    // Legacy alias
    NVG_IMAGE_REPEATX = RepeatX,

    /// <summary>
    /// Repeat image in Y direction.
    /// </summary>
    RepeatY = 1 << 2,

    // Legacy alias
    NVG_IMAGE_REPEATY = RepeatY,

    /// <summary>
    /// Flip the image vertically.
    /// </summary>
    FlipY = 1 << 3,

    // Legacy alias
    NVG_IMAGE_FLIPY = FlipY,

    /// <summary>
    /// Image data has premultiplied alpha.
    /// </summary>
    Premultiplied = 1 << 4,

    // Legacy alias
    NVG_IMAGE_PREMULTIPLIED = Premultiplied,

    /// <summary>
    /// Use nearest filtering instead of linear.
    /// </summary>
    Nearest = 1 << 5,

    // Legacy alias
    NVG_IMAGE_NEAREST = Nearest,

    /// <summary>
    /// Do not delete Metal texture handle.
    /// </summary>
    NoDelete = 1 << 16,

    // Legacy alias
    NVG_IMAGE_NODELETE = NoDelete,
}

/// <summary>
/// Texture types.
/// </summary>
public enum NVGtextureType
{
    /// <summary>
    /// Alpha texture.
    /// </summary>
    Alpha = 0x01,

    /// <summary>
    /// RGBA texture.
    /// </summary>
    RGBA = 0x02,
}

/// <summary>
/// Legacy texture enum used by older ports.
/// </summary>
public enum NVGtexture
{
    NVG_TEXTURE_ALPHA = 0,
    NVG_TEXTURE_RGBA = 1,
}

/// <summary>
/// Winding direction.
/// </summary>
public enum NVGwinding
{
    /// <summary>
    /// Counter-clockwise winding for solid shapes.
    /// </summary>
    CCW = 1,

    /// <summary>
    /// Clockwise winding for holes.
    /// </summary>
    CW = 2,
}

/// <summary>
/// Solidity types.
/// </summary>
public enum NVGsolidity
{
    /// <summary>
    /// Solid shape (CCW).
    /// </summary>
    Solid = 1,

    /// <summary>
    /// Hole shape (CW).
    /// </summary>
    Hole = 2,
}

/// <summary>
/// Line cap styles.
/// </summary>
public enum NVGlineCap
{
    /// <summary>
    /// Butt line cap.
    /// </summary>
    Butt = 0,

    /// <summary>
    /// Round line cap.
    /// </summary>
    Round = 1,

    /// <summary>
    /// Square line cap.
    /// </summary>
    Square = 2,
}

/// <summary>
/// Line join styles.
/// </summary>
public enum NVGlineJoin
{
    /// <summary>
    /// Round line join.
    /// </summary>
    Round = 1,

    /// <summary>
    /// Bevel line join.
    /// </summary>
    Bevel = 3,

    /// <summary>
    /// Miter line join.
    /// </summary>
    Miter = 4,
}

/// <summary>
/// Text horizontal alignment.
/// </summary>
[Flags]
public enum NVGalign
{
    /// <summary>
    /// Default, align text horizontally to left.
    /// </summary>
    Left = 1 << 0,

    /// <summary>
    /// Align text horizontally to center.
    /// </summary>
    Center = 1 << 1,

    /// <summary>
    /// Align text horizontally to right.
    /// </summary>
    Right = 1 << 2,

    /// <summary>
    /// Align text vertically to top.
    /// </summary>
    Top = 1 << 3,

    /// <summary>
    /// Align text vertically to middle.
    /// </summary>
    Middle = 1 << 4,

    /// <summary>
    /// Align text vertically to bottom.
    /// </summary>
    Bottom = 1 << 5,

    /// <summary>
    /// Align text vertically to baseline.
    /// </summary>
    Baseline = 1 << 6,
}

/// <summary>
/// Blend factors for composite operations.
/// </summary>
public enum NVGblendFactor
{
    Zero = 1 << 0,
    One = 1 << 1,
    SrcColor = 1 << 2,
    OneMinusSrcColor = 1 << 3,
    DstColor = 1 << 4,
    OneMinusDstColor = 1 << 5,
    SrcAlpha = 1 << 6,
    OneMinusSrcAlpha = 1 << 7,
    DstAlpha = 1 << 8,
    OneMinusDstAlpha = 1 << 9,
    SrcAlphaSaturate = 1 << 10,
}

/// <summary>
/// Composite operations.
/// </summary>
public enum NVGcompositeOperation
{
    SourceOver,
    SourceIn,
    SourceOut,
    Atop,
    DestinationOver,
    DestinationIn,
    DestinationOut,
    DestinationAtop,
    Lighter,
    Copy,
    Xor,
}

/// <summary>
/// RGBA color structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NVGcolor
{
    public float R;
    public float G;
    public float B;
    public float A;

    public NVGcolor(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static NVGcolor RGB(byte r, byte g, byte b)
    {
        return RGBA(r, g, b, 255);
    }

    public static NVGcolor RGBf(float r, float g, float b)
    {
        return RGBAf(r, g, b, 1.0f);
    }

    public static NVGcolor RGBA(byte r, byte g, byte b, byte a)
    {
        return new NVGcolor(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
    }

    public static NVGcolor RGBAf(float r, float g, float b, float a)
    {
        return new NVGcolor(r, g, b, a);
    }

    public static NVGcolor HSL(float h, float s, float l)
    {
        return HSLA(h, s, l, 255);
    }

    public static NVGcolor HSLA(float h, float s, float l, byte a)
    {
        float hueToRgb(float p, float q, float t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            if (t < 1.0f / 6.0f)
            {
                return p + (q - p) * 6.0f * t;
            }

            if (t < 1.0f / 2.0f)
            {
                return q;
            }

            if (t < 2.0f / 3.0f)
            {
                return p + (q - p) * (2.0f / 3.0f - t) * 6.0f;
            }

            return p;
        }

        h = h % 1.0f;
        if (h < 0.0f)
        {
            h += 1.0f;
        }

        s = Math.Clamp(s, 0.0f, 1.0f);
        l = Math.Clamp(l, 0.0f, 1.0f);

        float r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = hueToRgb(p, q, h + 1.0f / 3.0f);
            g = hueToRgb(p, q, h);
            b = hueToRgb(p, q, h - 1.0f / 3.0f);
        }

        return new NVGcolor(r, g, b, a / 255.0f);
    }

    public static NVGcolor Lerp(NVGcolor c0, NVGcolor c1, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        var oneminu = 1.0f - t;
        return new NVGcolor(
            c0.R * oneminu + c1.R * t,
            c0.G * oneminu + c1.G * t,
            c0.B * oneminu + c1.B * t,
            c0.A * oneminu + c1.A * t);
    }

    public NVGcolor WithAlpha(byte a)
    {
        return new NVGcolor(R, G, B, a / 255.0f);
    }

    public NVGcolor WithAlphaf(float a)
    {
        return new NVGcolor(R, G, B, a);
    }

    public Vector4 ToVector4() => new Vector4(R, G, B, A);

    public Vector4 ToPremultiplied()
    {
        return new Vector4(R * A, G * A, B * A, A);
    }

    public static readonly NVGcolor White = new(1.0f, 1.0f, 1.0f, 1.0f);
    public static readonly NVGcolor Black = new(0.0f, 0.0f, 0.0f, 1.0f);
    public static readonly NVGcolor Transparent = new(0.0f, 0.0f, 0.0f, 0.0f);
}

/// <summary>
/// Paint style for fills and strokes.
/// </summary>
public struct NVGpaint
{
    public float[] Xform; // [6] - 2x3 transform matrix
    public float[] Extent; // [2] - extent
    public float Radius;
    public float Feather;
    public NVGcolor InnerColor;
    public NVGcolor OuterColor;
    public int Image;

    // For backward compatibility with fixed array access
    public float[] xform
    {
        get => Xform ??= new float[6];
        set => Xform = value;
    }

    public float[] extent
    {
        get => Extent ??= new float[2];
        set => Extent = value;
    }

    public int image
    {
        get => Image;
        set => Image = value;
    }

    public NVGcolor innerColor
    {
        get => InnerColor;
        set => InnerColor = value;
    }

    public NVGcolor outerColor
    {
        get => OuterColor;
        set => OuterColor = value;
    }

    public float radius
    {
        get => Radius;
        set => Radius = value;
    }

    public float feather
    {
        get => Feather;
        set => Feather = value;
    }
}

/// <summary>
/// Composite operation state.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NVGcompositeOperationState
{
    public int SrcRGB;
    public int DstRGB;
    public int SrcAlpha;
    public int DstAlpha;
}

/// <summary>
/// Glyph position info.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NVGglyphPosition
{
    /// <summary>
    /// Pointer to the input string.
    /// </summary>
    public nint Str;

    /// <summary>
    /// X coordinate of the glyph in the input string.
    /// </summary>
    public float X;

    /// <summary>
    /// The bounds of the glyph box.
    /// </summary>
    public float MinX, MaxX;
}

/// <summary>
/// Text row info.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NVGtextRow
{
    /// <summary>
    /// Pointer to the input text where the row starts.
    /// </summary>
    public nint Start;

    /// <summary>
    /// Pointer to the input text where the row ends (one past the last character).
    /// </summary>
    public nint End;

    /// <summary>
    /// Pointer to the beginning of the next row.
    /// </summary>
    public nint Next;

    /// <summary>
    /// Logical width of the row.
    /// </summary>
    public float Width;

    /// <summary>
    /// Actual bounds of the row (can be wider than the logical width).
    /// </summary>
    public float MinX, MaxX;
}

/// <summary>
/// Vertex structure used internally.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NVGvertex
{
    public float X;
    public float Y;
    public float U;
    public float V;

    public NVGvertex(float x, float y, float u, float v)
    {
        X = x;
        Y = y;
        U = u;
        V = v;
    }
}

/// <summary>
/// Path structure used internally.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NVGpath
{
    public int First;
    public int Count;
    public byte Closed;
    public int NBevel;
    public NVGvertex* Fill;
    public int NFill;
    public NVGvertex* Stroke;
    public int NStroke;
    public int Winding;
    public int Convex;
}

/// <summary>
/// Scissor structure used internally.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NVGscissor
{
    public fixed float Transform[6];
    public fixed float Extent[2];
}