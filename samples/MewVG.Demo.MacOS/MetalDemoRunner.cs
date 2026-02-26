using System.Runtime.InteropServices;

using Aprillz.MewVG;
using Aprillz.MewVG.Interop;
using MetalInterop = Aprillz.MewVG.Interop.Metal;

namespace MewVG.Demo.MacOS;

internal sealed unsafe partial class MetalDemoRunner : DemoRunner
{
    private static nint _stencilTexture;
    private static int _stencilWidth;
    private static int _stencilHeight;
    private static nint _device;
    private static nint _commandQueue;
    private static NanoVGMetal? _vg;
    private static nint _contentView;
    private static nint _metalLayer;
    private static nint _layerDelegate;
    private static nint _renderTimer;

    private nint _pool;
    private nint _app;

    public MetalDemoRunner()
    {
        _device = MetalDevice.CreateSystemDefaultDevice();
        if (_device == nint.Zero)
        {
            throw new InvalidOperationException("Metal device creation failed.");
        }

        _vg = new NanoVGMetal(_device);
        _vg.PixelFormat = MTLPixelFormat.BGRA8Unorm;
        _vg.StencilFormat = MTLPixelFormat.Stencil8;
    }

    protected override void Initialize()
    {
        _pool = CreateAutoreleasePool();

        _ = NSApplicationLoad();

        _app = CreateApplication();
        if (_app == nint.Zero)
        {
            throw new InvalidOperationException("NSApplication creation failed.");
        }

        nint window = CreateWindow(_app, DefaultWidth, DefaultHeight, DefaultTitle);
        if (window == nint.Zero)
        {
            throw new InvalidOperationException("NSWindow creation failed.");
        }

        nint view = CreateContentView(window, DefaultWidth, DefaultHeight, _device, out nint metalLayer);
        if (view == nint.Zero)
        {
            throw new InvalidOperationException("Content view creation failed.");
        }

        _contentView = view;
        _metalLayer = metalLayer;

        ObjCRuntime.SendMessageNoReturn(window, Sel.MakeKeyAndOrderFront, nint.Zero);
        ObjCRuntime.SendMessageNoReturn(window, Sel.Display);
        ObjCRuntime.SendMessage(_app, Sel.ActivateIgnoringOtherApps, true);

        _commandQueue = ObjCRuntime.SendMessage(_device, MetalInterop.Sel.NewCommandQueue);
        if (_commandQueue == nint.Zero)
        {
            throw new InvalidOperationException("Command queue creation failed.");
        }

        SetupLayerDelegate(metalLayer);
        StartRenderTimer();
    }

    protected override void Execute()
    {
        ObjCRuntime.SendMessageNoReturn(_app, Sel.Run);
    }

    protected override void Shutdown()
    {
        if (_pool != nint.Zero)
        {
            ObjCRuntime.SendMessageNoReturn(_pool, ObjCRuntime.Selectors.release);
        }
    }

    public override void Dispose()
    {
        _vg?.Dispose();
        _vg = null;
    }

    // ─── Rendering ──────────────────────────────────────────────────────────

    private static void RenderFrame(nint drawable, nint drawableTexture, NanoVGMetal vg, nint commandQueue, int width, int height, float dpr, int drawableWidth, int drawableHeight)
    {
        if (dpr <= 0.0f)
        {
            dpr = 1.0f;
        }

        drawableWidth = Math.Max(1, drawableWidth);
        drawableHeight = Math.Max(1, drawableHeight);

        nint commandBuffer = ObjCRuntime.SendMessage(commandQueue, MetalInterop.Sel.CommandBuffer);
        if (commandBuffer == nint.Zero)
        {
            return;
        }

        nint passDesc = CreateRenderPass(drawableTexture, _stencilTexture);
        nint encoder = ObjCRuntime.SendMessage(commandBuffer, MetalInterop.Sel.RenderCommandEncoderWithDescriptor, passDesc);
        if (encoder == nint.Zero)
        {
            return;
        }

        vg.SetRenderEncoder(encoder, commandBuffer);
        vg.BeginFrame(width, height, dpr);
        DemoScene.DrawDemo(vg, width, height);
        vg.EndFrame();

        ObjCRuntime.SendMessageNoReturn(encoder, MetalInterop.Sel.EndEncoding);
        ObjCRuntime.SendMessageNoReturn(commandBuffer, MetalInterop.Sel.Commit);
        ObjCRuntime.SendMessageNoReturn(commandBuffer, MetalInterop.Sel.WaitUntilScheduled);
        ObjCRuntime.SendMessageNoReturn(drawable, Sel.Present);
        vg.FrameCompleted();
    }

