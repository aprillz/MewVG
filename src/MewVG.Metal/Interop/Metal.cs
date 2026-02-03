// Copyright (c) 2024 .NET Port
// MIT License

using System.Runtime.InteropServices;

namespace Aprillz.MewVG.Interop;

/// <summary>
/// Metal framework interop for macOS.
/// </summary>
public static unsafe partial class Metal
{
    private const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";

    [LibraryImport(MetalFramework, EntryPoint = "MTLCreateSystemDefaultDevice")]
    public static partial nint CreateSystemDefaultDevice();

    // Metal classes
    public static readonly nint MTLDevice = ObjCRuntime.GetClass("MTLDevice");
    public static readonly nint MTLCommandQueue = ObjCRuntime.GetClass("MTLCommandQueue");
    public static readonly nint MTLRenderPassDescriptor = ObjCRuntime.GetClass("MTLRenderPassDescriptor");
    public static readonly nint MTLTextureDescriptor = ObjCRuntime.GetClass("MTLTextureDescriptor");
    public static readonly nint MTLVertexDescriptor = ObjCRuntime.GetClass("MTLVertexDescriptor");
    public static readonly nint MTLSamplerDescriptor = ObjCRuntime.GetClass("MTLSamplerDescriptor");
    public static readonly nint MTLDepthStencilDescriptor = ObjCRuntime.GetClass("MTLDepthStencilDescriptor");
    public static readonly nint MTLStencilDescriptor = ObjCRuntime.GetClass("MTLStencilDescriptor");
    public static readonly nint MTLRenderPipelineDescriptor = ObjCRuntime.GetClass("MTLRenderPipelineDescriptor");

    // CAMetalLayer class
    public static readonly nint CAMetalLayer = ObjCRuntime.GetClass("CAMetalLayer");

    // Selectors
    public static class Sel
    {
        // Device
        public static readonly nint Device = ObjCRuntime.RegisterSelector("device");
        public static readonly nint SetDevice = ObjCRuntime.RegisterSelector("setDevice:");
        public static readonly nint NewCommandQueue = ObjCRuntime.RegisterSelector("newCommandQueue");
        public static readonly nint NewBufferWithLength = ObjCRuntime.RegisterSelector("newBufferWithLength:options:");
        public static readonly nint NewTextureWithDescriptor = ObjCRuntime.RegisterSelector("newTextureWithDescriptor:");
        public static readonly nint NewSamplerStateWithDescriptor = ObjCRuntime.RegisterSelector("newSamplerStateWithDescriptor:");
        public static readonly nint NewDepthStencilStateWithDescriptor = ObjCRuntime.RegisterSelector("newDepthStencilStateWithDescriptor:");
        public static readonly nint NewRenderPipelineStateWithDescriptor = ObjCRuntime.RegisterSelector("newRenderPipelineStateWithDescriptor:error:");
        public static readonly nint NewLibraryWithData = ObjCRuntime.RegisterSelector("newLibraryWithData:error:");
        public static readonly nint NewFunctionWithName = ObjCRuntime.RegisterSelector("newFunctionWithName:");

        // CommandQueue
        public static readonly nint CommandBuffer = ObjCRuntime.RegisterSelector("commandBuffer");

        // CommandBuffer
        public static readonly nint Enqueue = ObjCRuntime.RegisterSelector("enqueue");
        public static readonly nint Commit = ObjCRuntime.RegisterSelector("commit");
        public static readonly nint WaitUntilCompleted = ObjCRuntime.RegisterSelector("waitUntilCompleted");
        public static readonly nint WaitUntilScheduled = ObjCRuntime.RegisterSelector("waitUntilScheduled");
        public static readonly nint PresentDrawable = ObjCRuntime.RegisterSelector("presentDrawable:");
        public static readonly nint RenderCommandEncoderWithDescriptor = ObjCRuntime.RegisterSelector("renderCommandEncoderWithDescriptor:");
        public static readonly nint BlitCommandEncoder = ObjCRuntime.RegisterSelector("blitCommandEncoder");
        public static readonly nint AddCompletedHandler = ObjCRuntime.RegisterSelector("addCompletedHandler:");

