// NanoVG OpenGL backend (GL3 core profile)
// Ported from nanovg_gl.h

using System.Runtime.InteropServices;

namespace Aprillz.MewVG;

internal sealed class GLNVGContext : IDisposable, INVGRenderer
{
    private const int UniformArraySize = 11;
    private const int UniformFloatCount = UniformArraySize * 4;

    private struct GLNVGShader
    {
        public int Program;
        public int Vert;
        public int Frag;
        public int LocViewSize;
        public int LocTex;
        public int LocFrag;
    }

    private struct GLNVGTexture
    {
        public int Id;
        public int Tex;
        public int Width;
        public int Height;
        public NVGtextureType Type;
        public NVGimageFlags Flags;
    }

    private struct GLNVGBlend
    {
        public BlendingFactorSrc SrcRGB;
        public BlendingFactorDest DstRGB;
        public BlendingFactorSrc SrcAlpha;
        public BlendingFactorDest DstAlpha;
    }

    private enum GLNVGCallType
    {
        None = 0,
        Fill,
        ConvexFill,
        Stroke,
        Triangles,
    }

    private struct GLNVGCall
    {
        public GLNVGCallType Type;
        public int Image;
        public int PathOffset;
        public int PathCount;
        public int TriangleOffset;
        public int TriangleCount;
        public int UniformOffset;
        public GLNVGBlend BlendFunc;
    }

    private struct GLNVGPath
    {
        public int FillOffset;
        public int FillCount;
        public int StrokeOffset;
        public int StrokeCount;
    }

    private struct GLNVGFragUniforms
    {
        public float[] Data;
    }

    private enum GLNVGShaderType
    {
        FillGrad = 0,
        FillImg = 1,
        Simple = 2,
        Img = 3,
    }

    private readonly NVGcreateFlags _flags;
    private GLNVGShader _shader;

    private int _vao;
    private int _vbo;

    private GLNVGTexture[] _textures = Array.Empty<GLNVGTexture>();
    private int _textureCount;
    private int _textureCapacity;
    private int _textureId;

    private GLNVGCall[] _calls = Array.Empty<GLNVGCall>();
    private int _callCount;
    private int _callCapacity;

    private GLNVGPath[] _paths = Array.Empty<GLNVGPath>();
    private int _pathCount;
    private int _pathCapacity;

    private NVGvertex[] _verts = Array.Empty<NVGvertex>();
    private int _vertCount;
    private int _vertCapacity;

    private GLNVGFragUniforms[] _uniforms = Array.Empty<GLNVGFragUniforms>();
    private int _uniformCount;
    private int _uniformCapacity;

    private readonly float[] _view = new float[2];
    private int _dummyTex;

    // cached state
    private int _boundTexture;
    private int _stencilMask;
    private StencilFunction _stencilFunc;
    private int _stencilFuncRef;
    private int _stencilFuncMask;
    private GLNVGBlend _blendFunc;

    private bool _disposed;

    public GLNVGContext(NVGcreateFlags flags)
    {
        _flags = flags;
        GL.EnsureLoaded();
        CreateResources();
    }

    public int CreateImageFromHandle(int textureId, int width, int height, NVGimageFlags flags)
    {
        var tex = AllocTexture();
        if (tex == null)
        {
            return 0;
        }

        var index = tex.Value;
        ref var t = ref _textures[index];

        t.Type = NVGtextureType.RGBA;
        t.Tex = textureId;
        t.Flags = flags;
        t.Width = width;
        t.Height = height;

        return t.Id;
    }

    public int GetImageHandle(int image)
    {
        ref var tex = ref FindTexture(image);
        return tex.Id != 0 ? tex.Tex : 0;
    }