    [UnmanagedCallersOnly]
    private static void DisplayLayer(nint self, nint cmd, nint layer)
    {
        if (_vg == null || _commandQueue == nint.Zero || _contentView == nint.Zero)
        {
            return;
        }

        var bounds = objc_msgSend_rect(_contentView, Sel.Bounds);
        int width = Math.Max(1, (int)MathF.Round((float)bounds.Size.Width));
        int height = Math.Max(1, (int)MathF.Round((float)bounds.Size.Height));

        nint window = ObjCRuntime.SendMessage(_contentView, Sel.Window);
        float dpr = GetBackingScaleFactor(window);
        if (dpr <= 0.0f)
        {
            dpr = 1.0f;
        }

        var backingSize = objc_msgSend_size(_contentView, Sel.ConvertSizeToBacking, new NSSize(width, height));
        objc_msgSend(layer, Sel.SetDrawableSize, new CGSize(backingSize.Width, backingSize.Height));
        objc_msgSend(layer, Sel.SetContentsScale, dpr);

        EnsureStencilTexture(_device, (int)MathF.Round((float)backingSize.Width), (int)MathF.Round((float)backingSize.Height));

        nint drawable = ObjCRuntime.SendMessage(layer, MetalInterop.Sel.NextDrawable);
        if (drawable == nint.Zero)
        {
            return;
        }

        nint drawableTexture = ObjCRuntime.SendMessage(drawable, Sel.Texture);
        if (drawableTexture == nint.Zero)
        {
            return;
        }

        RenderFrame(drawable, drawableTexture, _vg, _commandQueue, width, height, dpr, (int)MathF.Round((float)backingSize.Width), (int)MathF.Round((float)backingSize.Height));
    }

    [UnmanagedCallersOnly]
    private static void Tick(nint self, nint cmd, nint timer)
    {
        if (_metalLayer != nint.Zero)
        {
            ObjCRuntime.SendMessageNoReturn(_metalLayer, Sel.SetNeedsDisplay);
        }
    }

    // ─── Setup Helpers ──────────────────────────────────────────────────────

    private static void SetupLayerDelegate(nint layer)
    {
        const string className = "MewVGLayerDelegate";
        nint baseClass = ObjCRuntime.GetClass("NSObject");
        nint cls = objc_allocateClassPair(baseClass, className, IntPtr.Zero);
        if (cls != nint.Zero)
        {
            class_addMethod(cls, Sel.DisplayLayer, (nint)(delegate* unmanaged<nint, nint, nint, void>)&DisplayLayer, "v@:@");
            class_addMethod(cls, Sel.Tick, (nint)(delegate* unmanaged<nint, nint, nint, void>)&Tick, "v@:@");
            objc_registerClassPair(cls);
        }
        else
        {
            cls = ObjCRuntime.GetClass(className);
        }

        nint obj = ObjCRuntime.SendMessage(cls, ObjCRuntime.Selectors.alloc);
        _layerDelegate = ObjCRuntime.SendMessage(obj, ObjCRuntime.Selectors.init);

        ObjCRuntime.SendMessageNoReturn(layer, Sel.SetDelegate, _layerDelegate);
        ObjCRuntime.SendMessage(layer, Sel.SetNeedsDisplayOnBoundsChange, true);
    }