        // RenderCommandEncoder
        public static readonly nint EndEncoding = ObjCRuntime.RegisterSelector("endEncoding");
        public static readonly nint SetRenderPipelineState = ObjCRuntime.RegisterSelector("setRenderPipelineState:");
        public static readonly nint SetDepthStencilState = ObjCRuntime.RegisterSelector("setDepthStencilState:");
        public static readonly nint SetCullMode = ObjCRuntime.RegisterSelector("setCullMode:");
        public static readonly nint SetFrontFacingWinding = ObjCRuntime.RegisterSelector("setFrontFacingWinding:");
        public static readonly nint SetStencilReferenceValue = ObjCRuntime.RegisterSelector("setStencilReferenceValue:");
        public static readonly nint SetViewport = ObjCRuntime.RegisterSelector("setViewport:");
        public static readonly nint SetVertexBuffer = ObjCRuntime.RegisterSelector("setVertexBuffer:offset:atIndex:");
        public static readonly nint SetFragmentBuffer = ObjCRuntime.RegisterSelector("setFragmentBuffer:offset:atIndex:");
        public static readonly nint SetFragmentBufferOffset = ObjCRuntime.RegisterSelector("setFragmentBufferOffset:atIndex:");
        public static readonly nint SetFragmentTexture = ObjCRuntime.RegisterSelector("setFragmentTexture:atIndex:");
        public static readonly nint SetFragmentSamplerState = ObjCRuntime.RegisterSelector("setFragmentSamplerState:atIndex:");
        public static readonly nint DrawPrimitives = ObjCRuntime.RegisterSelector("drawPrimitives:vertexStart:vertexCount:");
        public static readonly nint DrawIndexedPrimitives = ObjCRuntime.RegisterSelector("drawIndexedPrimitives:indexCount:indexType:indexBuffer:indexBufferOffset:");

        // BlitCommandEncoder
        public static readonly nint CopyFromBufferToTexture = ObjCRuntime.RegisterSelector("copyFromBuffer:sourceOffset:sourceBytesPerRow:sourceBytesPerImage:sourceSize:toTexture:destinationSlice:destinationLevel:destinationOrigin:");
        public static readonly nint CopyFromTextureToBuffer = ObjCRuntime.RegisterSelector("copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toBuffer:destinationOffset:destinationBytesPerRow:destinationBytesPerImage:");
        public static readonly nint GenerateMipmapsForTexture = ObjCRuntime.RegisterSelector("generateMipmapsForTexture:");
        public static readonly nint SynchronizeResource = ObjCRuntime.RegisterSelector("synchronizeResource:");

        // Buffer
        public static readonly nint Contents = ObjCRuntime.RegisterSelector("contents");
        public static readonly nint Length = ObjCRuntime.RegisterSelector("length");

        // Texture
        public static readonly nint Width = ObjCRuntime.RegisterSelector("width");
        public static readonly nint Height = ObjCRuntime.RegisterSelector("height");
        public static readonly nint ReplaceRegion = ObjCRuntime.RegisterSelector("replaceRegion:mipmapLevel:withBytes:bytesPerRow:");
        public static readonly nint GetBytesFromRegion = ObjCRuntime.RegisterSelector("getBytes:bytesPerRow:fromRegion:mipmapLevel:");

        // TextureDescriptor
        public static readonly nint Texture2DDescriptorWithPixelFormat = ObjCRuntime.RegisterSelector("texture2DDescriptorWithPixelFormat:width:height:mipmapped:");
        public static readonly nint SetUsage = ObjCRuntime.RegisterSelector("setUsage:");
        public static readonly nint SetStorageMode = ObjCRuntime.RegisterSelector("setStorageMode:");
        public static readonly nint PixelFormat = ObjCRuntime.RegisterSelector("pixelFormat");

        // RenderPassDescriptor
        public static readonly nint RenderPassDescriptor = ObjCRuntime.RegisterSelector("renderPassDescriptor");
        public static readonly nint ColorAttachments = ObjCRuntime.RegisterSelector("colorAttachments");
        public static readonly nint StencilAttachment = ObjCRuntime.RegisterSelector("stencilAttachment");

        // RenderPassAttachmentDescriptor
        public static readonly nint SetTexture = ObjCRuntime.RegisterSelector("setTexture:");
        public static readonly nint SetLoadAction = ObjCRuntime.RegisterSelector("setLoadAction:");
        public static readonly nint SetStoreAction = ObjCRuntime.RegisterSelector("setStoreAction:");
        public static readonly nint SetClearColor = ObjCRuntime.RegisterSelector("setClearColor:");
        public static readonly nint SetClearStencil = ObjCRuntime.RegisterSelector("setClearStencil:");
        public static readonly nint Texture = ObjCRuntime.RegisterSelector("texture");

        // RenderPassColorAttachmentDescriptorArray
        public static readonly nint ObjectAtIndexedSubscript = ObjCRuntime.RegisterSelector("objectAtIndexedSubscript:");

