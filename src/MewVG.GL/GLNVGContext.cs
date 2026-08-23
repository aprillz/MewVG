// NanoVG OpenGL backend (GL3 core profile)
// Ported from nanovg_gl.h

using System.Runtime.CompilerServices;

namespace Aprillz.MewVG;

internal sealed class GLNVGContext : IDisposable, INVGRenderer
{
    private const int UniformArraySize = 13;
    private const int UniformFloatCount = UniformArraySize * 4;

    private struct GLNVGShader
    {
        public int Program;
        public int Vert;
        public int Frag;
        public int LocViewSize;
        public int LocTex;
        public int LocFrag;
        public int LocCoverageOrigin;
        public int LocClipTex;
        public int LocClipEnabled;
        public int LocClipOrigin;
        public int LocClipSize;
        public int LocMaskTex;
        public int LocMaskEnabled;
        public int LocMaskOrigin;
        public int LocMaskSize;
        public int LocMaskScale;
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
        Clip,
        ClipReset,
        Fill,
        Stroke,
        Triangles,
        MaskFill,
    }

    private struct GLNVGCall
    {
        public GLNVGCallType Type;
        public int Image;
        public int MaskImage;
        public float MaskOriginX;
        public float MaskOriginY;
        public int PathOffset;
        public int PathCount;
        public int TriangleOffset;
        public int TriangleCount;
        public int UniformOffset;
        public GLNVGBlend BlendFunc;
        public bool HasTransparency;
        public bool CpuResolvedFill;
        public int MergedFringeOffset;
        public int MergedFringeCount;
        public bool MergedFringeIsStrip;
        public int MergedStrokeOffset;
        public int MergedStrokeCount;
        public bool MergedStrokeIsStrip;
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
        CoverageOutput = 4,
        CoverageComposite = 5,
        GradientRadial = 6,
        GradientLinear = 7,
    }

    // MEWVG_GL_DEBUG=1 turns on the glGetError check after each state change.
    private static readonly bool _debugChecks = Environment.GetEnvironmentVariable("MEWVG_GL_DEBUG") == "1";
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

    // coverage buffer for transparent fills
    private int _coverageFbo;
    private int _coverageTex;
    private int _coverageStencilRb;
    // Device copies of coverage masks; see MaskImageCache for the reuse and eviction policy.
    private readonly MaskImageCache _maskImages;
    private int _coverageTexWidth;
    private int _coverageTexHeight;
    private float _devicePixelRatio = 1.0f;

    // Bounded R8 clip masks. Two targets are used so nested clips can intersect the
    // new path with the previous mask without sampling the render target.
    private readonly int[] _clipMaskFbos = new int[2];
    private readonly int[] _clipMaskTextures = new int[2];
    private readonly int[] _clipMaskTextureWidths = new int[2];
    private readonly int[] _clipMaskTextureHeights = new int[2];
    private int _clipMaskIndex;
    private bool _clipMaskActive;
    private bool _clipMaskEmpty;
    private int _clipMaskX;
    private int _clipMaskY;
    private int _clipMaskWidth;
    private int _clipMaskHeight;

    // cached state
    private int _boundTexture;
    private int _stencilMask;
    private StencilFunction _stencilFunc;
    private int _stencilFuncRef;
    private int _stencilFuncMask;
    private GLNVGBlend _blendFunc;
    private bool _disposed;
    private readonly bool _coverageFillAaEnabled;

    // captured once per Flush() so the coverage passes don't re-query GL state per call
    private int _flushMainFbo;
    private int _flushViewportX;
    private int _flushViewportY;
    private int _flushViewportWidth;
    private int _flushViewportHeight;

    private static bool HasTransparency(in NVGpaint paint)
        => paint.InnerColor.A < 0.999f || paint.OuterColor.A < 0.999f;

    public GLNVGContext()
    {
        _coverageFillAaEnabled = true;
        // GL orders commands: once a frame's calls are flushed, its images may be rewritten.
        _maskImages = new MaskImageCache(
            (width, height, coverage) => CreateTexture(NVGtextureType.Alpha, width, height, NVGimageFlags.Nearest, coverage),
            (image, width, height, coverage) => UpdateTexture(image, 0, 0, width, height, coverage),
            DeleteTexture,
            framesInFlight: 1);
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
        if (!TryFindTexture(image, out var index))
        {
            return 0;
        }

        return _textures[index].Tex;
    }

