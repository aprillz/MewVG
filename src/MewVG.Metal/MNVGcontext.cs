// Copyright (c) 2017 Ollix
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
// ---
// Author: olliwang@ollix.com (Olli Wang)


using System.Numerics;
using System.Runtime.InteropServices;

using Aprillz.MewVG.Interop;

namespace Aprillz.MewVG;

/// <summary>
/// Shader types matching the Metal shader uniforms
/// </summary>
public enum MNVGshaderType
{
    MNVG_SHADER_FILLGRAD = 0,
    MNVG_SHADER_FILLIMG = 1,
    MNVG_SHADER_SIMPLE = 2,
    MNVG_SHADER_IMG = 3,
    MNVG_SHADER_COVERAGE_OUTPUT = 4,
    MNVG_SHADER_COVERAGE_COMPOSITE = 5,
    MNVG_SHADER_GRADIENT_RADIAL = 6,
    MNVG_SHADER_GRADIENT_LINEAR = 7,
}

/// <summary>
/// Call types for rendering
/// </summary>
public enum MNVGcallType
{
    MNVG_NONE = 0,
    MNVG_FILL = 1,
    MNVG_STROKE = 3,
    MNVG_TRIANGLES = 4,
    MNVG_CLIP = 5,
    MNVG_CLIP_RESET = 6,
}

/// <summary>
/// Uniform data structure matching Metal shader uniforms
/// Size must be 176 bytes to match the shader's expectations
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MNVGfragUniforms
{
    public Buffer12<float> scissorMat;     // 48 bytes - float3x4 (3 columns of float4)
    public Buffer12<float> paintMat;       // 48 bytes - float3x4
    public NVGcolor innerCol;              // 16 bytes
    public NVGcolor outerCol;              // 16 bytes
    public Buffer2<float> scissorExt;      // 8 bytes
    public Buffer2<float> scissorScale;    // 8 bytes
    public Buffer2<float> extent;          // 8 bytes
    public float radius;                   // 4 bytes
    public float feather;                  // 4 bytes
    public Buffer2<float> gradientCenter;  // 8 bytes
    public Buffer2<float> gradientRadii;   // 8 bytes
    public Buffer2<float> gradientFocal;   // 8 bytes
    public float gradientSpread;           // 4 bytes
    public float gradientReserved;         // 4 bytes
    public float strokeMult;               // 4 bytes
    public float strokeThr;                // 4 bytes
    public int texType;                    // 4 bytes
    public int type;                       // 4 bytes
    // Total: 208 bytes
}

/// <summary>
/// Draw call structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MNVGcall
{
    public MNVGcallType type;
    public int image;
    public int pathOffset;
    public int pathCount;
    public int triangleOffset;
    public int triangleCount;
    public int indexOffset;
    public int indexCount;
    public int uniformOffset;
    public int cpuResolvedFill;
    public NVGcompositeOperationState blendFunc;
    // Coverage AA (transparent fill/stroke): when true, this call is dispatched
    // through the coverage-build + composite passes that use FB fetch on color[1]
    // to keep each pixel single-blended despite overlapping segment quads.
    public bool hasCoverageAA;
}

/// <summary>
/// Path structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MNVGpath
{
    public int fillOffset;
    public int fillCount;
    public int strokeOffset;
    public int strokeCount;
}

/// <summary>
/// Buffer structure for Metal resources
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MNVGbuffers
{
    public IntPtr stencilTexture;  // id<MTLTexture>
    public IntPtr indexBuffer;     // id<MTLBuffer>
    public int nindexes;
    public IntPtr vertBuffer;      // id<MTLBuffer>
    public int nverts;
    public IntPtr uniformBuffer;   // id<MTLBuffer>
    public int nuniforms;
    public int image;
}

/// <summary>
/// Texture structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MNVGtexture
{
    public int id;
    public IntPtr tex;       // id<MTLTexture>
    public IntPtr sampler;   // id<MTLSamplerState>
    public int width;
    public int height;
    public int type;
    public int flags;
}

/// <summary>
/// Main Metal NanoVG context
/// </summary>
public unsafe class MNVGcontext : IDisposable, INVGRenderer
{
    // Constants
    public const int MNVG_INIT_BUFFER_COUNT = 4;
    public const int MNVG_UNIFORM_ALIGN = 256;

    // Embedded Metal shader source (matches GL backend's strokeMask analytical fill coverage)
    private const string ShaderSource = """
        #include <metal_stdlib>
        #include <simd/simd.h>
        using namespace metal;

        typedef struct {
          float2 pos [[attribute(0)]];
          float2 tcoord [[attribute(1)]];
        } Vertex;

        typedef struct {
          float4 pos  [[position]];
          float2 fpos;
          float2 ftcoord;
        } RasterizerData;

        typedef struct  {
          float3x3 scissorMat;
          float3x3 paintMat;
          float4 innerCol;
          float4 outerCol;
          float2 scissorExt;
          float2 scissorScale;
          float2 extent;
          float radius;
          float feather;
          float2 gradientCenter;
          float2 gradientRadii;
          float2 gradientFocal;
          float gradientSpread;
          float gradientReserved;
          float strokeMult;
          float strokeThr;
          int texType;
          int type;
        } Uniforms;

        float gradientRadialT(float2 p, float2 center, float2 focal, float2 radii, int spread) {
          float2 np = (p - center) / radii;
          float2 nf = (focal - center) / radii;
          float2 d = np - nf;
          float a = dot(d, d);
          if (a <= 1e-6f) return 0.0f;
          float b = 2.0f * dot(nf, d);
          float c = dot(nf, nf) - 1.0f;
          float disc = b * b - 4.0f * a * c;
          if (disc <= 0.0f) return 1.0f;
          float u = (-b + sqrt(disc)) / (2.0f * a);
          if (u <= 1e-6f) return 1.0f;
          float t = 1.0f / u;
          if (spread == 0) return clamp(t, 0.0f, 1.0f);
          if (spread == 2) return fract(max(t, 0.0f));
          float r = fmod(max(t, 0.0f), 2.0f);
          return r <= 1.0f ? r : 2.0f - r;
        }

        float gradientLinearT(float2 p, float2 startPt, float2 endPt, int spread) {
          float2 axis = endPt - startPt;
          float len2 = dot(axis, axis);
          if (len2 <= 1e-6f) return 0.0f;
          float t = dot(p - startPt, axis) / len2;
          if (spread == 0) return clamp(t, 0.0f, 1.0f);
          if (spread == 2) return fract(max(t, 0.0f));
          float r = fmod(max(t, 0.0f), 2.0f);
          return r <= 1.0f ? r : 2.0f - r;
        }

        float scissorMask(constant Uniforms& uniforms, float2 p) {
          float2 sc = (abs((uniforms.scissorMat * float3(p, 1.0f)).xy)
                          - uniforms.scissorExt)
                      * uniforms.scissorScale;
          sc = saturate(float2(0.5f) - sc);
          return sc.x * sc.y;
        }

        float sdroundrect(constant Uniforms& uniforms, float2 pt) {
          float2 ext2 = uniforms.extent - float2(uniforms.radius);
          float2 d = abs(pt) - ext2;
          return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - uniforms.radius;
        }

        float strokeMask(constant Uniforms& uniforms, float2 ftcoord) {
          if (uniforms.strokeMult < 0.0) {
            return clamp(ftcoord.x + 0.5, 0.0, 1.0) * min(1.0, ftcoord.y);
          }
          return clamp((1.0 - abs(ftcoord.x * 2.0 - 1.0)) * uniforms.strokeMult, 0.0, 1.0)
                 * min(1.0, ftcoord.y);
        }

        vertex RasterizerData vertexShader(Vertex vert [[stage_in]],
                                           constant float2& viewSize [[buffer(1)]]) {
          RasterizerData out;
          out.ftcoord = vert.tcoord;
          out.fpos = vert.pos;
          out.pos = float4(2.0 * vert.pos.x / viewSize.x - 1.0,
                           1.0 - 2.0 * vert.pos.y / viewSize.y,
                           0, 1);
          return out;
        }

        fragment float4 fragmentShader(RasterizerData in [[stage_in]],
                                       constant Uniforms& uniforms [[buffer(0)]],
                                       texture2d<float> texture [[texture(0)]],
                                       sampler sampler [[sampler(0)]]) {
          float scissor = scissorMask(uniforms, in.fpos);
          if (scissor == 0)
            return float4(0);

          if (uniforms.type == 0) {
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float d = saturate((uniforms.feather * 0.5 + sdroundrect(uniforms, pt))
                               / uniforms.feather);
            float4 color = mix(uniforms.innerCol, uniforms.outerCol, d);
            return color * scissor;
          } else if (uniforms.type == 6) {
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float t = gradientRadialT(pt, uniforms.gradientCenter, uniforms.gradientFocal, uniforms.gradientRadii, int(uniforms.gradientSpread));
            float4 color = texture.sample(sampler, float2(t, 0.5f));
            return color * uniforms.innerCol * scissor;
          } else if (uniforms.type == 7) {
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float t = gradientLinearT(pt, uniforms.gradientCenter, uniforms.gradientFocal, int(uniforms.gradientSpread));
            float4 color = texture.sample(sampler, float2(t, 0.5f));
            return color * uniforms.innerCol * scissor;
          } else if (uniforms.type == 1) {
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy / uniforms.extent;
            float4 color = texture.sample(sampler, pt);
            if (uniforms.texType == 1)
              color = float4(color.xyz * color.w, color.w);
            else if (uniforms.texType == 2)
              color = float4(color.x);
            color *= scissor;
            return color * uniforms.innerCol;
          } else if (uniforms.type == 3) {  // MNVG_SHADER_IMG
            float4 color = texture.sample(sampler, in.ftcoord);
            if (uniforms.texType == 1)
              color = float4(color.xyz * color.w, color.w);
            else if (uniforms.texType == 2)
              color = float4(color.x);
            color *= scissor;
            return color * uniforms.innerCol;
          } else {  // MNVG_SHADER_SIMPLE (stencil-only, color write masked)
            return uniforms.innerCol * scissor;
          }
        }

        fragment float4 fragmentShaderAA(RasterizerData in [[stage_in]],
                                         constant Uniforms& uniforms [[buffer(0)]],
                                         texture2d<float> texture [[texture(0)]],
                                         sampler sampler [[sampler(0)]]) {
          float scissor = scissorMask(uniforms, in.fpos);
          if (scissor == 0)
            return float4(0);

          if (uniforms.type == 3) {  // MNVG_SHADER_IMG — no strokeAlpha
            float4 color = texture.sample(sampler, in.ftcoord);
            if (uniforms.texType == 1)
              color = float4(color.xyz * color.w, color.w);
            else if (uniforms.texType == 2)
              color = float4(color.x);
            color *= scissor;
            return color * uniforms.innerCol;
          }

          if (uniforms.type == 2) {  // MNVG_SHADER_SIMPLE
            return uniforms.innerCol * scissor;
          }

          float strokeAlpha = strokeMask(uniforms, in.ftcoord);
          if (strokeAlpha < uniforms.strokeThr) {
            return float4(0);
          }

          if (uniforms.type == 0) {  // MNVG_SHADER_FILLGRAD
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float d = saturate((uniforms.feather * 0.5 + sdroundrect(uniforms, pt))
                                / uniforms.feather);
            float4 color = mix(uniforms.innerCol, uniforms.outerCol, d);
            color *= scissor;
            color *= strokeAlpha;
            return color;
          } else if (uniforms.type == 6) {  // MNVG_SHADER_GRADIENT_RADIAL
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float t = gradientRadialT(pt, uniforms.gradientCenter, uniforms.gradientFocal, uniforms.gradientRadii, int(uniforms.gradientSpread));
            float4 color = texture.sample(sampler, float2(t, 0.5f));
            color *= uniforms.innerCol;
            color *= scissor;
            color *= strokeAlpha;
            return color;
          } else if (uniforms.type == 7) {  // MNVG_SHADER_GRADIENT_LINEAR
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float t = gradientLinearT(pt, uniforms.gradientCenter, uniforms.gradientFocal, int(uniforms.gradientSpread));
            float4 color = texture.sample(sampler, float2(t, 0.5f));
            color *= uniforms.innerCol;
            color *= scissor;
            color *= strokeAlpha;
            return color;
          } else {  // MNVG_SHADER_FILLIMG
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy / uniforms.extent;
            float4 color = texture.sample(sampler, pt);
            if (uniforms.texType == 1)
              color = float4(color.xyz * color.w, color.w);
            else if (uniforms.texType == 2)
              color = float4(color.x);
            color *= scissor;
            color *= strokeAlpha;
            return color * uniforms.innerCol;
          }
        }

        // ─── Coverage AA (transparent stroke/fill, single-encoder) ────────────
        // Output struct used by the build pass to write strokeAlpha to color[1]
        // while leaving color[0] untouched (writeMask handled at pipeline level).
        struct CoverageOut {
          float4 main [[color(0)]];
          float4 cov  [[color(1)]];
        };

        // Coverage build: rasterize stroke / fill geometry once with MAX blending
        // on color[1]. Each pixel ends up holding max(strokeAlpha) across all
        // overlapping fragments — i.e. a true coverage value, not an SrcOver
        // accumulation. color[0] is masked off via the pipeline's writeMask so
        // this pass leaves the main framebuffer alone.
        fragment CoverageOut fragmentCoverageBuild(RasterizerData in [[stage_in]],
                                                    constant Uniforms& uniforms [[buffer(0)]]) {
          float scissor = scissorMask(uniforms, in.fpos);
          float strokeAlpha = strokeMask(uniforms, in.ftcoord);
          if (strokeAlpha < uniforms.strokeThr) {
            discard_fragment();
          }
          CoverageOut o;
          o.main = float4(0);                          // ignored by writeMask
          // Coverage texture is R8Unorm — only the red channel is stored. Write
          // the same value to all channels so MAX blend with the destination's
          // single channel still picks up the largest contribution; reads happen
          // via .r in the composite shader.
          o.cov = float4(strokeAlpha * scissor);
          return o;
        }

        // Coverage composite: a bounds quad with SrcOver on color[0]. Reads the
        // per-pixel coverage that the build pass wrote to color[1] using
        // framebuffer fetch (`[[color(1)]]`) — no encoder switch, no tile flush.
        // Outputs `paint × coverage` in premultiplied space; standard SrcOver
        // blends that against the existing framebuffer exactly once per pixel.
        // Also writes 0 back to color[1] so the next coverage pass on this frame
        // starts from a zeroed scratch (eliminates the need for a clear pass).
        fragment CoverageOut fragmentCoverageComposite(RasterizerData in [[stage_in]],
                                                        constant Uniforms& uniforms [[buffer(0)]],
                                                        texture2d<float> texture [[texture(0)]],
                                                        sampler textureSampler [[sampler(0)]],
                                                        float4 prevCov [[color(1)]]) {
          // Coverage texture is R8Unorm so the build pass wrote into the red
          // channel; read .r here (Metal returns 1.0 in .a for single-channel
          // formats, which would silently give "fully covered" everywhere).
          float coverage = prevCov.r;
          float scissor = scissorMask(uniforms, in.fpos);

          float4 color;
          if (uniforms.type == 0) {                    // FILLGRAD (or solid via degenerate sdroundrect)
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float d = saturate((uniforms.feather * 0.5 + sdroundrect(uniforms, pt))
                                / uniforms.feather);
            color = mix(uniforms.innerCol, uniforms.outerCol, d);
          } else if (uniforms.type == 6) {             // GRADIENT_RADIAL
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float t = gradientRadialT(pt, uniforms.gradientCenter, uniforms.gradientFocal,
                                      uniforms.gradientRadii, int(uniforms.gradientSpread));
            color = texture.sample(textureSampler, float2(t, 0.5f)) * uniforms.innerCol;
          } else if (uniforms.type == 7) {             // GRADIENT_LINEAR
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy;
            float t = gradientLinearT(pt, uniforms.gradientCenter, uniforms.gradientFocal,
                                      int(uniforms.gradientSpread));
            color = texture.sample(textureSampler, float2(t, 0.5f)) * uniforms.innerCol;
          } else {                                     // FILLIMG fallback (image-paint stroke is rare)
            float2 pt = (uniforms.paintMat * float3(in.fpos, 1.0)).xy / uniforms.extent;
            color = texture.sample(textureSampler, pt);
            if (uniforms.texType == 1)
              color = float4(color.xyz * color.w, color.w);
            else if (uniforms.texType == 2)
              color = float4(color.x);
            color *= uniforms.innerCol;
          }

          CoverageOut o;
          o.main = color * coverage * scissor;
          o.cov = float4(0);                           // self-clear for next stroke this frame
          return o;
        }
        """;
    // Reserve the MSB for clip so NanoVG's own stencil usage can keep using the lower bits.
    // This avoids clip getting overwritten by fill/stroke stencil passes.
    private const uint ClipStencilRef = 0x80;
    private const ulong ClipStencilMask = 0x80;
    // Temporary bit for clip intersection while recording nested clips.
    // This shares space with NanoVG's lower bits but is always cleared immediately.
    private const ulong ClipTempMask = 0x01;
    private const ulong NanoVgStencilMask = 0x7F;