        // VertexDescriptor
        public static readonly nint VertexDescriptor = ObjCRuntime.RegisterSelector("vertexDescriptor");
        public static readonly nint Attributes = ObjCRuntime.RegisterSelector("attributes");
        public static readonly nint Layouts = ObjCRuntime.RegisterSelector("layouts");
        public static readonly nint SetFormat = ObjCRuntime.RegisterSelector("setFormat:");
        public static readonly nint SetOffset = ObjCRuntime.RegisterSelector("setOffset:");
        public static readonly nint SetBufferIndex = ObjCRuntime.RegisterSelector("setBufferIndex:");
        public static readonly nint SetStride = ObjCRuntime.RegisterSelector("setStride:");
        public static readonly nint SetStepFunction = ObjCRuntime.RegisterSelector("setStepFunction:");

        // SamplerDescriptor
        public static readonly nint SetMinFilter = ObjCRuntime.RegisterSelector("setMinFilter:");
        public static readonly nint SetMagFilter = ObjCRuntime.RegisterSelector("setMagFilter:");
        public static readonly nint SetMipFilter = ObjCRuntime.RegisterSelector("setMipFilter:");
        public static readonly nint SetSAddressMode = ObjCRuntime.RegisterSelector("setSAddressMode:");
        public static readonly nint SetTAddressMode = ObjCRuntime.RegisterSelector("setTAddressMode:");

        // DepthStencilDescriptor
        public static readonly nint SetDepthCompareFunction = ObjCRuntime.RegisterSelector("setDepthCompareFunction:");
        public static readonly nint SetFrontFaceStencil = ObjCRuntime.RegisterSelector("setFrontFaceStencil:");
        public static readonly nint SetBackFaceStencil = ObjCRuntime.RegisterSelector("setBackFaceStencil:");

        // StencilDescriptor
        public static readonly nint SetStencilCompareFunction = ObjCRuntime.RegisterSelector("setStencilCompareFunction:");
        public static readonly nint SetStencilFailureOperation = ObjCRuntime.RegisterSelector("setStencilFailureOperation:");
        public static readonly nint SetDepthFailureOperation = ObjCRuntime.RegisterSelector("setDepthFailureOperation:");
        public static readonly nint SetDepthStencilPassOperation = ObjCRuntime.RegisterSelector("setDepthStencilPassOperation:");

        // RenderPipelineDescriptor
        public static readonly nint SetVertexFunction = ObjCRuntime.RegisterSelector("setVertexFunction:");
        public static readonly nint SetFragmentFunction = ObjCRuntime.RegisterSelector("setFragmentFunction:");
        public static readonly nint SetVertexDescriptor = ObjCRuntime.RegisterSelector("setVertexDescriptor:");
        public static readonly nint SetStencilAttachmentPixelFormat = ObjCRuntime.RegisterSelector("setStencilAttachmentPixelFormat:");

        // RenderPipelineColorAttachmentDescriptor
        public static readonly nint SetPixelFormat = ObjCRuntime.RegisterSelector("setPixelFormat:");
        public static readonly nint SetBlendingEnabled = ObjCRuntime.RegisterSelector("setBlendingEnabled:");
        public static readonly nint SetSourceRGBBlendFactor = ObjCRuntime.RegisterSelector("setSourceRGBBlendFactor:");
        public static readonly nint SetSourceAlphaBlendFactor = ObjCRuntime.RegisterSelector("setSourceAlphaBlendFactor:");
        public static readonly nint SetDestinationRGBBlendFactor = ObjCRuntime.RegisterSelector("setDestinationRGBBlendFactor:");
        public static readonly nint SetDestinationAlphaBlendFactor = ObjCRuntime.RegisterSelector("setDestinationAlphaBlendFactor:");
        public static readonly nint SetWriteMask = ObjCRuntime.RegisterSelector("setWriteMask:");

        // CAMetalLayer
        public static readonly nint NextDrawable = ObjCRuntime.RegisterSelector("nextDrawable");
        public static readonly nint SetOpaque = ObjCRuntime.RegisterSelector("setOpaque:");
        public static readonly nint PresentsWithTransaction = ObjCRuntime.RegisterSelector("presentsWithTransaction");

        // dispatch
        public static readonly nint DispatchDataCreate = ObjCRuntime.RegisterSelector("dispatch_data_create");
    }
}