    public void BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio)
    {
        GL.EnsureLoaded();
        _view[0] = windowWidth;
        _view[1] = windowHeight;
        _devicePixelRatio = devicePixelRatio;

        _callCount = 0;
        _pathCount = 0;
        _vertCount = 0;
        _uniformCount = 0;
        _maskImages.BeginFrame();
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

        // Captured once here instead of per coverage call (FillWithCoverage/StrokeWithCoverage
        // used to re-query the bound framebuffer on every call and never restored the caller's
        // actual viewport, only the coverage-sized one).
        _flushMainFbo = GL.GetInteger(GetPName.FramebufferBinding);
        GL.GetViewport(out _flushViewportX, out _flushViewportY, out _flushViewportWidth, out _flushViewportHeight);

        // Upload vertex data
        var vertexSize = Unsafe.SizeOf<NVGvertex>();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertCount * vertexSize, _verts, BufferUsageHint.StreamDraw);

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, vertexSize, 0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, vertexSize, sizeof(float) * 2);

        GL.Uniform1(_shader.LocTex, 0);
        GL.Uniform1(_shader.LocClipTex, 1);
        GL.Uniform1(_shader.LocMaskTex, 2);
        GL.Uniform1(_shader.LocMaskEnabled, 0);
        GL.Uniform2(_shader.LocViewSize, 1, _view);

        _clipMaskActive = false;
        _clipMaskEmpty = false;

        for (var i = 0; i < _callCount; i++)
        {
            ref var call = ref _calls[i];

            switch (call.Type)
            {
                case GLNVGCallType.ClipReset:
                    ClipReset(call);
                    break;
                case GLNVGCallType.Clip:
                    Clip(call);
                    break;
                case GLNVGCallType.Fill:
                    BlendFuncSeparate(call.BlendFunc);
                    Fill(call);
                    break;
                case GLNVGCallType.Stroke:
                    BlendFuncSeparate(call.BlendFunc);
                    Stroke(call);
                    break;
                case GLNVGCallType.Triangles:
                    BlendFuncSeparate(call.BlendFunc);
                    Triangles(call);
                    break;
                case GLNVGCallType.MaskFill:
                    BlendFuncSeparate(call.BlendFunc);
                    MaskFill(call);
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

        CheckError("flush");
    }

    void INVGRenderer.BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio) => BeginFrame(windowWidth, windowHeight, devicePixelRatio);
    void INVGRenderer.Cancel() => Cancel();
    void INVGRenderer.Flush() => Flush();
    void INVGRenderer.RenderClip(ref NVGscissorState scissor, float fringe, ReadOnlySpan<float> bounds, ReadOnlySpan<NVGpathData> paths, ReadOnlySpan<NVGvertex> verts)
        => RenderClip(ref scissor, fringe, bounds, paths, verts);
    void INVGRenderer.ResetClip() => ResetClip();
    void INVGRenderer.RenderMaskFill(ref NVGpaint paint, NVGcompositeOperationState compositeOperation, ref NVGscissorState scissor, float fringe, ReadOnlySpan<float> bounds, ReadOnlySpan<byte> coverage, int maskWidth, int maskHeight, float maskOriginX, float maskOriginY, object? cacheKey, int cacheVersion)
        => RenderMaskFill(ref paint, compositeOperation, ref scissor, fringe, bounds, coverage, maskWidth, maskHeight, maskOriginX, maskOriginY, cacheKey, cacheVersion);

    private void RenderClip(
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.Clip;
        call.PathOffset = AllocPaths(paths.Length);
        call.PathCount = paths.Length;
        call.Image = 0;
        call.BlendFunc = default;

        // The clip is written into its mask the way a fill is written into the coverage buffer:
        // the tessellated body (full coverage) and the AA fringe strip, merged into one draw and
        // accumulated with max blending. The body is a triangle list from the core's tessellator,
        // so nothing overlaps and no winding pass is needed.
        var singlePath = paths.Length == 1;
        var maxVerts = 4;
        for (var i = 0; i < paths.Length; i++)
        {
            maxVerts += paths[i].NFill + (singlePath ? paths[i].NStroke : StripToTriangleCount(paths[i].NStroke));
        }

        var vertOffset = AllocVerts(maxVerts);
        call.MergedFringeOffset = vertOffset;
        call.MergedFringeIsStrip = false;
        for (var i = 0; i < paths.Length; i++)
        {
            ref var copy = ref _paths[call.PathOffset + i];
            ref readonly var path = ref paths[i];
            copy = default;

            if (path.NFill > 0)
            {
                copy.FillOffset = vertOffset;
                verts.Slice(path.FillOffset, path.NFill).CopyTo(_verts.AsSpan(vertOffset));
                copy.FillCount = path.NFill;
                vertOffset += path.NFill;
            }
        }
        for (var i = 0; i < paths.Length; i++)
        {
            ref var copy = ref _paths[call.PathOffset + i];
            ref readonly var path = ref paths[i];
            if (path.NStroke > 0)
            {
                copy.StrokeOffset = vertOffset;
                // A fringe strip can only follow the body triangles in one draw as triangles.
                var written = ConvertStripToTriangles(verts.Slice(path.StrokeOffset, path.NStroke), _verts.AsSpan(vertOffset));
                copy.StrokeCount = written;
                vertOffset += written;
            }
        }
        call.MergedFringeCount = vertOffset - call.MergedFringeOffset;

        call.TriangleOffset = vertOffset;
        call.TriangleCount = 4;
        var quad = _verts.AsSpan(call.TriangleOffset, 4);
        quad[0] = new NVGvertex(_view[0], _view[1], 0.5f, 1.0f);
        quad[1] = new NVGvertex(_view[0], 0, 0.5f, 1.0f);
        quad[2] = new NVGvertex(0, _view[1], 0.5f, 1.0f);
        quad[3] = new NVGvertex(0, 0, 0.5f, 1.0f);

        // Coverage output: the body texcoord reads as full coverage under the analytical-fill
        // strokeMult, the fringe ramps, and the scissor is left to each draw that samples the mask.
        call.UniformOffset = AllocUniforms(1);
        var coverageOut = _uniforms[call.UniformOffset].Data;
        Array.Clear(coverageOut);
        SetUniformVec4(coverageOut, 8, 1.0f, 1.0f, 1.0f, 1.0f);
        SetUniformValue(coverageOut, 12, 0, -1.0f);
        SetUniformValue(coverageOut, 12, 1, -1.0f);
        SetUniformValue(coverageOut, 12, 3, (float)GLNVGShaderType.CoverageOutput);
    }

    private void ResetClip()
    {
        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.ClipReset;
        call.Image = 0;
        call.BlendFunc = default;

        call.TriangleOffset = AllocVerts(4);
        call.TriangleCount = 4;
        var quad = _verts.AsSpan(call.TriangleOffset, 4);
        quad[0] = new NVGvertex(_view[0], _view[1], 0.5f, 1.0f);
        quad[1] = new NVGvertex(_view[0], 0, 0.5f, 1.0f);
        quad[2] = new NVGvertex(0, _view[1], 0.5f, 1.0f);
        quad[3] = new NVGvertex(0, 0, 0.5f, 1.0f);
        call.UniformOffset = AllocUniforms(1);
        var simple = _uniforms[call.UniformOffset].Data;
        Array.Clear(simple);
        SetUniformVec4(simple, 8, 1.0f, 1.0f, 1.0f, 1.0f);
        SetUniformValue(simple, 12, 1, -1.0f);
        SetUniformValue(simple, 12, 3, (float)GLNVGShaderType.Simple);
    }

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
        call.CpuResolvedFill = true;

        var singlePath = paths.Length == 1;
        var maxVerts = 0;
        for (var i = 0; i < paths.Length; i++)
        {
            maxVerts += paths[i].NFill + (singlePath ? paths[i].NStroke : StripToTriangleCount(paths[i].NStroke));
        }

        maxVerts += call.TriangleCount;
        var vertOffset = AllocVerts(maxVerts);

        // Pass 1: fill vertices (contiguous)
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
                if ((path.NFill % 3) != 0)
                {
                    call.CpuResolvedFill = false;
                }
            }
        }

        // Pass 2: fringe vertices (strip→triangle, or keep strip for single path)
        var singleFringePath = paths.Length == 1 && paths[0].NStroke > 0;
        call.MergedFringeOffset = vertOffset;
        call.MergedFringeIsStrip = singleFringePath;
        for (var i = 0; i < paths.Length; i++)
        {
            ref var copy = ref _paths[call.PathOffset + i];
            ref readonly var path = ref paths[i];

            if (path.NStroke > 0)
            {
                var strokeSrc = verts.Slice(path.StrokeOffset, path.NStroke);

                copy.StrokeOffset = vertOffset;
                if (singleFringePath)
                {
                    strokeSrc.CopyTo(_verts.AsSpan(vertOffset));
                    copy.StrokeCount = strokeSrc.Length;
                    vertOffset += strokeSrc.Length;
                }
                else
                {
                    var written = ConvertStripToTriangles(strokeSrc, _verts.AsSpan(vertOffset));
                    copy.StrokeCount = written;
                    vertOffset += written;
                }
            }
        }
        call.MergedFringeCount = vertOffset - call.MergedFringeOffset;

        call.TriangleOffset = vertOffset;
        // The AA fringe extends half a fringe beyond the path bounds. The
        // coverage pipeline composites only pixels covered by this quad, so an
        // unexpanded quad would drop the outermost partial-coverage pixels on
        // the bounds-facing edges (visible as a missing AA column when an edge
        // lands past a pixel center).
        var quadMargin = fringe;
        var quad = _verts.AsSpan(call.TriangleOffset, 4);
        quad[0] = new NVGvertex(bounds[2] + quadMargin, bounds[3] + quadMargin, 0.5f, 1.0f);
        quad[1] = new NVGvertex(bounds[2] + quadMargin, bounds[1] - quadMargin, 0.5f, 1.0f);
        quad[2] = new NVGvertex(bounds[0] - quadMargin, bounds[3] + quadMargin, 0.5f, 1.0f);
        quad[3] = new NVGvertex(bounds[0] - quadMargin, bounds[1] - quadMargin, 0.5f, 1.0f);

        // Check convexity based on fill paths only; fringe-only paths (NFill==0) don't affect fill overlap.
        var isConvexFill = false;
        {
            int fillPathCount = 0;
            int fillPathIndex = -1;
            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i].NFill > 0)
                {
                    fillPathCount++;
                    fillPathIndex = i;
                }
            }
            if (fillPathCount == 1 && fillPathIndex >= 0 && paths[fillPathIndex].Convex)
                isConvexFill = true;
        }
        // Coverage AA triggers when the fill path was flagged non-convex by the core
        // tessellator - this happens for transparent paints (to avoid double-blending
        // in the fringe overlay) AND for self-intersecting sources like a pentagram.
        // The coverage buffer's Max-blended accumulation collapses overlapping fringe
        // strips to a clean boundary, where the stencil-fill + fringe-overlay path
        // would leave visible seams cutting across the fill interior.
        call.HasTransparency = _coverageFillAaEnabled && !isConvexFill && paint.Image == 0;

        if (call.HasTransparency)
        {
            // Coverage buffer path: 3 uniform sets
            call.UniformOffset = AllocUniforms(3);

            // uniformOffset+0: Simple (stencil pass + interior coverage = 1.0)
            var simple = _uniforms[call.UniformOffset].Data;
            Array.Clear(simple);
            SetUniformVec4(simple, 8, 1.0f, 1.0f, 1.0f, 1.0f); // no scissor
            SetUniformValue(simple, 12, 1, -1.0f); // strokeThr
            SetUniformValue(simple, 12, 3, (float)GLNVGShaderType.Simple);

            // uniformOffset+1: Paint for composite (type = CoverageComposite)
            if (!ConvertPaint(_uniforms[call.UniformOffset + 1].Data, ref paint, ref scissor, fringe, fringe, -1.0f))
            {
                return;
            }
            SetUniformValue(_uniforms[call.UniformOffset + 1].Data, 12, 0, -1.0f); // strokeMult
            SetUniformValue(_uniforms[call.UniformOffset + 1].Data, 12, 3, (float)GLNVGShaderType.CoverageComposite);

            // uniformOffset+2: Coverage output (type = CoverageOutput)
            var coverageOut = _uniforms[call.UniformOffset + 2].Data;
            Array.Clear(coverageOut);
            SetUniformVec4(coverageOut, 8, 1.0f, 1.0f, 1.0f, 1.0f); // no scissor
            SetUniformValue(coverageOut, 12, 0, -1.0f); // strokeMult (analytical fill)
            SetUniformValue(coverageOut, 12, 1, -1.0f); // strokeThr (no discard)
            SetUniformValue(coverageOut, 12, 3, (float)GLNVGShaderType.CoverageOutput);
        }
        else
        {
            call.UniformOffset = AllocUniforms(2);
            var simple = _uniforms[call.UniformOffset].Data;
            Array.Clear(simple);
            SetUniformValue(simple, 12, 1, -1.0f); // strokeThr
            SetUniformValue(simple, 12, 3, (float)GLNVGShaderType.Simple);

            if (!ConvertPaint(_uniforms[call.UniformOffset + 1].Data, ref paint, ref scissor, fringe, fringe, -1.0f))
            {
                return;
            }
            SetUniformValue(_uniforms[call.UniformOffset + 1].Data, 12, 0, -1.0f); // strokeMult < 0 → analytical fill coverage
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
        var isConvexStroke = paths.Length == 1 && paths[0].Convex;
        call.HasTransparency = _coverageFillAaEnabled && !isConvexStroke && paint.Image == 0 && HasTransparency(paint);

        var singleStrokePath = paths.Length == 1;
        var maxVerts = 0;
        for (var i = 0; i < paths.Length; i++)
        {
            maxVerts += singleStrokePath ? paths[i].NStroke : StripToTriangleCount(paths[i].NStroke);
        }

        // Reserve 4 extra verts for the composite-pass bounds quad used by
        // StrokeWithCoverage (must come AFTER the stroke geometry so MergedStrokeOffset
        // points at the original strip head).
        var vertOffset = AllocVerts(maxVerts + 4);
        call.MergedStrokeOffset = vertOffset;
        call.MergedStrokeIsStrip = singleStrokePath;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        for (var i = 0; i < paths.Length; i++)
        {
            ref var copy = ref _paths[call.PathOffset + i];
            ref readonly var path = ref paths[i];
            copy = default;
            if (path.NStroke > 0)
            {
                copy.StrokeOffset = vertOffset;
                var strokeSrc = verts.Slice(path.StrokeOffset, path.NStroke);
                if (singleStrokePath)
                {
                    strokeSrc.CopyTo(_verts.AsSpan(vertOffset));
                    copy.StrokeCount = strokeSrc.Length;
                    vertOffset += strokeSrc.Length;
                }
                else
                {
                    var written = ConvertStripToTriangles(strokeSrc, _verts.AsSpan(vertOffset));
                    copy.StrokeCount = written;
                    vertOffset += written;
                }
                for (var j = 0; j < strokeSrc.Length; j++)
                {
                    ref readonly var v = ref strokeSrc[j];
                    if (v.X < minX) minX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y > maxY) maxY = v.Y;
                }
            }
        }
        call.MergedStrokeCount = vertOffset - call.MergedStrokeOffset;

        // Bounds quad (TriangleStrip) for the coverage-composite pass. Drawing the
        // stroke geometry itself for composite would re-rasterize overlapping segment
        // quads at sharp corners and SrcOver-blend the same pixel multiple times -
        // visible as darker spikes at every join with transparent strokes (issue 224-01).
        // The quad covers each pixel exactly once; the coverage texture (built earlier
        // with MAX blending) provides the real per-pixel alpha.
        call.TriangleOffset = vertOffset;
        call.TriangleCount = 4;
        if (float.IsPositiveInfinity(minX))
        {
            // No stroke verts - use a degenerate quad. Composite pass becomes a no-op.
            minX = minY = 0f;
            maxX = maxY = 0f;
        }
        _verts[vertOffset++] = new NVGvertex(maxX, maxY, 0.5f, 1.0f);
        _verts[vertOffset++] = new NVGvertex(maxX, minY, 0.5f, 1.0f);
        _verts[vertOffset++] = new NVGvertex(minX, maxY, 0.5f, 1.0f);
        _verts[vertOffset++] = new NVGvertex(minX, minY, 0.5f, 1.0f);

        if (call.HasTransparency)
        {
            call.UniformOffset = AllocUniforms(2);
            if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, strokeWidth, fringe, -1.0f))
            {
                return;
            }
            SetUniformValue(_uniforms[call.UniformOffset].Data, 12, 3, (float)GLNVGShaderType.CoverageComposite);

            var coverageOut = _uniforms[call.UniformOffset + 1].Data;
            _uniforms[call.UniformOffset].Data.CopyTo(coverageOut, 0);
            SetUniformValue(coverageOut, 12, 1, -1.0f);
            SetUniformValue(coverageOut, 12, 3, (float)GLNVGShaderType.CoverageOutput);
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

        SetUniformValue(_uniforms[call.UniformOffset].Data, 12, 3, (float)GLNVGShaderType.Img);
    }

    public void RenderMaskFill(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<byte> coverage,
        int maskWidth,
        int maskHeight,
        float maskOriginX,
        float maskOriginY,
        object? cacheKey,
        int cacheVersion)
    {
        var maskImage = _maskImages.Acquire(coverage, maskWidth, maskHeight, cacheKey, cacheVersion);
        if (maskImage == 0)
        {
            return;
        }

        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.MaskFill;
        call.Image = paint.Image;
        call.MaskImage = maskImage;
        call.MaskOriginX = maskOriginX;
        call.MaskOriginY = maskOriginY;
        call.BlendFunc = BlendCompositeOperation(compositeOperation);

        // Bounds quad with the fill-body texcoord (0.5, 1): strokeMask reads 1 for it under
        // the analytical-fill strokeMult below, so only the mask shapes the coverage.
        call.TriangleOffset = AllocVerts(4);
        call.TriangleCount = 4;
        var quad = _verts.AsSpan(call.TriangleOffset, 4);
        quad[0] = new NVGvertex(bounds[2], bounds[3], 0.5f, 1.0f);
        quad[1] = new NVGvertex(bounds[2], bounds[1], 0.5f, 1.0f);
        quad[2] = new NVGvertex(bounds[0], bounds[3], 0.5f, 1.0f);
        quad[3] = new NVGvertex(bounds[0], bounds[1], 0.5f, 1.0f);

        call.UniformOffset = AllocUniforms(1);
        if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, fringe, fringe, -1.0f))
        {
            call.Type = GLNVGCallType.None;
            return;
        }
        SetUniformValue(_uniforms[call.UniformOffset].Data, 12, 0, -1.0f); // strokeMult < 0: analytical fill
    }

    private void MaskFill(in GLNVGCall call)
    {
        if (!TryFindTexture(call.MaskImage, out var maskIndex))
        {
            return;
        }

        GL.Disable(EnableCap.CullFace);
        SetUniforms(call.UniformOffset, call.Image);

        ref var mask = ref _textures[maskIndex];
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, mask.Tex);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(_shader.LocMaskEnabled, 1);
        Span<float> origin = stackalloc float[2] { call.MaskOriginX, call.MaskOriginY };
        Span<float> size = stackalloc float[2] { mask.Width, mask.Height };
        GL.Uniform2(_shader.LocMaskOrigin, 1, origin);
        GL.Uniform2(_shader.LocMaskSize, 1, size);
        GL.Uniform1(_shader.LocMaskScale, _devicePixelRatio);

        GL.DrawArrays(PrimitiveType.TriangleStrip, call.TriangleOffset, call.TriangleCount);

        GL.Uniform1(_shader.LocMaskEnabled, 0);
        GL.Enable(EnableCap.CullFace);
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

        // Mipmapped colour textures must be premultiplied. glGenerateMipmap averages texels, and averaging
        // straight (non-premultiplied) alpha bleeds transparent-pixel RGB into lower mip levels, which shows
        // as fuzzy / haloed edges when the image is minified (the base level at 100% looks fine). Premultiply
        // here and flag the texture premultiplied so the shader samples the already-correct texels.
        ReadOnlySpan<byte> uploadData = data;
        if ((flags & NVGimageFlags.GenerateMipmaps) != 0
            && (flags & NVGimageFlags.Premultiplied) == 0
            && (type == NVGtextureType.RGBA || type == NVGtextureType.BGRA)
            && data.Length >= width * height * 4)
        {
            uploadData = PremultiplyCopy(data, width, height);
            flags |= NVGimageFlags.Premultiplied;
            tex.Flags = flags;
        }

        BindTexture(glTex);

        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, width);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);

        if (type == NVGtextureType.RGBA)
        {
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, uploadData);
        }
        else if (type == NVGtextureType.BGRA)
        {
            // Internal format is GL_RGBA8 (driver swizzles for shader sampling). The
            // BGRA + UnsignedInt_8_8_8_8_Rev pair tells the driver "the source is BGRA
            // little-endian bytes" - on desktop NV/AMD/Intel this is the native upload
            // path, faster than RGBA because no CPU/driver swizzle is needed.
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Bgra, PixelType.UnsignedInt_8_8_8_8_Rev, uploadData);
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
        CheckError("create texture");
        return tex.Id;
    }

    // Returns a premultiplied (straight RGB * alpha) copy of a tight width*height*4 BGRA/RGBA buffer.
    // Byte order is irrelevant: indices 0..2 are colour channels, index 3 is alpha for both layouts.
    private static byte[] PremultiplyCopy(ReadOnlySpan<byte> src, int width, int height)
    {
        int count = width * height;
        var dst = new byte[count * 4];
        for (int i = 0; i < count; i++)
        {
            int offset = i * 4;
            byte alpha = src[offset + 3];
            if (alpha == 255)
            {
                dst[offset] = src[offset];
                dst[offset + 1] = src[offset + 1];
                dst[offset + 2] = src[offset + 2];
            }
            else if (alpha != 0)
            {
                dst[offset] = (byte)(src[offset] * alpha / 255);
                dst[offset + 1] = (byte)(src[offset + 1] * alpha / 255);
                dst[offset + 2] = (byte)(src[offset + 2] * alpha / 255);
            }
            // alpha == 0 leaves colour channels at 0 (clean transparent texel for mip averaging).
            dst[offset + 3] = alpha;
        }
        return dst;
    }

    public void DeleteTexture(int id)
    {
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].Id != id)
            {
                continue;
            }

            int glTex = _textures[i].Tex;
            if (glTex != 0 && (_textures[i].Flags & NVGimageFlags.NoDelete) == 0)
            {
                // Invalidate bind cache before GL frees the name; otherwise a later
                // GenTextures reusing the same name would skip the real bind call.
                if (_boundTexture == glTex)
                {
                    _boundTexture = -1;
                }
                GL.DeleteTexture(glTex);
            }

            _textures[i] = default;
            return;
        }
    }

    public bool UpdateTexture(int image, int x, int y, int width, int height, ReadOnlySpan<byte> data)
    {
        if (!TryFindTexture(image, out var index))
        {
            return false;
        }

        ref var tex = ref _textures[index];
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
        else if (tex.Type == NVGtextureType.BGRA)
        {
            GL.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, width, height,
                PixelFormat.Bgra, PixelType.UnsignedInt_8_8_8_8_Rev, data);
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


    public bool GetTextureSize(int image, out int width, out int height)
    {
        if (!TryFindTexture(image, out var index))
        {
            width = 0;
            height = 0;
            return false;
        }

        ref var tex = ref _textures[index];
        width = tex.Width;
        height = tex.Height;
        return true;
    }

    private void Fill(in GLNVGCall call)
    {
        if (call.HasTransparency)
        {
            FillWithCoverage(call);
            return;
        }

        var paths = _paths.AsSpan(call.PathOffset, call.PathCount);
        if (call.CpuResolvedFill)
        {
            GL.Disable(EnableCap.CullFace);

            SetUniforms(call.UniformOffset + 1, call.Image);
            var fillStart = -1;
            var fillCount = 0;
            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i].FillCount > 0)
                {
                    if (fillStart < 0)
                    {
                        fillStart = paths[i].FillOffset;
                    }

                    fillCount += paths[i].FillCount;
                }
            }
            if (fillStart >= 0 && fillCount > 0)
            {
                GL.DrawArrays(PrimitiveType.Triangles, fillStart, fillCount);
            }

            if (call.MergedFringeCount > 0)
            {
                var fringeMode = call.MergedFringeIsStrip ? PrimitiveType.TriangleStrip : PrimitiveType.Triangles;
                GL.DrawArrays(fringeMode, call.MergedFringeOffset, call.MergedFringeCount);
            }

            GL.Enable(EnableCap.CullFace);

            return;
        }

        GL.Enable(EnableCap.StencilTest);
        StencilMask(0xff);
        StencilFunc(StencilFunction.Always, 0, 0xff);
        GL.ColorMask(false, false, false, false);

        SetUniforms(call.UniformOffset, 0);

        GL.StencilOpSeparate(StencilFace.Front, StencilOp.Keep, StencilOp.Keep, StencilOp.IncrWrap);
        GL.StencilOpSeparate(StencilFace.Back, StencilOp.Keep, StencilOp.Keep, StencilOp.DecrWrap);
        GL.Disable(EnableCap.CullFace);
        // Non-CpuResolvedFill: fans already converted to triangles in RenderFill
        {
            var fillStart = -1;
            var fillCount = 0;
            for (var i = 0; i < paths.Length; i++)
            {
                if (paths[i].FillCount > 0)
                {
                    if (fillStart < 0) fillStart = paths[i].FillOffset;
                    fillCount += paths[i].FillCount;
                }
            }
            if (fillStart >= 0 && fillCount > 0)
            {
                GL.DrawArrays(PrimitiveType.Triangles, fillStart, fillCount);
            }
        }

        GL.Enable(EnableCap.CullFace);

        GL.ColorMask(true, true, true, true);

        SetUniforms(call.UniformOffset + 1, call.Image);

        // Fill quad first (stencil != 0, zeros stencil). The clip, if any, is the mask bound by
        // SetUniforms and multiplies in the shader.
        StencilFunc(StencilFunction.Notequal, 0x0, 0xff);
        GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero);
        GL.DrawArrays(PrimitiveType.TriangleStrip, call.TriangleOffset, call.TriangleCount);

        GL.Disable(EnableCap.StencilTest);

        // AA fringe on top without stencil.
        if (call.MergedFringeCount > 0)
        {
            GL.Disable(EnableCap.CullFace);
            var fringeMode = call.MergedFringeIsStrip ? PrimitiveType.TriangleStrip : PrimitiveType.Triangles;
            GL.DrawArrays(fringeMode, call.MergedFringeOffset, call.MergedFringeCount);
            GL.Enable(EnableCap.CullFace);
        }
    }

    private void Stroke(in GLNVGCall call)
    {
        if (call.HasTransparency)
        {
            StrokeWithCoverage(call);
            return;
        }

        var strokeMode = call.MergedStrokeIsStrip ? PrimitiveType.TriangleStrip : PrimitiveType.Triangles;

        GL.Disable(EnableCap.CullFace);
        SetUniforms(call.UniformOffset, call.Image);
        if (call.MergedStrokeCount > 0)
            GL.DrawArrays(strokeMode, call.MergedStrokeOffset, call.MergedStrokeCount);
        GL.Enable(EnableCap.CullFace);
    }

    private void Triangles(in GLNVGCall call)
    {
        GL.Disable(EnableCap.CullFace);
        SetUniforms(call.UniformOffset, call.Image);

        GL.DrawArrays(PrimitiveType.Triangles, call.TriangleOffset, call.TriangleCount);
        GL.Enable(EnableCap.CullFace);
    }

    private void Clip(in GLNVGCall call) => RenderShaderClip(call);

    private void ClipReset(in GLNVGCall call)
    {
        _clipMaskActive = false;
        _clipMaskEmpty = false;
    }

    private void EnsureClipMaskTexture(int index, int width, int height)
    {
        width = Math.Max(16, (width + 15) & ~15);
        height = Math.Max(16, (height + 15) & ~15);
        if (_clipMaskTextures[index] != 0 &&
            _clipMaskTextureWidths[index] >= width && _clipMaskTextureHeights[index] >= height)
        {
            return;
        }

        var defaultFbo = GL.GetInteger(GetPName.FramebufferBinding);
        if (_clipMaskTextures[index] != 0) GL.DeleteTexture(_clipMaskTextures[index]);
        if (_clipMaskFbos[index] != 0) GL.DeleteFramebuffer(_clipMaskFbos[index]);

        _clipMaskTextures[index] = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _clipMaskTextures[index]);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, width, height, 0,
            PixelFormat.Red, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _clipMaskFbos[index] = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _clipMaskFbos[index]);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _clipMaskTextures[index], 0);
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, defaultFbo);
            throw new InvalidOperationException($"MewVG.GL: clip mask framebuffer incomplete (status={status}).");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, defaultFbo);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        _boundTexture = 0;
        _clipMaskTextureWidths[index] = width;
        _clipMaskTextureHeights[index] = height;
    }

    private void RenderShaderClip(in GLNVGCall call)
    {
        if (_flushViewportWidth <= 0 || _flushViewportHeight <= 0 || call.MergedFringeCount <= 0)
        {
            _clipMaskActive = true;
            _clipMaskEmpty = true;
            return;
        }

        var previousActive = _clipMaskActive;
        var destination = previousActive ? 1 - _clipMaskIndex : 0;
        GetCoverageScissor(call.MergedFringeOffset, call.MergedFringeCount,
            _flushViewportWidth, _flushViewportHeight,
            out var clipX, out var clipY, out var clipWidth, out var clipHeight);

        if (previousActive)
        {
            var right = Math.Min(clipX + clipWidth, _clipMaskX + _clipMaskWidth);
            var top = Math.Min(clipY + clipHeight, _clipMaskY + _clipMaskHeight);
            clipX = Math.Max(clipX, _clipMaskX);
            clipY = Math.Max(clipY, _clipMaskY);
            clipWidth = Math.Max(0, right - clipX);
            clipHeight = Math.Max(0, top - clipY);
        }

        if (clipWidth == 0 || clipHeight == 0 || (previousActive && _clipMaskEmpty))
        {
            _clipMaskActive = true;
            _clipMaskEmpty = true;
            _clipMaskX = clipX;
            _clipMaskY = clipY;
            _clipMaskWidth = clipWidth;
            _clipMaskHeight = clipHeight;
            return;
        }

        EnsureClipMaskTexture(destination, clipWidth, clipHeight);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _clipMaskFbos[destination]);
        GL.Viewport(-clipX, -clipY, _flushViewportWidth, _flushViewportHeight);
        GL.Disable(EnableCap.ScissorTest);
        GL.Disable(EnableCap.StencilTest);
        GL.Disable(EnableCap.CullFace);
        GL.ColorMask(true, true, true, true);
        GL.ClearColor(0, 0, 0, 0);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        // Coverage accumulates by max: the body writes 1, the fringe its ramp, and where they meet
        // the larger value wins instead of adding up. A previous clip bound as clipTex multiplies
        // in, which is how nested clips intersect.
        var previousBlend = _blendFunc;
        BlendFuncSeparate(new GLNVGBlend
        {
            SrcRGB = BlendingFactorSrc.One,
            DstRGB = BlendingFactorDest.One,
            SrcAlpha = BlendingFactorSrc.One,
            DstAlpha = BlendingFactorDest.One
        });
        GL.BlendEquation(BlendEquationMode.Max);

        GL.Enable(EnableCap.ScissorTest);
        GL.Scissor(0, 0, clipWidth, clipHeight);
        SetUniforms(call.UniformOffset, 0, applyClipMask: false);
        if (previousActive)
        {
            BindClipMask(_clipMaskX - clipX, _clipMaskY - clipY, _clipMaskWidth, _clipMaskHeight);
        }
        GL.DrawArrays(PrimitiveType.Triangles, call.MergedFringeOffset, call.MergedFringeCount);

        GL.Disable(EnableCap.ScissorTest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _flushMainFbo);
        GL.Viewport(_flushViewportX, _flushViewportY, _flushViewportWidth, _flushViewportHeight);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        BlendFuncSeparate(previousBlend);
        GL.Enable(EnableCap.CullFace);
        _clipMaskIndex = destination;
        _clipMaskActive = true;
        _clipMaskEmpty = false;
        _clipMaskX = clipX;
        _clipMaskY = clipY;
        _clipMaskWidth = clipWidth;
        _clipMaskHeight = clipHeight;
    }

    private void EnsureCoverageTexture(int width, int height)
    {
        if (_coverageTex != 0 && _coverageTexWidth >= width && _coverageTexHeight >= height)
        {
            return;
        }

        // Never give up an axis already allocated: callers pass per-call scissor sizes, so
        // reallocating at exactly the requested size makes alternating shapes rebuild the
        // texture, its renderbuffer, and its framebuffer on every draw.
        width = Math.Max(width, _coverageTexWidth);
        height = Math.Max(height, _coverageTexHeight);

        if (_coverageTex != 0) GL.DeleteTexture(_coverageTex);
        if (_coverageStencilRb != 0) GL.DeleteRenderbuffer(_coverageStencilRb);
        if (_coverageFbo != 0) GL.DeleteFramebuffer(_coverageFbo);

        var defaultFbo = GL.GetInteger(GetPName.FramebufferBinding);

        _coverageTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _coverageTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, width, height, 0, PixelFormat.Red, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _coverageStencilRb = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _coverageStencilRb);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.StencilIndex8, width, height);

        _coverageFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _coverageFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _coverageTex, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.StencilAttachment, RenderbufferTarget.Renderbuffer, _coverageStencilRb);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            // Fallback: depth24+stencil8 combo
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _coverageStencilRb);

            status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, defaultFbo);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                _boundTexture = 0;

                GL.DeleteTexture(_coverageTex);
                GL.DeleteRenderbuffer(_coverageStencilRb);
                GL.DeleteFramebuffer(_coverageFbo);
                _coverageTex = 0;
                _coverageStencilRb = 0;
                _coverageFbo = 0;
                _coverageTexWidth = 0;
                _coverageTexHeight = 0;

                throw new InvalidOperationException($"MewVG.GL: coverage framebuffer incomplete (status={status}).");
            }
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, defaultFbo);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        _boundTexture = 0;

        _coverageTexWidth = width;
        _coverageTexHeight = height;
    }

    private static int ConvertStripToTriangles(ReadOnlySpan<NVGvertex> strip, Span<NVGvertex> output)
    {
        if (strip.Length < 3) return 0;
        var written = 0;
        for (var i = 0; i < strip.Length - 2; i++)
        {
            if ((i & 1) == 0)
            {
                output[written++] = strip[i];
                output[written++] = strip[i + 1];
                output[written++] = strip[i + 2];
            }
            else
            {
                output[written++] = strip[i + 1];
                output[written++] = strip[i];
                output[written++] = strip[i + 2];
            }
        }
        return written;
    }

    private static int StripToTriangleCount(int stripVertCount)
        => stripVertCount < 3 ? 0 : (stripVertCount - 2) * 3;

    // Bounds quad vertices are in NanoVG space (y-down, CSS pixels). The coverage
    // texture is device pixels, and GL's scissor rect is y-up with origin at the
    // bottom-left, so the y range must be flipped using the coverage buffer height.
    private void GetCoverageScissor(
        int triangleOffset,
        int triangleCount,
        int coverageWidth,
        int coverageHeight,
        out int scissorX,
        out int scissorY,
        out int scissorWidth,
        out int scissorHeight)
    {
        const int scissorMargin = 2; // extra room for the AA fringe

        var quad = _verts.AsSpan(triangleOffset, triangleCount);
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        for (var i = 0; i < quad.Length; i++)
        {
            if (quad[i].X < minX) minX = quad[i].X;
            if (quad[i].X > maxX) maxX = quad[i].X;
            if (quad[i].Y < minY) minY = quad[i].Y;
            if (quad[i].Y > maxY) maxY = quad[i].Y;
        }

        var left = (int)MathF.Floor(minX * _devicePixelRatio) - scissorMargin;
        var right = (int)MathF.Ceiling(maxX * _devicePixelRatio) + scissorMargin;
        var top = (int)MathF.Floor(minY * _devicePixelRatio) - scissorMargin;
        var bottom = (int)MathF.Ceiling(maxY * _devicePixelRatio) + scissorMargin;

        scissorX = Math.Clamp(left, 0, coverageWidth);
        var clampedRight = Math.Clamp(right, 0, coverageWidth);
        scissorWidth = Math.Max(0, clampedRight - scissorX);

        scissorY = Math.Clamp(coverageHeight - bottom, 0, coverageHeight);
        var clampedTop = Math.Clamp(coverageHeight - top, 0, coverageHeight);
        scissorHeight = Math.Max(0, clampedTop - scissorY);
    }

    private void FillWithCoverage(in GLNVGCall call)
    {
        var paths = _paths.AsSpan(call.PathOffset, call.PathCount);

        var viewportW = (int)MathF.Ceiling(_view[0] * _devicePixelRatio);
        var viewportH = (int)MathF.Ceiling(_view[1] * _devicePixelRatio);

        var mainFbo = _flushMainFbo;

        GetCoverageScissor(call.TriangleOffset, call.TriangleCount, viewportW, viewportH,
            out var scissorX, out var scissorY, out var scissorWidth, out var scissorHeight);
        if (scissorWidth == 0 || scissorHeight == 0)
        {
            return;
        }
        EnsureCoverageTexture(scissorWidth, scissorHeight);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _coverageFbo);
        // Keep NanoVG's full-window clip-space transform, but translate its viewport so
        // this call's device-pixel bounds land at (0,0) in the smaller shared scratch.
        GL.Viewport(-scissorX, -scissorY, viewportW, viewportH);
        GL.Enable(EnableCap.ScissorTest);
        GL.Scissor(0, 0, scissorWidth, scissorHeight);
        GL.ClearColor(0, 0, 0, 0);
        GL.ClearStencil(0);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);

        BlendFuncSeparate(new GLNVGBlend
        {
            SrcRGB = BlendingFactorSrc.One,
            DstRGB = BlendingFactorDest.One,
            SrcAlpha = BlendingFactorSrc.One,
            DstAlpha = BlendingFactorDest.One
        });
        GL.BlendEquation(BlendEquationMode.Max);

        if (call.CpuResolvedFill)
        {
            GL.Disable(EnableCap.StencilTest);
            GL.Disable(EnableCap.CullFace);
            SetUniforms(call.UniformOffset, 0, applyClipMask: false);
            {
                var fillStart = -1;
                var fillCount = 0;
                for (var i = 0; i < paths.Length; i++)
                {
                    if (paths[i].FillCount > 0)
                    {
                        if (fillStart < 0) fillStart = paths[i].FillOffset;
                        fillCount += paths[i].FillCount;
                    }
                }
                if (fillStart >= 0 && fillCount > 0)
                    GL.DrawArrays(PrimitiveType.Triangles, fillStart, fillCount);
            }
        }
        else
        {
            GL.Enable(EnableCap.StencilTest);
            StencilMask(0xff);
            StencilFunc(StencilFunction.Always, 0, 0xff);
            GL.ColorMask(false, false, false, false);

            SetUniforms(call.UniformOffset, 0, applyClipMask: false);

            GL.StencilOpSeparate(StencilFace.Front, StencilOp.Keep, StencilOp.Keep, StencilOp.IncrWrap);
            GL.StencilOpSeparate(StencilFace.Back, StencilOp.Keep, StencilOp.Keep, StencilOp.DecrWrap);
            GL.Disable(EnableCap.CullFace);
            {
                var fillStart = -1;
                var fillCount = 0;
                for (var i = 0; i < paths.Length; i++)
                {
                    if (paths[i].FillCount > 0)
                    {
                        if (fillStart < 0) fillStart = paths[i].FillOffset;
                        fillCount += paths[i].FillCount;
                    }
                }
                if (fillStart >= 0 && fillCount > 0)
                    GL.DrawArrays(PrimitiveType.Triangles, fillStart, fillCount);
            }

            GL.ColorMask(true, true, true, true);
            SetUniforms(call.UniformOffset, 0, applyClipMask: false);
            StencilFunc(StencilFunction.Notequal, 0x0, 0xff);
            GL.StencilOp(StencilOp.Zero, StencilOp.Zero, StencilOp.Zero);
            GL.DrawArrays(PrimitiveType.TriangleStrip, call.TriangleOffset, call.TriangleCount);
            GL.Disable(EnableCap.StencilTest);
        }

        SetUniforms(call.UniformOffset + 2, 0, applyClipMask: false);
        GL.Disable(EnableCap.CullFace);
        if (call.MergedFringeCount > 0)
        {
            var fringeMode = call.MergedFringeIsStrip ? PrimitiveType.TriangleStrip : PrimitiveType.Triangles;
            GL.DrawArrays(fringeMode, call.MergedFringeOffset, call.MergedFringeCount);
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFbo);
        GL.Viewport(_flushViewportX, _flushViewportY, _flushViewportWidth, _flushViewportHeight);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        BlendFuncSeparate(call.BlendFunc);

        SetUniforms(call.UniformOffset + 1, call.Image, bindTexture: false);
        SetCoverageOrigin(scissorX, scissorY);
        BindTexture(_coverageTex);
        GL.DrawArrays(PrimitiveType.TriangleStrip, call.TriangleOffset, call.TriangleCount);
        GL.Enable(EnableCap.CullFace);
        _boundTexture = -1;
    }

    private void StrokeWithCoverage(in GLNVGCall call)
    {
        var paths = _paths.AsSpan(call.PathOffset, call.PathCount);
        var viewportW = (int)MathF.Ceiling(_view[0] * _devicePixelRatio);
        var viewportH = (int)MathF.Ceiling(_view[1] * _devicePixelRatio);

        var mainFbo = _flushMainFbo;

        GetCoverageScissor(call.TriangleOffset, call.TriangleCount, viewportW, viewportH,
            out var scissorX, out var scissorY, out var scissorWidth, out var scissorHeight);
        if (scissorWidth == 0 || scissorHeight == 0)
        {
            return;
        }
        EnsureCoverageTexture(scissorWidth, scissorHeight);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _coverageFbo);
        GL.Viewport(-scissorX, -scissorY, viewportW, viewportH);
        GL.Enable(EnableCap.ScissorTest);
        GL.Scissor(0, 0, scissorWidth, scissorHeight);
        GL.ClearColor(0, 0, 0, 0);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.Disable(EnableCap.StencilTest);
        GL.Disable(EnableCap.CullFace);

        BlendFuncSeparate(new GLNVGBlend
        {
            SrcRGB = BlendingFactorSrc.One,
            DstRGB = BlendingFactorDest.One,
            SrcAlpha = BlendingFactorSrc.One,
            DstAlpha = BlendingFactorDest.One
        });
        GL.BlendEquation(BlendEquationMode.Max);

        var strokeMode = call.MergedStrokeIsStrip ? PrimitiveType.TriangleStrip : PrimitiveType.Triangles;
        SetUniforms(call.UniformOffset + 1, call.Image, applyClipMask: false);
        if (call.MergedStrokeCount > 0)
            GL.DrawArrays(strokeMode, call.MergedStrokeOffset, call.MergedStrokeCount);

        GL.Disable(EnableCap.ScissorTest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, mainFbo);
        GL.Viewport(_flushViewportX, _flushViewportY, _flushViewportWidth, _flushViewportHeight);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        BlendFuncSeparate(call.BlendFunc);
        SetUniforms(call.UniformOffset, call.Image, bindTexture: false);
        SetCoverageOrigin(scissorX, scissorY);
        BindTexture(_coverageTex);
        // Composite via the bounds quad (4 verts as TriangleStrip), not the stroke
        // geometry itself - see RenderStroke for the rationale (avoid SrcOver double-
        // blend at sharp corners where segment quads/joins overlap).
        if (call.TriangleCount > 0)
            GL.DrawArrays(PrimitiveType.TriangleStrip, call.TriangleOffset, call.TriangleCount);
        GL.Enable(EnableCap.CullFace);
        _boundTexture = -1;
    }

    private void SetCoverageOrigin(int x, int y)
    {
        Span<float> origin = stackalloc float[2] { x, y };
        GL.Uniform2(_shader.LocCoverageOrigin, 1, origin);
    }

    private static void SetSimpleUniform(float[] frag, ref NVGscissorState scissor, float fringe)
    {
        Array.Clear(frag);

        if (scissor.Extent[0] < -0.5f || scissor.Extent[1] < -0.5f)
        {
            SetUniformVec4(frag, 0, 0, 0, 0, 0);
            SetUniformVec4(frag, 1, 0, 0, 0, 0);
            SetUniformVec4(frag, 2, 0, 0, 0, 0);
            SetUniformVec4(frag, 8, 1.0f, 1.0f, 1.0f, 1.0f);
        }
        else
        {
            Span<float> invxform = stackalloc float[6];
            NVGMath.TransformInverse(invxform, scissor.Xform);
            SetUniformMat3x4(frag, 0, invxform);

            var sx = MathF.Sqrt(scissor.Xform[0] * scissor.Xform[0] + scissor.Xform[2] * scissor.Xform[2]) / fringe;
            var sy = MathF.Sqrt(scissor.Xform[1] * scissor.Xform[1] + scissor.Xform[3] * scissor.Xform[3]) / fringe;
            SetUniformVec4(frag, 8, scissor.Extent[0], scissor.Extent[1], sx, sy);
        }

        SetUniformValue(frag, 12, 1, -1.0f); // strokeThr
        SetUniformValue(frag, 12, 3, (float)GLNVGShaderType.Simple);
    }

    private void CheckError(string label)
    {
        if (!_debugChecks)
        {
            return;
        }

        var error = GL.GetError();
        if (error != 0)
        {
            Console.Error.WriteLine($"[MewVG.GL] GL error 0x{error:X} after {label}");
        }
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

    private void SetUniforms(int uniformOffset, int image, bool bindTexture = true, bool applyClipMask = true)
    {
        GL.Uniform4(_shader.LocFrag, UniformArraySize, _uniforms[uniformOffset].Data);

        if (bindTexture)
        {
            var index = -1;
            if (image != 0)
            {
                TryFindTexture(image, out index);
            }

            if (index < 0)
            {
                TryFindTexture(_dummyTex, out index);
            }

            BindTexture(index >= 0 ? _textures[index].Tex : 0);
        }

        var useClipMask = applyClipMask && _clipMaskActive;
        if (!useClipMask)
        {
            GL.Uniform1(_shader.LocClipEnabled, 0);
        }
        else if (_clipMaskEmpty)
        {
            GL.Uniform1(_shader.LocClipEnabled, 2);
        }
        else
        {
            BindClipMask(_flushViewportX + _clipMaskX, _flushViewportY + _clipMaskY,
                _clipMaskWidth, _clipMaskHeight);
        }

        CheckError("set uniforms");
    }

    private void BindClipMask(int originX, int originY, int width, int height)
    {
        GL.Uniform1(_shader.LocClipEnabled, 1);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _clipMaskTextures[_clipMaskIndex]);
        GL.ActiveTexture(TextureUnit.Texture0);
        Span<float> origin = stackalloc float[2] { originX, originY };
        Span<float> size = stackalloc float[2] { width, height };
        GL.Uniform2(_shader.LocClipOrigin, 1, origin);
        GL.Uniform2(_shader.LocClipSize, 1, size);
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
        SetUniformVec4(frag, 12, strokeMult, strokeThr, 0.0f, 0.0f);

        if (paint.PaintKind == (int)NVGpaintKind.GradientRadial)
        {
            if (!TryFindTexture(paint.Image, out _))
            {
                return false;
            }

            SetUniformVec4(frag, 10, paint.Center[0], paint.Center[1], paint.Radius2[0], paint.Radius2[1]);
            SetUniformVec4(frag, 11, paint.Focal[0], paint.Focal[1], paint.SpreadMethod, 0.0f);
            SetUniformValue(frag, 12, 2, 0.0f);
            SetUniformValue(frag, 12, 3, (float)GLNVGShaderType.GradientRadial);
            NVGMath.TransformInverse(invxform, paint.Xform);
        }
        else if (paint.PaintKind == (int)NVGpaintKind.GradientLinear)
        {
            if (!TryFindTexture(paint.Image, out _))
            {
                return false;
            }

            SetUniformVec4(frag, 10, paint.Center[0], paint.Center[1], 0.0f, 0.0f);
            SetUniformVec4(frag, 11, paint.Focal[0], paint.Focal[1], paint.SpreadMethod, 0.0f);
            SetUniformValue(frag, 12, 2, 0.0f);
            SetUniformValue(frag, 12, 3, (float)GLNVGShaderType.GradientLinear);
            NVGMath.TransformInverse(invxform, paint.Xform);
        }
        else if (paint.Image != 0)
        {
            if (!TryFindTexture(paint.Image, out var index))
            {
                return false;
            }

            ref var tex = ref _textures[index];
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

            SetUniformValue(frag, 12, 3, (float)GLNVGShaderType.FillImg);
            // BGRA textures sample to (R,G,B,A) the same as RGBA - the GL_BGRA + REV upload
            // already swizzled at upload time. So texType is colour (0/1) for both formats;
            // only ALPHA textures take texType=2 (replicate red to alpha).
            if (tex.Type == NVGtextureType.RGBA || tex.Type == NVGtextureType.BGRA)
            {
                SetUniformValue(frag, 12, 2, (tex.Flags & NVGimageFlags.Premultiplied) != 0 ? 0.0f : 1.0f);
            }
            else
            {
                SetUniformValue(frag, 12, 2, 2.0f);
            }
        }
        else
        {
            SetUniformValue(frag, 12, 3, (float)GLNVGShaderType.FillGrad);
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

    private bool TryFindTexture(int id, out int index)
    {
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].Id == id)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private int? AllocTexture()
    {
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].Id == 0)
            {
                ref var tex = ref _textures[i];
                tex = default;
                tex.Id = ++_textureId;
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
        ref var newTex = ref _textures[index];
        newTex = default;
        newTex.Id = ++_textureId;
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

        var offset = _uniformCount;
        _uniformCount += n;
        return offset;
    }

    private void CreateResources()
    {
        const string header = "#version 140\n" +
                        "#define UNIFORMARRAY_SIZE 13\n\n";

        const string fillVertShader =
            "\tuniform vec2 viewSize;\n" +
            "\tin vec2 vertex;\n" +
            "\tin vec2 tcoord;\n" +
            "\tout vec2 ftcoord;\n" +
            "\tout vec2 fpos;\n" +
            "void main(void) {\n" +
            "\tftcoord = tcoord;\n" +
            "\tfpos = vertex;\n" +
            "\tgl_Position = vec4(2.0*vertex.x/viewSize.x - 1.0, 1.0 - 2.0*vertex.y/viewSize.y, 0, 1);\n" +
            "}\n";

        const string fillFragShader =
            "\tuniform vec4 frag[UNIFORMARRAY_SIZE];\n" +
            "\tuniform sampler2D tex;\n" +
            "\tuniform vec2 coverageOrigin;\n" +
            "\tuniform sampler2D clipTex;\n" +
            "\tuniform int clipEnabled;\n" +
            "\tuniform vec2 clipOrigin;\n" +
            "\tuniform vec2 clipSize;\n" +
            "\tuniform sampler2D maskTex;\n" +
            "\tuniform int maskEnabled;\n" +
            "\tuniform vec2 maskOrigin;\n" +
            "\tuniform vec2 maskSize;\n" +
            "\tuniform float maskScale;\n" +
            "\tin vec2 ftcoord;\n" +
            "\tin vec2 fpos;\n" +
            "\tout vec4 outColor;\n" +
            "\t#define scissorMat mat3(frag[0].xyz, frag[1].xyz, frag[2].xyz)\n" +
            "\t#define paintMat mat3(frag[3].xyz, frag[4].xyz, frag[5].xyz)\n" +
            "\t#define innerCol frag[6]\n" +
            "\t#define outerCol frag[7]\n" +
            "\t#define scissorExt frag[8].xy\n" +
            "\t#define scissorScale frag[8].zw\n" +
            "\t#define extent frag[9].xy\n" +
            "\t#define radius frag[9].z\n" +
            "\t#define feather frag[9].w\n" +
            "\t#define gradientCenter frag[10].xy\n" +
            "\t#define gradientRadii frag[10].zw\n" +
            "\t#define gradientFocal frag[11].xy\n" +
            "\t#define gradientSpread int(frag[11].z)\n" +
            "\t#define strokeMult frag[12].x\n" +
            "\t#define strokeThr frag[12].y\n" +
            "\t#define texType int(frag[12].z)\n" +
            "\t#define type int(frag[12].w)\n" +
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
            "float clipMask() {\n" +
            "\tif (clipEnabled == 0) return 1.0;\n" +
            "\tif (clipEnabled == 2) return 0.0;\n" +
            "\tvec2 p = gl_FragCoord.xy - clipOrigin;\n" +
            "\tif (p.x < 0.0 || p.y < 0.0 || p.x >= clipSize.x || p.y >= clipSize.y) return 0.0;\n" +
            "\treturn texelFetch(clipTex, ivec2(p), 0).r;\n" +
            "}\n" +
            "float maskCoverage() {\n" +
            "\tif (maskEnabled == 0) return 1.0;\n" +
            "\tvec2 p = (fpos - maskOrigin) * maskScale;\n" +
            "\tif (p.x < 0.0 || p.y < 0.0 || p.x >= maskSize.x || p.y >= maskSize.y) return 0.0;\n" +
            "\treturn texelFetch(maskTex, ivec2(p), 0).r;\n" +
            "}\n" +
            "\n" +
            "float gradientRadialT(vec2 p, vec2 center, vec2 focal, vec2 radii, int spread) {\n" +
            "\tvec2 np = (p - center) / radii;\n" +
            "\tvec2 nf = (focal - center) / radii;\n" +
            "\tvec2 d = np - nf;\n" +
            "\tfloat a = dot(d, d);\n" +
            "\tif (a <= 1e-6) return 0.0;\n" +
            "\tfloat b = 2.0 * dot(nf, d);\n" +
            "\tfloat c = dot(nf, nf) - 1.0;\n" +
            "\tfloat disc = b * b - 4.0 * a * c;\n" +
            "\tif (disc <= 0.0) return 1.0;\n" +
            "\tfloat u = (-b + sqrt(disc)) / (2.0 * a);\n" +
            "\tif (u <= 1e-6) return 1.0;\n" +
            "\tfloat t = 1.0 / u;\n" +
            "\tif (spread == 0) return clamp(t, 0.0, 1.0);\n" +
            "\tif (spread == 2) return fract(max(t, 0.0));\n" +
            "\tfloat r = mod(max(t, 0.0), 2.0);\n" +
            "\treturn r <= 1.0 ? r : 2.0 - r;\n" +
            "}\n" +
            "float gradientLinearT(vec2 p, vec2 startPt, vec2 endPt, int spread) {\n" +
            "\tvec2 axis = endPt - startPt;\n" +
            "\tfloat len2 = dot(axis, axis);\n" +
            "\tif (len2 <= 1e-6) return 0.0;\n" +
            "\tfloat t = dot(p - startPt, axis) / len2;\n" +
            "\tif (spread == 0) return clamp(t, 0.0, 1.0);\n" +
            "\tif (spread == 2) return fract(max(t, 0.0));\n" +
            "\tfloat r = mod(max(t, 0.0), 2.0);\n" +
            "\treturn r <= 1.0 ? r : 2.0 - r;\n" +
            "}\n" +
            "#ifdef EDGE_AA\n" +
            "float strokeMask() {\n" +
            "\tif (strokeMult < 0.0) {\n" +
            "\t\treturn clamp(ftcoord.x + 0.5, 0.0, 1.0) * min(1.0, ftcoord.y);\n" +
            "\t}\n" +
            "\treturn clamp((1.0-abs(ftcoord.x*2.0-1.0))*strokeMult, 0.0, 1.0) * min(1.0, ftcoord.y);\n" +
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
            "\t} else if (type == 6) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat t = gradientRadialT(pt, gradientCenter, gradientFocal, gradientRadii, gradientSpread);\n" +
            "\t\tvec4 color = texture(tex, vec2(t, 0.5));\n" +
            "\t\tcolor *= innerCol;\n" +
            "\t\tcolor *= strokeAlpha * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t} else if (type == 7) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat t = gradientLinearT(pt, gradientCenter, gradientFocal, gradientSpread);\n" +
            "\t\tvec4 color = texture(tex, vec2(t, 0.5));\n" +
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
            "\t} else if (type == 4) {\n" +
            "\t\tresult = vec4(strokeAlpha);\n" +
            "\t} else if (type == 5) {\n" +
            "\t\tfloat coverage = texelFetch(tex, ivec2(gl_FragCoord.xy - coverageOrigin), 0).r;\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat d = clamp((sdroundrect(pt, extent, radius) + feather*0.5) / feather, 0.0, 1.0);\n" +
            "\t\tvec4 color = mix(innerCol,outerCol,d);\n" +
            "\t\tcolor *= coverage * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t}\n" +
            "\toutColor = result * clipMask() * maskCoverage();\n" +
            "}\n";

        var opts = "#define EDGE_AA 1\n";

        _shader = CreateShader("shader", header, opts, fillVertShader, fillFragShader);
        GetUniforms(ref _shader);

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        _dummyTex = CreateTexture(NVGtextureType.Alpha, 1, 1, 0, ReadOnlySpan<byte>.Empty);

        CheckError("create done");
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
        shader.LocCoverageOrigin = GL.GetUniformLocation(shader.Program, "coverageOrigin");
        shader.LocClipTex = GL.GetUniformLocation(shader.Program, "clipTex");
        shader.LocClipEnabled = GL.GetUniformLocation(shader.Program, "clipEnabled");
        shader.LocClipOrigin = GL.GetUniformLocation(shader.Program, "clipOrigin");
        shader.LocClipSize = GL.GetUniformLocation(shader.Program, "clipSize");
        shader.LocMaskTex = GL.GetUniformLocation(shader.Program, "maskTex");
        shader.LocMaskEnabled = GL.GetUniformLocation(shader.Program, "maskEnabled");
        shader.LocMaskOrigin = GL.GetUniformLocation(shader.Program, "maskOrigin");
        shader.LocMaskSize = GL.GetUniformLocation(shader.Program, "maskSize");
        shader.LocMaskScale = GL.GetUniformLocation(shader.Program, "maskScale");
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


        if (_coverageFbo != 0) GL.DeleteFramebuffer(_coverageFbo);
        if (_coverageTex != 0) GL.DeleteTexture(_coverageTex);
        if (_coverageStencilRb != 0) GL.DeleteRenderbuffer(_coverageStencilRb);
        for (var i = 0; i < 2; i++)
        {
            if (_clipMaskFbos[i] != 0) GL.DeleteFramebuffer(_clipMaskFbos[i]);
            if (_clipMaskTextures[i] != 0) GL.DeleteTexture(_clipMaskTextures[i]);
        }

        // Mask images live in the texture table; releasing them first keeps the table loop below
        // from deleting the same names again.
        _maskImages.Dispose();

        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].Tex != 0 && (_textures[i].Flags & NVGimageFlags.NoDelete) == 0)
            {
                GL.DeleteTexture(_textures[i].Tex);
            }
        }
    }
}
