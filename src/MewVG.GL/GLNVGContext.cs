// NanoVG OpenGL backend (GL3 core profile)
// Ported from nanovg_gl.h

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        // The value the program's clipEnabled uniform holds; a new program starts at 0 as the uniform does.
        public int ClipEnabled;
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
        public MewVGImageFlags Flags;
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
        // The call draws through the coverage scratch instead of directly (see RenderFill/RenderStroke).
        public bool UsesCoverageAA;
        public int MergedFringeOffset;
        public int MergedFringeCount;
        public bool MergedFringeIsStrip;
        public int MergedStrokeOffset;
        public int MergedStrokeCount;
        public bool MergedStrokeIsStrip;
        // Device box of every vertex the call draws, which tells whether a later call may be moved in front of it.
        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;
        // Next call drawn in the same batch, or -1 (see BuildBatches).
        public int NextInBatch;
        // Stored geometry the fill's vertices came from and the offset they were drawn at (see TryAddFillInstance).
        public object? Source;
        public int SourceVersion;
        public Vector2 SourceOffset;
        // Instances the fill is drawn as, from _instances; 0 when it is drawn once as recorded.
        public int InstanceOffset;
        public int InstanceCount;
        public bool InstanceTinted;
        // The unmoved geometry the instances draw, as one triangle list (see AppendInstanceGeometry).
        public int InstanceVertexOffset;
        public int InstanceVertexCount;
    }

    // One more draw of a fill's geometry: moved by Offset, its colour times Tint.
    private struct GLNVGInstance
    {
        public Vector2 Offset;
        public Vector4 Tint;
    }

    /// <summary>Calls the flush draws as one: the first call's state, every member's geometry in member order.</summary>
    private struct GLNVGBatch
    {
        public int FirstCall;
        public int LastCall;
        public int MemberCount;
        public float MinX;
        public float MinY;
        public float MaxX;
        public float MaxY;
        // Moves no call across it: it changes state the calls after it draw under, or draws through other targets.
        public bool Barrier;
        // Takes no further call: its call is too large to be worth copying (see BATCH_MAX_CALL_VERTICES).
        public bool Closed;
        // Members differ in their colour, which then goes per vertex while the uniforms hold white.
        public bool Tinted;
        public int VertexOffset;
        public int VertexCount;
        public int UniformOffset;
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
        RoundedRect = 8,
    }

    private const int TINT_ATTRIBUTE = 2;
    private const int INSTANCE_OFFSET_ATTRIBUTE = 3;
    // Flushes in a row under a quarter of the vertex buffer capacity before it shrinks.
    private const int VBO_SHRINK_FLUSHES = 120;
    // MEWVG_GL_BATCH=0 draws every call on its own (A/B comparison).
    private static readonly bool _batchingOff = Environment.GetEnvironmentVariable("MEWVG_GL_BATCH") == "0";
    // How many batches back a call looks for one to join; each step is one bounds test.
    private const int BATCH_LOOKBACK = 10;
    // A call with more vertices than this is drawn on its own: copying its geometry into a batch costs more than the
    // draw it saves.
    private const int BATCH_MAX_CALL_VERTICES = 128;
    // A batch of fewer calls is drawn call by call: copying their geometry costs more than the draws it saves.
    private const int BATCH_MIN_MEMBERS = 8;
    // Index of innerCol in the fragment uniforms; outerCol follows it.
    private const int INNER_COLOR_INDEX = 6 * 4;
    // MEWVG_GL_DEBUG=1 turns on the glGetError check after each state change.
    private static readonly bool _debugChecks = Environment.GetEnvironmentVariable("MEWVG_GL_DEBUG") == "1";
    // Fill programs: the variants that drop the branches a draw does not need, then the uber shader
    // that handles every combination (see GLNVGShaderSource). Variants compile on first use.
    private const int VARIANT_FLAG_SCISSOR = 1;
    private const int VARIANT_FLAG_CLIP = 2;
    private const int VARIANT_FLAG_MASK = 4;
    private const int VARIANT_FLAG_FULL_COVERAGE = 8;
    private const int VARIANT_FLAG_TINT = 16;
    private const int VARIANT_FLAG_COUNT = 32;
    private const int VARIANT_PAINT_SOLID = 1;
    private const int VARIANT_PAINT_IMAGE = 2;
    private const int VARIANT_PAINT_IMAGE_TRIANGLES = 3;
    private const int VARIANT_PAINT_BOX_GRADIENT = 4;
    private const int VARIANT_PAINT_LINEAR_GRADIENT = 5;
    private const int VARIANT_PAINT_RADIAL_GRADIENT = 6;
    private const int VARIANT_PAINT_SIMPLE = 7;
    private const int VARIANT_PAINT_COVERAGE_OUTPUT = 8;
    private const int VARIANT_PAINT_COVERAGE_COMPOSITE = 9;
    private const int VARIANT_PAINT_COVERAGE_SOLID = 10;
    private const int VARIANT_PAINT_IMAGE_UNPREMULTIPLIED = 11;
    private const int VARIANT_PAINT_IMAGE_ALPHA = 12;
    private const int VARIANT_PAINT_IMAGE_TRIANGLES_UNPREMULTIPLIED = 13;
    private const int VARIANT_PAINT_IMAGE_TRIANGLES_ALPHA = 14;
    private const int VARIANT_PAINT_ROUNDED_RECT = 15;
    private const int VARIANT_PAINT_COUNT = 16;
    // The inner half extent of a filled rounded rectangle: every point lies far outside a box this inside out.
    private const float EMPTY_INNER_HALF_EXTENT = -1.0e4f;
    private const int UBER_SHADER = VARIANT_PAINT_COUNT * VARIANT_FLAG_COUNT;
    // MEWVG_GL_UBERSHADER=1 draws everything with the uber shader (A/B comparison, driver workaround).
    private static readonly bool _uberShaderOnly = Environment.GetEnvironmentVariable("MEWVG_GL_UBERSHADER") == "1";
    private readonly GLNVGShader[] _programs = new GLNVGShader[UBER_SHADER + 1];
    // Flush in which each program last had its per-flush uniforms set.
    private readonly int[] _programFlush = new int[UBER_SHADER + 1];
    private readonly bool[] _variantFailed = new bool[UBER_SHADER];
    private int _flushNumber;
    private GLNVGBatch[] _batches = new GLNVGBatch[128];
    private GLNVGInstance[] _instances = new GLNVGInstance[64];
    private int _instanceCount;
    // Uniforms of a fill that may become an instance, worked out before it is recorded.
    private readonly float[] _instanceUniforms = new float[UniformFloatCount];
    // Byte offsets in the vertex buffer of this flush's tints (-1 for none) and instances.
    private int _tintPointer = -1;
    private int _instanceBase;
    private int _batchCount;
    // Tint of each vertex a tinted batch draws, indexed like _verts.
    private Vector4[] _tints = Array.Empty<Vector4>();
    private bool _tintArrayEnabled;
    private int _currentProgram = -1;
    private bool _clipUnitBound;

    private int _vao;
    private int _vbo;

    private GLNVGTexture[] _textures = Array.Empty<GLNVGTexture>();
    private int _textureCount;
    // Where each live texture id sits in _textures, and the slots deleted textures left, so that finding a
    // texture by id or a slot for a new one does not scan the table.
    private readonly Dictionary<int, int> _textureSlots = new();
    private readonly Stack<int> _freeTextureSlots = new();
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
    // The GL name of the dummy texture, bound for every call that samples none.
    private int _dummyTexName;

    // coverage buffer for transparent fills
    private int _coverageFbo;
    private int _coverageTex;
    private int _vboCapacity;
    // Flushes in a row that used under a quarter of the capacity.
    private int _vboSmallFlushes;
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

    // What each mask texture holds: a hash of the clip drawn into it, the box it covers, and the mask it was
    // intersected with. Drawing a clip that a texture already holds is skipped, since writing a texture the
    // pending drawing still reads makes the GPU driver submit that drawing early.
    private readonly long[] _clipMaskContents = new long[2];
    private readonly bool[] _clipMaskContentsKnown = new bool[2];
    private bool _clipMaskActive;
    private bool _clipMaskEmpty;
    private int _clipMaskX;
    private int _clipMaskY;
    private int _clipMaskWidth;
    private int _clipMaskHeight;

    // cached state
    private int _boundTexture;
    private GLNVGBlend _blendFunc;
    private bool _disposed;

    // captured once per Flush() so the coverage passes don't re-query GL state per call
    private int _flushMainFbo;
    private int _flushViewportX;
    private int _flushViewportY;
    private int _flushViewportWidth;
    private int _flushViewportHeight;

    private static bool PaintHasTransparency(in MewVGPaint paint)
        => paint.InnerColor.A < 0.999f || paint.OuterColor.A < 0.999f;

    public GLNVGContext()
    {
        // GL orders commands: once a frame's calls are flushed, its images may be rewritten.
        _maskImages = new MaskImageCache(
            (width, height, coverage) => CreateTexture(NVGtextureType.Alpha, width, height, MewVGImageFlags.Nearest, coverage),
            (image, width, height, coverage) => UpdateTexture(image, 0, 0, width, height, coverage),
            DeleteTexture,
            framesInFlight: 1);
        GL.EnsureLoaded();
        CreateResources();
    }

    public int CreateImageFromHandle(int textureId, int width, int height, MewVGImageFlags flags)
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
        _instanceCount = 0;
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

        _flushNumber++;
        _currentProgram = -1;
        UseProgram(UBER_SHADER);

        // Nothing here relies on the winding of what it draws.
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        _boundTexture = 0;
        _blendFunc = new GLNVGBlend { SrcRGB = 0, SrcAlpha = 0, DstRGB = 0, DstAlpha = 0 };

        // Captured once here instead of per coverage call (FillWithCoverage/StrokeWithCoverage
        // used to re-query the bound framebuffer on every call and never restored the caller's
        // actual viewport, only the coverage-sized one).
        _flushMainFbo = GL.GetInteger(GetPName.FramebufferBinding);
        GL.GetViewport(out _flushViewportX, out _flushViewportY, out _flushViewportWidth, out _flushViewportHeight);

        // Batches draw their geometry from copies put after what the calls recorded, so they are formed before the upload.
        BuildBatches();
        var tintStart = AppendBatchGeometry(out var tintEnd);
        PrepareInstancedFills();

        // Upload vertex data. Orphan at a fixed, power-of-two capacity and copy in with SubData:
        // orphaning with a different size every flush defeats the driver's buffer-storage reuse
        // pool, so a burst of many small flushes leaves one freshly sized storage per flush in
        // flight instead of recycling a handful of same-sized ones.
        var vertexSize = Unsafe.SizeOf<NVGvertex>();
        var tintSize = Unsafe.SizeOf<Vector4>();
        int vertexBytes = _vertCount * vertexSize;
        // The tints follow the vertices, and the tint attribute starts tintStart tints before them, so it reads the tint
        // of vertex v at its own index.
        int tintBytes = tintStart < tintEnd ? (tintEnd - tintStart) * tintSize : 0;
        _instanceBase = vertexBytes + tintBytes;
        int uploadBytes = _instanceBase + _instanceCount * Unsafe.SizeOf<GLNVGInstance>();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        if (uploadBytes > _vboCapacity)
        {
            _vboCapacity = Math.Max(256 * 1024, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)uploadBytes));
            _vboSmallFlushes = 0;
        }
        else if (uploadBytes < _vboCapacity / 4 && ++_vboSmallFlushes >= VBO_SHRINK_FLUSHES)
        {
            // Every flush orphans the whole capacity, so one large frame would otherwise cost every small one after it.
            _vboCapacity = Math.Max(256 * 1024, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)uploadBytes * 2));
            _vboSmallFlushes = 0;
        }
        else if (uploadBytes >= _vboCapacity / 4)
        {
            _vboSmallFlushes = 0;
        }
        GL.BufferDataOrphan(BufferTarget.ArrayBuffer, _vboCapacity, BufferUsageHint.StreamDraw);
        GL.BufferSubData(BufferTarget.ArrayBuffer, 0, vertexBytes, _verts);

        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, vertexSize, 0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, vertexSize, sizeof(float) * 2);
        _tintPointer = -1;
        if (tintStart < tintEnd)
        {
            GL.BufferSubData(BufferTarget.ArrayBuffer, vertexBytes, tintBytes, _tints.AsSpan(tintStart, tintEnd - tintStart));
            _tintPointer = vertexBytes - tintStart * tintSize;
            GL.VertexAttribPointer(TINT_ATTRIBUTE, 4, VertexAttribPointerType.Float, false, tintSize, _tintPointer);
        }

        if (_instanceCount > 0)
        {
            GL.BufferSubData(BufferTarget.ArrayBuffer, _instanceBase, _instanceCount * Unsafe.SizeOf<GLNVGInstance>(),
                _instances.AsSpan(0, _instanceCount));
        }

        // Every draw that is not instanced reads no offset.
        GL.DisableVertexAttribArray(INSTANCE_OFFSET_ATTRIBUTE);
        GL.VertexAttrib4(INSTANCE_OFFSET_ATTRIBUTE, 0.0f, 0.0f, 0.0f, 0.0f);

        // Every call outside a tinted batch reads the tint as white.
        GL.DisableVertexAttribArray(TINT_ATTRIBUTE);
        GL.VertexAttrib4(TINT_ATTRIBUTE, 1.0f, 1.0f, 1.0f, 1.0f);
        _tintArrayEnabled = false;

        _clipMaskActive = false;
        _clipMaskEmpty = false;

        // Whatever ran since the last flush may have left a texture on the clip unit.
        _clipUnitBound = true;
        UnbindClipMask();

        for (var batchIndex = 0; batchIndex < _batchCount; batchIndex++)
        {
            ref readonly var batch = ref _batches[batchIndex];
            if (batch.MemberCount >= BATCH_MIN_MEMBERS)
            {
                DrawBatch(in batch);
                continue;
            }

            // A batch too small to be drawn as one draws its calls one by one, in its place and member order.
            SetTintArray(false);
            for (var member = batch.FirstCall; member >= 0; member = _calls[member].NextInBatch)
            {
                DrawCall(in _calls[member]);
            }
        }

        GL.DisableVertexAttribArray(0);
        GL.DisableVertexAttribArray(1);
        SetTintArray(false);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.UseProgram(0);
        _currentProgram = -1;
        BindTexture(0);

        // Release the clip and mask units too. Desktop GL tolerates a texture that stays bound while
        // the framebuffer owning it is drawn into; GLES3/WebGL2 calls that a feedback loop and drops
        // the draw, so clipped content rendered through an offscreen target disappears.
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);

        _vertCount = 0;
        _pathCount = 0;
        _callCount = 0;
        _uniformCount = 0;
        _batchCount = 0;
        _instanceCount = 0;

        CheckError("flush");
    }

    /// <summary>
    /// Groups the calls into batches, in draw order. A call joins an earlier batch drawn with the same state when
    /// nothing drawn between them overlaps it, so drawing it with that batch changes no pixel.
    /// </summary>
    private void BuildBatches()
    {
        _batchCount = 0;
        for (var callIndex = 0; callIndex < _callCount; callIndex++)
        {
            ref var call = ref _calls[callIndex];
            call.NextInBatch = -1;
            if (call.Type == GLNVGCallType.None)
            {
                continue;
            }

            var batchable = !_batchingOff && IsBatchable(in call);
            var joinable = batchable && call.InstanceCount == 0 && BatchVertexCount(in call) <= BATCH_MAX_CALL_VERTICES;
            if (joinable && TryFindBatch(in call, out var found, out var tinted))
            {
                ref var batch = ref _batches[found];
                _calls[batch.LastCall].NextInBatch = callIndex;
                batch.LastCall = callIndex;
                batch.MemberCount++;
                batch.Tinted |= tinted;
                batch.MinX = MathF.Min(batch.MinX, call.MinX);
                batch.MinY = MathF.Min(batch.MinY, call.MinY);
                batch.MaxX = MathF.Max(batch.MaxX, call.MaxX);
                batch.MaxY = MathF.Max(batch.MaxY, call.MaxY);
            }
            else
            {
                if (_batchCount == _batches.Length)
                {
                    Array.Resize(ref _batches, _batches.Length * 2);
                }

                _batches[_batchCount++] = new GLNVGBatch
                {
                    FirstCall = callIndex,
                    LastCall = callIndex,
                    MemberCount = 1,
                    MinX = call.MinX,
                    MinY = call.MinY,
                    MaxX = call.MaxX,
                    MaxY = call.MaxY,
                    Barrier = !batchable,
                    Closed = !joinable,
                    UniformOffset = call.UniformOffset,
                };
            }
        }
    }

    /// <summary>A call drawn by one draw of its own geometry under one program, which a batch can draw for it.</summary>
    private static bool IsBatchable(in GLNVGCall call) => call.Type switch
    {
        GLNVGCallType.Fill or GLNVGCallType.Stroke => !call.UsesCoverageAA,
        GLNVGCallType.Triangles => true,
        _ => false,
    };

    /// <summary>
    /// Finds the latest batch among the last few that the call can join: drawn the same way, with nothing drawn
    /// after it that the call overlaps.
    /// </summary>
    private bool TryFindBatch(in GLNVGCall call, out int found, out bool tinted)
    {
        // What the batches after the one looked at cover, empty at first.
        var laterMinX = float.PositiveInfinity;
        var laterMinY = float.PositiveInfinity;
        var laterMaxX = float.NegativeInfinity;
        var laterMaxY = float.NegativeInfinity;
        var stop = Math.Max(0, _batchCount - BATCH_LOOKBACK);
        for (var index = _batchCount - 1; index >= stop; index--)
        {
            ref readonly var batch = ref _batches[index];
            if (batch.Barrier)
            {
                break;
            }

            if (!batch.Closed && CanShareBatch(in _calls[batch.FirstCall], in call, out tinted))
            {
                found = index;
                return true;
            }

            laterMinX = MathF.Min(laterMinX, batch.MinX);
            laterMinY = MathF.Min(laterMinY, batch.MinY);
            laterMaxX = MathF.Max(laterMaxX, batch.MaxX);
            laterMaxY = MathF.Max(laterMaxY, batch.MaxY);
            if (call.MinX < laterMaxX && laterMinX < call.MaxX && call.MinY < laterMaxY && laterMinY < call.MaxY)
            {
                break;
            }
        }

        found = -1;
        tinted = false;
        return false;
    }

    /// <summary>
    /// True when the call draws with the state the batch's first call draws with, its colour aside when that colour
    /// can go per vertex (<paramref name="tinted"/>).
    /// </summary>
    private bool CanShareBatch(in GLNVGCall first, in GLNVGCall call, out bool tinted)
    {
        tinted = false;
        if (first.Type != call.Type || first.Image != call.Image || DrawsFullCoverage(in first) != DrawsFullCoverage(in call) ||
            first.BlendFunc.SrcRGB != call.BlendFunc.SrcRGB || first.BlendFunc.DstRGB != call.BlendFunc.DstRGB ||
            first.BlendFunc.SrcAlpha != call.BlendFunc.SrcAlpha || first.BlendFunc.DstAlpha != call.BlendFunc.DstAlpha)
        {
            return false;
        }

        return UniformsMatch(_uniforms[first.UniformOffset].Data, _uniforms[call.UniformOffset].Data, out tinted);
    }

    /// <summary>
    /// True when two calls' uniforms are the same, their colour aside when that colour can go per vertex or per instance
    /// (<paramref name="tinted"/>).
    /// </summary>
    private static bool UniformsMatch(float[] firstUniforms, float[] uniforms, out bool tinted)
    {
        tinted = false;

        // Uniforms compare bit for bit, which vectorizes where comparing them as floats does not.
        var firstFrag = MemoryMarshal.AsBytes(firstUniforms.AsSpan());
        var frag = MemoryMarshal.AsBytes(uniforms.AsSpan());
        const int COLORS_START = INNER_COLOR_INDEX * sizeof(float);
        const int COLORS_END = (INNER_COLOR_INDEX + 8) * sizeof(float);
        if (!firstFrag[..COLORS_START].SequenceEqual(frag[..COLORS_START]) ||
            !firstFrag[COLORS_END..].SequenceEqual(frag[COLORS_END..]))
        {
            return false;
        }

        if (firstFrag[COLORS_START..COLORS_END].SequenceEqual(frag[COLORS_START..COLORS_END]))
        {
            return true;
        }

        // The paint type lies outside the colours, so it is the same for both.
        tinted = TakesTint(uniforms) && TakesTint(firstUniforms);
        return tinted;
    }

    /// <summary>
    /// True when the paint's colour is its inner colour alone, or that colour times what it samples, so a white inner
    /// colour times a per-vertex tint draws it the same.
    /// </summary>
    private static bool TakesTint(float[] frag) => (GLNVGShaderType)(int)frag[12 * 4 + 3] switch
    {
        GLNVGShaderType.FillGrad => IsSolidPaint(frag),
        GLNVGShaderType.Img or GLNVGShaderType.RoundedRect => true,
        _ => false,
    };

    /// <summary>How many vertices the call adds to a batch, its strips turned into triangles.</summary>
    private int BatchVertexCount(in GLNVGCall call)
    {
        switch (call.Type)
        {
            case GLNVGCallType.Fill:
                var count = call.MergedFringeIsStrip ? StripToTriangleCount(call.MergedFringeCount) : call.MergedFringeCount;
                for (var index = 0; index < call.PathCount; index++)
                {
                    count += _paths[call.PathOffset + index].FillCount;
                }

                return count;
            case GLNVGCallType.Stroke:
                return call.MergedStrokeIsStrip ? StripToTriangleCount(call.MergedStrokeCount) : call.MergedStrokeCount;
            default:
                return call.TriangleCount;
        }
    }

    private static bool DrawsFullCoverage(in GLNVGCall call) => call.Type == GLNVGCallType.Fill && call.MergedFringeCount == 0;

    /// <summary>
    /// Copies the geometry of every batch of more than one call after the recorded vertices, as one triangle list in
    /// member order, and gives a tinted batch its tints and white uniforms. Returns the first vertex that has a tint, and
    /// in <paramref name="tintEnd"/> the end of the last.
    /// </summary>
    private int AppendBatchGeometry(out int tintEnd)
    {
        var tintStart = int.MaxValue;
        tintEnd = 0;
        for (var batchIndex = 0; batchIndex < _batchCount; batchIndex++)
        {
            ref var batch = ref _batches[batchIndex];
            if (batch.MemberCount < BATCH_MIN_MEMBERS)
            {
                continue;
            }

            batch.VertexOffset = _vertCount;
            for (var member = batch.FirstCall; member >= 0; member = _calls[member].NextInBatch)
            {
                var memberStart = _vertCount;
                AppendCallGeometry(in _calls[member]);
                if (batch.Tinted)
                {
                    var frag = _uniforms[_calls[member].UniformOffset].Data;
                    var tint = new Vector4(frag[INNER_COLOR_INDEX], frag[INNER_COLOR_INDEX + 1], frag[INNER_COLOR_INDEX + 2], frag[INNER_COLOR_INDEX + 3]);
                    if (_tints.Length < _vertCapacity)
                    {
                        Array.Resize(ref _tints, _vertCapacity);
                    }

                    _tints.AsSpan(memberStart, _vertCount - memberStart).Fill(tint);
                }
            }

            batch.VertexCount = _vertCount - batch.VertexOffset;
            if (batch.Tinted)
            {
                tintStart = Math.Min(tintStart, batch.VertexOffset);
                tintEnd = batch.VertexOffset + batch.VertexCount;
                batch.UniformOffset = AllocUniforms(1);
                var white = _uniforms[batch.UniformOffset].Data;
                _uniforms[_calls[batch.FirstCall].UniformOffset].Data.CopyTo(white, 0);
                white.AsSpan(INNER_COLOR_INDEX, 8).Fill(1.0f);
            }
        }

        return tintStart;
    }

    /// <summary>Appends what the call draws as a triangle list, in the order its own draw takes it.</summary>
    private void AppendCallGeometry(in GLNVGCall call)
    {
        switch (call.Type)
        {
            case GLNVGCallType.Fill:
                for (var index = 0; index < call.PathCount; index++)
                {
                    ref readonly var path = ref _paths[call.PathOffset + index];
                    if (path.FillCount > 0)
                    {
                        AppendVertices(path.FillOffset, path.FillCount, strip: false);
                    }
                }

                AppendVertices(call.MergedFringeOffset, call.MergedFringeCount, call.MergedFringeIsStrip);
                break;
            case GLNVGCallType.Stroke:
                AppendVertices(call.MergedStrokeOffset, call.MergedStrokeCount, call.MergedStrokeIsStrip);
                break;
            case GLNVGCallType.Triangles:
                AppendVertices(call.TriangleOffset, call.TriangleCount, strip: false);
                break;
        }
    }

    private void AppendVertices(int offset, int count, bool strip)
    {
        if (count <= 0)
        {
            return;
        }

        if (strip)
        {
            var destination = AllocVerts(StripToTriangleCount(count));
            var written = ConvertStripToTriangles(_verts.AsSpan(offset, count), _verts.AsSpan(destination), Vector2.Zero);
            _vertCount = destination + written;
        }
        else
        {
            var destination = AllocVerts(count);
            _verts.AsSpan(offset, count).CopyTo(_verts.AsSpan(destination, count));
        }
    }

    private void DrawCall(in GLNVGCall call)
    {
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
                if (call.InstanceCount > 1)
                {
                    DrawInstances(call);
                }
                else
                {
                    Fill(call);
                }

                break;
            case GLNVGCallType.Stroke:
                BlendFuncSeparate(call.BlendFunc);
                if (call.InstanceCount > 1)
                {
                    DrawInstances(call);
                }
                else
                {
                    Stroke(call);
                }

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

    private void DrawBatch(in GLNVGBatch batch)
    {
        ref readonly var first = ref _calls[batch.FirstCall];
        BlendFuncSeparate(first.BlendFunc);
        SetTintArray(batch.Tinted);
        SetUniforms(batch.UniformOffset, first.Image, fullCoverage: DrawsFullCoverage(in first), tint: batch.Tinted);
        GL.DrawArrays(PrimitiveType.Triangles, batch.VertexOffset, batch.VertexCount);
    }

    private void SetTintArray(bool enabled)
    {
        if (_tintArrayEnabled == enabled)
        {
            return;
        }

        _tintArrayEnabled = enabled;
        if (enabled)
        {
            GL.EnableVertexAttribArray(TINT_ATTRIBUTE);
        }
        else
        {
            GL.DisableVertexAttribArray(TINT_ATTRIBUTE);
        }
    }

    void INVGRenderer.BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio) => BeginFrame(windowWidth, windowHeight, devicePixelRatio);
    void INVGRenderer.Cancel() => Cancel();
    void INVGRenderer.Flush() => Flush();
    void INVGRenderer.RenderClip(ref NVGscissorState scissor, float fringe, ReadOnlySpan<float> bounds, ReadOnlySpan<NVGpathData> paths, ReadOnlySpan<NVGvertex> verts)
        => RenderClip(ref scissor, fringe, bounds, paths, verts);
    void INVGRenderer.ResetClip() => ResetClip();
    void INVGRenderer.RenderMaskFill(ref MewVGPaint paint, NVGcompositeOperationState compositeOperation, ref NVGscissorState scissor, float fringe, ReadOnlySpan<float> bounds, ReadOnlySpan<byte> coverage, int maskWidth, int maskHeight, float maskOriginX, float maskOriginY, object? cacheKey, int cacheVersion)
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
                var written = ConvertStripToTriangles(verts.Slice(path.StrokeOffset, path.NStroke), _verts.AsSpan(vertOffset), Vector2.Zero);
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
        ref MewVGPaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts,
        Vector2 offset,
        NVGGeometrySource source = default)
    {
        if (source.Owner != null && TryAddFillInstance(ref paint, compositeOperation, ref scissor, fringe, bounds, paths, verts, offset, source))
        {
            return;
        }

        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.Fill;
        call.PathOffset = AllocPaths(paths.Length);
        call.PathCount = paths.Length;
        call.Image = paint.Image;
        call.BlendFunc = BlendCompositeOperation(compositeOperation);
        call.TriangleCount = 4;

        var singlePath = paths.Length == 1;
        var maxVerts = 0;
        for (var i = 0; i < paths.Length; i++)
        {
            maxVerts += paths[i].NFill + (singlePath ? paths[i].NStroke : StripToTriangleCount(paths[i].NStroke));
        }

        maxVerts += call.TriangleCount;
        var vertOffset = AllocVerts(maxVerts);
#if DEBUG
        var bodyOffset = vertOffset;
#endif

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
                CopyMoved(verts.Slice(path.FillOffset, path.NFill), _verts.AsSpan(vertOffset), offset);
                vertOffset += path.NFill;
                if ((path.NFill % 3) != 0)
                {
                    throw new InvalidOperationException("MewVG.GL: fill vertices must form a triangle list; the renderer has no stencil winding route.");
                }
            }
        }

        // Pass 2: fringe vertices (strip?뭪riangle, or keep strip for single path)
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
                    CopyMoved(strokeSrc, _verts.AsSpan(vertOffset), offset);
                    copy.StrokeCount = strokeSrc.Length;
                    vertOffset += strokeSrc.Length;
                }
                else
                {
                    var written = ConvertStripToTriangles(strokeSrc, _verts.AsSpan(vertOffset), offset);
                    copy.StrokeCount = written;
                    vertOffset += written;
                }
            }
        }
        call.MergedFringeCount = vertOffset - call.MergedFringeOffset;
        SetFillBounds(ref call, bounds, call.MergedFringeCount > 0 ? fringe * 2.0f : 0.0f);