    public void BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio)
    {
        GL.EnsureLoaded();
        _view[0] = windowWidth;
        _view[1] = windowHeight;

        _callCount = 0;
        _pathCount = 0;
        _vertCount = 0;
        _uniformCount = 0;
    }

    public void Cancel()
    {
        _callCount = 0;
        _pathCount = 0;
        _vertCount = 0;
        _uniformCount = 0;
    }

    public void Flush()
    {
        if (_callCount <= 0)
        {
            return;
        }

        GL.UseProgram(_shader.Program);

        GL.Enable(EnableCap.CullFace);
        GL.CullFace(CullFaceMode.Back);
        GL.FrontFace(FrontFaceDirection.Ccw);
        GL.Enable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        GL.StencilMask(unchecked((int)0xffffffff));
        GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
        GL.StencilFunc(StencilFunction.Always, 0, unchecked((int)0xffffffff));
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        _boundTexture = 0;
        _stencilMask = unchecked((int)0xffffffff);
        _stencilFunc = StencilFunction.Always;
        _stencilFuncRef = 0;
        _stencilFuncMask = unchecked((int)0xffffffff);
        _blendFunc = new GLNVGBlend { SrcRGB = 0, SrcAlpha = 0, DstRGB = 0, DstAlpha = 0 };

        // Upload vertex data
        var vertexSize = Marshal.SizeOf<NVGvertex>();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertCount * vertexSize, _verts, BufferUsageHint.StreamDraw);

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, vertexSize, 0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, vertexSize, sizeof(float) * 2);

        GL.Uniform1(_shader.LocTex, 0);
        GL.Uniform2(_shader.LocViewSize, 1, _view);

        for (var i = 0; i < _callCount; i++)
        {
            ref var call = ref _calls[i];
            BlendFuncSeparate(call.BlendFunc);

            switch (call.Type)
            {
                case GLNVGCallType.Fill:
                    Fill(call);
                    break;
                case GLNVGCallType.ConvexFill:
                    ConvexFill(call);
                    break;
                case GLNVGCallType.Stroke:
                    Stroke(call);
                    break;
                case GLNVGCallType.Triangles:
                    Triangles(call);
                    break;
            }
        }

        GL.DisableVertexAttribArray(0);
        GL.DisableVertexAttribArray(1);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.Disable(EnableCap.CullFace);
        GL.UseProgram(0);
        BindTexture(0);

        _vertCount = 0;
        _pathCount = 0;
        _callCount = 0;
        _uniformCount = 0;
    }

    void INVGRenderer.BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio) => BeginFrame(windowWidth, windowHeight, devicePixelRatio);
    void INVGRenderer.Cancel() => Cancel();
    void INVGRenderer.Flush() => Flush();

    public void RenderFill(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.Fill;
        call.PathOffset = AllocPaths(paths.Length);
        call.PathCount = paths.Length;
        call.Image = paint.Image;
        call.BlendFunc = BlendCompositeOperation(compositeOperation);
        call.TriangleCount = 4;

        if (paths.Length == 1 && paths[0].Convex)
        {
            call.Type = GLNVGCallType.ConvexFill;
            call.TriangleCount = 0;
        }

        var maxVerts = 0;
        for (var i = 0; i < paths.Length; i++)
        {
            maxVerts += paths[i].NFill + paths[i].NStroke;
        }

        maxVerts += call.TriangleCount;
        var vertOffset = AllocVerts(maxVerts);

        for (var i = 0; i < paths.Length; i++)
        {
            ref var copy = ref _paths[call.PathOffset + i];
            ref readonly var path = ref paths[i];

            copy = default;
            if (path.NFill > 0)
            {
                copy.FillOffset = vertOffset;
                copy.FillCount = path.NFill;
                verts.Slice(path.FillOffset, path.NFill).CopyTo(_verts.AsSpan(vertOffset));
                vertOffset += path.NFill;
            }
            if (path.NStroke > 0)
            {
                copy.StrokeOffset = vertOffset;
                copy.StrokeCount = path.NStroke;
                verts.Slice(path.StrokeOffset, path.NStroke).CopyTo(_verts.AsSpan(vertOffset));
                vertOffset += path.NStroke;
            }
        }

        if (call.Type == GLNVGCallType.Fill)
        {
            call.TriangleOffset = vertOffset;
            var quad = _verts.AsSpan(call.TriangleOffset, 4);
            quad[0] = new NVGvertex(bounds[2], bounds[3], 0.5f, 1.0f);
            quad[1] = new NVGvertex(bounds[2], bounds[1], 0.5f, 1.0f);
            quad[2] = new NVGvertex(bounds[0], bounds[3], 0.5f, 1.0f);
            quad[3] = new NVGvertex(bounds[0], bounds[1], 0.5f, 1.0f);

            call.UniformOffset = AllocUniforms(2);
            var simple = _uniforms[call.UniformOffset].Data;
            Array.Clear(simple);
            SetUniformValue(simple, 10, 1, -1.0f); // strokeThr
            SetUniformValue(simple, 10, 3, (float)GLNVGShaderType.Simple);

            if (!ConvertPaint(_uniforms[call.UniformOffset + 1].Data, ref paint, ref scissor, fringe, fringe, -1.0f))
            {
                return;
            }
        }
        else
        {
            call.UniformOffset = AllocUniforms(1);
            if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, fringe, fringe, -1.0f))
            {
                return;
            }
        }
    }

    public void RenderStroke(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        float strokeWidth,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.Stroke;
        call.PathOffset = AllocPaths(paths.Length);
        call.PathCount = paths.Length;
        call.Image = paint.Image;
        call.BlendFunc = BlendCompositeOperation(compositeOperation);

        var maxVerts = 0;
        for (var i = 0; i < paths.Length; i++)
        {
            maxVerts += paths[i].NStroke;
        }

        var vertOffset = AllocVerts(maxVerts);

        for (var i = 0; i < paths.Length; i++)
        {
            ref var copy = ref _paths[call.PathOffset + i];
            ref readonly var path = ref paths[i];
            copy = default;
            if (path.NStroke > 0)
            {
                copy.StrokeOffset = vertOffset;
                copy.StrokeCount = path.NStroke;
                verts.Slice(path.StrokeOffset, path.NStroke).CopyTo(_verts.AsSpan(vertOffset));
                vertOffset += path.NStroke;
            }
        }

        if ((_flags & NVGcreateFlags.StencilStrokes) != 0)
        {
            call.UniformOffset = AllocUniforms(2);
            if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, strokeWidth, fringe, -1.0f))
            {
                return;
            }

            if (!ConvertPaint(_uniforms[call.UniformOffset + 1].Data, ref paint, ref scissor, strokeWidth, fringe, 1.0f - 0.5f / 255.0f))
            {
                return;
            }
        }
        else
        {
            call.UniformOffset = AllocUniforms(1);
            if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, strokeWidth, fringe, -1.0f))
            {
                return;
            }
        }
    }

    public void RenderTriangles(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        ReadOnlySpan<NVGvertex> verts,
        float fringe)
    {
        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.Triangles;
        call.Image = paint.Image;
        call.BlendFunc = BlendCompositeOperation(compositeOperation);

        call.TriangleOffset = AllocVerts(verts.Length);
        call.TriangleCount = verts.Length;
        verts.CopyTo(_verts.AsSpan(call.TriangleOffset, verts.Length));

        call.UniformOffset = AllocUniforms(1);
        if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, 1.0f, fringe, -1.0f))
        {
            return;
        }

        SetUniformValue(_uniforms[call.UniformOffset].Data, 10, 3, (float)GLNVGShaderType.Img);
    }

    public int CreateTexture(NVGtextureType type, int width, int height, NVGimageFlags flags, ReadOnlySpan<byte> data)
    {
        var texIndex = AllocTexture();
        if (texIndex == null)
        {
            return 0;
        }

        ref var tex = ref _textures[texIndex.Value];

        var glTex = GL.GenTexture();
        tex.Tex = glTex;
        tex.Width = width;
        tex.Height = height;
        tex.Type = type;
        tex.Flags = flags;

        BindTexture(glTex);

        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, width);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);

        if (type == NVGtextureType.RGBA)
        {
            if (!data.IsEmpty && (flags & NVGimageFlags.Premultiplied) == 0)
            {
                var premul = PremultiplyRgba(data, width, height);
                data = premul;
            }

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
        else
        {
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, width, height, 0,
                PixelFormat.Red, PixelType.UnsignedByte, data);
        }

        if ((flags & NVGimageFlags.GenerateMipmaps) != 0)
        {
            if ((flags & NVGimageFlags.Nearest) != 0)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapNearest);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            }
        }
        else
        {
            if ((flags & NVGimageFlags.Nearest) != 0)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            }
        }

        if ((flags & NVGimageFlags.Nearest) != 0)
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }
        else
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            (int)((flags & NVGimageFlags.RepeatX) != 0 ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            (int)((flags & NVGimageFlags.RepeatY) != 0 ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));

        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);

        if ((flags & NVGimageFlags.GenerateMipmaps) != 0)
        {
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        }

        BindTexture(0);
        return tex.Id;
    }

    public void DeleteTexture(int id)
    {
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].Id != id)
            {
                continue;
            }

            if (_textures[i].Tex != 0 && (_textures[i].Flags & NVGimageFlags.NoDelete) == 0)
            {
                GL.DeleteTexture(_textures[i].Tex);
            }

            _textures[i] = default;
            return;
        }
    }

    public bool UpdateTexture(int image, int x, int y, int width, int height, ReadOnlySpan<byte> data)
    {
        ref var tex = ref FindTexture(image);
        if (tex.Id == 0)
        {
            return false;
        }

        if (!data.IsEmpty && tex.Type == NVGtextureType.RGBA && (tex.Flags & NVGimageFlags.Premultiplied) == 0)
        {
            data = PremultiplyRgba(data, tex.Width, tex.Height);
        }

        BindTexture(tex.Tex);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, tex.Width);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, x);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, y);

        if (tex.Type == NVGtextureType.RGBA)
        {
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, width, height,
                PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
        else
        {
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, width, height,
                PixelFormat.Red, PixelType.UnsignedByte, data);
        }

        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);

        BindTexture(0);
        return true;
    }

    private static byte[] PremultiplyRgba(ReadOnlySpan<byte> src, int width, int height)
    {
        var expected = width * height * 4;
        if (src.Length < expected)
        {
            return src.ToArray();
        }

        var dst = new byte[expected];
        for (var i = 0; i < expected; i += 4)
        {
            var a = src[i + 3];
            if (a == 0)
            {
                dst[i] = 0;
                dst[i + 1] = 0;
                dst[i + 2] = 0;
                dst[i + 3] = 0;
                continue;
            }

            if (a == 255)
            {
                dst[i] = src[i];
                dst[i + 1] = src[i + 1];
                dst[i + 2] = src[i + 2];
                dst[i + 3] = 255;
                continue;
            }

            var ai = a + 1;
            dst[i] = (byte)((src[i] * ai) >> 8);
            dst[i + 1] = (byte)((src[i + 1] * ai) >> 8);
            dst[i + 2] = (byte)((src[i + 2] * ai) >> 8);
            dst[i + 3] = a;
        }

        return dst;
    }

    public bool GetTextureSize(int image, out int width, out int height)
    {
        ref var tex = ref FindTexture(image);
        if (tex.Id == 0)
        {
            width = 0;
            height = 0;
            return false;
        }

        width = tex.Width;
        height = tex.Height;
        return true;
    }

    private void Fill(in GLNVGCall call)
    {
        var paths = _paths.AsSpan(call.PathOffset, call.PathCount);

        GL.Enable(EnableCap.StencilTest);
        StencilMask(0xff);
        StencilFunc(StencilFunction.Always, 0, 0xff);
        GL.ColorMask(false, false, false, false);

        SetUniforms(call.UniformOffset, 0);

        GL.StencilOpSeparate(StencilFace.Front, StencilOp.Keep, StencilOp.Keep, StencilOp.IncrWrap);
        GL.StencilOpSeparate(StencilFace.Back, StencilOp.Keep, StencilOp.Keep, StencilOp.DecrWrap);
        GL.Disable(EnableCap.CullFace);
        for (var i = 0; i < paths.Length; i++)
        {
            GL.DrawArrays(PrimitiveType.TriangleFan, paths[i].FillOffset, paths[i].FillCount);
        }

        GL.Enable(EnableCap.CullFace);

        GL.ColorMask(true, true, true, true);

        SetUniforms(call.UniformOffset + 1, call.Image);

        if ((_flags & NVGcreateFlags.Antialias) != 0)
        {
            StencilFunc(StencilFunction.Equal, 0x00, 0xff);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            for (var i = 0; i < paths.Length; i++)
            {
                GL.DrawArrays(PrimitiveType.TriangleStrip, paths[i].StrokeOffset, paths[i].StrokeCount);
            }
        }

        StencilFunc(StencilFunction.Notequal, 0x0, 0xff);
        GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero);
        GL.DrawArrays(PrimitiveType.TriangleStrip, call.TriangleOffset, call.TriangleCount);

        GL.Disable(EnableCap.StencilTest);
    }

    private void ConvexFill(in GLNVGCall call)
    {
        var paths = _paths.AsSpan(call.PathOffset, call.PathCount);

        GL.Disable(EnableCap.CullFace);
        SetUniforms(call.UniformOffset, call.Image);

        for (var i = 0; i < paths.Length; i++)
        {
            GL.DrawArrays(PrimitiveType.TriangleFan, paths[i].FillOffset, paths[i].FillCount);
            if (paths[i].StrokeCount > 0)
            {
                GL.DrawArrays(PrimitiveType.TriangleStrip, paths[i].StrokeOffset, paths[i].StrokeCount);
            }
        }

        GL.Enable(EnableCap.CullFace);
    }

    private void Stroke(in GLNVGCall call)
    {
        var paths = _paths.AsSpan(call.PathOffset, call.PathCount);

        if ((_flags & NVGcreateFlags.StencilStrokes) != 0)
        {
            GL.Enable(EnableCap.StencilTest);
            StencilMask(0xff);

            StencilFunc(StencilFunction.Equal, 0x0, 0xff);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Incr);
            SetUniforms(call.UniformOffset + 1, call.Image);
            for (var i = 0; i < paths.Length; i++)
            {
                GL.DrawArrays(PrimitiveType.TriangleStrip, paths[i].StrokeOffset, paths[i].StrokeCount);
            }

            SetUniforms(call.UniformOffset, call.Image);
            StencilFunc(StencilFunction.Equal, 0x00, 0xff);
            GL.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            for (var i = 0; i < paths.Length; i++)
            {
                GL.DrawArrays(PrimitiveType.TriangleStrip, paths[i].StrokeOffset, paths[i].StrokeCount);
            }

            GL.ColorMask(false, false, false, false);
            StencilFunc(StencilFunction.Always, 0x0, 0xff);
            GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero);
            for (var i = 0; i < paths.Length; i++)
            {
                GL.DrawArrays(PrimitiveType.TriangleStrip, paths[i].StrokeOffset, paths[i].StrokeCount);
            }

            GL.ColorMask(true, true, true, true);

            GL.Disable(EnableCap.StencilTest);
        }
        else
        {
            SetUniforms(call.UniformOffset, call.Image);
            for (var i = 0; i < paths.Length; i++)
            {
                GL.DrawArrays(PrimitiveType.TriangleStrip, paths[i].StrokeOffset, paths[i].StrokeCount);
            }
        }
    }

    private void Triangles(in GLNVGCall call)
    {
        SetUniforms(call.UniformOffset, call.Image);
        GL.DrawArrays(PrimitiveType.Triangles, call.TriangleOffset, call.TriangleCount);
    }

    private void BindTexture(int tex)
    {
        if (_boundTexture == tex)
        {
            return;
        }

        _boundTexture = tex;
        GL.BindTexture(TextureTarget.Texture2D, tex);
    }

    private void StencilMask(int mask)
    {
        if (_stencilMask == mask)
        {
            return;
        }

        _stencilMask = mask;
        GL.StencilMask(mask);
    }

    private void StencilFunc(StencilFunction func, int reference, int mask)
    {
        if (_stencilFunc == func && _stencilFuncRef == reference && _stencilFuncMask == mask)
        {
            return;
        }

        _stencilFunc = func;
        _stencilFuncRef = reference;
        _stencilFuncMask = mask;
        GL.StencilFunc(func, reference, mask);
    }

    private void BlendFuncSeparate(GLNVGBlend blend)
    {
        if (_blendFunc.SrcRGB == blend.SrcRGB && _blendFunc.DstRGB == blend.DstRGB &&
            _blendFunc.SrcAlpha == blend.SrcAlpha && _blendFunc.DstAlpha == blend.DstAlpha)
        {
            return;
        }

        _blendFunc = blend;
        GL.BlendFuncSeparate(blend.SrcRGB, blend.DstRGB, blend.SrcAlpha, blend.DstAlpha);
    }

    private void SetUniforms(int uniformOffset, int image)
    {
        GL.Uniform4(_shader.LocFrag, UniformArraySize, _uniforms[uniformOffset].Data);

        ref var tex = ref FindTexture(image != 0 ? image : _dummyTex);
        if (tex.Id == 0)
        {
            tex = ref FindTexture(_dummyTex);
        }

        BindTexture(tex.Tex);
    }

    private static void SetUniformValue(float[] data, int vecIndex, int component, float value) => data[vecIndex * 4 + component] = value;

    private static void SetUniformVec4(float[] data, int vecIndex, float x, float y, float z, float w)
    {
        var baseIndex = vecIndex * 4;
        data[baseIndex] = x;
        data[baseIndex + 1] = y;
        data[baseIndex + 2] = z;
        data[baseIndex + 3] = w;
    }

    private static void SetUniformMat3x4(float[] data, int vecIndex, ReadOnlySpan<float> t)
    {
        SetUniformVec4(data, vecIndex + 0, t[0], t[1], 0.0f, 0.0f);
        SetUniformVec4(data, vecIndex + 1, t[2], t[3], 0.0f, 0.0f);
        SetUniformVec4(data, vecIndex + 2, t[4], t[5], 1.0f, 0.0f);
    }

    private static NVGcolor Premultiply(NVGcolor color) => new NVGcolor(color.R * color.A, color.G * color.A, color.B * color.A, color.A);

    private bool ConvertPaint(float[] frag, ref NVGpaint paint, ref NVGscissorState scissor, float width, float fringe, float strokeThr)
    {
        Span<float> invxform = stackalloc float[6];

        Array.Clear(frag);

        var inner = Premultiply(paint.InnerColor);
        var outer = Premultiply(paint.OuterColor);
        SetUniformVec4(frag, 6, inner.R, inner.G, inner.B, inner.A);
        SetUniformVec4(frag, 7, outer.R, outer.G, outer.B, outer.A);

        if (scissor.Extent[0] < -0.5f || scissor.Extent[1] < -0.5f)
        {
            SetUniformVec4(frag, 0, 0, 0, 0, 0);
            SetUniformVec4(frag, 1, 0, 0, 0, 0);
            SetUniformVec4(frag, 2, 0, 0, 0, 0);
            SetUniformVec4(frag, 8, 1.0f, 1.0f, 1.0f, 1.0f);
        }
        else
        {
            NVGMath.TransformInverse(invxform, scissor.Xform);
            SetUniformMat3x4(frag, 0, invxform);

            var sx = MathF.Sqrt(scissor.Xform[0] * scissor.Xform[0] + scissor.Xform[2] * scissor.Xform[2]) / fringe;
            var sy = MathF.Sqrt(scissor.Xform[1] * scissor.Xform[1] + scissor.Xform[3] * scissor.Xform[3]) / fringe;
            SetUniformVec4(frag, 8, scissor.Extent[0], scissor.Extent[1], sx, sy);
        }

        var extentX = paint.Extent[0];
        var extentY = paint.Extent[1];
        SetUniformVec4(frag, 9, extentX, extentY, paint.Radius, paint.Feather);

        var strokeMult = (width * 0.5f + fringe * 0.5f) / fringe;
        SetUniformVec4(frag, 10, strokeMult, strokeThr, 0.0f, 0.0f);

        if (paint.Image != 0)
        {
            ref var tex = ref FindTexture(paint.Image);
            if (tex.Id == 0)
            {
                return false;
            }

            if ((tex.Flags & NVGimageFlags.FlipY) != 0)
            {
                Span<float> m1 = stackalloc float[6];
                Span<float> m2 = stackalloc float[6];
                Span<float> px = stackalloc float[6];
                px = paint.Xform;

                NVGMath.TransformTranslate(m1, 0.0f, extentY * 0.5f);
                NVGMath.TransformMultiply(m1, px);
                NVGMath.TransformScale(m2, 1.0f, -1.0f);
                NVGMath.TransformMultiply(m2, m1);
                NVGMath.TransformTranslate(m1, 0.0f, -extentY * 0.5f);
                NVGMath.TransformMultiply(m1, m2);
                NVGMath.TransformInverse(invxform, m1);
            }
            else
            {
                NVGMath.TransformInverse(invxform, paint.Xform);
            }

            SetUniformValue(frag, 10, 3, (float)GLNVGShaderType.FillImg);
            if (tex.Type == NVGtextureType.RGBA)
            {
                SetUniformValue(frag, 10, 2, (tex.Flags & NVGimageFlags.Premultiplied) != 0 ? 0.0f : 1.0f);
            }
            else
            {
                SetUniformValue(frag, 10, 2, 2.0f);
            }
        }
        else
        {
            SetUniformValue(frag, 10, 3, (float)GLNVGShaderType.FillGrad);
            NVGMath.TransformInverse(invxform, paint.Xform);
        }

        SetUniformMat3x4(frag, 3, invxform);
        return true;
    }

    private GLNVGBlend BlendCompositeOperation(NVGcompositeOperationState op)
    {
        var ok = true;
        var blend = new GLNVGBlend
        {
            SrcRGB = ConvertBlendFuncFactorSrc(op.SrcRGB, ref ok),
            DstRGB = ConvertBlendFuncFactorDst(op.DstRGB, ref ok),
            SrcAlpha = ConvertBlendFuncFactorSrc(op.SrcAlpha, ref ok),
            DstAlpha = ConvertBlendFuncFactorDst(op.DstAlpha, ref ok)
        };

        if (!ok)
        {
            blend.SrcRGB = BlendingFactorSrc.One;
            blend.DstRGB = BlendingFactorDest.OneMinusSrcAlpha;
            blend.SrcAlpha = BlendingFactorSrc.One;
            blend.DstAlpha = BlendingFactorDest.OneMinusSrcAlpha;
        }

        return blend;
    }

    private static BlendingFactorSrc ConvertBlendFuncFactorSrc(int factor, ref bool ok) => factor switch
    {
        (int)NVGblendFactor.Zero => BlendingFactorSrc.Zero,
        (int)NVGblendFactor.One => BlendingFactorSrc.One,
        (int)NVGblendFactor.SrcColor => BlendingFactorSrc.SrcColor,
        (int)NVGblendFactor.OneMinusSrcColor => BlendingFactorSrc.OneMinusSrcColor,
        (int)NVGblendFactor.DstColor => BlendingFactorSrc.DstColor,
        (int)NVGblendFactor.OneMinusDstColor => BlendingFactorSrc.OneMinusDstColor,
        (int)NVGblendFactor.SrcAlpha => BlendingFactorSrc.SrcAlpha,
        (int)NVGblendFactor.OneMinusSrcAlpha => BlendingFactorSrc.OneMinusSrcAlpha,
        (int)NVGblendFactor.DstAlpha => BlendingFactorSrc.DstAlpha,
        (int)NVGblendFactor.OneMinusDstAlpha => BlendingFactorSrc.OneMinusDstAlpha,
        (int)NVGblendFactor.SrcAlphaSaturate => BlendingFactorSrc.SrcAlphaSaturate,
        _ => FailBlendSrc(ref ok)
    };

    private static BlendingFactorDest ConvertBlendFuncFactorDst(int factor, ref bool ok) => factor switch
    {
        (int)NVGblendFactor.Zero => BlendingFactorDest.Zero,
        (int)NVGblendFactor.One => BlendingFactorDest.One,
        (int)NVGblendFactor.SrcColor => BlendingFactorDest.SrcColor,
        (int)NVGblendFactor.OneMinusSrcColor => BlendingFactorDest.OneMinusSrcColor,
        (int)NVGblendFactor.DstColor => BlendingFactorDest.DstColor,
        (int)NVGblendFactor.OneMinusDstColor => BlendingFactorDest.OneMinusDstColor,
        (int)NVGblendFactor.SrcAlpha => BlendingFactorDest.SrcAlpha,
        (int)NVGblendFactor.OneMinusSrcAlpha => BlendingFactorDest.OneMinusSrcAlpha,
        (int)NVGblendFactor.DstAlpha => BlendingFactorDest.DstAlpha,
        (int)NVGblendFactor.OneMinusDstAlpha => BlendingFactorDest.OneMinusDstAlpha,
        (int)NVGblendFactor.SrcAlphaSaturate => BlendingFactorDest.SrcAlphaSaturate,
        _ => FailBlendDst(ref ok)
    };

    private static BlendingFactorSrc FailBlendSrc(ref bool ok)
    {
        ok = false;
        return BlendingFactorSrc.One;
    }

    private static BlendingFactorDest FailBlendDst(ref bool ok)
    {
        ok = false;
        return BlendingFactorDest.One;
    }

    private ref GLNVGTexture FindTexture(int id)
    {
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].Id == id)
            {
                return ref _textures[i];
            }
        }

        return ref _textures[0];
    }

    private int? AllocTexture()
    {
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].Id == 0)
            {
                return i;
            }
        }

        if (_textureCount + 1 > _textureCapacity)
        {
            var newCapacity = Math.Max(_textureCount + 1, 4) + _textureCapacity / 2;
            Array.Resize(ref _textures, newCapacity);
            _textureCapacity = newCapacity;
        }

        var index = _textureCount++;
        _textures[index] = default;
        _textures[index].Id = ++_textureId;
        return index;
    }

    private ref GLNVGCall AllocCall()
    {
        if (_callCount + 1 > _callCapacity)
        {
            var newCapacity = Math.Max(_callCount + 1, 128) + _callCapacity / 2;
            Array.Resize(ref _calls, newCapacity);
            _callCapacity = newCapacity;
        }

        ref var call = ref _calls[_callCount++];
        call = default;
        return ref call;
    }

    private int AllocPaths(int n)
    {
        if (_pathCount + n > _pathCapacity)
        {
            var newCapacity = Math.Max(_pathCount + n, 128) + _pathCapacity / 2;
            Array.Resize(ref _paths, newCapacity);
            _pathCapacity = newCapacity;
        }

        var offset = _pathCount;
        _pathCount += n;
        return offset;
    }

    private int AllocVerts(int n)
    {
        if (_vertCount + n > _vertCapacity)
        {
            var newCapacity = Math.Max(_vertCount + n, 4096) + _vertCapacity / 2;
            Array.Resize(ref _verts, newCapacity);
            _vertCapacity = newCapacity;
        }

        var offset = _vertCount;
        _vertCount += n;
        return offset;
    }

    private int AllocUniforms(int n)
    {
        if (_uniformCount + n > _uniformCapacity)
        {
            var newCapacity = Math.Max(_uniformCount + n, 128) + _uniformCapacity / 2;
            Array.Resize(ref _uniforms, newCapacity);
            for (var i = _uniformCapacity; i < newCapacity; i++)
            {
                _uniforms[i].Data = new float[UniformFloatCount];
            }

            _uniformCapacity = newCapacity;
        }

        for (var i = _uniformCount; i < _uniformCount + n; i++)
        {
            if (_uniforms[i].Data == null || _uniforms[i].Data.Length != UniformFloatCount)
            {
                _uniforms[i].Data = new float[UniformFloatCount];
            }
        }

        var offset = _uniformCount;
        _uniformCount += n;
        return offset;
    }

    private void CreateResources()
    {
        var header = "#version 150 core\n" +
                        "#define NANOVG_GL3 1\n" +
                        "#define UNIFORMARRAY_SIZE 11\n\n";

        const string fillVertShader =
            "#ifdef NANOVG_GL3\n" +
            "\tuniform vec2 viewSize;\n" +
            "\tin vec2 vertex;\n" +
            "\tin vec2 tcoord;\n" +
            "\tout vec2 ftcoord;\n" +
            "\tout vec2 fpos;\n" +
            "#else\n" +
            "\tuniform vec2 viewSize;\n" +
            "\tattribute vec2 vertex;\n" +
            "\tattribute vec2 tcoord;\n" +
            "\tvarying vec2 ftcoord;\n" +
            "\tvarying vec2 fpos;\n" +
            "#endif\n" +
            "void main(void) {\n" +
            "\tftcoord = tcoord;\n" +
            "\tfpos = vertex;\n" +
            "\tgl_Position = vec4(2.0*vertex.x/viewSize.x - 1.0, 1.0 - 2.0*vertex.y/viewSize.y, 0, 1);\n" +
            "}\n";

        const string fillFragShader =
            "#ifdef GL_ES\n" +
            "#if defined(GL_FRAGMENT_PRECISION_HIGH) || defined(NANOVG_GL3)\n" +
            " precision highp float;\n" +
            "#else\n" +
            " precision mediump float;\n" +
            "#endif\n" +
            "#endif\n" +
            "#ifdef NANOVG_GL3\n" +
            "\tuniform vec4 frag[UNIFORMARRAY_SIZE];\n" +
            "\tuniform sampler2D tex;\n" +
            "\tin vec2 ftcoord;\n" +
            "\tin vec2 fpos;\n" +
            "\tout vec4 outColor;\n" +
            "#else\n" +
            "\tuniform vec4 frag[UNIFORMARRAY_SIZE];\n" +
            "\tuniform sampler2D tex;\n" +
            "\tvarying vec2 ftcoord;\n" +
            "\tvarying vec2 fpos;\n" +
            "#endif\n" +
            "\t#define scissorMat mat3(frag[0].xyz, frag[1].xyz, frag[2].xyz)\n" +
            "\t#define paintMat mat3(frag[3].xyz, frag[4].xyz, frag[5].xyz)\n" +
            "\t#define innerCol frag[6]\n" +
            "\t#define outerCol frag[7]\n" +
            "\t#define scissorExt frag[8].xy\n" +
            "\t#define scissorScale frag[8].zw\n" +
            "\t#define extent frag[9].xy\n" +
            "\t#define radius frag[9].z\n" +
            "\t#define feather frag[9].w\n" +
            "\t#define strokeMult frag[10].x\n" +
            "\t#define strokeThr frag[10].y\n" +
            "\t#define texType int(frag[10].z)\n" +
            "\t#define type int(frag[10].w)\n" +
            "\n" +
            "float sdroundrect(vec2 pt, vec2 ext, float rad) {\n" +
            "\tvec2 ext2 = ext - vec2(rad,rad);\n" +
            "\tvec2 d = abs(pt) - ext2;\n" +
            "\treturn min(max(d.x,d.y),0.0) + length(max(d,0.0)) - rad;\n" +
            "}\n" +
            "\n" +
            "float scissorMask(vec2 p) {\n" +
            "\tvec2 sc = (abs((scissorMat * vec3(p,1.0)).xy) - scissorExt);\n" +
            "\tsc = vec2(0.5,0.5) - sc * scissorScale;\n" +
            "\treturn clamp(sc.x,0.0,1.0) * clamp(sc.y,0.0,1.0);\n" +
            "}\n" +
            "#ifdef EDGE_AA\n" +
            "float strokeMask() {\n" +
            "\treturn min(1.0, (1.0-abs(ftcoord.x*2.0-1.0))*strokeMult) * min(1.0, ftcoord.y);\n" +
            "}\n" +
            "#endif\n" +
            "\n" +
            "void main(void) {\n" +
            "\tvec4 result;\n" +
            "\tfloat scissor = scissorMask(fpos);\n" +
            "#ifdef EDGE_AA\n" +
            "\tfloat strokeAlpha = strokeMask();\n" +
            "\tif (strokeAlpha < strokeThr) discard;\n" +
            "#else\n" +
            "\tfloat strokeAlpha = 1.0;\n" +
            "#endif\n" +
            "\tif (type == 0) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat d = clamp((sdroundrect(pt, extent, radius) + feather*0.5) / feather, 0.0, 1.0);\n" +
            "\t\tvec4 color = mix(innerCol,outerCol,d);\n" +
            "\t\tcolor *= strokeAlpha * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t} else if (type == 1) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy / extent;\n" +
            "\t\tvec4 color = texture(tex, pt);\n" +
            "\t\tif (texType == 1) color = vec4(color.xyz*color.w,color.w);\n" +
            "\t\tif (texType == 2) color = vec4(color.x);\n" +
            "\t\tcolor *= innerCol;\n" +
            "\t\tcolor *= strokeAlpha * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t} else if (type == 2) {\n" +
            "\t\tresult = vec4(1,1,1,1);\n" +
            "\t} else if (type == 3) {\n" +
            "\t\tvec4 color = texture(tex, ftcoord);\n" +
            "\t\tif (texType == 1) color = vec4(color.xyz*color.w,color.w);\n" +
            "\t\tif (texType == 2) color = vec4(color.x);\n" +
            "\t\tcolor *= scissor;\n" +
            "\t\tresult = color * innerCol;\n" +
            "\t}\n" +
            "#ifdef NANOVG_GL3\n" +
            "\toutColor = result;\n" +
            "#else\n" +
            "\tgl_FragColor = result;\n" +
            "#endif\n" +
            "}\n";

        var opts = (_flags & NVGcreateFlags.Antialias) != 0 ? "#define EDGE_AA 1\n" : string.Empty;

        _shader = CreateShader("shader", header, opts, fillVertShader, fillFragShader);
        GetUniforms(ref _shader);

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        _dummyTex = CreateTexture(NVGtextureType.Alpha, 1, 1, 0, ReadOnlySpan<byte>.Empty);

        GL.Finish();
    }

    private static GLNVGShader CreateShader(string name, string header, string opts, string vshader, string fshader)
    {
        var program = GL.CreateProgram();
        var vert = GL.CreateShader(ShaderType.VertexShader);
        var frag = GL.CreateShader(ShaderType.FragmentShader);

        string[] vertSrc = { header, opts, vshader };
        string[] fragSrc = { header, opts, fshader };

        GL.ShaderSource(vert, vertSrc.Length, vertSrc, null);
        GL.ShaderSource(frag, fragSrc.Length, fragSrc, null);

        GL.CompileShader(vert);
        GL.GetShader(vert, ShaderParameter.CompileStatus, out var statusVert);
        if (statusVert != (int)All.True)
        {
            throw new InvalidOperationException($"Failed to compile vertex shader: {GL.GetShaderInfoLog(vert)}");
        }

        GL.CompileShader(frag);
        GL.GetShader(frag, ShaderParameter.CompileStatus, out var statusFrag);
        if (statusFrag != (int)All.True)
        {
            throw new InvalidOperationException($"Failed to compile fragment shader: {GL.GetShaderInfoLog(frag)}");
        }

        GL.AttachShader(program, vert);
        GL.AttachShader(program, frag);

        GL.BindAttribLocation(program, 0, "vertex");
        GL.BindAttribLocation(program, 1, "tcoord");

        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var statusProg);
        if (statusProg != (int)All.True)
        {
            throw new InvalidOperationException($"Failed to link program: {GL.GetProgramInfoLog(program)}");
        }

        return new GLNVGShader
        {
            Program = program,
            Vert = vert,
            Frag = frag,
        };
    }

    private static void GetUniforms(ref GLNVGShader shader)
    {
        shader.LocViewSize = GL.GetUniformLocation(shader.Program, "viewSize");
        shader.LocTex = GL.GetUniformLocation(shader.Program, "tex");
        shader.LocFrag = GL.GetUniformLocation(shader.Program, "frag");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_shader.Program != 0)
        {
            GL.DeleteProgram(_shader.Program);
        }

        if (_shader.Vert != 0)
        {
            GL.DeleteShader(_shader.Vert);
        }

        if (_shader.Frag != 0)
        {
            GL.DeleteShader(_shader.Frag);
        }

        if (_vao != 0)
        {
            GL.DeleteVertexArray(_vao);
        }

        if (_vbo != 0)
        {
            GL.DeleteBuffer(_vbo);
        }

        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].Tex != 0 && (_textures[i].Flags & NVGimageFlags.NoDelete) == 0)
            {
                GL.DeleteTexture(_textures[i].Tex);
            }
        }
    }
}
