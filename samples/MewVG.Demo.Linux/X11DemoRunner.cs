using System.Runtime.InteropServices;
using Aprillz.MewVG;

namespace MewVG.Demo.Linux;

internal sealed unsafe partial class X11DemoRunner : DemoRunner
{
    private readonly nint _display;
    private readonly nint _window;
    private readonly nint _ctx;
    private readonly nint _wmDeleteWindow;
    private nint _openglLib;
    private int _winw = DefaultWidth;
    private int _winh = DefaultHeight;
    private bool _running = true;
    private GLMinimal? _gl;
    private NanoVGGL? _vg;

    public X11DemoRunner()
    {
        _display = X11.XOpenDisplay(nint.Zero);
        if (_display == nint.Zero)
        {
            throw new InvalidOperationException("Cannot open X display.");
        }

        int screen = X11.XDefaultScreen(_display);
        nint root = X11.XRootWindow(_display, screen);

        // GLX visual: RGBA, double-buffered, depth 24, stencil 8
        int* attribs = stackalloc int[]
        {
            4,  // GLX_RGBA
            5,  // GLX_DOUBLEBUFFER
            8, 8,   // GLX_RED_SIZE, 8
            9, 8,   // GLX_GREEN_SIZE, 8
            10, 8,  // GLX_BLUE_SIZE, 8
            11, 8,  // GLX_ALPHA_SIZE, 8
            12, 24, // GLX_DEPTH_SIZE, 24
            13, 8,  // GLX_STENCIL_SIZE, 8
            0       // None
        };

        var vi = GLX.glXChooseVisual(_display, screen, (nint)attribs);
        if (vi == nint.Zero)
        {
            X11.XCloseDisplay(_display);
            throw new InvalidOperationException("glXChooseVisual failed.");
        }

        var visualInfo = *(XVisualInfo*)vi;

        var cmap = X11.XCreateColormap(_display, root, visualInfo.visual, 0 /* AllocNone */);

        var swa = new XSetWindowAttributes();
        swa.colormap = cmap;
        swa.event_mask = X11.StructureNotifyMask | X11.ExposureMask | X11.KeyPressMask;

        _window = X11.XCreateWindow(
            _display, root,
            0, 0, (uint)DefaultWidth, (uint)DefaultHeight,
            0, visualInfo.depth,
            1, // InputOutput
            visualInfo.visual,
            (nuint)(X11.CWColormap | X11.CWEventMask),
            ref swa);

        X11.XStoreName(_display, _window, DefaultTitle);

        // Handle WM_DELETE_WINDOW
        _wmDeleteWindow = X11.XInternAtom(_display, "WM_DELETE_WINDOW", false);
        var wmDelete = _wmDeleteWindow;
        X11.XSetWMProtocols(_display, _window, ref wmDelete, 1);

        X11.XMapWindow(_display, _window);

        // Create GLX context
        _ctx = GLX.glXCreateContext(_display, vi, nint.Zero, 1);
        if (_ctx == nint.Zero)
        {
            X11.XDestroyWindow(_display, _window);
            X11.XCloseDisplay(_display);
            throw new InvalidOperationException("glXCreateContext failed.");
        }

        GLX.glXMakeCurrent(_display, _window, _ctx);

        // VSync
        SetSwapInterval(_display, _window, 1);
    }

    protected override void Initialize()
    {
        Func<string, nint> getProcAddress = ResolveGLProc;

        NanoVGGL.Initialize(getProcAddress);

        _gl = new GLMinimal(getProcAddress);
        _vg = new NanoVGGL();
    }

    protected override void Execute()
    {
        while (_running)
        {
            ProcessEvents();

            var pxRatio = _winw > 0 ? (float)_winw / _winw : 1.0f;

            _gl!.Viewport(0, 0, _winw, _winh);
            _gl.ClearColor(0f, 0f, 0f, 0f);
            _gl.ClearStencil(0);
            _gl.Clear(GLMinimal.ColorBufferBit | GLMinimal.StencilBufferBit);

            _vg!.BeginFrame(_winw, _winh, pxRatio);
            DemoScene.DrawDemo(_vg, _winw, _winh);
            _vg.EndFrame();

            GLX.glXSwapBuffers(_display, _window);
        }
    }

    protected override void Shutdown()
    {
        GLX.glXMakeCurrent(_display, _window, _ctx);
        _vg?.Dispose();
        _vg = null;
    }

    public override void Dispose()
    {
        GLX.glXDestroyContext(_display, _ctx);
        X11.XDestroyWindow(_display, _window);
        X11.XCloseDisplay(_display);
    }

    private nint ResolveGLProc(string name)
    {
        var ptr = GLX.glXGetProcAddress(name);
        if (ptr != nint.Zero)
        {
            return ptr;
        }

        if (_openglLib == nint.Zero)
        {
            if (!NativeLibrary.TryLoad("libGL.so.1", out _openglLib))
            {
                NativeLibrary.TryLoad("libGL.so", out _openglLib);
            }
        }

        if (_openglLib != nint.Zero && NativeLibrary.TryGetExport(_openglLib, name, out var exported))
        {
            return exported;
        }

        return nint.Zero;
    }