/// <summary>
/// Metal pixel formats.
/// </summary>
public enum MTLPixelFormat : ulong
{
    Invalid = 0,
    A8Unorm = 1,
    R8Unorm = 10,
    R8Snorm = 12,
    R8Uint = 13,
    R8Sint = 14,
    R16Unorm = 20,
    R16Snorm = 22,
    R16Uint = 23,
    R16Sint = 24,
    R16Float = 25,
    RG8Unorm = 30,
    RG8Snorm = 32,
    RG8Uint = 33,
    RG8Sint = 34,
    R32Uint = 53,
    R32Sint = 54,
    R32Float = 55,
    RG16Unorm = 60,
    RG16Snorm = 62,
    RG16Uint = 63,
    RG16Sint = 64,
    RG16Float = 65,
    RGBA8Unorm = 70,
    RGBA8Unorm_sRGB = 71,
    RGBA8Snorm = 72,
    RGBA8Uint = 73,
    RGBA8Sint = 74,
    BGRA8Unorm = 80,
    BGRA8Unorm_sRGB = 81,
    RGB10A2Unorm = 90,
    RGB10A2Uint = 91,
    RG11B10Float = 92,
    RGB9E5Float = 93,
    RG32Uint = 103,
    RG32Sint = 104,
    RG32Float = 105,
    RGBA16Unorm = 110,
    RGBA16Snorm = 112,
    RGBA16Uint = 113,
    RGBA16Sint = 114,
    RGBA16Float = 115,
    RGBA32Uint = 123,
    RGBA32Sint = 124,
    RGBA32Float = 125,
    Stencil8 = 253,
    Depth16Unorm = 250,
    Depth32Float = 252,
    Depth24Unorm_Stencil8 = 255,
    Depth32Float_Stencil8 = 260,
}

/// <summary>
/// Metal texture usage.
/// </summary>
[Flags]
public enum MTLTextureUsage : ulong
{
    Unknown = 0,
    ShaderRead = 1,
    ShaderWrite = 2,
    RenderTarget = 4,
    PixelFormatView = 0x10,
}

/// <summary>
/// Metal storage mode.
/// </summary>
public enum MTLStorageMode : ulong
{
    Shared = 0,
    Managed = 1,
    Private = 2,
    Memoryless = 3,
}

/// <summary>
/// Metal resource options.
/// </summary>
[Flags]
public enum MTLResourceOptions : ulong
{
    DefaultCache = 0,
    CPUCacheModeWriteCombined = 1,
    StorageModeShared = 0 << 4,
    StorageModeManaged = 1 << 4,
    StorageModePrivate = 2 << 4,
    StorageModeMemoryless = 3 << 4,
    HazardTrackingModeDefault = 0 << 8,
    HazardTrackingModeUntracked = 1 << 8,
    HazardTrackingModeTracked = 2 << 8,
}

/// <summary>
/// Metal blend factor.
/// </summary>
public enum MTLBlendFactor : ulong
{
    Zero = 0,
    One = 1,
    SourceColor = 2,
    OneMinusSourceColor = 3,
    SourceAlpha = 4,
    OneMinusSourceAlpha = 5,
    DestinationColor = 6,
    OneMinusDestinationColor = 7,
    DestinationAlpha = 8,
    OneMinusDestinationAlpha = 9,
    SourceAlphaSaturated = 10,
    BlendColor = 11,
    OneMinusBlendColor = 12,
    BlendAlpha = 13,
    OneMinusBlendAlpha = 14,
}

/// <summary>
/// Metal primitive type.
/// </summary>
public enum MTLPrimitiveType : ulong
{
    Point = 0,
    Line = 1,
    LineStrip = 2,
    Triangle = 3,
    TriangleStrip = 4,
    // Metal does not natively support triangle fans; keep as alias for compatibility.
    TriangleFan = TriangleStrip,
}

/// <summary>
/// Metal index type.
/// </summary>
public enum MTLIndexType : ulong
{
    UInt16 = 0,
    UInt32 = 1,
}

/// <summary>
/// Metal load action.
/// </summary>
public enum MTLLoadAction : ulong
{
    DontCare = 0,
    Load = 1,
    Clear = 2,
}

/// <summary>
/// Metal store action.
/// </summary>
public enum MTLStoreAction : ulong
{
    DontCare = 0,
    Store = 1,
    MultisampleResolve = 2,
    StoreAndMultisampleResolve = 3,
    Unknown = 4,
    CustomSampleDepthStore = 5,
}

/// <summary>
/// Metal cull mode.
/// </summary>
public enum MTLCullMode : ulong
{
    None = 0,
    Front = 1,
    Back = 2,
}

/// <summary>
/// Metal winding.
/// </summary>
public enum MTLWinding : ulong
{
    Clockwise = 0,
    CounterClockwise = 1,
}

/// <summary>
/// Metal compare function.
/// </summary>
public enum MTLCompareFunction : ulong
{
    Never = 0,
    Less = 1,
    Equal = 2,
    LessEqual = 3,
    Greater = 4,
    NotEqual = 5,
    GreaterEqual = 6,
    Always = 7,
}