    // Metal objects
    private IntPtr _device;              // id<MTLDevice>
    private IntPtr _commandQueue;        // id<MTLCommandQueue>
    private IntPtr _library;             // id<MTLLibrary>
    private IntPtr _vertexFunction;      // id<MTLFunction>
    private IntPtr _fragmentFunction;            // id<MTLFunction>
    private IntPtr _fragmentAAFunction;          // id<MTLFunction>
    private IntPtr _fragmentCoverageBuildFn;     // id<MTLFunction>
    private IntPtr _fragmentCoverageCompositeFn; // id<MTLFunction>

    // Pipeline states
    private IntPtr _pipelineState;           // id<MTLRenderPipelineState>
    private IntPtr _stencilOnlyPipelineState;
    private IntPtr _pseudoSampler;           // id<MTLSamplerState>
    private IntPtr _pseudoTexture;           // id<MTLTexture>
    private MTLPixelFormat _pipelinePixelFormat;
    private int _pipelineSampleCount;
    private MTLBlendFactor _blendSrcRgb;
    private MTLBlendFactor _blendDstRgb;
    private MTLBlendFactor _blendSrcAlpha;
    private MTLBlendFactor _blendDstAlpha;

    // Depth stencil states
    private IntPtr _defaultStencilState;
    private IntPtr _fillShapeStencilState;
    private IntPtr _fillShapeStencilStateClipped;
    private IntPtr _fillAntiAliasStencilState;
    private IntPtr _fillAntiAliasStencilStateClipped;
    private IntPtr _fillStencilState;
    private IntPtr _strokeShapeStencilState;
    private IntPtr _strokeAntiAliasStencilState;
    private IntPtr _strokeClearStencilState;
    private IntPtr _clipWriteStencilState;
    private IntPtr _clipTestStencilState;
    private IntPtr _clipClearStencilState;
    private IntPtr _clipCopyToTempStencilState;
    private IntPtr _clipWriteIntersectStencilState;
    private IntPtr _clipClearTempStencilState;

    // Buffers and textures
    private MNVGbuffers[] _buffers;
    private int _bufferCount;
    private int _currentBuffer;
    private MNVGtexture[] _textures;
    private int _textureCount;
    private int _textureCapacity;

    // Rendering state
    private MNVGcall[] _calls;
    private int _callCount;
    private int _callCapacity;
    private MNVGpath[] _paths;
    private int _pathCount;
    private int _pathCapacity;
    private NVGvertex[] _verts;
    private int _vertCount;
    private int _vertCapacity;
    private byte[] _uniforms;
    private int _uniformCount;
    private int _uniformCapacity;
    private uint[] _indexes;
    private int _indexCount;

    // Frame state
    private IntPtr _renderEncoder;       // id<MTLRenderCommandEncoder>
    private IntPtr _commandBuffer;       // id<MTLCommandBuffer>
    private DispatchSemaphore _semaphore;

    // Coverage AA infrastructure. Transparent stroke/fill calls take a build +
    // composite path inside the same render encoder: build writes strokeAlpha to
    // color[1] with MAX blend, composite reads that value via framebuffer fetch
    // (`[[color(1)]]`) and applies paint×coverage to color[0] with SrcOver.
    // Single-encoder, no tile flush, works on every Metal GPU.
    private IntPtr _coverageTexture;            // R8Unorm, attached as color[1] of host's main pass
    private int _coverageWidth;
    private int _coverageHeight;
    private IntPtr _coverageBuildPipeline;      // 2-attachment PSO: writeMask color[0]=None, color[1] MAX blend
    private IntPtr _coverageCompositePipeline;  // 2-attachment PSO: writeMask color[0]=All SrcOver, color[1] cleared via shader

    // Settings
    private NVGcreateFlags _flags;
    private MTLPixelFormat _pixelFormat;
    private MTLPixelFormat _stencilFormat;
    private int _sampleCount = 1;
    private float _devicePixelRatio;
    private Vector2 _viewSize;
    private bool _recordingClipActive;
    private bool _clipActiveInRender;
    private bool _disposed;

    /// <summary>
    /// Whether geometry-based fringe anti-aliasing is active (not MSAA).
    /// </summary>
    private bool UseGeometryAA => (_flags & NVGcreateFlags.Antialias) != 0 && _sampleCount <= 1;

    /// <summary>
    /// Creates a new Metal NanoVG context
    /// </summary>
    /// <param name="device">Metal device</param>
    /// <param name="flags">Creation flags</param>
    public MNVGcontext(IntPtr device, NVGcreateFlags flags)
    {
        if (device == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(device));
        }

        _device = device;
        _flags = flags;
        _pixelFormat = MTLPixelFormat.BGRA8Unorm;
        _stencilFormat = MTLPixelFormat.Stencil8;
        _devicePixelRatio = 1.0f;

        // Retain the device
        ObjCRuntime.SendMessage(_device, ObjCRuntime.Selectors.retain);

        // Initialize arrays
        _buffers = new MNVGbuffers[MNVG_INIT_BUFFER_COUNT];
        _bufferCount = MNVG_INIT_BUFFER_COUNT;
        _currentBuffer = 0;

        _textures = new MNVGtexture[16];
        _textureCapacity = 16;

        _calls = new MNVGcall[128];
        _callCapacity = 128;

        _paths = new MNVGpath[128];
        _pathCapacity = 128;

        _verts = new NVGvertex[4096];
        _vertCapacity = 4096;

        _uniforms = new byte[MNVG_UNIFORM_ALIGN * 128];
        _uniformCapacity = 128;

        _indexes = new uint[4096];

        // Create semaphore for buffer synchronization
        _semaphore = new DispatchSemaphore(MNVG_INIT_BUFFER_COUNT);