    private static void StartRenderTimer()
    {
        nint timerClass = ObjCRuntime.GetClass("NSTimer");
        if (timerClass == nint.Zero || _layerDelegate == nint.Zero)
        {
            return;
        }

        _renderTimer = objc_msgSend(timerClass, Sel.ScheduledTimer, 1.0 / 60.0, _layerDelegate, Sel.Tick, nint.Zero, true);
    }

    private static nint CreateAutoreleasePool()
    {
        nint cls = ObjCRuntime.GetClass("NSAutoreleasePool");
        nint pool = ObjCRuntime.SendMessage(cls, ObjCRuntime.Selectors.alloc);
        return ObjCRuntime.SendMessage(pool, ObjCRuntime.Selectors.init);
    }

    private static nint CreateApplication()
    {
        nint nsAppClass = ObjCRuntime.GetClass("NSApplication");
        if (nsAppClass == nint.Zero)
        {
            return nint.Zero;
        }
        nint app = ObjCRuntime.SendMessage(nsAppClass, Sel.SharedApplication);
        ObjCRuntime.SendMessageNoReturn(app, Sel.SetActivationPolicy, (ulong)NSApplicationActivationPolicy.Regular);
        ObjCRuntime.SendMessageNoReturn(app, Sel.FinishLaunching);
        return app;
    }

    private static nint CreateWindow(nint app, int width, int height, string title)
    {
        nint nsWindowClass = ObjCRuntime.GetClass("NSWindow");
        if (nsWindowClass == nint.Zero)
        {
            return nint.Zero;
        }
        nint window = ObjCRuntime.SendMessage(nsWindowClass, ObjCRuntime.Selectors.alloc);

        var rect = new NSRect(0, 0, width, height);
        ulong style = (ulong)(NSWindowStyleMask.Titled | NSWindowStyleMask.Closable | NSWindowStyleMask.Resizable | NSWindowStyleMask.Miniaturizable);
        ulong backing = (ulong)NSBackingStore.Buffered;

        window = objc_msgSend(window, Sel.InitWithContentRectStyleMaskBackingDefer, rect, style, backing, false);

        nint titleStr = ObjCRuntime.CreateNSString(title);
        ObjCRuntime.SendMessageNoReturn(window, Sel.SetTitle, titleStr);

        ObjCRuntime.SendMessageNoReturn(window, Sel.Center);
        ObjCRuntime.SendMessage(window, Sel.SetReleasedWhenClosed, false);
        ObjCRuntime.SendMessage(window, Sel.SetPreservesContentDuringLiveResize, true);
        return window;
    }

    private static nint CreateContentView(nint window, int width, int height, nint device, out nint metalLayer)
    {
        nint nsViewClass = ObjCRuntime.GetClass("NSView");
        if (nsViewClass == nint.Zero)
        {
            metalLayer = nint.Zero;
            return nint.Zero;
        }

        nint view = ObjCRuntime.SendMessage(nsViewClass, ObjCRuntime.Selectors.alloc);
        view = objc_msgSend(view, Sel.InitWithFrame, new NSRect(0, 0, width, height));

        ObjCRuntime.SendMessageNoReturn(view, Sel.SetAutoresizingMask, (ulong)(NSViewAutoresizingMask.WidthSizable | NSViewAutoresizingMask.HeightSizable));
        ObjCRuntime.SendMessage(view, Sel.SetWantsLayer, true);
        ObjCRuntime.SendMessageNoReturn(view, Sel.SetLayerContentsRedrawPolicy, (ulong)NSViewLayerContentsRedrawPolicy.DuringViewResize);

        metalLayer = ObjCRuntime.SendMessage(MetalInterop.CAMetalLayer, ObjCRuntime.Selectors.@new);
        ObjCRuntime.SendMessageNoReturn(metalLayer, MetalInterop.Sel.SetDevice, device);
        ObjCRuntime.SendMessageNoReturn(metalLayer, Sel.SetPixelFormat, (ulong)MTLPixelFormat.BGRA8Unorm);
        ObjCRuntime.SendMessage(metalLayer, Sel.SetFramebufferOnly, true);
        ObjCRuntime.SendMessage(metalLayer, Sel.SetPresentsWithTransaction, true);
        ObjCRuntime.SendMessage(metalLayer, Sel.SetAllowsNextDrawableTimeout, false);
        ObjCRuntime.SendMessageNoReturn(metalLayer, Sel.SetAutoresizingMask, (ulong)(CALayerAutoresizingMask.WidthSizable | CALayerAutoresizingMask.HeightSizable));
        ObjCRuntime.SendMessage(metalLayer, Sel.SetNeedsDisplayOnBoundsChange, true);

        ObjCRuntime.SendMessageNoReturn(view, Sel.SetLayer, metalLayer);
        ObjCRuntime.SendMessageNoReturn(window, Sel.SetContentView, view);
        return view;
    }