/// <summary>
/// Metal stencil operation.
/// </summary>
public enum MTLStencilOperation : ulong
{
    Keep = 0,
    Zero = 1,
    Replace = 2,
    IncrementClamp = 3,
    DecrementClamp = 4,
    Invert = 5,
    IncrementWrap = 6,
    DecrementWrap = 7,
}

/// <summary>
/// Metal vertex format.
/// </summary>
public enum MTLVertexFormat : ulong
{
    Invalid = 0,
    UChar2 = 1,
    UChar3 = 2,
    UChar4 = 3,
    Char2 = 4,
    Char3 = 5,
    Char4 = 6,
    UChar2Normalized = 7,
    UChar3Normalized = 8,
    UChar4Normalized = 9,
    Char2Normalized = 10,
    Char3Normalized = 11,
    Char4Normalized = 12,
    UShort2 = 13,
    UShort3 = 14,
    UShort4 = 15,
    Short2 = 16,
    Short3 = 17,
    Short4 = 18,
    UShort2Normalized = 19,
    UShort3Normalized = 20,
    UShort4Normalized = 21,
    Short2Normalized = 22,
    Short3Normalized = 23,
    Short4Normalized = 24,
    Half2 = 25,
    Half3 = 26,
    Half4 = 27,
    Float = 28,
    Float2 = 29,
    Float3 = 30,
    Float4 = 31,
    Int = 32,
    Int2 = 33,
    Int3 = 34,
    Int4 = 35,
    UInt = 36,
    UInt2 = 37,
    UInt3 = 38,
    UInt4 = 39,
}

/// <summary>
/// Metal vertex step function.
/// </summary>
public enum MTLVertexStepFunction : ulong
{
    Constant = 0,
    PerVertex = 1,
    PerInstance = 2,
    PerPatch = 3,
    PerPatchControlPoint = 4,
}

/// <summary>
/// Metal sampler min/mag filter.
/// </summary>
public enum MTLSamplerMinMagFilter : ulong
{
    Nearest = 0,
    Linear = 1,
}

/// <summary>
/// Metal sampler mip filter.
/// </summary>
public enum MTLSamplerMipFilter : ulong
{
    NotMipmapped = 0,
    Nearest = 1,
    Linear = 2,
}

/// <summary>
/// Metal sampler address mode.
/// </summary>
public enum MTLSamplerAddressMode : ulong
{
    ClampToEdge = 0,
    MirrorClampToEdge = 1,
    Repeat = 2,
    MirrorRepeat = 3,
    ClampToZero = 4,
    ClampToBorderColor = 5,
}

/// <summary>
/// Metal color write mask.
/// </summary>
[Flags]
public enum MTLColorWriteMask : ulong
{
    None = 0,
    Red = 1 << 3,
    Green = 1 << 2,
    Blue = 1 << 1,
    Alpha = 1 << 0,
    All = 0xf,
}

/// <summary>
/// Metal clear color.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MTLClearColor
{
    public double Red;
    public double Green;
    public double Blue;
    public double Alpha;

    public MTLClearColor(double red, double green, double blue, double alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }
}

/// <summary>
/// Metal viewport.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MTLViewport
{
    public double OriginX;
    public double OriginY;
    public double Width;
    public double Height;
    public double ZNear;
    public double ZFar;

    public MTLViewport(double originX, double originY, double width, double height, double znear, double zfar)
    {
        OriginX = originX;
        OriginY = originY;
        Width = width;
        Height = height;
        ZNear = znear;
        ZFar = zfar;
    }
}

/// <summary>
/// Metal region.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MTLRegion
{
    public MTLOrigin Origin;
    public MTLSize Size;

    public static MTLRegion Make2D(nuint x, nuint y, nuint width, nuint height)
    {
        return new MTLRegion
        {
            Origin = new MTLOrigin { X = x, Y = y, Z = 0 },
            Size = new MTLSize { Width = width, Height = height, Depth = 1 }
        };
    }
}

/// <summary>
/// Metal origin.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MTLOrigin
{
    public nuint X;
    public nuint Y;
    public nuint Z;

    public static MTLOrigin Make(nuint x, nuint y, nuint z)
    {
        return new MTLOrigin { X = x, Y = y, Z = z };
    }
}

/// <summary>
/// Metal size.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MTLSize
{
    public nuint Width;
    public nuint Height;
    public nuint Depth;

    public static MTLSize Make(nuint width, nuint height, nuint depth)
    {
        return new MTLSize { Width = width, Height = height, Depth = depth };
    }
}
