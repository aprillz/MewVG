// Compatibility selector map for legacy Metal selectors
// MIT License

namespace Aprillz.MewVG.Interop;

/// <summary>
/// Legacy selector names used by older ports.
/// </summary>
public static class MetalSelectors
{
    // Device
    public static readonly nint newCommandQueue = Metal.Sel.NewCommandQueue;
        public static readonly nint commandBuffer = Metal.Sel.CommandBuffer;
    public static readonly nint newBufferWithLength_options = Metal.Sel.NewBufferWithLength;
    public static readonly nint newTextureWithDescriptor = Metal.Sel.NewTextureWithDescriptor;
    public static readonly nint newSamplerStateWithDescriptor = Metal.Sel.NewSamplerStateWithDescriptor;
    public static readonly nint newDepthStencilStateWithDescriptor = Metal.Sel.NewDepthStencilStateWithDescriptor;
    public static readonly nint newRenderPipelineStateWithDescriptor_error = Metal.Sel.NewRenderPipelineStateWithDescriptor;
    public static readonly nint newLibraryWithData_error = Metal.Sel.NewLibraryWithData;
    public static readonly nint newFunctionWithName = Metal.Sel.NewFunctionWithName;

    // Render pipeline descriptor
    public static readonly nint setVertexFunction = Metal.Sel.SetVertexFunction;
    public static readonly nint setFragmentFunction = Metal.Sel.SetFragmentFunction;
    public static readonly nint setVertexDescriptor = Metal.Sel.SetVertexDescriptor;
    public static readonly nint setStencilAttachmentPixelFormat = Metal.Sel.SetStencilAttachmentPixelFormat;
    public static readonly nint colorAttachments = Metal.Sel.ColorAttachments;

    // Render pipeline color attachment descriptor
    public static readonly nint setPixelFormat = Metal.Sel.SetPixelFormat;
    public static readonly nint setBlendingEnabled = Metal.Sel.SetBlendingEnabled;
    public static readonly nint setSourceRGBBlendFactor = Metal.Sel.SetSourceRGBBlendFactor;
    public static readonly nint setSourceAlphaBlendFactor = Metal.Sel.SetSourceAlphaBlendFactor;
    public static readonly nint setDestinationRGBBlendFactor = Metal.Sel.SetDestinationRGBBlendFactor;
    public static readonly nint setDestinationAlphaBlendFactor = Metal.Sel.SetDestinationAlphaBlendFactor;
    public static readonly nint setWriteMask = Metal.Sel.SetWriteMask;

    // Vertex descriptor
    public static readonly nint attributes = Metal.Sel.Attributes;
    public static readonly nint layouts = Metal.Sel.Layouts;
    public static readonly nint objectAtIndexedSubscript = Metal.Sel.ObjectAtIndexedSubscript;
    public static readonly nint setFormat = Metal.Sel.SetFormat;
    public static readonly nint setOffset = Metal.Sel.SetOffset;
    public static readonly nint setBufferIndex = Metal.Sel.SetBufferIndex;
    public static readonly nint setStride = Metal.Sel.SetStride;
    public static readonly nint setStepFunction = Metal.Sel.SetStepFunction;
    public static readonly nint setStepRate = ObjCRuntime.RegisterSelector("setStepRate:");

    // Depth/stencil
    public static readonly nint setDepthCompareFunction = Metal.Sel.SetDepthCompareFunction;
    public static readonly nint setStencilCompareFunction = Metal.Sel.SetStencilCompareFunction;
    public static readonly nint setStencilFailureOperation = Metal.Sel.SetStencilFailureOperation;
    public static readonly nint setDepthFailureOperation = Metal.Sel.SetDepthFailureOperation;
    public static readonly nint setDepthStencilPassOperation = Metal.Sel.SetDepthStencilPassOperation;
    public static readonly nint setFrontFaceStencil = Metal.Sel.SetFrontFaceStencil;
    public static readonly nint setBackFaceStencil = Metal.Sel.SetBackFaceStencil;

    // Texture descriptor
    public static readonly nint texture2DDescriptorWithPixelFormat_width_height_mipmapped = Metal.Sel.Texture2DDescriptorWithPixelFormat;
    public static readonly nint setUsage = Metal.Sel.SetUsage;

    // Texture
    public static readonly nint replaceRegion_mipmapLevel_withBytes_bytesPerRow = Metal.Sel.ReplaceRegion;
    // Blit command encoder
    public static readonly nint blitCommandEncoder = Metal.Sel.BlitCommandEncoder;
    public static readonly nint generateMipmapsForTexture = Metal.Sel.GenerateMipmapsForTexture;
    public static readonly nint endEncoding = Metal.Sel.EndEncoding;
    public static readonly nint commit = Metal.Sel.Commit;
    public static readonly nint waitUntilCompleted = Metal.Sel.WaitUntilCompleted;

    // Sampler descriptor
    public static readonly nint setMinFilter = Metal.Sel.SetMinFilter;
    public static readonly nint setMagFilter = Metal.Sel.SetMagFilter;
    public static readonly nint setMipFilter = Metal.Sel.SetMipFilter;
    public static readonly nint setSAddressMode = Metal.Sel.SetSAddressMode;
    public static readonly nint setTAddressMode = Metal.Sel.SetTAddressMode;

    // Buffer
    public static readonly nint contents = Metal.Sel.Contents;
    public static readonly nint didModifyRange = ObjCRuntime.RegisterSelector("didModifyRange:");

    // Render command encoder
    public static readonly nint setViewport = Metal.Sel.SetViewport;
    public static readonly nint setRenderPipelineState = Metal.Sel.SetRenderPipelineState;
    public static readonly nint setVertexBuffer_offset_atIndex = Metal.Sel.SetVertexBuffer;
    public static readonly nint setVertexBytes_length_atIndex = ObjCRuntime.RegisterSelector("setVertexBytes:length:atIndex:");
    public static readonly nint setFragmentTexture_atIndex = Metal.Sel.SetFragmentTexture;
    public static readonly nint setFragmentSamplerState_atIndex = Metal.Sel.SetFragmentSamplerState;
    public static readonly nint setDepthStencilState = Metal.Sel.SetDepthStencilState;
    public static readonly nint setCullMode = Metal.Sel.SetCullMode;
    public static readonly nint setStencilReferenceValue = Metal.Sel.SetStencilReferenceValue;
    public static readonly nint setFragmentBuffer_offset_atIndex = Metal.Sel.SetFragmentBuffer;
    public static readonly nint drawPrimitives_vertexStart_vertexCount = Metal.Sel.DrawPrimitives;
    public static readonly nint drawIndexedPrimitives_indexCount_indexType_indexBuffer_indexBufferOffset = Metal.Sel.DrawIndexedPrimitives;

    // Render pass
    public static readonly nint renderPassDescriptor = Metal.Sel.RenderPassDescriptor;
    public static readonly nint stencilAttachment = Metal.Sel.StencilAttachment;
    public static readonly nint setTexture = Metal.Sel.SetTexture;
    public static readonly nint setLoadAction = Metal.Sel.SetLoadAction;
    public static readonly nint setStoreAction = Metal.Sel.SetStoreAction;
    public static readonly nint setClearColor = Metal.Sel.SetClearColor;
    public static readonly nint setClearStencil = Metal.Sel.SetClearStencil;
    public static readonly nint texture = Metal.Sel.Texture;
}