    // ─── Metal Helpers ──────────────────────────────────────────────────────

    private static void EnsureStencilTexture(nint device, int width, int height)
    {
        if (_stencilTexture != nint.Zero && _stencilWidth == width && _stencilHeight == height)
        {
            return;
        }

        if (_stencilTexture != nint.Zero)
        {
            ObjCRuntime.SendMessageNoReturn(_stencilTexture, ObjCRuntime.Selectors.release);
            _stencilTexture = nint.Zero;
        }

        _stencilTexture = CreateTexture(device, MTLPixelFormat.Stencil8, width, height, MTLTextureUsage.RenderTarget);
        _stencilWidth = width;
        _stencilHeight = height;
    }

    private static nint CreateTexture(nint device, MTLPixelFormat format, int width, int height, MTLTextureUsage usage)
    {
        nint desc = ObjCRuntime.SendMessage(ObjCRuntime.GetClass("MTLTextureDescriptor"), MetalSelectors.texture2DDescriptorWithPixelFormat_width_height_mipmapped,
            (ulong)format, (nuint)width, (nuint)height, false);

        if (desc == nint.Zero)
            return nint.Zero;

        ObjCRuntime.SendMessageNoReturn(desc, MetalSelectors.setUsage, (ulong)usage);
        return ObjCRuntime.SendMessage(device, MetalSelectors.newTextureWithDescriptor, desc);
    }

    private static nint CreateRenderPass(nint colorTexture, nint stencilTexture)
    {
        nint passDesc = ObjCRuntime.SendMessage(ObjCRuntime.GetClass("MTLRenderPassDescriptor"), MetalSelectors.renderPassDescriptor);

        nint colorAttachments = ObjCRuntime.SendMessage(passDesc, MetalSelectors.colorAttachments);
        nint color0 = ObjCRuntime.SendMessage(colorAttachments, MetalSelectors.objectAtIndexedSubscript, (nuint)0);
        ObjCRuntime.SendMessageNoReturn(color0, MetalSelectors.setTexture, colorTexture);
        ObjCRuntime.SendMessageNoReturn(color0, MetalSelectors.setLoadAction, (ulong)MTLLoadAction.Clear);
        ObjCRuntime.SendMessageNoReturn(color0, MetalSelectors.setStoreAction, (ulong)MTLStoreAction.Store);
        SetClearColor(color0, new MTLClearColor(0.0, 0.0, 0.0, 1.0));

        nint stencil = ObjCRuntime.SendMessage(passDesc, MetalSelectors.stencilAttachment);
        ObjCRuntime.SendMessageNoReturn(stencil, MetalSelectors.setTexture, stencilTexture);
        ObjCRuntime.SendMessageNoReturn(stencil, MetalSelectors.setLoadAction, (ulong)MTLLoadAction.Clear);
        ObjCRuntime.SendMessageNoReturn(stencil, MetalSelectors.setStoreAction, (ulong)MTLStoreAction.DontCare);
        ObjCRuntime.SendMessageNoReturn(stencil, MetalSelectors.setClearStencil, (uint)0);

        return passDesc;
    }