#if DEBUG
        if (call.MergedFringeCount == 0)
        {
            foreach (ref readonly var vert in _verts.AsSpan(bodyOffset, call.MergedFringeOffset - bodyOffset))
            {
                System.Diagnostics.Debug.Assert(vert.U == 0.5f && vert.V == 1.0f, "A fill without a fringe has a vertex off the body texcoord, which full coverage would draw too strongly.");
            }
        }
#endif

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

        var isConvexFill = IsConvexFill(paths);
        // A transparent non-convex fill goes through the coverage buffer, whose Max blending counts overlapping fringe
        // strips once. An opaque one is drawn directly: its fringe over its own body blends a colour over itself.
        call.UsesCoverageAA = !isConvexFill && paint.Image == 0 && PaintHasTransparency(paint);

        if (call.UsesCoverageAA)
        {
            // Coverage buffer path: 3 uniform sets
            call.UniformOffset = AllocUniforms(3);

            // uniformOffset+0: Simple (coverage build pass, interior coverage = 1.0)
            var simple = _uniforms[call.UniformOffset].Data;
            Array.Clear(simple);
            SetUniformVec4(simple, 8, 1.0f, 1.0f, 1.0f, 1.0f); // no scissor
            SetUniformValue(simple, 12, 1, -1.0f); // strokeThr
            SetUniformValue(simple, 12, 3, (float)GLNVGShaderType.Simple);

            // uniformOffset+1: Paint for composite (type = CoverageComposite), drawn over the quad alone.
            var compositeScissorCuts = ScissorCuts(ref scissor, bounds[0] - quadMargin, bounds[1] - quadMargin, bounds[2] + quadMargin, bounds[3] + quadMargin, fringe);
            if (!ConvertPaint(_uniforms[call.UniformOffset + 1].Data, ref paint, ref scissor, fringe, fringe, -1.0f, compositeScissorCuts))
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
            // The fringe of a fill reaches at most half a fringe times the miter limit past its points.
            var reach = fringe * 2.0f;
            var scissorCuts = ScissorCuts(ref scissor, bounds[0] - reach, bounds[1] - reach, bounds[2] + reach, bounds[3] + reach, fringe);
            call.UniformOffset = AllocUniforms(1);
            if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, fringe, fringe, -1.0f, scissorCuts))
            {
                return;
            }
            SetUniformValue(_uniforms[call.UniformOffset].Data, 12, 0, -1.0f); // strokeMult < 0 ??analytical fill coverage
            call.Source = source.Owner;
            call.SourceVersion = source.Version;
            call.SourceOffset = offset;
        }
    }

    /// <summary>
    /// Sets the box of a fill from the path bounds the core passes, which already include the replay offset, widened by
    /// how far the fringe reaches past the points.
    /// </summary>
    private static void SetFillBounds(ref GLNVGCall call, ReadOnlySpan<float> bounds, float reach)
    {
        call.MinX = bounds[0] - reach;
        call.MinY = bounds[1] - reach;
        call.MaxX = bounds[2] + reach;
        call.MaxY = bounds[3] + reach;
    }

    /// <summary>True when the fill's body is one convex path; paths with a fringe alone leave the body as it is.</summary>
    private static bool IsConvexFill(ReadOnlySpan<NVGpathData> paths)
    {
        var fillPathCount = 0;
        var fillPathIndex = -1;
        for (var index = 0; index < paths.Length; index++)
        {
            if (paths[index].NFill > 0)
            {
                fillPathCount++;
                fillPathIndex = index;
            }
        }

        return fillPathCount == 1 && paths[fillPathIndex].Convex;
    }

    /// <summary>
    /// Draws a fill of the same stored geometry as the fill just recorded as another instance of that one, moved by the
    /// offset it is drawn at, when the two differ in nothing else but a colour a tint can carry. Nothing is drawn between
    /// them, so the order is kept.
    /// </summary>
    private bool TryAddFillInstance(ref MewVGPaint paint, NVGcompositeOperationState compositeOperation, ref NVGscissorState scissor,
        float fringe, ReadOnlySpan<float> bounds, ReadOnlySpan<NVGpathData> paths, ReadOnlySpan<NVGvertex> verts, Vector2 offset,
        NVGGeometrySource source)
    {
        if (_batchingOff || !GL.SupportsInstancing || _callCount == 0)
        {
            return false;
        }

        ref var previous = ref _calls[_callCount - 1];
        var blend = BlendCompositeOperation(compositeOperation);
        if (previous.Type != GLNVGCallType.Fill || previous.UsesCoverageAA || !ReferenceEquals(previous.Source, source.Owner) ||
            previous.SourceVersion != source.Version || previous.Image != paint.Image ||
            previous.BlendFunc.SrcRGB != blend.SrcRGB || previous.BlendFunc.DstRGB != blend.DstRGB ||
            previous.BlendFunc.SrcAlpha != blend.SrcAlpha || previous.BlendFunc.DstAlpha != blend.DstAlpha)
        {
            return false;
        }

        // The same geometry with a transparent paint would go through the coverage buffer.
        if (paint.Image == 0 && PaintHasTransparency(paint) && !IsConvexFill(paths))
        {
            return false;
        }

        var reach = fringe * 2.0f;
        var scissorCuts = ScissorCuts(ref scissor, bounds[0] - reach, bounds[1] - reach, bounds[2] + reach, bounds[3] + reach, fringe);
        if (!ConvertPaint(_instanceUniforms, ref paint, ref scissor, fringe, fringe, -1.0f, scissorCuts))
        {
            return false;
        }

        SetUniformValue(_instanceUniforms, 12, 0, -1.0f);
        if (!UniformsMatch(_uniforms[previous.UniformOffset].Data, _instanceUniforms, out var tinted))
        {
            return false;
        }

        if (previous.InstanceCount == 0)
        {
            // Each instance moves the stored geometry by its whole offset, the sum the vertices would otherwise have been
            // copied with.
            AppendInstanceGeometry(ref previous, paths, verts);
            previous.InstanceOffset = _instanceCount;
            previous.InstanceCount = 1;
            AddInstance(previous.SourceOffset, _uniforms[previous.UniformOffset].Data);
        }

        AddInstance(offset, _instanceUniforms);
        previous.InstanceCount++;
        previous.InstanceTinted |= tinted;
        var fringeReach = previous.MergedFringeCount > 0 ? reach : 0.0f;
        previous.MinX = MathF.Min(previous.MinX, bounds[0] - fringeReach);
        previous.MinY = MathF.Min(previous.MinY, bounds[1] - fringeReach);
        previous.MaxX = MathF.Max(previous.MaxX, bounds[2] + fringeReach);
        previous.MaxY = MathF.Max(previous.MaxY, bounds[3] + fringeReach);
        return true;
    }

    /// <summary>
    /// Lays out a fill's or stroke's stored, unmoved geometry once as one triangle list, every body before every fringe
    /// strip as the call draws them, so one instanced draw draws each instance whole before the next.
    /// </summary>
    private void AppendInstanceGeometry(ref GLNVGCall call, ReadOnlySpan<NVGpathData> paths, ReadOnlySpan<NVGvertex> verts)
    {
        var start = _vertCount;
        foreach (ref readonly var path in paths)
        {
            if (path.NFill > 0)
            {
                var destination = AllocVerts(path.NFill);
                verts.Slice(path.FillOffset, path.NFill).CopyTo(_verts.AsSpan(destination));
            }
        }

        foreach (ref readonly var path in paths)
        {
            if (path.NStroke >= 3)
            {
                var destination = AllocVerts(StripToTriangleCount(path.NStroke));
                ConvertStripToTriangles(verts.Slice(path.StrokeOffset, path.NStroke), _verts.AsSpan(destination), Vector2.Zero);
            }
        }

        call.InstanceVertexOffset = start;
        call.InstanceVertexCount = _vertCount - start;
    }

    private void AddInstance(Vector2 offset, float[] frag)
    {
        if (_instanceCount == _instances.Length)
        {
            Array.Resize(ref _instances, _instances.Length * 2);
        }

        _instances[_instanceCount++] = new GLNVGInstance
        {
            Offset = offset,
            Tint = new Vector4(frag[INNER_COLOR_INDEX], frag[INNER_COLOR_INDEX + 1], frag[INNER_COLOR_INDEX + 2], frag[INNER_COLOR_INDEX + 3]),
        };
    }

    /// <summary>Copies vertices moved by an offset, in one pass.</summary>
    private static void CopyMoved(ReadOnlySpan<NVGvertex> source, Span<NVGvertex> destination, Vector2 offset)
    {
        if (offset == Vector2.Zero)
        {
            source.CopyTo(destination);
            return;
        }

        for (var index = 0; index < source.Length; index++)
        {
            destination[index] = Moved(source[index], offset);
        }
    }

    public void RenderStroke(
        ref MewVGPaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        float strokeWidth,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts,
        Vector2 offset,
        NVGGeometrySource source = default)
    {
        if (source.Owner != null && TryAddStrokeInstance(ref paint, compositeOperation, ref scissor, fringe, strokeWidth, paths, verts, offset, source))
        {
            return;
        }

        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.Stroke;
        call.PathOffset = AllocPaths(paths.Length);
        call.PathCount = paths.Length;
        call.Image = paint.Image;
        call.BlendFunc = BlendCompositeOperation(compositeOperation);
        var isConvexStroke = paths.Length == 1 && paths[0].Convex;
        call.UsesCoverageAA = !isConvexStroke && paint.Image == 0 && PaintHasTransparency(paint);

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
                    CopyMoved(strokeSrc, _verts.AsSpan(vertOffset), offset);
                    copy.StrokeCount = strokeSrc.Length;
                    vertOffset += strokeSrc.Length;
                }
                else
                {
                    var written = ConvertStripToTriangles(strokeSrc, _verts.AsSpan(vertOffset), offset);
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
        minX += offset.X;
        minY += offset.Y;
        maxX += offset.X;
        maxY += offset.Y;

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
        call.MinX = minX;
        call.MinY = minY;
        call.MaxX = maxX;
        call.MaxY = maxY;

        // The stroke geometry and the composite quad both lie within these bounds.
        var scissorCuts = ScissorCuts(ref scissor, minX, minY, maxX, maxY, fringe);
        if (call.UsesCoverageAA)
        {
            call.UniformOffset = AllocUniforms(2);
            if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, strokeWidth, fringe, -1.0f, scissorCuts))
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
            if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, strokeWidth, fringe, -1.0f, scissorCuts))
            {
                return;
            }

            call.Source = source.Owner;
            call.SourceVersion = source.Version;
            call.SourceOffset = offset;
        }
    }

    /// <summary>
    /// Draws a stroke of the same stored geometry as the stroke just recorded as another instance of that one, as
    /// <see cref="TryAddFillInstance"/> does for fills.
    /// </summary>
    private bool TryAddStrokeInstance(ref MewVGPaint paint, NVGcompositeOperationState compositeOperation, ref NVGscissorState scissor,
        float fringe, float strokeWidth, ReadOnlySpan<NVGpathData> paths, ReadOnlySpan<NVGvertex> verts, Vector2 offset,
        NVGGeometrySource source)
    {
        if (_batchingOff || !GL.SupportsInstancing || _callCount == 0)
        {
            return false;
        }

        ref var previous = ref _calls[_callCount - 1];
        var blend = BlendCompositeOperation(compositeOperation);
        if (previous.Type != GLNVGCallType.Stroke || previous.UsesCoverageAA || !ReferenceEquals(previous.Source, source.Owner) ||
            previous.SourceVersion != source.Version || previous.Image != paint.Image ||
            previous.BlendFunc.SrcRGB != blend.SrcRGB || previous.BlendFunc.DstRGB != blend.DstRGB ||
            previous.BlendFunc.SrcAlpha != blend.SrcAlpha || previous.BlendFunc.DstAlpha != blend.DstAlpha)
        {
            return false;
        }

        // The same geometry with a transparent paint would go through the coverage buffer.
        var isConvexStroke = paths.Length == 1 && paths[0].Convex;
        if (!isConvexStroke && paint.Image == 0 && PaintHasTransparency(paint))
        {
            return false;
        }

        // The box RenderStroke works out from the same vertices, for the same scissor test.
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        foreach (ref readonly var path in paths)
        {
            if (path.NStroke <= 0)
            {
                continue;
            }

            foreach (ref readonly var vertex in verts.Slice(path.StrokeOffset, path.NStroke))
            {
                if (vertex.X < minX) minX = vertex.X;
                if (vertex.Y < minY) minY = vertex.Y;
                if (vertex.X > maxX) maxX = vertex.X;
                if (vertex.Y > maxY) maxY = vertex.Y;
            }
        }

        if (float.IsPositiveInfinity(minX))
        {
            return false;
        }

        minX += offset.X;
        minY += offset.Y;
        maxX += offset.X;
        maxY += offset.Y;
        var scissorCuts = ScissorCuts(ref scissor, minX, minY, maxX, maxY, fringe);
        if (!ConvertPaint(_instanceUniforms, ref paint, ref scissor, strokeWidth, fringe, -1.0f, scissorCuts) ||
            !UniformsMatch(_uniforms[previous.UniformOffset].Data, _instanceUniforms, out var tinted))
        {
            return false;
        }

        if (previous.InstanceCount == 0)
        {
            AppendInstanceGeometry(ref previous, paths, verts);
            previous.InstanceOffset = _instanceCount;
            previous.InstanceCount = 1;
            AddInstance(previous.SourceOffset, _uniforms[previous.UniformOffset].Data);
        }

        AddInstance(offset, _instanceUniforms);
        previous.InstanceCount++;
        previous.InstanceTinted |= tinted;
        previous.MinX = MathF.Min(previous.MinX, minX);
        previous.MinY = MathF.Min(previous.MinY, minY);
        previous.MaxX = MathF.Max(previous.MaxX, maxX);
        previous.MaxY = MathF.Max(previous.MaxY, maxY);
        return true;
    }

    /// <summary>
    /// Draws a solid rounded rectangle, or its outline, as geometry around it whose fragments work out their coverage
    /// from signed distances to rounded rectangles; the texcoord carries each vertex's position relative to the centre.
    /// </summary>
    public bool TryRenderRoundedRect(
        ref MewVGPaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        in NVGRoundedRect shape)
    {
        var outline = shape.StrokeHalfWidth >= 0.0f;
        var strokeHalfWidth = Math.Max(shape.StrokeHalfWidth, 0.0f);
        var antialias = Math.Max(fringe, 1.0f / _devicePixelRatio);

        // The geometry reaches past the outer edge by a pixel of antialiasing.
        var outerHalfWidth = shape.HalfWidth + strokeHalfWidth;
        var outerHalfHeight = shape.HalfHeight + strokeHalfWidth;
        var coverHalfWidth = outerHalfWidth + antialias;
        var coverHalfHeight = outerHalfHeight + antialias;

        // Inside an outline lies a box that the inner edge leaves uncovered by at least a pixel; the outline is
        // drawn as the ring around it, so the fragments in the middle are never run.
        var innerHalfWidth = shape.HalfWidth - strokeHalfWidth;
        var innerHalfHeight = shape.HalfHeight - strokeHalfWidth;
        var innerTopLeft = Math.Max(shape.TopLeft - strokeHalfWidth, 0.0f);
        var innerTopRight = Math.Max(shape.TopRight - strokeHalfWidth, 0.0f);
        var innerBottomRight = Math.Max(shape.BottomRight - strokeHalfWidth, 0.0f);
        var innerBottomLeft = Math.Max(shape.BottomLeft - strokeHalfWidth, 0.0f);
        var innerRadius = Math.Max(Math.Max(innerTopLeft, innerTopRight), Math.Max(innerBottomRight, innerBottomLeft));
        var holeHalfWidth = innerHalfWidth - innerRadius - antialias;
        var holeHalfHeight = innerHalfHeight - innerRadius - antialias;
        var hollow = outline && holeHalfWidth > 0.0f && holeHalfHeight > 0.0f;

        ref var call = ref AllocCall();
        call.Type = GLNVGCallType.Triangles;
        call.Image = 0;
        call.BlendFunc = BlendCompositeOperation(compositeOperation);
        call.TriangleCount = hollow ? 24 : 6;
        call.TriangleOffset = AllocVerts(call.TriangleCount);
        var geometry = _verts.AsSpan(call.TriangleOffset, call.TriangleCount);
        if (hollow)
        {
            WriteRing(geometry, shape.CenterX, shape.CenterY, coverHalfWidth, coverHalfHeight, holeHalfWidth, holeHalfHeight);
        }
        else
        {
            WriteQuad(geometry, shape.CenterX, shape.CenterY, coverHalfWidth, coverHalfHeight);
        }

        call.MinX = shape.CenterX - coverHalfWidth;
        call.MinY = shape.CenterY - coverHalfHeight;
        call.MaxX = shape.CenterX + coverHalfWidth;
        call.MaxY = shape.CenterY + coverHalfHeight;

        call.UniformOffset = AllocUniforms(1);
        var frag = _uniforms[call.UniformOffset].Data;
        var scissorCuts = ScissorCuts(ref scissor, shape.CenterX - coverHalfWidth, shape.CenterY - coverHalfHeight,
            shape.CenterX + coverHalfWidth, shape.CenterY + coverHalfHeight, fringe);
        if (!ConvertPaint(frag, ref paint, ref scissor, fringe, fringe, -1.0f, scissorCuts))
        {
            call.Type = GLNVGCallType.None;
            return true;
        }

        // A hard edge is a ramp too steep to leave any pixel partly covered.
        var coverageScale = fringe > 0.0f ? 1.0f / fringe : 1.0e6f;
        SetUniformValue(frag, 9, 0, coverageScale);

        // Fills and outlines share one program, so a run of them never switches programs: a fill is an outline
        // whose inner rectangle lies so far inside out that it covers nothing.
        if (outline)
        {
            // Round corners grow and shrink with the outline; square ones stay square.
            SetUniformVec4(frag, 3, outerHalfWidth, outerHalfHeight, innerHalfWidth, innerHalfHeight);
            SetUniformVec4(frag, 4, GrowCorner(shape.TopLeft, strokeHalfWidth), GrowCorner(shape.TopRight, strokeHalfWidth),
                GrowCorner(shape.BottomRight, strokeHalfWidth), GrowCorner(shape.BottomLeft, strokeHalfWidth));
            SetUniformVec4(frag, 5, innerTopLeft, innerTopRight, innerBottomRight, innerBottomLeft);
        }
        else
        {
            SetUniformVec4(frag, 3, shape.HalfWidth, shape.HalfHeight, EMPTY_INNER_HALF_EXTENT, EMPTY_INNER_HALF_EXTENT);
            SetUniformVec4(frag, 4, shape.TopLeft, shape.TopRight, shape.BottomRight, shape.BottomLeft);
            SetUniformVec4(frag, 5, 0.0f, 0.0f, 0.0f, 0.0f);
        }

        SetUniformValue(frag, 12, 3, (float)GLNVGShaderType.RoundedRect);
        return true;
    }

    private static float GrowCorner(float radius, float amount) => radius > 0.0f ? radius + amount : 0.0f;

    /// <summary>Writes two triangles covering a box around a centre, with texcoords relative to the centre.</summary>
    private static void WriteQuad(Span<NVGvertex> output, float centerX, float centerY, float halfWidth, float halfHeight)
    {
        var topLeft = Relative(centerX, centerY, -halfWidth, -halfHeight);
        var topRight = Relative(centerX, centerY, halfWidth, -halfHeight);
        var bottomRight = Relative(centerX, centerY, halfWidth, halfHeight);
        var bottomLeft = Relative(centerX, centerY, -halfWidth, halfHeight);
        output[0] = topLeft;
        output[1] = topRight;
        output[2] = bottomRight;
        output[3] = topLeft;
        output[4] = bottomRight;
        output[5] = bottomLeft;
    }

    /// <summary>Writes eight triangles covering a box around a centre less a smaller box, with texcoords relative to the centre.</summary>
    private static void WriteRing(Span<NVGvertex> output, float centerX, float centerY, float halfWidth, float halfHeight, float holeHalfWidth, float holeHalfHeight)
    {
        Span<NVGvertex> outer =
        [
            Relative(centerX, centerY, -halfWidth, -halfHeight),
            Relative(centerX, centerY, halfWidth, -halfHeight),
            Relative(centerX, centerY, halfWidth, halfHeight),
            Relative(centerX, centerY, -halfWidth, halfHeight),
        ];
        Span<NVGvertex> hole =
        [
            Relative(centerX, centerY, -holeHalfWidth, -holeHalfHeight),
            Relative(centerX, centerY, holeHalfWidth, -holeHalfHeight),
            Relative(centerX, centerY, holeHalfWidth, holeHalfHeight),
            Relative(centerX, centerY, -holeHalfWidth, holeHalfHeight),
        ];
        var written = 0;
        for (var side = 0; side < 4; side++)
        {
            var next = (side + 1) % 4;
            output[written++] = outer[side];
            output[written++] = outer[next];
            output[written++] = hole[next];
            output[written++] = outer[side];
            output[written++] = hole[next];
            output[written++] = hole[side];
        }
    }

    private static NVGvertex Relative(float centerX, float centerY, float offsetX, float offsetY)
        => new(centerX + offsetX, centerY + offsetY, offsetX, offsetY);

    public void RenderTriangles(
        ref MewVGPaint paint,
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

        float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        for (var i = 0; i < verts.Length; i++)
        {
            ref readonly var vert = ref verts[i];
            minX = MathF.Min(minX, vert.X);
            minY = MathF.Min(minY, vert.Y);
            maxX = MathF.Max(maxX, vert.X);
            maxY = MathF.Max(maxY, vert.Y);
        }

        call.MinX = minX;
        call.MinY = minY;
        call.MaxX = maxX;
        call.MaxY = maxY;
        call.UniformOffset = AllocUniforms(1);
        var scissorCuts = verts.Length == 0 || ScissorCuts(ref scissor, minX, minY, maxX, maxY, fringe);
        if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, 1.0f, fringe, -1.0f, scissorCuts))
        {
            return;
        }

        SetUniformValue(_uniforms[call.UniformOffset].Data, 12, 3, (float)GLNVGShaderType.Img);
        MergeWithPreviousTriangles();
    }

    /// <summary>
    /// Folds the triangles call just added into the call before it when both sample the same image with the same
    /// blending and uniforms and their vertices follow each other, so runs of glyph quads become one draw.
    /// </summary>
    private void MergeWithPreviousTriangles()
    {
        if (_callCount < 2)
        {
            return;
        }

        ref var call = ref _calls[_callCount - 1];
        ref var previous = ref _calls[_callCount - 2];
        if (previous.Type != GLNVGCallType.Triangles
            || previous.Image != call.Image
            || previous.BlendFunc.SrcRGB != call.BlendFunc.SrcRGB
            || previous.BlendFunc.DstRGB != call.BlendFunc.DstRGB
            || previous.BlendFunc.SrcAlpha != call.BlendFunc.SrcAlpha
            || previous.BlendFunc.DstAlpha != call.BlendFunc.DstAlpha
            || previous.TriangleOffset + previous.TriangleCount != call.TriangleOffset
            || previous.UniformOffset + 1 != call.UniformOffset
            || call.UniformOffset != _uniformCount - 1
            || !_uniforms[previous.UniformOffset].Data.AsSpan().SequenceEqual(_uniforms[call.UniformOffset].Data))
        {
            return;
        }

        previous.TriangleCount += call.TriangleCount;
        previous.MinX = MathF.Min(previous.MinX, call.MinX);
        previous.MinY = MathF.Min(previous.MinY, call.MinY);
        previous.MaxX = MathF.Max(previous.MaxX, call.MaxX);
        previous.MaxY = MathF.Max(previous.MaxY, call.MaxY);
        _callCount--;
        _uniformCount--;
    }

    public void RenderMaskFill(
        ref MewVGPaint paint,
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
        var scissorCuts = ScissorCuts(ref scissor, bounds[0], bounds[1], bounds[2], bounds[3], fringe);
        if (!ConvertPaint(_uniforms[call.UniformOffset].Data, ref paint, ref scissor, fringe, fringe, -1.0f, scissorCuts))
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

        SetUniforms(call.UniformOffset, call.Image, mask: true, fullCoverage: true);

        ref var shader = ref _programs[_currentProgram];
        ref var mask = ref _textures[maskIndex];
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, mask.Tex);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.Uniform1(shader.LocMaskEnabled, 1);
        Span<float> origin = stackalloc float[2] { call.MaskOriginX, call.MaskOriginY };
        Span<float> size = stackalloc float[2] { mask.Width, mask.Height };
        GL.Uniform2(shader.LocMaskOrigin, 1, origin);
        GL.Uniform2(shader.LocMaskSize, 1, size);
        GL.Uniform1(shader.LocMaskScale, _devicePixelRatio);

        GL.DrawArrays(PrimitiveType.TriangleStrip, call.TriangleOffset, call.TriangleCount);

        GL.Uniform1(shader.LocMaskEnabled, 0);
    }

    public int CreateTexture(NVGtextureType type, int width, int height, MewVGImageFlags flags, ReadOnlySpan<byte> data)
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
        if ((flags & MewVGImageFlags.GenerateMipmaps) != 0
            && (flags & MewVGImageFlags.Premultiplied) == 0
            && (type == NVGtextureType.RGBA || type == NVGtextureType.BGRA)
            && data.Length >= width * height * 4)
        {
            uploadData = PremultiplyCopy(data, width, height);
            flags |= MewVGImageFlags.Premultiplied;
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

        if ((flags & MewVGImageFlags.GenerateMipmaps) != 0)
        {
            if ((flags & MewVGImageFlags.Nearest) != 0)
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
            if ((flags & MewVGImageFlags.Nearest) != 0)
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            }
        }

        if ((flags & MewVGImageFlags.Nearest) != 0)
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }
        else
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            (int)((flags & MewVGImageFlags.RepeatX) != 0 ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            (int)((flags & MewVGImageFlags.RepeatY) != 0 ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));

        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);

        if ((flags & MewVGImageFlags.GenerateMipmaps) != 0)
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
        if (!TryFindTexture(id, out var i))
        {
            return;
        }

        int glTex = _textures[i].Tex;
        if (glTex != 0 && (_textures[i].Flags & MewVGImageFlags.NoDelete) == 0)
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
        _textureSlots.Remove(id);
        _freeTextureSlots.Push(i);
    }

    /// <summary>Uploads <paramref name="data"/>, the rows of the rectangle tightly packed, into that rectangle of the texture.</summary>
    public bool UpdateTexture(int image, int x, int y, int width, int height, ReadOnlySpan<byte> data)
    {
        if (!TryFindTexture(image, out var index))
        {
            return false;
        }

        ref var tex = ref _textures[index];
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > tex.Width || y + height > tex.Height
            || data.Length < width * height * (tex.Type == NVGtextureType.Alpha ? 1 : 4))
        {
            return false;
        }

        BindTexture(tex.Tex);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, width);
        GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);

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
        if (call.UsesCoverageAA)
        {
            FillWithCoverage(call);
            return;
        }

        var paths = _paths.AsSpan(call.PathOffset, call.PathCount);

        // Without fringe vertices the call draws only body triangles, all at full coverage.
        SetUniforms(call.UniformOffset, call.Image, fullCoverage: call.MergedFringeCount == 0);
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
        if (fillStart >= 0 && fillCount > 0 && call.MergedFringeCount > 0 && !call.MergedFringeIsStrip &&
            fillStart + fillCount == call.MergedFringeOffset)
        {
            // The body and its fringe lie one after the other as triangles: one draw, in the same order.
            GL.DrawArrays(PrimitiveType.Triangles, fillStart, fillCount + call.MergedFringeCount);
            return;
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
    }

    /// <summary>Gives each tinted instanced call a copy of its uniforms with white colours, which the instance tints multiply.</summary>
    private void PrepareInstancedFills()
    {
        for (var callIndex = 0; callIndex < _callCount; callIndex++)
        {
            ref var call = ref _calls[callIndex];
            if (call.InstanceCount > 1 && call.InstanceTinted)
            {
                var source = call.UniformOffset;
                call.UniformOffset = AllocUniforms(1);
                var white = _uniforms[call.UniformOffset].Data;
                _uniforms[source].Data.CopyTo(white, 0);
                white.AsSpan(INNER_COLOR_INDEX, 8).Fill(1.0f);
            }
        }
    }

    /// <summary>Draws a fill or stroke once per instance, each moved by its offset and, when tinted, coloured by its tint.</summary>
    private void DrawInstances(in GLNVGCall call)
    {
        SetUniforms(call.UniformOffset, call.Image, fullCoverage: DrawsFullCoverage(in call), tint: call.InstanceTinted);
        var stride = Unsafe.SizeOf<GLNVGInstance>();
        var at = _instanceBase + call.InstanceOffset * stride;
        GL.VertexAttribPointer(INSTANCE_OFFSET_ATTRIBUTE, 2, VertexAttribPointerType.Float, false, stride, at);
        GL.VertexAttribDivisor(INSTANCE_OFFSET_ATTRIBUTE, 1);
        GL.EnableVertexAttribArray(INSTANCE_OFFSET_ATTRIBUTE);
        if (call.InstanceTinted)
        {
            GL.VertexAttribPointer(TINT_ATTRIBUTE, 4, VertexAttribPointerType.Float, false, stride, at + sizeof(float) * 2);
            GL.VertexAttribDivisor(TINT_ATTRIBUTE, 1);
            GL.EnableVertexAttribArray(TINT_ATTRIBUTE);
        }

        GL.DrawArraysInstanced(PrimitiveType.Triangles, call.InstanceVertexOffset, call.InstanceVertexCount, call.InstanceCount);

        GL.VertexAttribDivisor(INSTANCE_OFFSET_ATTRIBUTE, 0);
        GL.DisableVertexAttribArray(INSTANCE_OFFSET_ATTRIBUTE);
        if (call.InstanceTinted)
        {
            // Batches read the tints per vertex again.
            GL.VertexAttribDivisor(TINT_ATTRIBUTE, 0);
            GL.DisableVertexAttribArray(TINT_ATTRIBUTE);
            _tintArrayEnabled = false;
            if (_tintPointer >= 0)
            {
                GL.VertexAttribPointer(TINT_ATTRIBUTE, 4, VertexAttribPointerType.Float, false, Unsafe.SizeOf<Vector4>(), _tintPointer);
            }
        }
    }

    private void Stroke(in GLNVGCall call)
    {
        if (call.UsesCoverageAA)
        {
            StrokeWithCoverage(call);
            return;
        }

        var strokeMode = call.MergedStrokeIsStrip ? PrimitiveType.TriangleStrip : PrimitiveType.Triangles;

        SetUniforms(call.UniformOffset, call.Image);
        if (call.MergedStrokeCount > 0)
            GL.DrawArrays(strokeMode, call.MergedStrokeOffset, call.MergedStrokeCount);
    }

    private void Triangles(in GLNVGCall call)
    {
        SetUniforms(call.UniformOffset, call.Image);

        GL.DrawArrays(PrimitiveType.Triangles, call.TriangleOffset, call.TriangleCount);
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
        _clipMaskContentsKnown[index] = false;

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

    /// <summary>A hash of everything that decides what a clip draws into its mask.</summary>
    private long ClipMaskContents(in GLNVGCall call, int clipX, int clipY, int clipWidth, int clipHeight, long previous, bool intersects)
    {
        var hash = new HashCode();
        hash.Add(clipX);
        hash.Add(clipY);
        hash.Add(clipWidth);
        hash.Add(clipHeight);
        hash.Add(_flushViewportWidth);
        hash.Add(_flushViewportHeight);
        hash.Add(intersects);
        hash.Add(previous);
        var verts = _verts.AsSpan(call.MergedFringeOffset, call.MergedFringeCount);
        hash.AddBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(verts));
        long low = hash.ToHashCode();

        // A second, independent hash keeps two different clips from sharing a value by chance.
        var second = new HashCode();
        second.Add(call.MergedFringeCount);
        second.Add(low);
        for (var index = 0; index < verts.Length; index += 7)
        {
            second.Add(verts[index].X);
            second.Add(verts[index].Y);
        }

        return ((long)second.ToHashCode() << 32) | (uint)low;
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

        long contents = ClipMaskContents(call, clipX, clipY, clipWidth, clipHeight,
            previousActive ? _clipMaskContents[_clipMaskIndex] : 0, previousActive);
        if (_clipMaskContentsKnown[destination] && _clipMaskContents[destination] == contents)
        {
            _clipMaskIndex = destination;
            _clipMaskActive = true;
            _clipMaskEmpty = false;
            _clipMaskX = clipX;
            _clipMaskY = clipY;
            _clipMaskWidth = clipWidth;
            _clipMaskHeight = clipHeight;
            return;
        }

        _clipMaskContents[destination] = contents;
        _clipMaskContentsKnown[destination] = true;

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _clipMaskFbos[destination]);
        GL.Viewport(-clipX, -clipY, _flushViewportWidth, _flushViewportHeight);
        GL.Disable(EnableCap.ScissorTest);
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
        SetUniforms(call.UniformOffset, 0, applyClipMask: false, callerBindsClip: previousActive);
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
        if (_coverageFbo != 0) GL.DeleteFramebuffer(_coverageFbo);

        var defaultFbo = GL.GetInteger(GetPName.FramebufferBinding);

        _coverageTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _coverageTex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, width, height, 0, PixelFormat.Red, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

        _coverageFbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _coverageFbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _coverageTex, 0);

        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, defaultFbo);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            _boundTexture = 0;

            GL.DeleteTexture(_coverageTex);
            GL.DeleteFramebuffer(_coverageFbo);
            _coverageTex = 0;
            _coverageFbo = 0;
            _coverageTexWidth = 0;
            _coverageTexHeight = 0;

            throw new InvalidOperationException($"MewVG.GL: coverage framebuffer incomplete (status={status}).");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, defaultFbo);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        _boundTexture = 0;

        _coverageTexWidth = width;
        _coverageTexHeight = height;
    }

    private static int ConvertStripToTriangles(ReadOnlySpan<NVGvertex> strip, Span<NVGvertex> output, Vector2 offset)
    {
        if (strip.Length < 3) return 0;
        var written = 0;
        for (var i = 0; i < strip.Length - 2; i++)
        {
            if ((i & 1) == 0)
            {
                output[written++] = Moved(strip[i], offset);
                output[written++] = Moved(strip[i + 1], offset);
                output[written++] = Moved(strip[i + 2], offset);
            }
            else
            {
                output[written++] = Moved(strip[i + 1], offset);
                output[written++] = Moved(strip[i], offset);
                output[written++] = Moved(strip[i + 2], offset);
            }
        }

        return written;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NVGvertex Moved(NVGvertex vert, Vector2 offset) => new(vert.X + offset.X, vert.Y + offset.Y, vert.U, vert.V);

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
        GL.Clear(ClearBufferMask.ColorBufferBit);

        BlendFuncSeparate(new GLNVGBlend
        {
            SrcRGB = BlendingFactorSrc.One,
            DstRGB = BlendingFactorDest.One,
            SrcAlpha = BlendingFactorSrc.One,
            DstAlpha = BlendingFactorDest.One
        });
        GL.BlendEquation(BlendEquationMode.Max);

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

        SetUniforms(call.UniformOffset + 2, 0, applyClipMask: false);
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
        _boundTexture = -1;
    }

    private void SetCoverageOrigin(int x, int y)
    {
        Span<float> origin = stackalloc float[2] { x, y };
        GL.Uniform2(_programs[_currentProgram].LocCoverageOrigin, 1, origin);
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

    /// <summary>
    /// Makes the program for a set of uniforms current and loads them; <paramref name="fullCoverage"/> says every
    /// vertex the call draws carries the fill-body texcoord, so its stroke mask is 1 throughout.
    /// </summary>
    private void SetUniforms(int uniformOffset, int image, bool bindTexture = true, bool applyClipMask = true, bool mask = false, bool callerBindsClip = false, bool fullCoverage = false, bool tint = false)
    {
        var frag = _uniforms[uniformOffset].Data;
        var clip = applyClipMask && _clipMaskActive;
        // A caller that binds a clip itself (a nested clip reading the previous one) needs a program that samples it.
        UseProgram(callerBindsClip
            ? SelectProgram(frag, true, mask, clipEmpty: false, fullCoverage, tint)
            : SelectProgram(frag, clip, mask, _clipMaskEmpty, fullCoverage, tint));
        GL.Uniform4(_programs[_currentProgram].LocFrag, UniformArraySize, frag);

        if (bindTexture)
        {
            if (image != 0 && TryFindTexture(image, out var index))
            {
                BindTexture(_textures[index].Tex);
            }
            else
            {
                BindTexture(_dummyTexName);
            }
        }

        if (!clip)
        {
            SetClipEnabled(0);
            UnbindClipMask();
        }
        else if (_clipMaskEmpty)
        {
            SetClipEnabled(2);
            UnbindClipMask();
        }
        else
        {
            BindClipMask(_flushViewportX + _clipMaskX, _flushViewportY + _clipMaskY,
                _clipMaskWidth, _clipMaskHeight);
        }

        CheckError("set uniforms");
    }

    /// <summary>
    /// Drops the clip sampler's binding. The shader ignores it once clipEnabled is 0, but a mask
    /// texture left bound is still a feedback loop the moment its own framebuffer is drawn into,
    /// and GLES3/WebGL2 discards that draw instead of reading stale texels.
    /// </summary>
    private void UnbindClipMask()
    {
        if (!_clipUnitBound)
        {
            return;
        }

        _clipUnitBound = false;
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Sets the current program's clipEnabled uniform, skipping the call when it already holds the value.</summary>
    private void SetClipEnabled(int value)
    {
        ref var shader = ref _programs[_currentProgram];
        if (shader.ClipEnabled != value)
        {
            shader.ClipEnabled = value;
            GL.Uniform1(shader.LocClipEnabled, value);
        }
    }

    private void BindClipMask(int originX, int originY, int width, int height)
    {
        ref var shader = ref _programs[_currentProgram];
        SetClipEnabled(1);
        _clipUnitBound = true;
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _clipMaskTextures[_clipMaskIndex]);
        GL.ActiveTexture(TextureUnit.Texture0);
        Span<float> origin = stackalloc float[2] { originX, originY };
        Span<float> size = stackalloc float[2] { width, height };
        GL.Uniform2(shader.LocClipOrigin, 1, origin);
        GL.Uniform2(shader.LocClipSize, 1, size);
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

    private static MewVGColor Premultiply(MewVGColor color) => new MewVGColor(color.R * color.A, color.G * color.A, color.B * color.A, color.A);

    /// <summary>
    /// False when the scissor leaves every pixel the box can cover at full strength, so the call draws the
    /// same pixels without it and can take a program that has no scissor in it.
    /// </summary>
    private bool ScissorCuts(ref NVGscissorState scissor, float minX, float minY, float maxX, float maxY, float fringe)
        => NVGScissorCoverage.Cuts(ref scissor, minX, minY, maxX, maxY, fringe, _view[0], _view[1], _devicePixelRatio);

    private bool ConvertPaint(float[] frag, ref MewVGPaint paint, ref NVGscissorState scissor, float width, float fringe, float strokeThr, bool scissorCuts)
    {
        Span<float> invxform = stackalloc float[6];

        Array.Clear(frag);

        var inner = Premultiply(paint.InnerColor);
        var outer = Premultiply(paint.OuterColor);
        SetUniformVec4(frag, 6, inner.R, inner.G, inner.B, inner.A);
        SetUniformVec4(frag, 7, outer.R, outer.G, outer.B, outer.A);

        if (!scissorCuts || scissor.Extent[0] < -0.5f || scissor.Extent[1] < -0.5f)
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
            if ((tex.Flags & MewVGImageFlags.FlipY) != 0)
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
                SetUniformValue(frag, 12, 2, (tex.Flags & MewVGImageFlags.Premultiplied) != 0 ? 0.0f : 1.0f);
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
        if (id != 0 && _textureSlots.TryGetValue(id, out index))
        {
            return true;
        }

        index = -1;
        return false;
    }

    private int? AllocTexture()
    {
        int index;
        if (_freeTextureSlots.Count > 0)
        {
            index = _freeTextureSlots.Pop();
        }
        else
        {
            if (_textureCount + 1 > _textureCapacity)
            {
                var newCapacity = Math.Max(_textureCount + 1, 4) + _textureCapacity / 2;
                Array.Resize(ref _textures, newCapacity);
                _textureCapacity = newCapacity;
            }

            index = _textureCount++;
        }

        ref var newTex = ref _textures[index];
        newTex = default;
        newTex.Id = ++_textureId;
        _textureSlots[newTex.Id] = index;
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
        var header = GLNVGShaderSource.Header(GL.Profile);

        _programs[UBER_SHADER] = CreateShader("shader", header, string.Empty, GLNVGShaderSource.FILL_VERT_SHADER, GLNVGShaderSource.FILL_FRAG_SHADER);
        GetUniforms(ref _programs[UBER_SHADER]);

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        _dummyTex = CreateTexture(NVGtextureType.Alpha, 1, 1, 0, ReadOnlySpan<byte>.Empty);
        _dummyTexName = TryFindTexture(_dummyTex, out var dummyIndex) ? _textures[dummyIndex].Tex : 0;

        CheckError("create done");
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
        GL.BindAttribLocation(program, TINT_ATTRIBUTE, "tint");
        GL.BindAttribLocation(program, INSTANCE_OFFSET_ATTRIBUTE, "instanceOffset");

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

    /// <summary>
    /// Picks the program for a draw from its uniforms and the flush state: a variant when one draws
    /// it exactly as the uber shader would, the uber shader otherwise.
    /// </summary>
    private int SelectProgram(float[] frag, bool clip, bool mask, bool clipEmpty, bool fullCoverage, bool tint)
    {
        // An empty clip is drawn by the uber shader (clipEnabled 2); the variants have no such state.
        if (_uberShaderOnly || (clip && clipEmpty))
        {
            return UBER_SHADER;
        }

        // The variants leave out the discard, which only a non-negative stroke threshold can trigger.
        if (frag[12 * 4 + 1] >= 0.0f)
        {
            return UBER_SHADER;
        }

        var paint = (GLNVGShaderType)(int)frag[12 * 4 + 3] switch
        {
            GLNVGShaderType.FillGrad => IsSolidPaint(frag) ? VARIANT_PAINT_SOLID : VARIANT_PAINT_BOX_GRADIENT,
            GLNVGShaderType.FillImg => ImagePaint(frag, VARIANT_PAINT_IMAGE, VARIANT_PAINT_IMAGE_UNPREMULTIPLIED, VARIANT_PAINT_IMAGE_ALPHA),
            GLNVGShaderType.Img => ImagePaint(frag, VARIANT_PAINT_IMAGE_TRIANGLES, VARIANT_PAINT_IMAGE_TRIANGLES_UNPREMULTIPLIED, VARIANT_PAINT_IMAGE_TRIANGLES_ALPHA),
            GLNVGShaderType.GradientLinear => VARIANT_PAINT_LINEAR_GRADIENT,
            GLNVGShaderType.GradientRadial => VARIANT_PAINT_RADIAL_GRADIENT,
            GLNVGShaderType.Simple => VARIANT_PAINT_SIMPLE,
            GLNVGShaderType.CoverageOutput => VARIANT_PAINT_COVERAGE_OUTPUT,
            GLNVGShaderType.CoverageComposite => IsSolidPaint(frag) ? VARIANT_PAINT_COVERAGE_SOLID : VARIANT_PAINT_COVERAGE_COMPOSITE,
            GLNVGShaderType.RoundedRect => VARIANT_PAINT_ROUNDED_RECT,
            _ => 0,
        };
        if (paint == 0)
        {
            return UBER_SHADER;
        }

        var flags = (HasScissor(frag) ? VARIANT_FLAG_SCISSOR : 0) |
                    (clip ? VARIANT_FLAG_CLIP : 0) |
                    (mask ? VARIANT_FLAG_MASK : 0) |
                    (fullCoverage && ReadsStrokeMask(paint) ? VARIANT_FLAG_FULL_COVERAGE : 0) |
                    (tint ? VARIANT_FLAG_TINT : 0);
        var index = paint * VARIANT_FLAG_COUNT + flags;
        return _variantFailed[index] ? UBER_SHADER : index;
    }

    /// <summary>The image variant of the three given for the texture type the uniforms name.</summary>
    private static int ImagePaint(float[] frag, int premultiplied, int unpremultiplied, int alpha) => (int)frag[12 * 4 + 2] switch
    {
        1 => unpremultiplied,
        2 => alpha,
        _ => premultiplied,
    };

    /// <summary>Whether the variant paint's colour depends on the stroke mask, which full coverage replaces by 1.</summary>
    private static bool ReadsStrokeMask(int paint) => paint switch
    {
        VARIANT_PAINT_SOLID or VARIANT_PAINT_BOX_GRADIENT or VARIANT_PAINT_LINEAR_GRADIENT or VARIANT_PAINT_RADIAL_GRADIENT or
        VARIANT_PAINT_IMAGE or VARIANT_PAINT_IMAGE_UNPREMULTIPLIED or VARIANT_PAINT_IMAGE_ALPHA => true,
        _ => false,
    };

    /// <summary>True when the box gradient's inner and outer colours match, so it is that colour everywhere.</summary>
    private static bool IsSolidPaint(float[] frag)
        => frag[24] == frag[28] && frag[25] == frag[29] && frag[26] == frag[30] && frag[27] == frag[31];

    /// <summary>
    /// False for the zero matrix and unit extent/scale ConvertPaint writes when no scissor is set, which
    /// make scissorMask 1 everywhere.
    /// </summary>
    private static bool HasScissor(float[] frag)
        => frag[0] != 0.0f || frag[1] != 0.0f || frag[2] != 0.0f ||
           frag[4] != 0.0f || frag[5] != 0.0f || frag[6] != 0.0f ||
           frag[8] != 0.0f || frag[9] != 0.0f || frag[10] != 0.0f ||
           frag[32] != 1.0f || frag[33] != 1.0f || frag[34] != 1.0f || frag[35] != 1.0f;

    /// <summary>Makes <paramref name="index"/> the current program, compiling it on first use.</summary>
    private void UseProgram(int index)
    {
        if (index == _currentProgram)
        {
            return;
        }

        if (_programs[index].Program == 0 && !TryCreateVariant(index))
        {
            // A driver that rejects a variant draws that combination with the uber shader for good.
            _variantFailed[index] = true;
            index = UBER_SHADER;
            if (index == _currentProgram)
            {
                return;
            }
        }

        ref var shader = ref _programs[index];

        GL.UseProgram(shader.Program);
        _currentProgram = index;

        // Sampler units and the view size are program state, set once per flush for each program used.
        if (_programFlush[index] != _flushNumber)
        {
            _programFlush[index] = _flushNumber;
            GL.Uniform1(shader.LocTex, 0);
            GL.Uniform1(shader.LocClipTex, 1);
            GL.Uniform1(shader.LocMaskTex, 2);
            GL.Uniform1(shader.LocMaskEnabled, 0);
            GL.Uniform2(shader.LocViewSize, 1, _view);
        }
    }

    private bool TryCreateVariant(int index)
    {
        try
        {
            _programs[index] = CreateShader("variant", GLNVGShaderSource.Header(GL.Profile), VariantDefines(index),
                GLNVGShaderSource.FILL_VERT_SHADER, GLNVGShaderSource.FILL_FRAG_SHADER);
            GetUniforms(ref _programs[index]);
            return true;
        }
        catch (InvalidOperationException)
        {
            _programs[index] = default;
            return false;
        }
    }

    private static string VariantDefines(int index)
    {
        var paint = (index / VARIANT_FLAG_COUNT) switch
        {
            VARIANT_PAINT_SOLID => "#define PAINT_SOLID\n",
            VARIANT_PAINT_IMAGE => "#define PAINT_IMAGE\n#define TEX_TYPE 0\n",
            VARIANT_PAINT_IMAGE_UNPREMULTIPLIED => "#define PAINT_IMAGE\n#define TEX_TYPE 1\n",
            VARIANT_PAINT_IMAGE_ALPHA => "#define PAINT_IMAGE\n#define TEX_TYPE 2\n",
            VARIANT_PAINT_IMAGE_TRIANGLES => "#define PAINT_IMAGE_TRIANGLES\n#define TEX_TYPE 0\n",
            VARIANT_PAINT_IMAGE_TRIANGLES_UNPREMULTIPLIED => "#define PAINT_IMAGE_TRIANGLES\n#define TEX_TYPE 1\n",
            VARIANT_PAINT_IMAGE_TRIANGLES_ALPHA => "#define PAINT_IMAGE_TRIANGLES\n#define TEX_TYPE 2\n",
            VARIANT_PAINT_BOX_GRADIENT => "#define PAINT_BOX_GRADIENT\n",
            VARIANT_PAINT_LINEAR_GRADIENT => "#define PAINT_LINEAR_GRADIENT\n",
            VARIANT_PAINT_RADIAL_GRADIENT => "#define PAINT_RADIAL_GRADIENT\n",
            VARIANT_PAINT_SIMPLE => "#define PAINT_SIMPLE\n",
            VARIANT_PAINT_COVERAGE_OUTPUT => "#define PAINT_COVERAGE_OUTPUT\n",
            VARIANT_PAINT_COVERAGE_COMPOSITE => "#define PAINT_COVERAGE_COMPOSITE\n",
            VARIANT_PAINT_ROUNDED_RECT => "#define PAINT_ROUNDED_RECT\n",
            _ => "#define PAINT_COVERAGE_SOLID\n",
        };
        var flags = index % VARIANT_FLAG_COUNT;
        return "#define VARIANT\n" + paint +
               ((flags & VARIANT_FLAG_SCISSOR) != 0 ? "#define SCISSOR\n" : string.Empty) +
               ((flags & VARIANT_FLAG_CLIP) != 0 ? "#define CLIP\n" : string.Empty) +
               ((flags & VARIANT_FLAG_MASK) != 0 ? "#define MASK\n" : string.Empty) +
               ((flags & VARIANT_FLAG_FULL_COVERAGE) != 0 ? "#define FULL_COVERAGE\n" : string.Empty) +
               ((flags & VARIANT_FLAG_TINT) != 0 ? "#define TINT\n" : string.Empty);
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

        foreach (var shader in _programs)
        {
            if (shader.Program != 0)
            {
                GL.DeleteProgram(shader.Program);
            }

            if (shader.Vert != 0)
            {
                GL.DeleteShader(shader.Vert);
            }

            if (shader.Frag != 0)
            {
                GL.DeleteShader(shader.Frag);
            }
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
            if (_textures[i].Tex != 0 && (_textures[i].Flags & MewVGImageFlags.NoDelete) == 0)
            {
                GL.DeleteTexture(_textures[i].Tex);
            }
        }
    }
}