        // Initialize Metal resources
        InitializeMetal();
    }

    /// <summary>
    /// Gets or sets the pixel format for rendering
    /// </summary>
    public MTLPixelFormat PixelFormat
    {
        get => _pixelFormat;
        set => _pixelFormat = value;
    }

    /// <summary>
    /// Gets or sets the stencil format
    /// </summary>
    public MTLPixelFormat StencilFormat
    {
        get => _stencilFormat;
        set => _stencilFormat = value;
    }

    /// <summary>
    /// Gets or sets the MSAA sample count (1 = no MSAA, 4 or 8 for MSAA).
    /// When greater than 1, hardware MSAA is used for anti-aliasing and
    /// geometry-based fringe AA is automatically skipped.
    /// </summary>
    public int SampleCount
    {
        get => _sampleCount;
        set => _sampleCount = Math.Max(1, value);
    }

    /// <summary>
    /// Gets the Metal device
    /// </summary>
    public IntPtr Device => _device;

    /// <summary>
    /// Gets the command queue
    /// </summary>
    public IntPtr CommandQueue => _commandQueue;

    private void InitializeMetal()
    {
        // Create command queue
        _commandQueue = ObjCRuntime.SendMessage(_device, MetalSelectors.newCommandQueue);
        if (_commandQueue == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create command queue");
        }

        // Load shader library from source (runtime compilation)
        LoadShaderLibrary();

        // Get shader functions
        _vertexFunction = GetFunction("vertexShader");
        _fragmentFunction = GetFunction("fragmentShader");
        _fragmentAAFunction = GetFunction("fragmentShaderAA");
        _fragmentCoverageBuildFn = GetFunction("fragmentCoverageBuild");
        _fragmentCoverageCompositeFn = GetFunction("fragmentCoverageComposite");

        // Create pipeline states
        CreatePipelineStates();

        // Create depth stencil states
        CreateDepthStencilStates();

        // Create pseudo texture and sampler for when no texture is bound
        CreatePseudoTexture();
    }

    private void LoadShaderLibrary()
    {
        using var source = new NSString(ShaderSource);
        var error = IntPtr.Zero;
        _library = ObjCRuntime.SendMessage(
            _device,
            Metal.Sel.NewLibraryWithSource,
            source.Handle,
            IntPtr.Zero,  // options (nil)
            (IntPtr)(&error)
        );

        if (_library == IntPtr.Zero || error != IntPtr.Zero)
        {
            var errorMsg = "Unknown error";
            if (error != IntPtr.Zero)
            {
                var desc = ObjCRuntime.SendMessage(error, ObjCRuntime.Selectors.description);
                if (desc != IntPtr.Zero)
                {
                    var utf8 = ObjCRuntime.SendMessage(desc, ObjCRuntime.Selectors.UTF8String);
                    if (utf8 != IntPtr.Zero)
                    {
                        errorMsg = Marshal.PtrToStringUTF8(utf8) ?? errorMsg;
                    }
                }
            }
            throw new InvalidOperationException($"Failed to compile shader library: {errorMsg}");
        }
    }

    private IntPtr GetFunction(string name)
    {
        using var nsName = new NSString(name);
        var function = ObjCRuntime.SendMessage(_library, MetalSelectors.newFunctionWithName, nsName.Handle);

        if (function == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to get shader function: {name}");
        }

        return function;
    }

    private void CreatePipelineStates()
    {
        var defaultBlend = new NVGcompositeOperationState
        {
            SrcRGB = (int)NVGblendFactor.One,
            DstRGB = (int)NVGblendFactor.OneMinusSrcAlpha,
            SrcAlpha = (int)NVGblendFactor.One,
            DstAlpha = (int)NVGblendFactor.OneMinusSrcAlpha
        };
        UpdatePipelineStatesForBlend(defaultBlend);
    }

    private void UpdatePipelineStatesForBlend(NVGcompositeOperationState blend)
    {
        var ok = true;
        var srcRgb = ConvertBlendFactor(blend.SrcRGB, ref ok);
        var dstRgb = ConvertBlendFactor(blend.DstRGB, ref ok);
        var srcAlpha = ConvertBlendFactor(blend.SrcAlpha, ref ok);
        var dstAlpha = ConvertBlendFactor(blend.DstAlpha, ref ok);

        if (!ok)
        {
            srcRgb = MTLBlendFactor.One;
            dstRgb = MTLBlendFactor.OneMinusSourceAlpha;
            srcAlpha = MTLBlendFactor.One;
            dstAlpha = MTLBlendFactor.OneMinusSourceAlpha;
        }

        if (_pipelineState != IntPtr.Zero &&
            _stencilOnlyPipelineState != IntPtr.Zero &&
            _pipelinePixelFormat == _pixelFormat &&
            _pipelineSampleCount == _sampleCount &&
            _blendSrcRgb == srcRgb &&
            _blendDstRgb == dstRgb &&
            _blendSrcAlpha == srcAlpha &&
            _blendDstAlpha == dstAlpha)
        {
            return;
        }

        var newPipeline = CreatePipelineState(srcRgb, dstRgb, srcAlpha, dstAlpha, stencilOnly: false);
        if (newPipeline == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create render pipeline state");
        }

        var newStencilPipeline = CreatePipelineState(srcRgb, dstRgb, srcAlpha, dstAlpha, stencilOnly: true);
        if (newStencilPipeline == IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(newPipeline, ObjCRuntime.Selectors.release);
            throw new InvalidOperationException("Failed to create stencil-only pipeline state");
        }

        if (_pipelineState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_pipelineState, ObjCRuntime.Selectors.release);
        }

        if (_stencilOnlyPipelineState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_stencilOnlyPipelineState, ObjCRuntime.Selectors.release);
        }

        _pipelineState = newPipeline;
        _stencilOnlyPipelineState = newStencilPipeline;
        _pipelinePixelFormat = _pixelFormat;
        _pipelineSampleCount = _sampleCount;
        _blendSrcRgb = srcRgb;
        _blendDstRgb = dstRgb;
        _blendSrcAlpha = srcAlpha;
        _blendDstAlpha = dstAlpha;
    }

    private IntPtr CreatePipelineState(
        MTLBlendFactor srcRgb,
        MTLBlendFactor dstRgb,
        MTLBlendFactor srcAlpha,
        MTLBlendFactor dstAlpha,
        bool stencilOnly)
    {
        var pipelineDescriptorClass = ObjCRuntime.GetClass("MTLRenderPipelineDescriptor");
        var pipelineDescriptor = ObjCRuntime.New(pipelineDescriptorClass);
        if (pipelineDescriptor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var vertexDescriptor = CreateVertexDescriptor();
        // With MSAA (sampleCount > 1) hardware handles edge smoothing, so skip geometry-based AA shader.
        var useGeometryAA = (_flags & NVGcreateFlags.Antialias) != 0 && _sampleCount <= 1;
        var fragmentFunc = useGeometryAA ? _fragmentAAFunction : _fragmentFunction;

        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setVertexFunction, _vertexFunction);
        // Even for stencil-only passes we keep a fragment function so fragments are generated
        // and depth/stencil tests can update the stencil buffer.
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setFragmentFunction, fragmentFunc);
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setVertexDescriptor, vertexDescriptor);
        if (_stencilFormat == MTLPixelFormat.Depth24Unorm_Stencil8 ||
            _stencilFormat == MTLPixelFormat.Depth32Float_Stencil8)
        {
            ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setDepthAttachmentPixelFormat, (ulong)_stencilFormat);
        }
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setStencilAttachmentPixelFormat, (ulong)_stencilFormat);

        // Set MSAA sample count on the pipeline descriptor.
        if (_sampleCount > 1)
        {
            ObjCRuntime.SendMessage(pipelineDescriptor, Metal.Sel.SetRasterSampleCount, (nuint)_sampleCount);
        }

        var colorAttachments = ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.colorAttachments);
        var colorAttachment0 = ObjCRuntime.SendMessage(colorAttachments, MetalSelectors.objectAtIndexedSubscript, (nuint)0);

        ObjCRuntime.SendMessage(colorAttachment0, MetalSelectors.setPixelFormat, (ulong)_pixelFormat);
        ObjCRuntime.SendMessage(colorAttachment0, MetalSelectors.setBlendingEnabled, true);
        ObjCRuntime.SendMessage(colorAttachment0, MetalSelectors.setSourceRGBBlendFactor, (ulong)srcRgb);
        ObjCRuntime.SendMessage(colorAttachment0, MetalSelectors.setSourceAlphaBlendFactor, (ulong)srcAlpha);
        ObjCRuntime.SendMessage(colorAttachment0, MetalSelectors.setDestinationRGBBlendFactor, (ulong)dstRgb);
        ObjCRuntime.SendMessage(colorAttachment0, MetalSelectors.setDestinationAlphaBlendFactor, (ulong)dstAlpha);
        ObjCRuntime.SendMessage(colorAttachment0, MetalSelectors.setWriteMask,
            (ulong)(stencilOnly ? MTLColorWriteMask.None : MTLColorWriteMask.All));

        // Mirror color[1] (coverage AA scratch) attachment configuration even on
        // pipelines that don't write to it: when the host adds a second color
        // attachment to the render pass for coverage AA, all PSOs in that pass
        // must declare matching attachment slots or Metal silently drops fragment
        // output / fails validation. WriteMask=None here keeps these pipelines
        // from disturbing the coverage texture during regular paint draws.
        var colorAttachment1 = ObjCRuntime.SendMessage(colorAttachments,
            MetalSelectors.objectAtIndexedSubscript, (nuint)1);
        ObjCRuntime.SendMessage(colorAttachment1, MetalSelectors.setPixelFormat,
            (ulong)CoveragePixelFormat);
        ObjCRuntime.SendMessage(colorAttachment1, MetalSelectors.setBlendingEnabled, false);
        ObjCRuntime.SendMessage(colorAttachment1, MetalSelectors.setWriteMask,
            (ulong)MTLColorWriteMask.None);

        var error = IntPtr.Zero;
        var pipeline = ObjCRuntime.SendMessage(
            _device,
            MetalSelectors.newRenderPipelineStateWithDescriptor_error,
            pipelineDescriptor,
            (IntPtr)(&error)
        );

        ObjCRuntime.SendMessage(pipelineDescriptor, ObjCRuntime.Selectors.release);
        ObjCRuntime.SendMessage(vertexDescriptor, ObjCRuntime.Selectors.release);

        return pipeline;
    }

    /// <summary>
    /// Builds the coverage-AA "build" pipeline: rasterizes stroke/fill geometry
    /// once with MAX blending on color attachment 1 (alpha8) and writeMask=None
    /// on color[0]. Uses <see cref="_fragmentCoverageBuildFn"/> which returns a
    /// CoverageOut struct writing strokeAlpha to color[1].
    /// </summary>
    private IntPtr CreateCoverageBuildPipeline(MTLPixelFormat coveragePixelFormat)
    {
        var pipelineDescriptorClass = ObjCRuntime.GetClass("MTLRenderPipelineDescriptor");
        var pipelineDescriptor = ObjCRuntime.New(pipelineDescriptorClass);
        if (pipelineDescriptor == IntPtr.Zero) return IntPtr.Zero;

        var vertexDescriptor = CreateVertexDescriptor();
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setVertexFunction, _vertexFunction);
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setFragmentFunction, _fragmentCoverageBuildFn);
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setVertexDescriptor, vertexDescriptor);

        if (_stencilFormat == MTLPixelFormat.Depth24Unorm_Stencil8 ||
            _stencilFormat == MTLPixelFormat.Depth32Float_Stencil8)
        {
            ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setDepthAttachmentPixelFormat, (ulong)_stencilFormat);
        }
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setStencilAttachmentPixelFormat, (ulong)_stencilFormat);

        if (_sampleCount > 1)
        {
            ObjCRuntime.SendMessage(pipelineDescriptor, Metal.Sel.SetRasterSampleCount, (nuint)_sampleCount);
        }

        var colorAttachments = ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.colorAttachments);

        // color[0]: main framebuffer pixel format, blending off (won't be touched).
        var color0 = ObjCRuntime.SendMessage(colorAttachments, MetalSelectors.objectAtIndexedSubscript, (nuint)0);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setPixelFormat, (ulong)_pixelFormat);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setBlendingEnabled, false);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setWriteMask, (ulong)MTLColorWriteMask.None);

        // color[1]: alpha8 coverage scratch with MAX blend so overlap pins to peak.
        var color1 = ObjCRuntime.SendMessage(colorAttachments, MetalSelectors.objectAtIndexedSubscript, (nuint)1);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setPixelFormat, (ulong)coveragePixelFormat);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setBlendingEnabled, true);
        ObjCRuntime.SendMessage(color1, Metal.Sel.SetRgbBlendOperation, (ulong)MTLBlendOperation.Max);
        ObjCRuntime.SendMessage(color1, Metal.Sel.SetAlphaBlendOperation, (ulong)MTLBlendOperation.Max);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setSourceRGBBlendFactor, (ulong)MTLBlendFactor.One);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setSourceAlphaBlendFactor, (ulong)MTLBlendFactor.One);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setDestinationRGBBlendFactor, (ulong)MTLBlendFactor.One);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setDestinationAlphaBlendFactor, (ulong)MTLBlendFactor.One);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setWriteMask, (ulong)MTLColorWriteMask.All);

        var error = IntPtr.Zero;
        var pipeline = ObjCRuntime.SendMessage(_device,
            MetalSelectors.newRenderPipelineStateWithDescriptor_error,
            pipelineDescriptor, (IntPtr)(&error));

        ObjCRuntime.SendMessage(pipelineDescriptor, ObjCRuntime.Selectors.release);
        ObjCRuntime.SendMessage(vertexDescriptor, ObjCRuntime.Selectors.release);
        return pipeline;
    }

    /// <summary>
    /// Builds the coverage-AA "composite" pipeline: a bounds quad with SrcOver on
    /// color[0]; <see cref="_fragmentCoverageCompositeFn"/> reads color[1] via
    /// framebuffer fetch (`[[color(1)]]`) for per-pixel coverage and writes 0
    /// back to color[1] so the next stroke this frame starts clean.
    /// </summary>
    private IntPtr CreateCoverageCompositePipeline(
        MTLPixelFormat coveragePixelFormat,
        MTLBlendFactor srcRgb, MTLBlendFactor dstRgb,
        MTLBlendFactor srcAlpha, MTLBlendFactor dstAlpha)
    {
        var pipelineDescriptorClass = ObjCRuntime.GetClass("MTLRenderPipelineDescriptor");
        var pipelineDescriptor = ObjCRuntime.New(pipelineDescriptorClass);
        if (pipelineDescriptor == IntPtr.Zero) return IntPtr.Zero;

        var vertexDescriptor = CreateVertexDescriptor();
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setVertexFunction, _vertexFunction);
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setFragmentFunction, _fragmentCoverageCompositeFn);
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setVertexDescriptor, vertexDescriptor);

        if (_stencilFormat == MTLPixelFormat.Depth24Unorm_Stencil8 ||
            _stencilFormat == MTLPixelFormat.Depth32Float_Stencil8)
        {
            ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setDepthAttachmentPixelFormat, (ulong)_stencilFormat);
        }
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setStencilAttachmentPixelFormat, (ulong)_stencilFormat);

        if (_sampleCount > 1)
        {
            ObjCRuntime.SendMessage(pipelineDescriptor, Metal.Sel.SetRasterSampleCount, (nuint)_sampleCount);
        }

        var colorAttachments = ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.colorAttachments);

        // color[0]: SrcOver-style blending (matched to caller's composite operation).
        var color0 = ObjCRuntime.SendMessage(colorAttachments, MetalSelectors.objectAtIndexedSubscript, (nuint)0);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setPixelFormat, (ulong)_pixelFormat);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setBlendingEnabled, true);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setSourceRGBBlendFactor, (ulong)srcRgb);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setSourceAlphaBlendFactor, (ulong)srcAlpha);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setDestinationRGBBlendFactor, (ulong)dstRgb);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setDestinationAlphaBlendFactor, (ulong)dstAlpha);
        ObjCRuntime.SendMessage(color0, MetalSelectors.setWriteMask, (ulong)MTLColorWriteMask.All);

        // color[1]: REPLACE blending (srcFactor=One, dstFactor=Zero, Add) so the
        // composite shader's `o.cov = 0` actually overwrites coverage to 0 — this
        // is the self-clear that lets back-to-back coverage AA calls reuse the
        // single coverage attachment without an explicit clear pass.
        var color1 = ObjCRuntime.SendMessage(colorAttachments, MetalSelectors.objectAtIndexedSubscript, (nuint)1);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setPixelFormat, (ulong)coveragePixelFormat);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setBlendingEnabled, true);
        ObjCRuntime.SendMessage(color1, Metal.Sel.SetRgbBlendOperation, (ulong)MTLBlendOperation.Add);
        ObjCRuntime.SendMessage(color1, Metal.Sel.SetAlphaBlendOperation, (ulong)MTLBlendOperation.Add);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setSourceRGBBlendFactor, (ulong)MTLBlendFactor.One);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setSourceAlphaBlendFactor, (ulong)MTLBlendFactor.One);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setDestinationRGBBlendFactor, (ulong)MTLBlendFactor.Zero);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setDestinationAlphaBlendFactor, (ulong)MTLBlendFactor.Zero);
        ObjCRuntime.SendMessage(color1, MetalSelectors.setWriteMask, (ulong)MTLColorWriteMask.All);

        var error = IntPtr.Zero;
        var pipeline = ObjCRuntime.SendMessage(_device,
            MetalSelectors.newRenderPipelineStateWithDescriptor_error,
            pipelineDescriptor, (IntPtr)(&error));

        ObjCRuntime.SendMessage(pipelineDescriptor, ObjCRuntime.Selectors.release);
        ObjCRuntime.SendMessage(vertexDescriptor, ObjCRuntime.Selectors.release);
        return pipeline;
    }

    /// <summary>
    /// Returns true when paint composition would visibly differ between single-blend
    /// and overlapping multi-blend — i.e. when the inner/outer color carries less
    /// than full alpha. Mirrors the threshold used by the GL backend's
    /// <c>HasTransparency(paint)</c>. Image paints are excluded (they have a separate
    /// fragment path that doesn't produce the same overlap artifact).
    /// </summary>
    private static bool PaintHasTransparency(in NVGpaint paint)
    {
        if (paint.Image != 0) return false;
        const float opaqueThreshold = 0.999f;
        return paint.InnerColor.A < opaqueThreshold || paint.OuterColor.A < opaqueThreshold;
    }

    /// <summary>
    /// Lazily creates and caches the coverage-build pipeline. Re-created if the
    /// pixel format or sample count changed since last call.
    /// </summary>
    private IntPtr GetCoverageBuildPipeline()
    {
        if (_coverageBuildPipeline == IntPtr.Zero)
        {
            _coverageBuildPipeline = CreateCoverageBuildPipeline(CoveragePixelFormat);
        }
        return _coverageBuildPipeline;
    }

    /// <summary>
    /// Lazily creates and caches the coverage-composite pipeline using the given
    /// blend factors (so callers can match the call's compositeOperation).
    /// </summary>
    private IntPtr GetCoverageCompositePipeline(MTLBlendFactor srcRgb, MTLBlendFactor dstRgb,
                                                 MTLBlendFactor srcAlpha, MTLBlendFactor dstAlpha)
    {
        // Cache shape: store factors used and recreate on mismatch. Most calls
        // use the same SrcOver blend, so churn is rare.
        if (_coverageCompositePipeline != IntPtr.Zero
            && _coverageCompSrcRgb == srcRgb && _coverageCompDstRgb == dstRgb
            && _coverageCompSrcAlpha == srcAlpha && _coverageCompDstAlpha == dstAlpha)
        {
            return _coverageCompositePipeline;
        }
        if (_coverageCompositePipeline != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_coverageCompositePipeline, ObjCRuntime.Selectors.release);
        }
        _coverageCompositePipeline = CreateCoverageCompositePipeline(
            CoveragePixelFormat, srcRgb, dstRgb, srcAlpha, dstAlpha);
        _coverageCompSrcRgb = srcRgb;
        _coverageCompDstRgb = dstRgb;
        _coverageCompSrcAlpha = srcAlpha;
        _coverageCompDstAlpha = dstAlpha;
        return _coverageCompositePipeline;
    }

    private MTLBlendFactor _coverageCompSrcRgb;
    private MTLBlendFactor _coverageCompDstRgb;
    private MTLBlendFactor _coverageCompSrcAlpha;
    private MTLBlendFactor _coverageCompDstAlpha;

    private static MTLBlendFactor ConvertBlendFactor(int factor, ref bool ok) => factor switch
    {
        (int)NVGblendFactor.Zero => MTLBlendFactor.Zero,
        (int)NVGblendFactor.One => MTLBlendFactor.One,
        (int)NVGblendFactor.SrcColor => MTLBlendFactor.SourceColor,
        (int)NVGblendFactor.OneMinusSrcColor => MTLBlendFactor.OneMinusSourceColor,
        (int)NVGblendFactor.DstColor => MTLBlendFactor.DestinationColor,
        (int)NVGblendFactor.OneMinusDstColor => MTLBlendFactor.OneMinusDestinationColor,
        (int)NVGblendFactor.SrcAlpha => MTLBlendFactor.SourceAlpha,
        (int)NVGblendFactor.OneMinusSrcAlpha => MTLBlendFactor.OneMinusSourceAlpha,
        (int)NVGblendFactor.DstAlpha => MTLBlendFactor.DestinationAlpha,
        (int)NVGblendFactor.OneMinusDstAlpha => MTLBlendFactor.OneMinusDestinationAlpha,
        (int)NVGblendFactor.SrcAlphaSaturate => MTLBlendFactor.SourceAlphaSaturated,
        _ => FailBlendFactor(ref ok)
    };

    private static MTLBlendFactor FailBlendFactor(ref bool ok)
    {
        ok = false;
        return MTLBlendFactor.One;
    }

    private IntPtr CreateVertexDescriptor()
    {
        var vertexDescriptorClass = ObjCRuntime.GetClass("MTLVertexDescriptor");
        var vertexDescriptor = ObjCRuntime.New(vertexDescriptorClass);

        // Get attributes array
        var attributes = ObjCRuntime.SendMessage(vertexDescriptor, MetalSelectors.attributes);

        // Position attribute (float2) at offset 0
        var attr0 = ObjCRuntime.SendMessage(attributes, MetalSelectors.objectAtIndexedSubscript, (nuint)0);
        ObjCRuntime.SendMessage(attr0, MetalSelectors.setFormat, (ulong)MTLVertexFormat.Float2);
        ObjCRuntime.SendMessage(attr0, MetalSelectors.setOffset, (nuint)0);
        ObjCRuntime.SendMessage(attr0, MetalSelectors.setBufferIndex, (nuint)0);

        // Texture coordinate attribute (float2) at offset 8
        var attr1 = ObjCRuntime.SendMessage(attributes, MetalSelectors.objectAtIndexedSubscript, (nuint)1);
        ObjCRuntime.SendMessage(attr1, MetalSelectors.setFormat, (ulong)MTLVertexFormat.Float2);
        ObjCRuntime.SendMessage(attr1, MetalSelectors.setOffset, (nuint)8);
        ObjCRuntime.SendMessage(attr1, MetalSelectors.setBufferIndex, (nuint)0);

        // Get layouts array
        var layouts = ObjCRuntime.SendMessage(vertexDescriptor, MetalSelectors.layouts);
        var layout0 = ObjCRuntime.SendMessage(layouts, MetalSelectors.objectAtIndexedSubscript, (nuint)0);

        // Set stride (sizeof(NVGvertex) = 16 bytes)
        ObjCRuntime.SendMessage(layout0, MetalSelectors.setStride, (nuint)16);
        ObjCRuntime.SendMessage(layout0, MetalSelectors.setStepRate, (nuint)1);
        ObjCRuntime.SendMessage(layout0, MetalSelectors.setStepFunction, (ulong)MTLVertexStepFunction.PerVertex);

        return vertexDescriptor;
    }

    private void CreateDepthStencilStates()
    {
        var stencilDescriptorClass = ObjCRuntime.GetClass("MTLStencilDescriptor");
        var depthStencilDescriptorClass = ObjCRuntime.GetClass("MTLDepthStencilDescriptor");

        // Default stencil state (no stencil operations)
        var depthStencilDescriptor = ObjCRuntime.New(depthStencilDescriptorClass);
        // Match NanoVG reference: always pass depth test (we only use stencil).
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setDepthCompareFunction, (ulong)MTLCompareFunction.Always);
        _defaultStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Fill shape stencil state (NanoVG uses stencil for winding; keep clip bit intact)
        var frontFaceStencil = ObjCRuntime.New(stencilDescriptorClass);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilReadMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.IncrementWrap);

        var backFaceStencil = ObjCRuntime.New(stencilDescriptorClass);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilReadMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.DecrementWrap);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, backFaceStencil);
        _fillShapeStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Fill shape stencil state (clipped): only update winding inside current clip bit.
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilReadMask, ClipStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.IncrementWrap);

        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilReadMask, ClipStencilMask);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.DecrementWrap);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, backFaceStencil);
        _fillShapeStencilStateClipped = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Fill anti-alias stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilReadMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        // Reference uses Zero here (not Keep) for correct AA fringe coverage.
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        // Triangle strips flip winding every other triangle; set both faces so stencil ops apply consistently.
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, frontFaceStencil);
        _fillAntiAliasStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Fill anti-alias stencil state (clipped): only zero winding bits inside current clip bit.
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilReadMask, ClipStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, frontFaceStencil);
        _fillAntiAliasStencilStateClipped = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Fill stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilReadMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.NotEqual);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, frontFaceStencil);
        _fillStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Stroke shape stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilReadMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.IncrementClamp);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, frontFaceStencil);
        _strokeShapeStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Stroke anti-alias stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilReadMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Keep);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, frontFaceStencil);
        _strokeAntiAliasStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Stroke clear stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilReadMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilWriteMask, NanoVgStencilMask);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, frontFaceStencil);
        _strokeClearStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Clip write stencil state
        var clipStencil = ObjCRuntime.New(stencilDescriptorClass);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilReadMask, ClipStencilMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilWriteMask, ClipStencilMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Replace);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, clipStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, clipStencil);
        _clipWriteStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Clip test stencil state
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilReadMask, ClipStencilMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilWriteMask, (ulong)0x00);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, clipStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, clipStencil);
        _clipTestStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Clip clear stencil state
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilReadMask, ClipStencilMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilWriteMask, ClipStencilMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, clipStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, clipStencil);
        _clipClearStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Clip copy-to-temp state: if (clipBit set) write tempBit = 1.
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilReadMask, ClipStencilMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilWriteMask, ClipTempMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Replace);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, clipStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, clipStencil);
        _clipCopyToTempStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Clip write-intersect state: if (tempBit == 1) write clipBit = 1.
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilReadMask, ClipTempMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilWriteMask, ClipStencilMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Replace);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, clipStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, clipStencil);
        _clipWriteIntersectStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Clip clear-temp state: tempBit = 0 everywhere.
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilReadMask, ClipTempMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilWriteMask, ClipTempMask);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(clipStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, clipStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, clipStencil);
        _clipClearTempStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Release descriptors
        ObjCRuntime.SendMessage(frontFaceStencil, ObjCRuntime.Selectors.release);
        ObjCRuntime.SendMessage(backFaceStencil, ObjCRuntime.Selectors.release);
        ObjCRuntime.SendMessage(clipStencil, ObjCRuntime.Selectors.release);
        ObjCRuntime.SendMessage(depthStencilDescriptor, ObjCRuntime.Selectors.release);
    }

    private void CreatePseudoTexture()
    {
        // Create pseudo texture (1x1 white texture)
        var textureDescriptorClass = ObjCRuntime.GetClass("MTLTextureDescriptor");
        var textureDescriptor = ObjCRuntime.SendMessage(
            textureDescriptorClass,
            MetalSelectors.texture2DDescriptorWithPixelFormat_width_height_mipmapped,
            (ulong)MTLPixelFormat.RGBA8Unorm,
            (nuint)1,
            (nuint)1,
            false
        );

        ObjCRuntime.SendMessage(
            textureDescriptor,
            MetalSelectors.setUsage,
            (ulong)(MTLTextureUsage.ShaderRead | MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderWrite));

        _pseudoTexture = ObjCRuntime.SendMessage(_device, MetalSelectors.newTextureWithDescriptor, textureDescriptor);

        // Fill with white pixel
        uint white = 0xFFFFFFFF;
        var ptr = (byte*)&white;

        var region = new MTLRegion
        {
            Origin = new MTLOrigin { X = 0, Y = 0, Z = 0 },
            Size = new MTLSize { Width = 1, Height = 1, Depth = 1 }
        };

        ObjCRuntime.SendMessage(
            _pseudoTexture,
            MetalSelectors.replaceRegion_mipmapLevel_withBytes_bytesPerRow,
            region,
            (nuint)0,
            (IntPtr)ptr,
            (nuint)4
        );

        // Create pseudo sampler
        var samplerDescriptorClass = ObjCRuntime.GetClass("MTLSamplerDescriptor");
        var samplerDescriptor = ObjCRuntime.New(samplerDescriptorClass);

        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMinFilter, (ulong)MTLSamplerMinMagFilter.Nearest);
        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMagFilter, (ulong)MTLSamplerMinMagFilter.Nearest);
        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMipFilter, (ulong)MTLSamplerMipFilter.NotMipmapped);

        _pseudoSampler = ObjCRuntime.SendMessage(_device, MetalSelectors.newSamplerStateWithDescriptor, samplerDescriptor);

        ObjCRuntime.SendMessage(samplerDescriptor, ObjCRuntime.Selectors.release);
    }

    /// <summary>
    /// Begins a frame for rendering
    /// </summary>
    public void BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio)
    {
        _viewSize = new Vector2(windowWidth, windowHeight);
        _devicePixelRatio = devicePixelRatio;

        // Reset state for new frame
        _callCount = 0;
        _pathCount = 0;
        _vertCount = 0;
        _uniformCount = 0;
        _indexCount = 0;
        _recordingClipActive = false;
    }

    /// <summary>
    /// Ends the current frame and submits rendering commands
    /// </summary>
    public void EndFrame()
    {
        // Nothing to render
        if (_callCount == 0)
        {
            return;
        }

        // Wait for buffer to be available
        _semaphore.Wait();

        // Get current buffer
        ref var buffers = ref _buffers[_currentBuffer];

        // Update buffers with current frame data
        UpdateBuffers(ref buffers);

        // Render
        Render(ref buffers);

        // Advance to next buffer
        _currentBuffer = (_currentBuffer + 1) % _bufferCount;
    }

    private void UpdateBuffers(ref MNVGbuffers buffers)
    {
        // Apple Silicon (unified memory): StorageModeShared means CPU and GPU share
        // the same allocation — no staging copies, no didModifyRange calls needed.
        // StorageModeManaged was designed for discrete GPUs with separate VRAM and
        // causes per-frame IOAccelerator staging allocations that are never reclaimed
        // at high frame rates.

        // Update vertex buffer
        var vertSize = _vertCount * sizeof(NVGvertex);
        if (buffers.vertBuffer == IntPtr.Zero || buffers.nverts < _vertCount)
        {
            if (buffers.vertBuffer != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(buffers.vertBuffer, ObjCRuntime.Selectors.release);
            }

            buffers.vertBuffer = ObjCRuntime.SendMessage(
                _device,
                MetalSelectors.newBufferWithLength_options,
                (nuint)vertSize,
                (ulong)MTLResourceOptions.StorageModeShared
            );
            buffers.nverts = _vertCount;
        }

        if (_vertCount > 0)
        {
            var contents = ObjCRuntime.SendMessage(buffers.vertBuffer, MetalSelectors.contents);
            fixed (NVGvertex* ptr = _verts)
            {
                Buffer.MemoryCopy(ptr, (void*)contents, vertSize, vertSize);
            }
        }

        // Update uniform buffer
        var uniformSize = _uniformCount * MNVG_UNIFORM_ALIGN;
        if (buffers.uniformBuffer == IntPtr.Zero || buffers.nuniforms < _uniformCount)
        {
            if (buffers.uniformBuffer != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(buffers.uniformBuffer, ObjCRuntime.Selectors.release);
            }

            buffers.uniformBuffer = ObjCRuntime.SendMessage(
                _device,
                MetalSelectors.newBufferWithLength_options,
                (nuint)uniformSize,
                (ulong)MTLResourceOptions.StorageModeShared
            );
            buffers.nuniforms = _uniformCount;
        }

        if (_uniformCount > 0)
        {
            var contents = ObjCRuntime.SendMessage(buffers.uniformBuffer, MetalSelectors.contents);
            fixed (byte* ptr = _uniforms)
            {
                Buffer.MemoryCopy(ptr, (void*)contents, uniformSize, uniformSize);
            }
        }

        // Update index buffer
        var indexSize = _indexCount * sizeof(uint);
        if (buffers.indexBuffer == IntPtr.Zero || buffers.nindexes < _indexCount)
        {
            if (buffers.indexBuffer != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(buffers.indexBuffer, ObjCRuntime.Selectors.release);
            }

            buffers.indexBuffer = ObjCRuntime.SendMessage(
                _device,
                MetalSelectors.newBufferWithLength_options,
                (nuint)indexSize,
                (ulong)MTLResourceOptions.StorageModeShared
            );
            buffers.nindexes = _indexCount;
        }

        if (_indexCount > 0)
        {
            var contents = ObjCRuntime.SendMessage(buffers.indexBuffer, MetalSelectors.contents);
            fixed (uint* ptr = _indexes)
            {
                Buffer.MemoryCopy(ptr, (void*)contents, indexSize, indexSize);
            }
        }
    }

    private void Render(ref MNVGbuffers buffers)
    {
        if (_callCount == 0)
        {
            return;
        }

        // Set viewport
        var viewport = new MTLViewport
        {
            OriginX = 0,
            OriginY = 0,
            Width = _viewSize.X * _devicePixelRatio,
            Height = _viewSize.Y * _devicePixelRatio,
            ZNear = 0,
            ZFar = 1
        };

        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setViewport, viewport);

        // Set pipeline state
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState, _pipelineState);

        // Set vertex buffer
        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.setVertexBuffer_offset_atIndex,
            buffers.vertBuffer,
            (nuint)0,
            (nuint)0
        );

        // Set view size uniform
        var viewSize = stackalloc float[2];
        viewSize[0] = _viewSize.X;
        viewSize[1] = _viewSize.Y;

        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.setVertexBytes_length_atIndex,
            (void*)viewSize,
            (nuint)8,
            (nuint)1
        );

        // Process all calls
        bool clipActive = false;
        _clipActiveInRender = false;

        for (var i = 0; i < _callCount; i++)
        {
            ref var call = ref _calls[i];


            // Set blend state
            SetBlendState(call.blendFunc);

            // Bind texture
            var texture = _pseudoTexture;
            var sampler = _pseudoSampler;

            if (call.image > 0)
            {
                ref var tex = ref FindTexture(call.image);
                if (tex.id != 0)
                {
                    texture = tex.tex;
                    sampler = tex.sampler;
                }
            }

            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.setFragmentTexture_atIndex,
                texture,
                (nuint)0
            );

            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.setFragmentSamplerState_atIndex,
                sampler,
                (nuint)0
            );

            // Process call based on type
            switch (call.type)
            {
                case MNVGcallType.MNVG_CLIP_RESET:
                    _clipActiveInRender = clipActive;
                    RenderClipReset(ref buffers, ref call);
                    clipActive = false;
                    break;
                case MNVGcallType.MNVG_CLIP:
                    _clipActiveInRender = clipActive;
                    RenderClip(ref buffers, ref call);
                    clipActive = true;
                    break;
                case MNVGcallType.MNVG_FILL:
                    _clipActiveInRender = clipActive;
                    if (call.hasCoverageAA)
                        RenderFillWithCoverage(ref buffers, ref call);
                    else
                        RenderFill(ref buffers, ref call);
                    break;
                case MNVGcallType.MNVG_STROKE:
                    _clipActiveInRender = clipActive;
                    if (call.hasCoverageAA)
                        RenderStrokeWithCoverage(ref buffers, ref call);
                    else
                        RenderStroke(ref buffers, ref call);
                    break;
                case MNVGcallType.MNVG_TRIANGLES:
                    _clipActiveInRender = clipActive;
                    RenderTriangles(ref buffers, ref call);
                    break;
            }
        }
    }

    private void SetBlendState(NVGcompositeOperationState blend)
    {
        UpdatePipelineStatesForBlend(blend);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState, _pipelineState);
    }

    private void RenderFill(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        if (call.cpuResolvedFill != 0)
        {
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);
            if (_clipActiveInRender)
            {
                ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, ClipStencilRef);
            }

            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.setFragmentBuffer_offset_atIndex,
                buffers.uniformBuffer,
                (nuint)((call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN),
                (nuint)0
            );

            var fillStart = -1;
            var fillCount = 0;
            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.fillCount > 0)
                {
                    if (fillStart < 0)
                    {
                        fillStart = path.fillOffset;
                    }

                    fillCount += path.fillCount;
                }
            }

            if (fillStart >= 0 && fillCount > 0)
            {
                ObjCRuntime.SendMessage(
                    _renderEncoder,
                    MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                    (ulong)MTLPrimitiveType.Triangle,
                    (nuint)fillStart,
                    (nuint)fillCount
                );
            }

            if (UseGeometryAA)
            {
                for (var i = 0; i < call.pathCount; i++)
                {
                    ref var path = ref _paths[call.pathOffset + i];
                    if (path.strokeCount > 0)
                    {
                        ObjCRuntime.SendMessage(
                            _renderEncoder,
                            MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                            (ulong)MTLPrimitiveType.TriangleStrip,
                            (nuint)path.strokeOffset,
                            (nuint)path.strokeCount
                        );
                    }
                }
            }

            return;
        }

        // Draw shapes using stencil
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState, _stencilOnlyPipelineState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState,
            _clipActiveInRender ? _fillShapeStencilStateClipped : _fillShapeStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue,
            _clipActiveInRender ? ClipStencilRef : (uint)0);

        // Set uniform for shape drawing
        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)(call.uniformOffset * MNVG_UNIFORM_ALIGN),
            (nuint)0
        );

        // Draw fill shapes
        for (var i = 0; i < call.pathCount; i++)
        {
            ref var path = ref _paths[call.pathOffset + i];
            if (path.fillCount > 0)
            {
                ObjCRuntime.SendMessage(
                    _renderEncoder,
                    MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                    (ulong)MTLPrimitiveType.Triangle,
                    (nuint)path.fillOffset,
                    (nuint)path.fillCount
                );
            }
        }

        // Draw anti-aliased edges
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState, _pipelineState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState,
            _clipActiveInRender ? _fillAntiAliasStencilStateClipped : _fillAntiAliasStencilState);

        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)((call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN),
            (nuint)0
        );

        if (UseGeometryAA)
        {
            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.strokeCount > 0)
                {
                    ObjCRuntime.SendMessage(
                        _renderEncoder,
                        MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                        (ulong)MTLPrimitiveType.TriangleStrip,
                        (nuint)path.strokeOffset,
                        (nuint)path.strokeCount
                    );
                }
            }
        }

        // Draw fill
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _fillStencilState);

        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.drawPrimitives_vertexStart_vertexCount,
            (ulong)MTLPrimitiveType.TriangleStrip,
            (nuint)call.triangleOffset,
            (nuint)call.triangleCount
        );

        // Reset stencil state
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
        if (_clipActiveInRender)
        {
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, ClipStencilRef);
        }
    }

    /// <summary>
    /// Coverage-AA dispatcher for transparent fills. Pass A clears coverage by
    /// virtue of the composite pipeline writing 0 back to color[1] at the end of
    /// the *previous* coverage AA call (or by the initial render-pass clear); on
    /// the first call of the frame the attachment is already 0 from clear action.
    /// Pass B (build) rasterizes fill triangles with MAX blend on color[1]. Pass
    /// C (composite) draws the bounds quad with FB fetch on color[1] and SrcOver
    /// on color[0]. All three within the same render encoder — no tile flush.
    /// </summary>
    private void RenderFillWithCoverage(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        if (call.triangleCount < 4) return;  // need a bounds quad

        // ── Pass B: build coverage ────────────────────────────────────────────
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState,
            GetCoverageBuildPipeline());
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState,
            _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);
        if (_clipActiveInRender)
        {
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, ClipStencilRef);
        }
        // Use the +1 paint uniform — strokeMult=-1 so strokeMask gives analytical
        // fill coverage. fragmentCoverageBuild ignores the type field.
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)((call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN), (nuint)0);

        var fillStart = -1;
        var fillCount = 0;
        for (var i = 0; i < call.pathCount; i++)
        {
            ref var path = ref _paths[call.pathOffset + i];
            if (path.fillCount > 0)
            {
                if (fillStart < 0) fillStart = path.fillOffset;
                fillCount += path.fillCount;
            }
        }
        if (fillStart >= 0 && fillCount > 0)
        {
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                (ulong)MTLPrimitiveType.Triangle, (nuint)fillStart, (nuint)fillCount);
        }

        // Geometry-AA fringe also contributes coverage so anti-aliased boundaries
        // make it into color[1].
        if (UseGeometryAA)
        {
            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.strokeCount > 0)
                {
                    ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                        (ulong)MTLPrimitiveType.TriangleStrip, (nuint)path.strokeOffset, (nuint)path.strokeCount);
                }
            }
        }

        // ── Pass C: composite ─────────────────────────────────────────────────
        var ok = true;
        var srcRgb = ConvertBlendFactor(call.blendFunc.SrcRGB, ref ok);
        var dstRgb = ConvertBlendFactor(call.blendFunc.DstRGB, ref ok);
        var srcAlpha = ConvertBlendFactor(call.blendFunc.SrcAlpha, ref ok);
        var dstAlpha = ConvertBlendFactor(call.blendFunc.DstAlpha, ref ok);
        if (!ok)
        {
            srcRgb = MTLBlendFactor.One; dstRgb = MTLBlendFactor.OneMinusSourceAlpha;
            srcAlpha = MTLBlendFactor.One; dstAlpha = MTLBlendFactor.OneMinusSourceAlpha;
        }
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState,
            GetCoverageCompositePipeline(srcRgb, dstRgb, srcAlpha, dstAlpha));
        // Same paint uniform — composite shader switches on type for paint color.
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)((call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN), (nuint)0);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.drawPrimitives_vertexStart_vertexCount,
            (ulong)MTLPrimitiveType.TriangleStrip,
            (nuint)call.triangleOffset, (nuint)call.triangleCount);

        // Restore default stencil state for following calls (matches non-coverage path).
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState,
            _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
    }

    /// <summary>
    /// Coverage-AA dispatcher for transparent strokes. Same structure as fill
    /// version but build pass rasterizes the stroke triangle strip; composite
    /// pass draws the bounds quad over the stroke bbox.
    /// </summary>
    private void RenderStrokeWithCoverage(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        if (call.triangleCount < 4) return;

        // ── Pass B: build coverage from stroke geometry ───────────────────────
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState,
            GetCoverageBuildPipeline());
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState,
            _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);
        if (_clipActiveInRender)
        {
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, ClipStencilRef);
        }
        // Stroke uses uniform[+0] (the original stroke paint with proper strokeMult).
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)(call.uniformOffset * MNVG_UNIFORM_ALIGN), (nuint)0);

        for (var i = 0; i < call.pathCount; i++)
        {
            ref var path = ref _paths[call.pathOffset + i];
            if (path.strokeCount > 0)
            {
                ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                    (ulong)MTLPrimitiveType.TriangleStrip, (nuint)path.strokeOffset, (nuint)path.strokeCount);
            }
        }

        // ── Pass C: composite ─────────────────────────────────────────────────
        var ok = true;
        var srcRgb = ConvertBlendFactor(call.blendFunc.SrcRGB, ref ok);
        var dstRgb = ConvertBlendFactor(call.blendFunc.DstRGB, ref ok);
        var srcAlpha = ConvertBlendFactor(call.blendFunc.SrcAlpha, ref ok);
        var dstAlpha = ConvertBlendFactor(call.blendFunc.DstAlpha, ref ok);
        if (!ok)
        {
            srcRgb = MTLBlendFactor.One; dstRgb = MTLBlendFactor.OneMinusSourceAlpha;
            srcAlpha = MTLBlendFactor.One; dstAlpha = MTLBlendFactor.OneMinusSourceAlpha;
        }
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState,
            GetCoverageCompositePipeline(srcRgb, dstRgb, srcAlpha, dstAlpha));
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)(call.uniformOffset * MNVG_UNIFORM_ALIGN), (nuint)0);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.drawPrimitives_vertexStart_vertexCount,
            (ulong)MTLPrimitiveType.TriangleStrip,
            (nuint)call.triangleOffset, (nuint)call.triangleCount);

        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState,
            _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
    }

    private void RenderStroke(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        if ((_flags & NVGcreateFlags.StencilStrokes) != 0 && !_clipActiveInRender)
        {
            // Stencil stroke
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _strokeShapeStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, (uint)0);

            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.setFragmentBuffer_offset_atIndex,
                buffers.uniformBuffer,
                (nuint)((call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN),
                (nuint)0
            );

            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.strokeCount > 0)
                {
                    ObjCRuntime.SendMessage(
                        _renderEncoder,
                        MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                        (ulong)MTLPrimitiveType.TriangleStrip,
                        (nuint)path.strokeOffset,
                        (nuint)path.strokeCount
                    );
                }
            }

            // Anti-alias
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _strokeAntiAliasStencilState);

            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.setFragmentBuffer_offset_atIndex,
                buffers.uniformBuffer,
                (nuint)(call.uniformOffset * MNVG_UNIFORM_ALIGN),
                (nuint)0
            );

            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.strokeCount > 0)
                {
                    ObjCRuntime.SendMessage(
                        _renderEncoder,
                        MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                        (ulong)MTLPrimitiveType.TriangleStrip,
                        (nuint)path.strokeOffset,
                        (nuint)path.strokeCount
                    );
                }
            }

            // Clear stencil
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _strokeClearStencilState);

            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.strokeCount > 0)
                {
                    ObjCRuntime.SendMessage(
                        _renderEncoder,
                        MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                        (ulong)MTLPrimitiveType.TriangleStrip,
                        (nuint)path.strokeOffset,
                        (nuint)path.strokeCount
                    );
                }
            }

            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
        }
        else
        {
            // Simple stroke
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);
            if (_clipActiveInRender)
            {
                ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, ClipStencilRef);
            }

            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.setFragmentBuffer_offset_atIndex,
                buffers.uniformBuffer,
                (nuint)(call.uniformOffset * MNVG_UNIFORM_ALIGN),
                (nuint)0
            );

            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.strokeCount > 0)
                {
                    ObjCRuntime.SendMessage(
                        _renderEncoder,
                        MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                        (ulong)MTLPrimitiveType.TriangleStrip,
                        (nuint)path.strokeOffset,
                        (nuint)path.strokeCount
                    );
                }
            }
        }
    }

    private void RenderTriangles(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipActiveInRender ? _clipTestStencilState : _defaultStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.Back);
        if (_clipActiveInRender)
        {
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, ClipStencilRef);
        }

        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)(call.uniformOffset * MNVG_UNIFORM_ALIGN),
            (nuint)0
        );

        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.drawPrimitives_vertexStart_vertexCount,
            (ulong)MTLPrimitiveType.Triangle,
            (nuint)call.triangleOffset,
            (nuint)call.triangleCount
        );
    }

    private void RenderClip(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        // Ensure fragments are generated (scissor disabled) so depth/stencil ops actually run.
        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)(call.uniformOffset * MNVG_UNIFORM_ALIGN),
            (nuint)0
        );

        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState, _stencilOnlyPipelineState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);

        if (_clipActiveInRender)
        {
            // Intersect new clip with existing clip using a temp bit.
            // Uses the same stencil reference for compare+replace:
            // - ref=0x81: (ref & 0x80)=0x80 for clipBit compare, (ref & 0x01)=1 for tempBit write.
            // - ref=0x81: (ref & 0x01)=1 for tempBit compare, (ref & 0x80)=0x80 for clipBit write.

            // 1) tempBit = 1 where old clipBit == 1.
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipCopyToTempStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, (uint)(ClipStencilRef | (uint)ClipTempMask));
            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                (ulong)MTLPrimitiveType.TriangleStrip,
                (nuint)call.triangleOffset,
                (nuint)call.triangleCount
            );

            // 2) Clear clipBit everywhere.
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipClearStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, (uint)0);
            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                (ulong)MTLPrimitiveType.TriangleStrip,
                (nuint)call.triangleOffset,
                (nuint)call.triangleCount
            );

            // 3) Write new clipBit where tempBit == 1 and the new path covers.
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipWriteIntersectStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, (uint)(ClipStencilRef | (uint)ClipTempMask));
            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.fillCount > 0)
                {
                    ObjCRuntime.SendMessage(
                        _renderEncoder,
                        MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                        (ulong)MTLPrimitiveType.Triangle,
                        (nuint)path.fillOffset,
                        (nuint)path.fillCount
                    );
                }
            }

            // 4) Clear tempBit everywhere.
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipClearTempStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, (uint)0);
            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                (ulong)MTLPrimitiveType.TriangleStrip,
                (nuint)call.triangleOffset,
                (nuint)call.triangleCount
            );
        }
        else
        {
            // Fresh clip: clear clipBit then write it for the new path.
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipClearStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, (uint)0);
            ObjCRuntime.SendMessage(
                _renderEncoder,
                MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                (ulong)MTLPrimitiveType.TriangleStrip,
                (nuint)call.triangleOffset,
                (nuint)call.triangleCount
            );

            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipWriteStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, ClipStencilRef);
            for (var i = 0; i < call.pathCount; i++)
            {
                ref var path = ref _paths[call.pathOffset + i];
                if (path.fillCount > 0)
                {
                    ObjCRuntime.SendMessage(
                        _renderEncoder,
                        MetalSelectors.drawPrimitives_vertexStart_vertexCount,
                        (ulong)MTLPrimitiveType.Triangle,
                        (nuint)path.fillOffset,
                        (nuint)path.fillCount
                    );
                }
            }
        }

        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipTestStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, ClipStencilRef);
    }

    private void RenderClipReset(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        if (call.triangleCount <= 0)
        {
            return;
        }

        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)(call.uniformOffset * MNVG_UNIFORM_ALIGN),
            (nuint)0
        );

        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState, _stencilOnlyPipelineState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _clipClearStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, (uint)0);

        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.drawPrimitives_vertexStart_vertexCount,
            (ulong)MTLPrimitiveType.TriangleStrip,
            (nuint)call.triangleOffset,
            (nuint)call.triangleCount
        );
    }

    private static void InitClipUniform(MNVGfragUniforms* frag)
    {
        *frag = default;
        // Disable scissor (match ConvertPaint's disabled-scissor output).
        frag->scissorExt[0] = 1.0f;
        frag->scissorExt[1] = 1.0f;
        frag->scissorScale[0] = 1.0f;
        frag->scissorScale[1] = 1.0f;
        frag->strokeThr = -1.0f;
        frag->type = (int)MNVGshaderType.MNVG_SHADER_SIMPLE;
    }

    private int AppendTriangleFan(ReadOnlySpan<NVGvertex> fan)
    {
        if (fan.Length < 3)
        {
            return 0;
        }

        var triCount = (fan.Length - 2) * 3;
        EnsureVerts(_vertCount + triCount);
        var v0 = fan[0];
        for (var i = 1; i < fan.Length - 1; i++)
        {
            _verts[_vertCount++] = v0;
            _verts[_vertCount++] = fan[i];
            _verts[_vertCount++] = fan[i + 1];
        }

        return triCount;
    }

    private ref MNVGtexture FindTexture(int id)
    {
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].id == id)
            {
                return ref _textures[i];
            }
        }
        return ref _textures[0]; // Return first (invalid) texture
    }

    /// <summary>
    /// Creates a new texture
    /// </summary>
    public int CreateTexture(int type, int width, int height, int imageFlags, ReadOnlySpan<byte> data)
    {
        // Find free texture slot
        var id = 0;
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].id == 0)
            {
                id = i + 1;
                break;
            }
        }

        if (id == 0)
        {
            if (_textureCount >= _textureCapacity)
            {
                var newCapacity = _textureCapacity * 2;
                Array.Resize(ref _textures, newCapacity);
                _textureCapacity = newCapacity;
            }
            id = ++_textureCount;
        }

        ref var tex = ref _textures[id - 1];
        tex.id = id;
        tex.width = width;
        tex.height = height;
        tex.type = type;
        tex.flags = imageFlags;

        // Determine pixel format. BGRA8Unorm tells Metal "storage layout is BGRA bytes"; the
        // GPU returns shader sample as RGBA-ordered float4, so callers with BGRA-native
        // sources (Direct2D / GDI / video decoders) skip the per-frame channel swap.
        var pixelFormat = type switch
        {
            (int)NVGtexture.Alpha => MTLPixelFormat.R8Unorm,
            (int)NVGtexture.BGRA => MTLPixelFormat.BGRA8Unorm,
            _ => MTLPixelFormat.RGBA8Unorm,
        };

        // Create texture descriptor
        var textureDescriptorClass = ObjCRuntime.GetClass("MTLTextureDescriptor");
        var textureDescriptor = ObjCRuntime.SendMessage(
            textureDescriptorClass,
            MetalSelectors.texture2DDescriptorWithPixelFormat_width_height_mipmapped,
            (ulong)pixelFormat,
            (nuint)width,
            (nuint)height,
            (imageFlags & (int)NVGimageFlags.GenerateMipmaps) != 0
        );

        ObjCRuntime.SendMessage(
            textureDescriptor,
            MetalSelectors.setUsage,
            (ulong)(MTLTextureUsage.ShaderRead | MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderWrite));

        tex.tex = ObjCRuntime.SendMessage(_device, MetalSelectors.newTextureWithDescriptor, textureDescriptor);

        // Upload data if provided
        if (!data.IsEmpty)
        {
            var bytesPerPixel = type == (int)NVGtexture.Alpha ? 1 : 4;
            var bytesPerRow = width * bytesPerPixel;

            fixed (byte* ptr = data)
            {
                var region = new MTLRegion
                {
                    Origin = new MTLOrigin { X = 0, Y = 0, Z = 0 },
                    Size = new MTLSize { Width = (nuint)width, Height = (nuint)height, Depth = 1 }
                };

                ObjCRuntime.SendMessage(
                    tex.tex,
                    MetalSelectors.replaceRegion_mipmapLevel_withBytes_bytesPerRow,
                    region,
                    (nuint)0,
                    (IntPtr)ptr,
                    (nuint)bytesPerRow
                );
            }

            if ((imageFlags & (int)NVGimageFlags.GenerateMipmaps) != 0)
            {
                GenerateMipmaps(tex.tex);
            }
        }

        // Create sampler
        var samplerDescriptorClass = ObjCRuntime.GetClass("MTLSamplerDescriptor");
        var samplerDescriptor = ObjCRuntime.New(samplerDescriptorClass);

        var nearest = (imageFlags & (int)NVGimageFlags.Nearest) != 0;
        var filter = nearest ? MTLSamplerMinMagFilter.Nearest : MTLSamplerMinMagFilter.Linear;

        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMinFilter, (ulong)filter);
        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMagFilter, (ulong)filter);

        if ((imageFlags & (int)NVGimageFlags.GenerateMipmaps) != 0)
        {
            ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMipFilter, (ulong)MTLSamplerMipFilter.Linear);
        }

        var repeatX = (imageFlags & (int)NVGimageFlags.RepeatX) != 0;
        var repeatY = (imageFlags & (int)NVGimageFlags.RepeatY) != 0;

        ObjCRuntime.SendMessage(
            samplerDescriptor,
            MetalSelectors.setSAddressMode,
            (ulong)(repeatX ? MTLSamplerAddressMode.Repeat : MTLSamplerAddressMode.ClampToEdge)
        );
        ObjCRuntime.SendMessage(
            samplerDescriptor,
            MetalSelectors.setTAddressMode,
            (ulong)(repeatY ? MTLSamplerAddressMode.Repeat : MTLSamplerAddressMode.ClampToEdge)
        );

        tex.sampler = ObjCRuntime.SendMessage(_device, MetalSelectors.newSamplerStateWithDescriptor, samplerDescriptor);

        ObjCRuntime.SendMessage(samplerDescriptor, ObjCRuntime.Selectors.release);

        return id;
    }

    /// <summary>
    /// Deletes a texture. Honors <see cref="NVGimageFlags.NoDelete"/>: when set, the
    /// MTLTexture is externally owned (e.g. wrapped via <c>CreateTextureFromHandle</c>)
    /// and only the slot + sampler are released here — the texture pointer is dropped
    /// without sending <c>release</c>.
    /// </summary>
    public void DeleteTexture(int id)
    {
        if (id <= 0 || id > _textureCount)
        {
            return;
        }

        ref var tex = ref _textures[id - 1];
        if (tex.id != id)
        {
            return;
        }

        bool noDelete = (tex.flags & (int)NVGimageFlags.NoDelete) != 0;
        if (tex.tex != IntPtr.Zero)
        {
            if (!noDelete)
            {
                ObjCRuntime.SendMessage(tex.tex, ObjCRuntime.Selectors.release);
            }
            tex.tex = IntPtr.Zero;
        }

        if (tex.sampler != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(tex.sampler, ObjCRuntime.Selectors.release);
            tex.sampler = IntPtr.Zero;
        }

        tex.id = 0;
    }

    /// <summary>
    /// Allocates an NVG texture slot wrapping an externally-owned MTLTexture. The
    /// texture pointer is stored as-is (no retain) and is NOT released on
    /// <see cref="DeleteTexture"/> — caller must include <see cref="NVGimageFlags.NoDelete"/>
    /// in <paramref name="imageFlags"/>. Sampler is allocated based on flags, just like
    /// <see cref="CreateTexture"/>. Returned id can be used with <c>nvgImagePattern</c> /
    /// <c>nvgFillPaint</c> exactly like a normal NVG image.
    /// </summary>
    public int CreateTextureFromHandle(nint mtlTexture, int width, int height, int imageFlags)
    {
        if (mtlTexture == IntPtr.Zero) return 0;

        // Find free texture slot (mirrors CreateTexture's bookkeeping).
        var id = 0;
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].id == 0)
            {
                id = i + 1;
                break;
            }
        }
        if (id == 0)
        {
            if (_textureCount >= _textureCapacity)
            {
                var newCapacity = _textureCapacity * 2;
                Array.Resize(ref _textures, newCapacity);
                _textureCapacity = newCapacity;
            }
            id = ++_textureCount;
        }

        ref var tex = ref _textures[id - 1];
        tex.id = id;
        tex.width = width;
        tex.height = height;
        tex.type = (int)NVGtexture.RGBA;
        // Force NoDelete — caller should have set it but enforce here so an accidental
        // miss doesn't leak into a release of an externally-owned texture.
        tex.flags = imageFlags | (int)NVGimageFlags.NoDelete;
        tex.tex = mtlTexture;

        // Sampler — mirror CreateTexture's logic without uploading data.
        var samplerDescriptorClass = ObjCRuntime.GetClass("MTLSamplerDescriptor");
        var samplerDescriptor = ObjCRuntime.New(samplerDescriptorClass);

        var nearest = (imageFlags & (int)NVGimageFlags.Nearest) != 0;
        var filter = nearest ? MTLSamplerMinMagFilter.Nearest : MTLSamplerMinMagFilter.Linear;
        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMinFilter, (ulong)filter);
        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMagFilter, (ulong)filter);
        if ((imageFlags & (int)NVGimageFlags.GenerateMipmaps) != 0)
        {
            ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setMipFilter, (ulong)MTLSamplerMipFilter.Linear);
        }
        var repeatX = (imageFlags & (int)NVGimageFlags.RepeatX) != 0;
        var repeatY = (imageFlags & (int)NVGimageFlags.RepeatY) != 0;
        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setSAddressMode,
            (ulong)(repeatX ? MTLSamplerAddressMode.Repeat : MTLSamplerAddressMode.ClampToEdge));
        ObjCRuntime.SendMessage(samplerDescriptor, MetalSelectors.setTAddressMode,
            (ulong)(repeatY ? MTLSamplerAddressMode.Repeat : MTLSamplerAddressMode.ClampToEdge));
        tex.sampler = ObjCRuntime.SendMessage(_device, MetalSelectors.newSamplerStateWithDescriptor, samplerDescriptor);
        ObjCRuntime.SendMessage(samplerDescriptor, ObjCRuntime.Selectors.release);

        return id;
    }

    /// <summary>
    /// Updates texture data
    /// </summary>
    public bool UpdateTexture(int id, int x, int y, int width, int height, ReadOnlySpan<byte> data)
    {
        if (id <= 0 || id > _textureCount)
        {
            return false;
        }

        ref var tex = ref _textures[id - 1];
        if (tex.id != id || tex.tex == IntPtr.Zero)
        {
            return false;
        }

        var bytesPerPixel = tex.type == (int)NVGtexture.Alpha ? 1 : 4;
        var bytesPerRow = tex.width * bytesPerPixel;
        var srcOffset = y * bytesPerRow + x * bytesPerPixel;

        fixed (byte* ptr = data)
        {
            var region = new MTLRegion
            {
                Origin = new MTLOrigin { X = (nuint)x, Y = (nuint)y, Z = 0 },
                Size = new MTLSize { Width = (nuint)width, Height = (nuint)height, Depth = 1 }
            };

            ObjCRuntime.SendMessage(
                tex.tex,
                MetalSelectors.replaceRegion_mipmapLevel_withBytes_bytesPerRow,
                region,
                (nuint)0,
                (IntPtr)(ptr + srcOffset),
                (nuint)bytesPerRow
            );
        }

        return true;
    }

    private void GenerateMipmaps(IntPtr texture)
    {
        if (_commandQueue == IntPtr.Zero || texture == IntPtr.Zero)
        {
            return;
        }

        var commandBuffer = ObjCRuntime.SendMessage(_commandQueue, MetalSelectors.commandBuffer);
        if (commandBuffer == IntPtr.Zero)
        {
            return;
        }

        var blitEncoder = ObjCRuntime.SendMessage(commandBuffer, MetalSelectors.blitCommandEncoder);
        if (blitEncoder == IntPtr.Zero)
        {
            return;
        }

        ObjCRuntime.SendMessage(blitEncoder, MetalSelectors.generateMipmapsForTexture, texture);
        ObjCRuntime.SendMessage(blitEncoder, MetalSelectors.endEncoding);
        ObjCRuntime.SendMessage(commandBuffer, MetalSelectors.commit);
        ObjCRuntime.SendMessage(commandBuffer, MetalSelectors.waitUntilCompleted);
    }

    /// <summary>
    /// Gets texture size
    /// </summary>
    public bool GetTextureSize(int id, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (id <= 0 || id > _textureCount)
        {
            return false;
        }

        ref var tex = ref _textures[id - 1];
        if (tex.id != id)
        {
            return false;
        }

        width = tex.width;
        height = tex.height;
        return true;
    }

    /// <summary>
    /// Sets the render encoder for the current frame
    /// </summary>
    public void SetRenderEncoder(IntPtr renderEncoder, IntPtr commandBuffer)
    {
        _renderEncoder = renderEncoder;
        _commandBuffer = commandBuffer;
    }

    /// <summary>
    /// Extended overload kept for API symmetry with the call site that also has
    /// the host's render-pass texture attachments at hand. Coverage AA itself
    /// runs entirely within the host's existing encoder via framebuffer fetch on
    /// color[1], so the texture handles are not stored here. Parameters are
    /// accepted (and ignored) so callers don't need to know about that detail.
    /// </summary>
    public void SetRenderEncoder(IntPtr renderEncoder, IntPtr commandBuffer,
        IntPtr colorTexture, IntPtr stencilTexture, IntPtr msaaColorTexture)
    {
        _ = colorTexture; _ = stencilTexture; _ = msaaColorTexture;
        _renderEncoder = renderEncoder;
        _commandBuffer = commandBuffer;
    }

    /// <summary>
    /// The pixel format used for the coverage AA scratch attachment (color[1]).
    /// R8Unorm — single-channel, 8-bit unsigned. The shaders only use the alpha
    /// channel of the float4 read via <c>[[color(1)]]</c>, but Metal interprets a
    /// single-channel texture as red; both endpoints (build write &amp; composite
    /// fetch) treat the channel consistently.
    /// </summary>
    public const MTLPixelFormat CoveragePixelFormat = MTLPixelFormat.R8Unorm;

    /// <summary>
    /// Ensures the coverage AA scratch texture (color[1] attachment) exists at the
    /// requested size. Call from the host before building the main render pass so
    /// the texture handle returned by <see cref="GetCoverageTexture"/> can be
    /// attached as color[1]. Tile-memory storage on Apple Silicon keeps this
    /// effectively free; on Intel/AMD it lands in private VRAM (~width×height bytes).
    /// </summary>
    public IntPtr EnsureCoverageTexture(int width, int height)
    {
        if (width <= 0 || height <= 0) return IntPtr.Zero;
        if (_coverageTexture != IntPtr.Zero
            && _coverageWidth >= width && _coverageHeight >= height)
        {
            return _coverageTexture;
        }

        if (_coverageTexture != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_coverageTexture, ObjCRuntime.Selectors.release);
            _coverageTexture = IntPtr.Zero;
        }

        var textureDescriptorClass = ObjCRuntime.GetClass("MTLTextureDescriptor");
        var textureDescriptor = ObjCRuntime.SendMessage(
            textureDescriptorClass,
            MetalSelectors.texture2DDescriptorWithPixelFormat_width_height_mipmapped,
            (ulong)CoveragePixelFormat,
            (nuint)width,
            (nuint)height,
            false);

        ObjCRuntime.SendMessage(textureDescriptor, MetalSelectors.setUsage,
            (ulong)(MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead));
        if (_sampleCount > 1)
        {
            ObjCRuntime.SendMessage(textureDescriptor, Metal.Sel.SetSampleCount, (nuint)_sampleCount);
            ObjCRuntime.SendMessage(textureDescriptor, Metal.Sel.SetTextureType,
                (ulong)MTLTextureType.Type2DMultisample);
        }

        // Try Memoryless first — on Apple Silicon TBDR this keeps the attachment in
        // tile cache only (zero DRAM, zero memory traffic). If the device doesn't
        // support Memoryless (Intel/AMD Macs lack it; newTextureWithDescriptor
        // returns nil), fall back to Private storage which still keeps the texture
        // GPU-only with one private VRAM allocation.
        ObjCRuntime.SendMessage(textureDescriptor, Metal.Sel.SetStorageMode,
            (ulong)MTLStorageMode.Memoryless);
        _coverageTexture = ObjCRuntime.SendMessage(_device, MetalSelectors.newTextureWithDescriptor, textureDescriptor);
        if (_coverageTexture == IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(textureDescriptor, Metal.Sel.SetStorageMode,
                (ulong)MTLStorageMode.Private);
            _coverageTexture = ObjCRuntime.SendMessage(_device, MetalSelectors.newTextureWithDescriptor, textureDescriptor);
        }

        _coverageWidth = width;
        _coverageHeight = height;
        return _coverageTexture;
    }

    /// <summary>The current coverage scratch texture, or IntPtr.Zero if not yet allocated.</summary>
    public IntPtr GetCoverageTexture() => _coverageTexture;

    /// <summary>
    /// Signals that the frame has completed
    /// </summary>
    public void FrameCompleted() => _semaphore.Signal();

    /// <summary>
    /// Sets viewport for rendering
    /// </summary>
    public void SetViewport(float width, float height, float devicePixelRatio)
    {
        _viewSize = new Vector2(width, height);
        _devicePixelRatio = devicePixelRatio;
    }

    /// <summary>
    /// Cancels the current frame
    /// </summary>
    public void Cancel()
    {
        _callCount = 0;
        _pathCount = 0;
        _vertCount = 0;
        _uniformCount = 0;
        _indexCount = 0;
        _recordingClipActive = false;
    }

    void INVGRenderer.BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio)
        => BeginFrame(windowWidth, windowHeight, devicePixelRatio);

    void INVGRenderer.Cancel() => Cancel();

    void INVGRenderer.Flush() => Flush();

    void INVGRenderer.RenderClip(ref NVGscissorState scissor, float fringe, ReadOnlySpan<float> bounds, ReadOnlySpan<NVGpathData> paths, ReadOnlySpan<NVGvertex> verts)
        => RenderClip(ref scissor, fringe, bounds, paths, verts);

    void INVGRenderer.ResetClip() => ResetClip();

    /// <summary>
    /// Flushes the current frame and submits rendering commands
    /// </summary>
    public void Flush() => EndFrame();

    /// <summary>
    /// Render fill paths from NVGContext
    /// </summary>
    internal void RenderFill(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        var pathOffset = _pathCount;

        for (var i = 0; i < paths.Length; i++)
        {
            ref readonly var path = ref paths[i];
            EnsurePaths(_pathCount + 1);
            ref var dstPath = ref _paths[_pathCount++];
            dstPath = default;

            // Fill: MewVG Core outputs triangle list (not fan). Copy as-is.
            dstPath.fillOffset = _vertCount;
            dstPath.fillCount = path.NFill;
            if (path.NFill > 0)
            {
                EnsureVerts(_vertCount + path.NFill);
                verts.Slice(path.FillOffset, path.NFill).CopyTo(_verts.AsSpan(_vertCount));
                _vertCount += path.NFill;
            }

            // Stroke: copy as-is.
            dstPath.strokeOffset = _vertCount;
            dstPath.strokeCount = path.NStroke;
            if (path.NStroke > 0)
            {
                EnsureVerts(_vertCount + path.NStroke);
                verts.Slice(path.StrokeOffset, path.NStroke).CopyTo(_verts.AsSpan(_vertCount));
                _vertCount += path.NStroke;
            }
        }

        // Add call
        EnsureCalls(_callCount + 1);
        ref var call = ref _calls[_callCount++];
        call = default;

        call.type = MNVGcallType.MNVG_FILL;
        call.pathOffset = pathOffset;
        call.pathCount = paths.Length;
        call.image = paint.Image;
        call.blendFunc = compositeOperation;
        call.cpuResolvedFill = 1;
        for (var i = 0; i < paths.Length; i++)
        {
            if (paths[i].NFill > 0 && (paths[i].NFill % 3) != 0)
            {
                call.cpuResolvedFill = 0;
                break;
            }
        }

        // Check convexity based on fill paths only; fringe-only paths (NFill==0)
        // should not force the fill down the non-convex stencil path.
        var convex = false;
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
            {
                convex = true;
            }
        }

        // Coverage AA path: non-convex shape with transparent paint. Build pass +
        // composite pass run within the same render encoder (no encoder switching
        // → no tile flush) by writing to color[1] with MAX blending and reading it
        // back via framebuffer fetch.
        call.hasCoverageAA = !convex && _coverageTexture != IntPtr.Zero
            && PaintHasTransparency(paint);

        // Always allocate 2 uniforms (simple at +0, fill paint at +1)
        // to match GL layout — CpuResolvedFill uses +1 directly.
        call.uniformOffset = AllocUniforms(2);

        // Simple shader at +0 (used by stencil path for non-convex)
        fixed (byte* ptr = &_uniforms[call.uniformOffset * MNVG_UNIFORM_ALIGN])
        {
            var frag = (MNVGfragUniforms*)ptr;
            *frag = default;
            frag->strokeThr = -1.0f;
            frag->type = (int)MNVGshaderType.MNVG_SHADER_SIMPLE;
        }

        // Fill shader at +1 with strokeMult = -1.0 (analytical fill coverage)
        fixed (byte* ptr = &_uniforms[(call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN])
        {
            var frag = (MNVGfragUniforms*)ptr;
            ConvertPaint(frag, ref paint, ref scissor, fringe, fringe, -1.0f);
            frag->strokeMult = -1.0f;
        }

        // Quad for stencil fill (non-convex only). Coverage AA reuses the same
        // bounds quad as the composite pass, so it must be allocated either way.
        if (!convex)
        {
            call.triangleOffset = _vertCount;
            call.triangleCount = 4;
            EnsureVerts(_vertCount + 4);

            _verts[_vertCount++] = new NVGvertex(bounds[2], bounds[3], 0.5f, 1.0f);
            _verts[_vertCount++] = new NVGvertex(bounds[2], bounds[1], 0.5f, 1.0f);
            _verts[_vertCount++] = new NVGvertex(bounds[0], bounds[3], 0.5f, 1.0f);
            _verts[_vertCount++] = new NVGvertex(bounds[0], bounds[1], 0.5f, 1.0f);
        }
    }

    /// <summary>
    /// Render stroke paths from NVGContext
    /// </summary>
    internal void RenderStroke(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        float strokeWidth,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        var maxverts = 0;
        var pathOffset = _pathCount;

        for (var i = 0; i < paths.Length; i++)
        {
            ref readonly var path = ref paths[i];
            EnsurePaths(_pathCount + 1);
            _paths[_pathCount++] = new MNVGpath
            {
                fillOffset = 0,
                fillCount = 0,
                strokeOffset = _vertCount + path.StrokeOffset,
                strokeCount = path.NStroke
            };
            maxverts = Math.Max(maxverts, path.StrokeOffset + path.NStroke);
        }

        // Copy vertices
        EnsureVerts(_vertCount + maxverts);
        for (var i = 0; i < maxverts && i < verts.Length; i++)
        {
            _verts[_vertCount + i] = verts[i];
        }
        _vertCount += maxverts;

        // Add call
        EnsureCalls(_callCount + 1);
        ref var call = ref _calls[_callCount++];
        call = default;

        call.type = MNVGcallType.MNVG_STROKE;
        call.pathOffset = pathOffset;
        call.pathCount = paths.Length;
        call.image = paint.Image;
        call.blendFunc = compositeOperation;

        // Coverage AA path: transparent stroke (any concave/non-convex) goes
        // through build + composite passes that share color[1] via FB fetch — no
        // encoder switch, single SrcOver per pixel even at sharp join overlaps.
        var isConvexStroke = paths.Length == 1 && paths[0].Convex;
        call.hasCoverageAA = !isConvexStroke && _coverageTexture != IntPtr.Zero
            && PaintHasTransparency(paint);

        // Allocate uniforms
        var stencilStrokes = (_flags & NVGcreateFlags.StencilStrokes) != 0 && !_recordingClipActive;
        call.uniformOffset = AllocUniforms(stencilStrokes ? 2 : 1);

        fixed (byte* ptr = &_uniforms[call.uniformOffset * MNVG_UNIFORM_ALIGN])
        {
            var frag = (MNVGfragUniforms*)ptr;
            ConvertPaint(frag, ref paint, ref scissor, strokeWidth, fringe, -1.0f);
        }

        if (stencilStrokes)
        {
            fixed (byte* ptr = &_uniforms[(call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN])
            {
                var frag = (MNVGfragUniforms*)ptr;
                ConvertPaint(frag, ref paint, ref scissor, strokeWidth, fringe, 1.0f - 0.5f / 255.0f);
            }
        }

        // Coverage AA composite needs a bounds quad. Compute from stroke verts
        // (path bounds aren't passed to RenderStroke) and append after geometry.
        if (call.hasCoverageAA)
        {
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (var i = 0; i < paths.Length; i++)
            {
                ref readonly var path = ref paths[i];
                if (path.NStroke <= 0) continue;
                var src = verts.Slice(path.StrokeOffset, path.NStroke);
                for (var j = 0; j < src.Length; j++)
                {
                    ref readonly var v = ref src[j];
                    if (v.X < minX) minX = v.X;
                    if (v.Y < minY) minY = v.Y;
                    if (v.X > maxX) maxX = v.X;
                    if (v.Y > maxY) maxY = v.Y;
                }
            }
            if (float.IsPositiveInfinity(minX))
            {
                // No verts collected — disable coverage path; falls back to normal stroke.
                call.hasCoverageAA = false;
            }
            else
            {
                EnsureVerts(_vertCount + 4);
                call.triangleOffset = _vertCount;
                call.triangleCount = 4;
                _verts[_vertCount++] = new NVGvertex(maxX, maxY, 0.5f, 1.0f);
                _verts[_vertCount++] = new NVGvertex(maxX, minY, 0.5f, 1.0f);
                _verts[_vertCount++] = new NVGvertex(minX, maxY, 0.5f, 1.0f);
                _verts[_vertCount++] = new NVGvertex(minX, minY, 0.5f, 1.0f);
            }
        }
    }

    /// <summary>
    /// Render triangles (for text)
    /// </summary>
    internal void RenderTriangles(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        ReadOnlySpan<NVGvertex> verts,
        float fringe)
    {
        // Add call
        EnsureCalls(_callCount + 1);
        ref var call = ref _calls[_callCount++];
        call = default;

        call.type = MNVGcallType.MNVG_TRIANGLES;
        call.image = paint.Image;
        call.blendFunc = compositeOperation;

        // Allocate uniforms
        call.uniformOffset = AllocUniforms(1);
        fixed (byte* ptr = &_uniforms[call.uniformOffset * MNVG_UNIFORM_ALIGN])
        {
            var frag = (MNVGfragUniforms*)ptr;
            ConvertPaint(frag, ref paint, ref scissor, 1.0f, fringe, -1.0f);
            frag->type = (int)MNVGshaderType.MNVG_SHADER_IMG;
        }

        // Copy vertices
        call.triangleOffset = _vertCount;
        call.triangleCount = verts.Length;
        EnsureVerts(_vertCount + verts.Length);
        verts.CopyTo(_verts.AsSpan(_vertCount));
        _vertCount += verts.Length;
    }

    internal void RenderClip(
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        var pathOffset = _pathCount;

        for (var i = 0; i < paths.Length; i++)
        {
            ref readonly var path = ref paths[i];
            EnsurePaths(_pathCount + 1);
            ref var dstPath = ref _paths[_pathCount++];
            dstPath = default;

            dstPath.fillOffset = _vertCount;
            dstPath.fillCount = path.NFill;
            if (path.NFill > 0)
            {
                EnsureVerts(_vertCount + path.NFill);
                verts.Slice(path.FillOffset, path.NFill).CopyTo(_verts.AsSpan(_vertCount));
                _vertCount += path.NFill;
            }

            dstPath.strokeOffset = 0;
            dstPath.strokeCount = 0;
        }

        EnsureCalls(_callCount + 1);
        ref var call = ref _calls[_callCount++];
        call = default;
        call.type = MNVGcallType.MNVG_CLIP;
        call.pathOffset = pathOffset;
        call.pathCount = paths.Length;
        call.blendFunc = default;

        // Allocate a safe "simple" uniform so stencil-only passes don't get clipped away by garbage scissor state.
        call.uniformOffset = AllocUniforms(1);
        fixed (byte* ptr = &_uniforms[call.uniformOffset * MNVG_UNIFORM_ALIGN])
        {
            InitClipUniform((MNVGfragUniforms*)ptr);
        }

        // Full-screen quad used for clear/intersection operations.
        call.triangleOffset = _vertCount;
        call.triangleCount = 4;
        EnsureVerts(_vertCount + 4);
        _verts[_vertCount++] = new NVGvertex(_viewSize.X, _viewSize.Y, 0.5f, 1.0f);
        _verts[_vertCount++] = new NVGvertex(_viewSize.X, 0, 0.5f, 1.0f);
        _verts[_vertCount++] = new NVGvertex(0, _viewSize.Y, 0.5f, 1.0f);
        _verts[_vertCount++] = new NVGvertex(0, 0, 0.5f, 1.0f);

        _recordingClipActive = true;
    }

    internal void ResetClip()
    {
        EnsureCalls(_callCount + 1);
        ref var call = ref _calls[_callCount++];
        call = default;
        call.type = MNVGcallType.MNVG_CLIP_RESET;
        call.pathOffset = 0;
        call.pathCount = 0;
        call.blendFunc = default;

        call.uniformOffset = AllocUniforms(1);
        fixed (byte* ptr = &_uniforms[call.uniformOffset * MNVG_UNIFORM_ALIGN])
        {
            InitClipUniform((MNVGfragUniforms*)ptr);
        }

        call.triangleOffset = _vertCount;
        call.triangleCount = 4;
        EnsureVerts(_vertCount + 4);

        _verts[_vertCount++] = new NVGvertex(_viewSize.X, _viewSize.Y, 0.5f, 1.0f);
        _verts[_vertCount++] = new NVGvertex(_viewSize.X, 0, 0.5f, 1.0f);
        _verts[_vertCount++] = new NVGvertex(0, _viewSize.Y, 0.5f, 1.0f);
        _verts[_vertCount++] = new NVGvertex(0, 0, 0.5f, 1.0f);

        _recordingClipActive = false;
    }

    private void ConvertPaint(MNVGfragUniforms* frag, ref NVGpaint paint, ref NVGscissorState scissor, float width, float fringe, float strokeThr)
    {
        Span<float> invxform = stackalloc float[6];

        // Scissor
        if (scissor.Extent[0] < -0.5f || scissor.Extent[1] < -0.5f)
        {
            frag->scissorMat[0] = 0;
            frag->scissorMat[1] = 0;
            frag->scissorMat[2] = 0;
            frag->scissorMat[3] = 0;
            frag->scissorMat[4] = 0;
            frag->scissorMat[5] = 0;
            frag->scissorMat[6] = 0;
            frag->scissorMat[7] = 0;
            frag->scissorMat[8] = 0;
            frag->scissorMat[9] = 0;
            frag->scissorMat[10] = 0;
            frag->scissorMat[11] = 0;
            frag->scissorExt[0] = 1.0f;
            frag->scissorExt[1] = 1.0f;
            frag->scissorScale[0] = 1.0f;
            frag->scissorScale[1] = 1.0f;
        }
        else
        {
            NVGMath.TransformInverse(invxform, scissor.Xform);
            frag->scissorMat[0] = invxform[0];
            frag->scissorMat[1] = invxform[1];
            frag->scissorMat[2] = 0;
            frag->scissorMat[3] = 0;
            frag->scissorMat[4] = invxform[2];
            frag->scissorMat[5] = invxform[3];
            frag->scissorMat[6] = 0;
            frag->scissorMat[7] = 0;
            frag->scissorMat[8] = invxform[4];
            frag->scissorMat[9] = invxform[5];
            frag->scissorMat[10] = 0;
            frag->scissorMat[11] = 0;
            frag->scissorExt[0] = scissor.Extent[0];
            frag->scissorExt[1] = scissor.Extent[1];
            frag->scissorScale[0] = MathF.Sqrt(scissor.Xform[0] * scissor.Xform[0] + scissor.Xform[2] * scissor.Xform[2]) / fringe;
            frag->scissorScale[1] = MathF.Sqrt(scissor.Xform[1] * scissor.Xform[1] + scissor.Xform[3] * scissor.Xform[3]) / fringe;
        }

        // Paint
        frag->extent = paint.Extent;
        frag->strokeMult = (width * 0.5f + fringe * 0.5f) / fringe;
        frag->strokeThr = strokeThr;

        if (paint.PaintKind == (int)NVGpaintKind.GradientRadial)
        {
            ref var tex = ref FindTexture(paint.Image);
            if (tex.id == 0)
            {
                return;
            }

            frag->type = (int)MNVGshaderType.MNVG_SHADER_GRADIENT_RADIAL;
            frag->texType = 0;
            frag->gradientCenter[0] = paint.Center[0];
            frag->gradientCenter[1] = paint.Center[1];
            frag->gradientRadii[0] = paint.Radius2[0];
            frag->gradientRadii[1] = paint.Radius2[1];
            frag->gradientFocal[0] = paint.Focal[0];
            frag->gradientFocal[1] = paint.Focal[1];
            frag->gradientSpread = paint.SpreadMethod;
            frag->gradientReserved = 0.0f;
            NVGMath.TransformInverse(invxform, paint.Xform);
        }
        else if (paint.PaintKind == (int)NVGpaintKind.GradientLinear)
        {
            ref var tex = ref FindTexture(paint.Image);
            if (tex.id == 0)
            {
                return;
            }

            frag->type = (int)MNVGshaderType.MNVG_SHADER_GRADIENT_LINEAR;
            frag->texType = 0;
            frag->gradientCenter[0] = paint.Center[0];
            frag->gradientCenter[1] = paint.Center[1];
            frag->gradientFocal[0] = paint.Focal[0];
            frag->gradientFocal[1] = paint.Focal[1];
            frag->gradientRadii[0] = 0.0f;
            frag->gradientRadii[1] = 0.0f;
            frag->gradientSpread = paint.SpreadMethod;
            frag->gradientReserved = 0.0f;
            NVGMath.TransformInverse(invxform, paint.Xform);
        }
        else if (paint.Image > 0)
        {
            if (GetTextureSize(paint.Image, out var tw, out var th))
            {
                ref var tex = ref FindTexture(paint.Image);
                if ((tex.flags & (int)NVGimageFlags.FlipY) != 0)
                {
                    Span<float> m1 = stackalloc float[6];
                    Span<float> m2 = stackalloc float[6];
                    NVGMath.TransformTranslate(m1, 0.0f, frag->extent[1] * 0.5f);
                    NVGMath.TransformMultiply(m1, paint.Xform);
                    NVGMath.TransformScale(m2, 1.0f, -1.0f);
                    NVGMath.TransformMultiply(m2, m1);
                    NVGMath.TransformTranslate(m1, 0.0f, -frag->extent[1] * 0.5f);
                    NVGMath.TransformMultiply(m1, m2);
                    NVGMath.TransformInverse(invxform, m1);
                }
                else
                {
                    NVGMath.TransformInverse(invxform, paint.Xform);
                }

                frag->type = (int)MNVGshaderType.MNVG_SHADER_FILLIMG;
                // BGRA8Unorm textures sample to (R,G,B,A) the same as RGBA8Unorm — the GPU
                // does the swizzle on read, so the shader sees colour data in either case.
                // Only Alpha textures need texType=2 (replicate red channel to all).
                if (tex.type == (int)NVGtexture.RGBA || tex.type == (int)NVGtexture.BGRA)
                {
                    frag->texType = (tex.flags & (int)NVGimageFlags.Premultiplied) != 0 ? 0 : 1;
                }
                else
                {
                    frag->texType = 2;
                }
            }
            else
            {
                frag->type = (int)MNVGshaderType.MNVG_SHADER_FILLGRAD;
                frag->texType = 0;
                frag->radius = paint.Radius;
                frag->feather = paint.Feather;
                NVGMath.TransformInverse(invxform, paint.Xform);
            }
        }
        else
        {
            frag->type = (int)MNVGshaderType.MNVG_SHADER_FILLGRAD;
            frag->texType = 0;
            frag->radius = paint.Radius;
            frag->feather = paint.Feather;
            NVGMath.TransformInverse(invxform, paint.Xform);
        }

        frag->paintMat[0] = invxform[0];
        frag->paintMat[1] = invxform[1];
        frag->paintMat[2] = 0;
        frag->paintMat[3] = 0;
        frag->paintMat[4] = invxform[2];
        frag->paintMat[5] = invxform[3];
        frag->paintMat[6] = 0;
        frag->paintMat[7] = 0;
        frag->paintMat[8] = invxform[4];
        frag->paintMat[9] = invxform[5];
        frag->paintMat[10] = 0;
        frag->paintMat[11] = 0;

        frag->innerCol = PremultiplyColor(paint.InnerColor);
        frag->outerCol = PremultiplyColor(paint.OuterColor);
    }

    private static NVGcolor PremultiplyColor(NVGcolor color)
        => new NVGcolor(color.R * color.A, color.G * color.A, color.B * color.A, color.A);

    private int AllocUniforms(int count)
    {
        var offset = _uniformCount;
        EnsureUniforms(_uniformCount + count);
        _uniformCount += count;
        return offset;
    }

    private void EnsureCalls(int count)
    {
        if (count > _callCapacity)
        {
            var newCapacity = Math.Max(count, _callCapacity * 2);
            Array.Resize(ref _calls, newCapacity);
            _callCapacity = newCapacity;
        }
    }

    private void EnsurePaths(int count)
    {
        if (count > _pathCapacity)
        {
            var newCapacity = Math.Max(count, _pathCapacity * 2);
            Array.Resize(ref _paths, newCapacity);
            _pathCapacity = newCapacity;
        }
    }

    private void EnsureVerts(int count)
    {
        if (count > _vertCapacity)
        {
            var newCapacity = Math.Max(count, _vertCapacity * 2);
            Array.Resize(ref _verts, newCapacity);
            _vertCapacity = newCapacity;
        }
    }

    private void EnsureUniforms(int count)
    {
        var size = count * MNVG_UNIFORM_ALIGN;
        if (size > _uniformCapacity)
        {
            var newCapacity = Math.Max(size, _uniformCapacity * 2);
            Array.Resize(ref _uniforms, newCapacity);
            _uniformCapacity = newCapacity;
        }
    }

    void INVGRenderer.RenderFill(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
        => RenderFill(ref paint, compositeOperation, ref scissor, fringe, bounds, paths, verts);

    void INVGRenderer.RenderStroke(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        float strokeWidth,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
        => RenderStroke(ref paint, compositeOperation, ref scissor, fringe, strokeWidth, paths, verts);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Release Metal resources
        if (_pipelineState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_pipelineState, ObjCRuntime.Selectors.release);
        }

        if (_stencilOnlyPipelineState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_stencilOnlyPipelineState, ObjCRuntime.Selectors.release);
        }

        if (_defaultStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_defaultStencilState, ObjCRuntime.Selectors.release);
        }

        if (_fillShapeStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fillShapeStencilState, ObjCRuntime.Selectors.release);
        }

        if (_fillShapeStencilStateClipped != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fillShapeStencilStateClipped, ObjCRuntime.Selectors.release);
        }

        if (_fillAntiAliasStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fillAntiAliasStencilState, ObjCRuntime.Selectors.release);
        }

        if (_fillAntiAliasStencilStateClipped != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fillAntiAliasStencilStateClipped, ObjCRuntime.Selectors.release);
        }

        if (_fillStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fillStencilState, ObjCRuntime.Selectors.release);
        }

        if (_strokeShapeStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_strokeShapeStencilState, ObjCRuntime.Selectors.release);
        }

        if (_strokeAntiAliasStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_strokeAntiAliasStencilState, ObjCRuntime.Selectors.release);
        }

        if (_strokeClearStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_strokeClearStencilState, ObjCRuntime.Selectors.release);
        }

        if (_clipWriteStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_clipWriteStencilState, ObjCRuntime.Selectors.release);
        }

        if (_clipTestStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_clipTestStencilState, ObjCRuntime.Selectors.release);
        }

        if (_clipClearStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_clipClearStencilState, ObjCRuntime.Selectors.release);
        }

        if (_clipCopyToTempStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_clipCopyToTempStencilState, ObjCRuntime.Selectors.release);
        }

        if (_clipWriteIntersectStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_clipWriteIntersectStencilState, ObjCRuntime.Selectors.release);
        }

        if (_clipClearTempStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_clipClearTempStencilState, ObjCRuntime.Selectors.release);
        }

        if (_vertexFunction != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_vertexFunction, ObjCRuntime.Selectors.release);
        }

        if (_fragmentFunction != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fragmentFunction, ObjCRuntime.Selectors.release);
        }

        if (_fragmentAAFunction != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fragmentAAFunction, ObjCRuntime.Selectors.release);
        }

        if (_fragmentCoverageBuildFn != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fragmentCoverageBuildFn, ObjCRuntime.Selectors.release);
        }

        if (_fragmentCoverageCompositeFn != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fragmentCoverageCompositeFn, ObjCRuntime.Selectors.release);
        }

        if (_coverageBuildPipeline != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_coverageBuildPipeline, ObjCRuntime.Selectors.release);
        }

        if (_coverageCompositePipeline != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_coverageCompositePipeline, ObjCRuntime.Selectors.release);
        }

        if (_coverageTexture != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_coverageTexture, ObjCRuntime.Selectors.release);
        }

        if (_library != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_library, ObjCRuntime.Selectors.release);
        }

        if (_pseudoTexture != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_pseudoTexture, ObjCRuntime.Selectors.release);
        }

        if (_pseudoSampler != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_pseudoSampler, ObjCRuntime.Selectors.release);
        }

        // Release textures
        for (var i = 0; i < _textureCount; i++)
        {
            if (_textures[i].tex != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(_textures[i].tex, ObjCRuntime.Selectors.release);
            }

            if (_textures[i].sampler != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(_textures[i].sampler, ObjCRuntime.Selectors.release);
            }
        }

        // Release buffers
        for (var i = 0; i < _bufferCount; i++)
        {
            if (_buffers[i].vertBuffer != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(_buffers[i].vertBuffer, ObjCRuntime.Selectors.release);
            }

            if (_buffers[i].uniformBuffer != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(_buffers[i].uniformBuffer, ObjCRuntime.Selectors.release);
            }

            if (_buffers[i].indexBuffer != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(_buffers[i].indexBuffer, ObjCRuntime.Selectors.release);
            }

            if (_buffers[i].stencilTexture != IntPtr.Zero)
            {
                ObjCRuntime.SendMessage(_buffers[i].stencilTexture, ObjCRuntime.Selectors.release);
            }
        }

        if (_commandQueue != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_commandQueue, ObjCRuntime.Selectors.release);
        }

        if (_device != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_device, ObjCRuntime.Selectors.release);
        }

        _semaphore?.Dispose();
    }
}

/// <summary>
/// NSString wrapper for Objective-C interop
/// </summary>
internal class NSString : IDisposable
{
    public IntPtr Handle { get; private set; }

    public NSString(string value)
    {
        Handle = ObjCRuntime.CreateNSString(value);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(Handle, ObjCRuntime.Selectors.release);
            Handle = IntPtr.Zero;
        }
    }
}

/// <summary>
/// NSRange structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NSRange
{
    public nuint location;
    public nuint length;
}