    private static void SetClearColor(nint attachment, MTLClearColor color) => objc_msgSend(attachment, Sel.SetClearColor, color);

    private static float GetBackingScaleFactor(nint window)
        => window == nint.Zero ? 1.0f : (float)ObjCRuntime.SendMessageDouble(window, Sel.BackingScaleFactor);

    // ─── ObjC Interop ───────────────────────────────────────────────────────

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend(nint receiver, nint selector, MTLClearColor color);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend(nint receiver, nint selector, NSRect rect, ulong styleMask, ulong backing, [MarshalAs(UnmanagedType.I1)] bool defer);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend(nint receiver, nint selector, NSRect rect);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial NSSize objc_msgSend_size(nint receiver, nint selector, NSSize size);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend(nint receiver, nint selector, NSRect rect, nint device);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial void objc_msgSend(nint receiver, nint selector, CGSize size);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend(nint receiver, nint selector, ulong mask, nint untilDate, nint mode, [MarshalAs(UnmanagedType.I1)] bool dequeue);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend(nint receiver, nint selector, double interval);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial nint objc_msgSend(nint receiver, nint selector, double interval, nint target, nint selector2, nint userInfo, [MarshalAs(UnmanagedType.I1)] bool repeats);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial NSRect objc_msgSend_rect(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial CGSize objc_msgSend_size(nint receiver, nint selector);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_allocateClassPair")]
    private static partial nint objc_allocateClassPair(nint superClass, [MarshalAs(UnmanagedType.LPStr)] string name, IntPtr extraBytes);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_registerClassPair")]
    private static partial void objc_registerClassPair(nint cls);

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "class_addMethod")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool class_addMethod(nint cls, nint name, nint imp, [MarshalAs(UnmanagedType.LPStr)] string types);

    [LibraryImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "NSApplicationLoad")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool NSApplicationLoad();

