// Copyright (c) 2024 .NET Port
// MIT License

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aprillz.MewVG.Interop;

/// <summary>
/// Objective-C runtime interop for macOS.
/// </summary>
public static unsafe partial class ObjCRuntime
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass")]
    public static partial nint GetClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName")]
    public static partial nint RegisterSelector([MarshalAs(UnmanagedType.LPStr)] string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, nint arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, nint arg1, nint arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, nint arg1, nint arg2, nint arg3);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, [MarshalAs(UnmanagedType.I1)] bool arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, uint arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, ulong arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, nuint arg1, ulong arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, int arg1, int arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, nint arg1, uint arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, nint arg1, nuint arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, void* arg1, nuint arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, void* arg1, nuint arg2, nuint arg3);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, nint arg1, nuint arg2, nuint arg3);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, ulong arg1, nuint arg2, nuint arg3);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, ulong arg1, nuint arg2, nuint arg3, [MarshalAs(UnmanagedType.I1)] bool arg4);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendMessageNoReturn(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendMessageNoReturn(nint receiver, nint selector, nint arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendMessageNoReturn(nint receiver, nint selector, nint arg1, nint arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendMessageNoReturn(nint receiver, nint selector, uint arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendMessageNoReturn(nint receiver, nint selector, ulong arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendMessageNoReturn(nint receiver, nint selector, double arg1);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendMessageNoReturn(nint receiver, nint selector, nint arg1, uint arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool SendMessageBool(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial uint SendMessageUInt(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial ulong SendMessageULong(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial double SendMessageDouble(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial float SendMessageFloat(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, void* arg1, nint arg2);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, MTLViewport viewport);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, MTLRegion region, nuint arg2, IntPtr arg3, nuint arg4);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint SendMessage(nint receiver, nint selector, NSRange range);

    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void* SendMessagePtr(nint receiver, nint selector);

    [LibraryImport(LibObjC, EntryPoint = "objc_retain")]
    public static partial nint Retain(nint obj);

    [LibraryImport(LibObjC, EntryPoint = "objc_release")]
    public static partial void Release(nint obj);

    [LibraryImport(LibObjC, EntryPoint = "objc_autorelease")]
    public static partial nint Autorelease(nint obj);

    /// <summary>
    /// Creates an Objective-C string (NSString) from a .NET string.
    /// </summary>
    public static nint CreateNSString(string str)
    {
        var nsStringClass = GetClass("NSString");
        var allocSel = RegisterSelector("alloc");
        var initWithUTF8Sel = RegisterSelector("initWithUTF8String:");

        var allocated = SendMessage(nsStringClass, allocSel);
        var bytes = System.Text.Encoding.UTF8.GetBytes(str + '\0');
        fixed (byte* ptr = bytes)
        {
            return SendMessage(allocated, initWithUTF8Sel, (nint)ptr);
        }
    }

    /// <summary>
    /// Allocates a new Objective-C object.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nint Alloc(nint cls) => SendMessage(cls, Selectors.alloc);

    /// <summary>
    /// Initializes an Objective-C object.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nint Init(nint obj) => SendMessage(obj, Selectors.init);

    /// <summary>
    /// Creates and initializes a new Objective-C object.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nint New(nint cls) => SendMessage(cls, Selectors.@new);

    /// <summary>
    /// Compatibility selectors used by older ports.
    /// </summary>
    public static class Selectors
    {
        public static readonly nint alloc = Aprillz.MewVG.Interop.Selectors.Alloc;
        public static readonly nint init = Aprillz.MewVG.Interop.Selectors.Init;
        public static readonly nint @new = Aprillz.MewVG.Interop.Selectors.New;
        public static readonly nint release = Aprillz.MewVG.Interop.Selectors.Release;
        public static readonly nint retain = Aprillz.MewVG.Interop.Selectors.Retain;
        public static readonly nint autorelease = Aprillz.MewVG.Interop.Selectors.Autorelease;
        public static readonly nint dealloc = Aprillz.MewVG.Interop.Selectors.Dealloc;
        public static readonly nint description = ObjCRuntime.RegisterSelector("description");
        public static readonly nint UTF8String = ObjCRuntime.RegisterSelector("UTF8String");
    }
}

/// <summary>
/// Cached selectors for common operations.
/// </summary>
public static class Selectors
{
    public static readonly nint Alloc = ObjCRuntime.RegisterSelector("alloc");
    public static readonly nint Init = ObjCRuntime.RegisterSelector("init");
    public static readonly nint New = ObjCRuntime.RegisterSelector("new");
    public static readonly nint Release = ObjCRuntime.RegisterSelector("release");
    public static readonly nint Retain = ObjCRuntime.RegisterSelector("retain");
    public static readonly nint Autorelease = ObjCRuntime.RegisterSelector("autorelease");
    public static readonly nint Dealloc = ObjCRuntime.RegisterSelector("dealloc");
}

/// <summary>
/// Cached Objective-C class references.
/// </summary>
public static class ObjCClasses
{
    public static readonly nint NSObject = ObjCRuntime.GetClass("NSObject");
    public static readonly nint NSString = ObjCRuntime.GetClass("NSString");
    public static readonly nint NSData = ObjCRuntime.GetClass("NSData");
    public static readonly nint NSError = ObjCRuntime.GetClass("NSError");
    public static readonly nint NSArray = ObjCRuntime.GetClass("NSArray");
    public static readonly nint NSMutableArray = ObjCRuntime.GetClass("NSMutableArray");
    public static readonly nint NSDictionary = ObjCRuntime.GetClass("NSDictionary");
}
