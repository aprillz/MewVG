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
}

/// <summary>
/// Call types for rendering
/// </summary>
public enum MNVGcallType
{
    MNVG_NONE = 0,
    MNVG_FILL = 1,
    MNVG_CONVEXFILL = 2,
    MNVG_STROKE = 3,
    MNVG_TRIANGLES = 4,
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
    public float strokeMult;               // 4 bytes
    public float strokeThr;                // 4 bytes
    public int texType;                    // 4 bytes
    public int type;                       // 4 bytes
    // Total: 176 bytes
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
    public NVGcompositeOperationState blendFunc;
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

    // Metal objects
    private IntPtr _device;              // id<MTLDevice>
    private IntPtr _commandQueue;        // id<MTLCommandQueue>
    private IntPtr _library;             // id<MTLLibrary>
    private IntPtr _vertexFunction;      // id<MTLFunction>
    private IntPtr _fragmentFunction;    // id<MTLFunction>
    private IntPtr _fragmentAAFunction;  // id<MTLFunction>

    // Pipeline states
    private IntPtr _pipelineState;           // id<MTLRenderPipelineState>
    private IntPtr _stencilOnlyPipelineState;
    private IntPtr _pseudoSampler;           // id<MTLSamplerState>
    private IntPtr _pseudoTexture;           // id<MTLTexture>
    private MTLPixelFormat _pipelinePixelFormat;
    private MTLBlendFactor _blendSrcRgb;
    private MTLBlendFactor _blendDstRgb;
    private MTLBlendFactor _blendSrcAlpha;
    private MTLBlendFactor _blendDstAlpha;

    // Depth stencil states
    private IntPtr _defaultStencilState;
    private IntPtr _fillShapeStencilState;
    private IntPtr _fillAntiAliasStencilState;
    private IntPtr _fillStencilState;
    private IntPtr _strokeShapeStencilState;
    private IntPtr _strokeAntiAliasStencilState;
    private IntPtr _strokeClearStencilState;

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
    private int _indexCapacity;

    // Frame state
    private IntPtr _renderEncoder;       // id<MTLRenderCommandEncoder>
    private IntPtr _commandBuffer;       // id<MTLCommandBuffer>
    private DispatchSemaphore _semaphore;

    // Settings
    private NVGcreateFlags _flags;
    private MTLPixelFormat _pixelFormat;
    private MTLPixelFormat _stencilFormat;
    private float _devicePixelRatio;
    private Vector2 _viewSize;
    private bool _disposed;

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
        _indexCapacity = 4096;

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

        // Load shader library from bitcode
        LoadShaderLibrary();

        // Get shader functions
        _vertexFunction = GetFunction("vertexShader");
        _fragmentFunction = GetFunction("fragmentShader");
        _fragmentAAFunction = GetFunction("fragmentShaderAA");

        // Create pipeline states
        CreatePipelineStates();

        // Create depth stencil states
        CreateDepthStencilStates();

