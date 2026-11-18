using System.Runtime.InteropServices;

namespace Aprillz.MewVG.Interop;

/// <summary>The messages the staged texture uploads and the paint variants send that the shared selector list lacks.</summary>
internal static unsafe partial class MetalMessages
{
    private const string LIB_OBJC = "/usr/lib/libobjc.A.dylib";

    // MTLDataType values of the function constants.
    public const nuint DATA_TYPE_INT = 29;
    public const nuint DATA_TYPE_BOOL = 53;

    public static readonly nint SetConstantValueSelector = ObjCRuntime.RegisterSelector("setConstantValue:type:atIndex:");
    public static readonly nint NewFunctionWithConstantsSelector = ObjCRuntime.RegisterSelector("newFunctionWithName:constantValues:error:");
    public static readonly nint SetVertexBufferOffsetSelector = ObjCRuntime.RegisterSelector("setVertexBufferOffset:atIndex:");

    public static readonly nint CommandQueueSelector = ObjCRuntime.RegisterSelector("commandQueue");
    public static readonly nint NewBufferWithBytesSelector = ObjCRuntime.RegisterSelector("newBufferWithBytes:length:options:");
    public static readonly nint CopyFromBufferSelector = ObjCRuntime.RegisterSelector(
        "copyFromBuffer:sourceOffset:sourceBytesPerRow:sourceBytesPerImage:sourceSize:toTexture:destinationSlice:destinationLevel:destinationOrigin:");

    [LibraryImport(LIB_OBJC, EntryPoint = "objc_msgSend")]
    public static partial nint NewBufferWithBytes(nint device, nint selector, void* bytes, nuint length, ulong options);

    [LibraryImport(LIB_OBJC, EntryPoint = "objc_msgSend")]
    public static partial void CopyFromBuffer(nint encoder, nint selector, nint buffer, nuint sourceOffset, nuint sourceBytesPerRow,
        nuint sourceBytesPerImage, MTLSize sourceSize, nint texture, nuint destinationSlice, nuint destinationLevel,
        MTLOrigin destinationOrigin);
}