    private void ProcessEvents()
    {
        var evt = new XEvent();
        while (X11.XPending(_display) > 0)
        {
            X11.XNextEvent(_display, ref evt);

            if (evt.type == X11.ConfigureNotify)
            {
                _winw = evt.xconfigure.width;
                _winh = evt.xconfigure.height;
            }
            else if (evt.type == X11.KeyPress)
            {
                var keysym = X11.XLookupKeysym(ref evt.xkey, 0);
                if (keysym == 0xFF1B) // XK_Escape
                {
                    _running = false;
                }
            }
            else if (evt.type == X11.ClientMessage)
            {
                if (evt.xclient.data_l0 == (long)_wmDeleteWindow)
                {
                    _running = false;
                }
            }
        }
    }

    private static void SetSwapInterval(nint display, nint window, int interval)
    {
        var fn = GLX.glXGetProcAddress("glXSwapIntervalEXT");
        if (fn != nint.Zero)
        {
            ((delegate* unmanaged<nint, nint, int, void>)fn)(display, window, interval);
            return;
        }

        fn = GLX.glXGetProcAddress("glXSwapIntervalMESA");
        if (fn != nint.Zero)
        {
            ((delegate* unmanaged<int, int>)fn)(interval);
            return;
        }

        fn = GLX.glXGetProcAddress("glXSwapIntervalSGI");
        if (fn != nint.Zero)
        {
            ((delegate* unmanaged<int, int>)fn)(interval);
        }
    }

    // ─── X11 Native ─────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct XVisualInfo
    {
        public nint visual;
        public nint visualid;
        public int screen;
        public int depth;
        public int @class;
        public nuint red_mask;
        public nuint green_mask;
        public nuint blue_mask;
        public int colormap_size;
        public int bits_per_rgb;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XSetWindowAttributes
    {
        public nint background_pixmap;
        public nuint background_pixel;
        public nint border_pixmap;
        public nuint border_pixel;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public nuint backing_planes;
        public nuint backing_pixel;
        public int save_under;
        public nint event_mask;
        public nint do_not_propagate_mask;
        public int override_redirect;
        public nint colormap;
        public nint cursor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XKeyEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nint window;
        public nint root;
        public nint subwindow;
        public nuint time;
        public int x, y;
        public int x_root, y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XConfigureEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nint @event;
        public nint window;
        public int x, y;
        public int width, height;
        public int border_width;
        public nint above;
        public int override_redirect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public nint display;
        public nint window;
        public nint message_type;
        public int format;
        public long data_l0;
        public long data_l1;
        public long data_l2;
        public long data_l3;
        public long data_l4;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XKeyEvent xkey;
        [FieldOffset(0)] public XConfigureEvent xconfigure;
        [FieldOffset(0)] public XClientMessageEvent xclient;
    }

    private static partial class X11
    {
        public const nint StructureNotifyMask = 1 << 17;
        public const nint ExposureMask = 1 << 15;
        public const nint KeyPressMask = 1 << 0;
        public const nint CWColormap = 1 << 13;
        public const nint CWEventMask = 1 << 11;
        public const int ConfigureNotify = 22;
        public const int KeyPress = 2;
        public const int ClientMessage = 33;

        private const string Lib = "libX11.so.6";

        [LibraryImport(Lib)]
        public static partial nint XOpenDisplay(nint display_name);

        [LibraryImport(Lib)]
        public static partial void XCloseDisplay(nint display);

        [LibraryImport(Lib)]
        public static partial int XDefaultScreen(nint display);

        [LibraryImport(Lib)]
        public static partial nint XRootWindow(nint display, int screen);

        [LibraryImport(Lib)]
        public static partial nint XCreateColormap(nint display, nint window, nint visual, int alloc);

        [LibraryImport(Lib)]
        public static partial nint XCreateWindow(
            nint display, nint parent,
            int x, int y, uint width, uint height,
            uint border_width, int depth, uint @class,
            nint visual, nuint valuemask, ref XSetWindowAttributes attributes);

        [LibraryImport(Lib)]
        public static partial void XDestroyWindow(nint display, nint window);

        [LibraryImport(Lib)]
        public static partial void XMapWindow(nint display, nint window);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        public static partial void XStoreName(nint display, nint window, string name);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        public static partial nint XInternAtom(nint display, string name, [MarshalAs(UnmanagedType.Bool)] bool only_if_exists);

        [LibraryImport(Lib)]
        public static partial int XSetWMProtocols(nint display, nint window, ref nint protocols, int count);

        [LibraryImport(Lib)]
        public static partial int XPending(nint display);

        [LibraryImport(Lib)]
        public static partial void XNextEvent(nint display, ref XEvent event_return);

        [LibraryImport(Lib)]
        public static partial nint XLookupKeysym(ref XKeyEvent key_event, int index);
    }

    // ─── GLX Native ─────────────────────────────────────────────────────────

    private static partial class GLX
    {
        private const string Lib = "libGL.so.1";

        [LibraryImport(Lib)]
        public static partial nint glXChooseVisual(nint display, int screen, nint attribList);

        [LibraryImport(Lib)]
        public static partial nint glXCreateContext(nint display, nint visualInfo, nint shareList, int direct);

        [LibraryImport(Lib)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool glXMakeCurrent(nint display, nint drawable, nint ctx);

        [LibraryImport(Lib)]
        public static partial void glXDestroyContext(nint display, nint ctx);

        [LibraryImport(Lib)]
        public static partial void glXSwapBuffers(nint display, nint drawable);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        public static partial nint glXGetProcAddress(string procName);
    }
}