    // ─── Structs & Enums ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct MTLClearColor
    {
        public double Red;
        public double Green;
        public double Blue;
        public double Alpha;

        public MTLClearColor(double r, double g, double b, double a)
        {
            Red = r;
            Green = g;
            Blue = b;
            Alpha = a;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSPoint
    {
        public double X;
        public double Y;

        public NSPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSSize
    {
        public double Width;
        public double Height;
        public NSSize(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NSRect
    {
        public NSPoint Origin;
        public NSSize Size;

        public NSRect(double x, double y, double width, double height)
        {
            Origin = new NSPoint(x, y);
            Size = new NSSize(width, height);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width;
        public double Height;

        public CGSize(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }

    [Flags]
    private enum NSViewAutoresizingMask : ulong
    {
        WidthSizable = 1 << 1,
        HeightSizable = 1 << 4
    }

    private enum NSViewLayerContentsRedrawPolicy : ulong
    {
        DuringViewResize = 2
    }

    [Flags]
    private enum CALayerAutoresizingMask : ulong
    {
        WidthSizable = 1 << 1,
        HeightSizable = 1 << 4
    }

    private enum NSApplicationActivationPolicy : ulong
    {
        Regular = 0
    }

    [Flags]
    private enum NSWindowStyleMask : ulong
    {
        Titled = 1 << 0,
        Closable = 1 << 1,
        Miniaturizable = 1 << 2,
        Resizable = 1 << 3
    }

    private enum NSBackingStore : ulong
    {
        Buffered = 2
    }

    // ─── Selectors ──────────────────────────────────────────────────────────

    private static class Sel
    {
        public static readonly nint SharedApplication = ObjCRuntime.RegisterSelector("sharedApplication");
        public static readonly nint SetActivationPolicy = ObjCRuntime.RegisterSelector("setActivationPolicy:");
        public static readonly nint ActivateIgnoringOtherApps = ObjCRuntime.RegisterSelector("activateIgnoringOtherApps:");
        public static readonly nint FinishLaunching = ObjCRuntime.RegisterSelector("finishLaunching");
        public static readonly nint Run = ObjCRuntime.RegisterSelector("run");

        public static readonly nint InitWithContentRectStyleMaskBackingDefer = ObjCRuntime.RegisterSelector("initWithContentRect:styleMask:backing:defer:");
        public static readonly nint SetTitle = ObjCRuntime.RegisterSelector("setTitle:");
        public static readonly nint Center = ObjCRuntime.RegisterSelector("center");
        public static readonly nint SetReleasedWhenClosed = ObjCRuntime.RegisterSelector("setReleasedWhenClosed:");
        public static readonly nint SetPreservesContentDuringLiveResize = ObjCRuntime.RegisterSelector("setPreservesContentDuringLiveResize:");
        public static readonly nint MakeKeyAndOrderFront = ObjCRuntime.RegisterSelector("makeKeyAndOrderFront:");
        public static readonly nint Display = ObjCRuntime.RegisterSelector("display");
        public static readonly nint SetContentView = ObjCRuntime.RegisterSelector("setContentView:");

        public static readonly nint InitWithFrame = ObjCRuntime.RegisterSelector("initWithFrame:");
        public static readonly nint SetWantsLayer = ObjCRuntime.RegisterSelector("setWantsLayer:");
        public static readonly nint SetLayer = ObjCRuntime.RegisterSelector("setLayer:");
        public static readonly nint SetAutoresizingMask = ObjCRuntime.RegisterSelector("setAutoresizingMask:");
        public static readonly nint SetLayerContentsRedrawPolicy = ObjCRuntime.RegisterSelector("setLayerContentsRedrawPolicy:");

        public static readonly nint SetPixelFormat = ObjCRuntime.RegisterSelector("setPixelFormat:");
        public static readonly nint SetFramebufferOnly = ObjCRuntime.RegisterSelector("setFramebufferOnly:");
        public static readonly nint SetNeedsDisplayOnBoundsChange = ObjCRuntime.RegisterSelector("setNeedsDisplayOnBoundsChange:");
        public static readonly nint SetPresentsWithTransaction = ObjCRuntime.RegisterSelector("setPresentsWithTransaction:");
        public static readonly nint SetAllowsNextDrawableTimeout = ObjCRuntime.RegisterSelector("setAllowsNextDrawableTimeout:");
        public static readonly nint SetDrawableSize = ObjCRuntime.RegisterSelector("setDrawableSize:");
        public static readonly nint SetContentsScale = ObjCRuntime.RegisterSelector("setContentsScale:");
        public static readonly nint Texture = ObjCRuntime.RegisterSelector("texture");
        public static readonly nint SetDelegate = ObjCRuntime.RegisterSelector("setDelegate:");
        public static readonly nint DisplayLayer = ObjCRuntime.RegisterSelector("displayLayer:");
        public static readonly nint SetNeedsDisplay = ObjCRuntime.RegisterSelector("setNeedsDisplay");
        public static readonly nint ScheduledTimer = ObjCRuntime.RegisterSelector("scheduledTimerWithTimeInterval:target:selector:userInfo:repeats:");
        public static readonly nint Tick = ObjCRuntime.RegisterSelector("tick:");

        public static readonly nint BackingScaleFactor = ObjCRuntime.RegisterSelector("backingScaleFactor");
        public static readonly nint Window = ObjCRuntime.RegisterSelector("window");
        public static readonly nint Bounds = ObjCRuntime.RegisterSelector("bounds");

        public static readonly nint SetClearColor = ObjCRuntime.RegisterSelector("setClearColor:");
        public static readonly nint ConvertSizeToBacking = ObjCRuntime.RegisterSelector("convertSizeToBacking:");
        public static readonly nint Present = ObjCRuntime.RegisterSelector("present");
    }
}