        // Create pseudo texture and sampler for when no texture is bound
        CreatePseudoTexture();
    }

    private void LoadShaderLibrary()
    {
        var bitcode = Shaders.ShaderBitcode.MacOS;

        if (bitcode.IsEmpty)
        {
            throw new InvalidOperationException("Shader bitcode is empty");
        }

        fixed (byte* ptr = bitcode)
        {
            // Create dispatch_data from the bitcode
            var dispatchData = Dispatch.DataCreate(
                (void*)ptr,
                (nuint)bitcode.Length,
                IntPtr.Zero,
                IntPtr.Zero
            );

            if (dispatchData == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create dispatch data");
            }

            try
            {
                // Create library from data
                var error = IntPtr.Zero;
                _library = ObjCRuntime.SendMessage(
                    _device,
                    MetalSelectors.newLibraryWithData_error,
                    dispatchData,
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
                    throw new InvalidOperationException($"Failed to create shader library: {errorMsg}");
                }
            }
            finally
            {
                // Release dispatch data
                ObjCRuntime.SendMessage(dispatchData, ObjCRuntime.Selectors.release);
            }
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
        var fragmentFunc = (_flags & NVGcreateFlags.Antialias) != 0 ? _fragmentAAFunction : _fragmentFunction;

        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setVertexFunction, _vertexFunction);
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setFragmentFunction, stencilOnly ? IntPtr.Zero : fragmentFunc);
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setVertexDescriptor, vertexDescriptor);
        ObjCRuntime.SendMessage(pipelineDescriptor, MetalSelectors.setStencilAttachmentPixelFormat, (ulong)_stencilFormat);

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

        // Fill shape stencil state
        var frontFaceStencil = ObjCRuntime.New(stencilDescriptorClass);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.IncrementWrap);

        var backFaceStencil = ObjCRuntime.New(stencilDescriptorClass);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(backFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.DecrementWrap);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, backFaceStencil);
        _fillShapeStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Fill anti-alias stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        // Reference uses Zero here (not Keep) for correct AA fringe coverage.
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, IntPtr.Zero);
        _fillAntiAliasStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Fill stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.NotEqual);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, IntPtr.Zero);
        _fillStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Stroke shape stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Equal);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Keep);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.IncrementClamp);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, IntPtr.Zero);
        _strokeShapeStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Stroke anti-alias stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Keep);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, IntPtr.Zero);
        _strokeAntiAliasStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Stroke clear stencil state
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilCompareFunction, (ulong)MTLCompareFunction.Always);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setStencilFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthFailureOperation, (ulong)MTLStencilOperation.Zero);
        ObjCRuntime.SendMessage(frontFaceStencil, MetalSelectors.setDepthStencilPassOperation, (ulong)MTLStencilOperation.Zero);

        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setFrontFaceStencil, frontFaceStencil);
        ObjCRuntime.SendMessage(depthStencilDescriptor, MetalSelectors.setBackFaceStencil, IntPtr.Zero);
        _strokeClearStencilState = ObjCRuntime.SendMessage(_device, MetalSelectors.newDepthStencilStateWithDescriptor, depthStencilDescriptor);

        // Release descriptors
        ObjCRuntime.SendMessage(frontFaceStencil, ObjCRuntime.Selectors.release);
        ObjCRuntime.SendMessage(backFaceStencil, ObjCRuntime.Selectors.release);
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
                (ulong)MTLResourceOptions.StorageModeManaged
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

            var range = new NSRange { location = 0, length = (nuint)vertSize };
            ObjCRuntime.SendMessage(buffers.vertBuffer, MetalSelectors.didModifyRange, range);
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
                (ulong)MTLResourceOptions.StorageModeManaged
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

            var range = new NSRange { location = 0, length = (nuint)uniformSize };
            ObjCRuntime.SendMessage(buffers.uniformBuffer, MetalSelectors.didModifyRange, range);
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
                (ulong)MTLResourceOptions.StorageModeManaged
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

            var range = new NSRange { location = 0, length = (nuint)indexSize };
            ObjCRuntime.SendMessage(buffers.indexBuffer, MetalSelectors.didModifyRange, range);
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
                case MNVGcallType.MNVG_FILL:
                    RenderFill(ref buffers, ref call);
                    break;
                case MNVGcallType.MNVG_CONVEXFILL:
                    RenderConvexFill(ref buffers, ref call);
                    break;
                case MNVGcallType.MNVG_STROKE:
                    RenderStroke(ref buffers, ref call);
                    break;
                case MNVGcallType.MNVG_TRIANGLES:
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
        // Draw shapes using stencil
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setRenderPipelineState, _stencilOnlyPipelineState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _fillShapeStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setStencilReferenceValue, (uint)0);

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
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _fillAntiAliasStencilState);

        ObjCRuntime.SendMessage(
            _renderEncoder,
            MetalSelectors.setFragmentBuffer_offset_atIndex,
            buffers.uniformBuffer,
            (nuint)((call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN),
            (nuint)0
        );

        if ((_flags & NVGcreateFlags.Antialias) != 0)
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
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _defaultStencilState);
    }

    private void RenderConvexFill(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _defaultStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);

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

            if ((_flags & NVGcreateFlags.Antialias) != 0 && path.strokeCount > 0)
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

    private void RenderStroke(ref MNVGbuffers buffers, ref MNVGcall call)
    {
        if ((_flags & NVGcreateFlags.StencilStrokes) != 0)
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

            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _defaultStencilState);
        }
        else
        {
            // Simple stroke
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _defaultStencilState);
            ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.None);

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
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setDepthStencilState, _defaultStencilState);
        ObjCRuntime.SendMessage(_renderEncoder, MetalSelectors.setCullMode, (ulong)MTLCullMode.Back);

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

        // Determine pixel format
        var pixelFormat = type == (int)global::Aprillz.MewVG.NVGtexture.Alpha
            ? MTLPixelFormat.R8Unorm
            : MTLPixelFormat.RGBA8Unorm;

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
            var bytesPerPixel = type == (int)global::Aprillz.MewVG.NVGtexture.Alpha ? 1 : 4;
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
    /// Deletes a texture
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

        if (tex.tex != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(tex.tex, ObjCRuntime.Selectors.release);
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

        var bytesPerPixel = tex.type == (int)global::Aprillz.MewVG.NVGtexture.Alpha ? 1 : 4;
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

        // Register completion handler to signal semaphore
        // This will be called when the command buffer completes
        // In production, you'd want to use a proper completion handler
    }

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
    }

    void INVGRenderer.BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio)
        => BeginFrame(windowWidth, windowHeight, devicePixelRatio);

    void INVGRenderer.Cancel() => Cancel();

    void INVGRenderer.Flush() => Flush();

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

            // Fill: convert triangle fan to triangle list for Metal.
            dstPath.fillOffset = _vertCount;
            dstPath.fillCount = 0;
            if (path.NFill >= 3)
            {
                var fillSpan = verts.Slice(path.FillOffset, path.NFill);
                dstPath.fillCount = AppendTriangleFan(fillSpan);
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

        var convex = paths.Length == 1 && paths[0].Convex;
        call.type = convex ? MNVGcallType.MNVG_CONVEXFILL : MNVGcallType.MNVG_FILL;
        call.pathOffset = pathOffset;
        call.pathCount = paths.Length;
        call.image = paint.Image;
        call.blendFunc = compositeOperation;

        // Allocate uniforms
        call.uniformOffset = AllocUniforms(convex ? 1 : 2);

        if (!convex)
        {
            // Simple shader for stencil (match GL layout: simple at base, fill at +1)
            fixed (byte* ptr = &_uniforms[call.uniformOffset * MNVG_UNIFORM_ALIGN])
            {
                var frag = (MNVGfragUniforms*)ptr;
                *frag = default;
                frag->strokeThr = -1.0f;
                frag->type = (int)MNVGshaderType.MNVG_SHADER_SIMPLE;
            }

            // Fill shader goes to +1
            fixed (byte* ptr = &_uniforms[(call.uniformOffset + 1) * MNVG_UNIFORM_ALIGN])
            {
                var frag = (MNVGfragUniforms*)ptr;
                ConvertPaint(frag, ref paint, ref scissor, fringe, fringe, -1.0f);
            }
        }
        else
        {
            // Convex fill uses single uniform
            fixed (byte* ptr = &_uniforms[call.uniformOffset * MNVG_UNIFORM_ALIGN])
            {
                var frag = (MNVGfragUniforms*)ptr;
                ConvertPaint(frag, ref paint, ref scissor, fringe, fringe, -1.0f);
            }
        }

        // Quad for fill
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

        // Allocate uniforms
        var stencilStrokes = (_flags & NVGcreateFlags.StencilStrokes) != 0;
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

        if (paint.Image > 0)
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
                if (tex.type == (int)NVGtextureType.RGBA)
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

        if (_fillAntiAliasStencilState != IntPtr.Zero)
        {
            ObjCRuntime.SendMessage(_fillAntiAliasStencilState, ObjCRuntime.Selectors.release);
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
