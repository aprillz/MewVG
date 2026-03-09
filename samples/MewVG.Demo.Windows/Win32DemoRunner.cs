using System.Runtime.InteropServices;

using Aprillz.MewVG;

namespace MewVG.Demo.Windows;

internal sealed unsafe partial class Win32DemoRunner : DemoRunner
{
    private static Win32DemoRunner? s_instance;

    private const string ClassName = "MewVGDemo";

    private readonly nint _hwnd;
    private readonly nint _hdc;
    private readonly nint _hglrc;
    private readonly nint _opengl32;
    private GLMinimal? _gl;
    private NanoVGGL? _vg;
    private int _winw = DefaultWidth;
    private int _winh = DefaultHeight;
    private bool _running = true;

    public bool IsTransparencyBackground { get; set; } = true;

    public Win32DemoRunner()
    {
        s_instance = this;

        _opengl32 = NativeLibrary.Load("opengl32.dll");
        var hInstance = Kernel32.GetModuleHandleW(null);

        // Register window class
        fixed (char* pClassName = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = User32.CS_OWNDC | User32.CS_HREDRAW | User32.CS_VREDRAW,
                lpfnWndProc = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&WndProc,
                hInstance = hInstance,
                hCursor = User32.LoadCursorW(nint.Zero, 32512),
                lpszClassName = (nint)pClassName,
            };

            if (User32.RegisterClassExW(ref wc) == 0)
            {
                throw new InvalidOperationException("RegisterClassExW failed.");
            }
        }

        // Calculate window size for desired client area
        uint dwStyle = User32.WS_OVERLAPPEDWINDOW;
        var rect = new RECT { right = DefaultWidth, bottom = DefaultHeight };
        User32.AdjustWindowRectEx(ref rect, dwStyle, 0, 0);

        _hwnd = User32.CreateWindowExW(
            0, ClassName, DefaultTitle, dwStyle,
            User32.CW_USEDEFAULT, User32.CW_USEDEFAULT,
            rect.right - rect.left, rect.bottom - rect.top,
            nint.Zero, nint.Zero, hInstance, nint.Zero);

        if (_hwnd == nint.Zero)
        {
            throw new InvalidOperationException("CreateWindowExW failed.");
        }

        if (IsTransparencyBackground)
        {
            ApplyBlurBehind();
        }

        _hdc = User32.GetDC(_hwnd);

        // Set pixel format
        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)sizeof(PIXELFORMATDESCRIPTOR),
            nVersion = 1,
            dwFlags = Gdi32.PFD_DRAW_TO_WINDOW | Gdi32.PFD_SUPPORT_OPENGL | Gdi32.PFD_DOUBLEBUFFER,
            cColorBits = 32,
            cAlphaBits = 8,
            cDepthBits = 24,
            cStencilBits = 8,
        };

        int pixelFormat = Gdi32.ChoosePixelFormat(_hdc, ref pfd);
        if (pixelFormat == 0)
        {
            throw new InvalidOperationException("ChoosePixelFormat failed.");
        }

        Gdi32.SetPixelFormat(_hdc, pixelFormat, ref pfd);

        // Create legacy context to bootstrap WGL extensions
        var legacyCtx = WGL.wglCreateContext(_hdc);
        if (legacyCtx == nint.Zero)
        {
            throw new InvalidOperationException("wglCreateContext failed.");
        }

        WGL.wglMakeCurrent(_hdc, legacyCtx);

        // Create GL 3.3 core profile context via wglCreateContextAttribsARB
        var createCtxAttribs = (delegate* unmanaged<nint, nint, int*, nint>)
            WGL.wglGetProcAddress("wglCreateContextAttribsARB");

        if (createCtxAttribs != null)
        {
            int* attribs = stackalloc int[]
            {
                0x2091, 3, // WGL_CONTEXT_MAJOR_VERSION_ARB
                0x2092, 3, // WGL_CONTEXT_MINOR_VERSION_ARB
                0x9126, 1, // WGL_CONTEXT_PROFILE_MASK_ARB = CORE_PROFILE
                0
            };

            _hglrc = createCtxAttribs(_hdc, nint.Zero, attribs);
            if (_hglrc == nint.Zero)
            {
                throw new InvalidOperationException("wglCreateContextAttribsARB failed.");
            }

            WGL.wglMakeCurrent(_hdc, _hglrc);
            WGL.wglDeleteContext(legacyCtx);
        }
        else
        {
            _hglrc = legacyCtx;
        }

        // VSync
        var swapInterval = (delegate* unmanaged<int, int>)
            WGL.wglGetProcAddress("wglSwapIntervalEXT");
        if (swapInterval != null)
        {
            swapInterval(1);
        }

        User32.ShowWindow(_hwnd, User32.SW_SHOW);
    }

    protected override void Initialize()
    {
        Func<string, nint> getProcAddress = ResolveGLProc;

        NanoVGGL.Initialize(getProcAddress);

        _gl = new GLMinimal(getProcAddress);
        _vg = new NanoVGGL( NVGcreateFlags.Antialias );
    }

    protected override void Execute()
    {
        while (_running)
        {
            ProcessEvents();

            _gl!.Viewport(0, 0, _winw, _winh);
            if (IsTransparencyBackground)
            {
                _gl.ClearColor(0, 0, 0, 0);
            }
            else
            {
                _gl.ClearColor(0.5f, 0.5f, 0.5f, 1f);
            }
            _gl.ClearStencil(0);
            _gl.Clear(GLMinimal.ColorBufferBit | GLMinimal.StencilBufferBit);

            _vg!.BeginFrame(_winw, _winh, 1.0f);
            DemoScene.DrawDemo(_vg, _winw, _winh);
            _vg.EndFrame();

            Gdi32.SwapBuffers(_hdc);
        }
    }

    protected override void Shutdown()
    {
        WGL.wglMakeCurrent(_hdc, _hglrc);
        _vg?.Dispose();
        _vg = null;
    }

    public override void Dispose()
    {
        WGL.wglDeleteContext(_hglrc);
        User32.ReleaseDC(_hwnd, _hdc);
        User32.DestroyWindow(_hwnd);
        s_instance = null;
    }

    private void ApplyBlurBehind()
    {
        User32.GetClientRect(_hwnd, out var cr);
        var rgn = Gdi32.CreateRectRgn(0, 0, cr.right, cr.bottom);
        var bb = new DWM_BLURBEHIND
        {
            dwFlags = 1 | 2, // DWM_BB_ENABLE | DWM_BB_BLURREGION
            fEnable = 1,
            hRgnBlur = rgn,
        };
        Dwmapi.DwmEnableBlurBehindWindow(_hwnd, ref bb);
        Gdi32.DeleteObject(rgn);
    }

    private nint ResolveGLProc(string name)
    {
        // wglGetProcAddress returns extension / GL 1.2+ entry points
        var ptr = WGL.wglGetProcAddress(name);
        if (ptr != nint.Zero && ptr != 1 && ptr != 2 && ptr != 3 && ptr != -1)
        {
            return ptr;
        }

        // Core GL 1.1 entry points are exported directly from opengl32.dll
        if (NativeLibrary.TryGetExport(_opengl32, name, out var exported))
        {
            return exported;
        }

        return nint.Zero;
    }

    private void ProcessEvents()
    {
        var msg = new MSG();
        while (User32.PeekMessageW(ref msg, nint.Zero, 0, 0, User32.PM_REMOVE) != 0)
        {
            User32.TranslateMessage(ref msg);
            User32.DispatchMessageW(ref msg);
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case User32.WM_SIZE:
                if (s_instance != null)
                {
                    s_instance._winw = (int)(lParam & 0xFFFF);
                    s_instance._winh = (int)((lParam >> 16) & 0xFFFF);
                }
                return 0;

            case User32.WM_CLOSE:
                if (s_instance != null)
                {
                    s_instance._running = false;
                }
                return 0;

            case User32.WM_KEYDOWN:
                if ((int)wParam == User32.VK_ESCAPE && s_instance != null)
                {
                    s_instance._running = false;
                }
                return 0;
        }

        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ─── Structs ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits;
        public byte cRedShift;
        public byte cGreenBits;
        public byte cGreenShift;
        public byte cBlueBits;
        public byte cBlueShift;
        public byte cAlphaBits;
        public byte cAlphaShift;
        public byte cAccumBits;
        public byte cAccumRedBits;
        public byte cAccumGreenBits;
        public byte cAccumBlueBits;
        public byte cAccumAlphaBits;
        public byte cDepthBits;
        public byte cStencilBits;
        public byte cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask;
        public uint dwVisibleMask;
        public uint dwDamageMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        public int fEnable;
        public nint hRgnBlur;
        public int fTransitionOnMaximized;
    }

    // ─── Win32 Native ────────────────────────────────────────────────────────

    private static partial class User32
    {
        public const uint CS_OWNDC = 0x0020;
        public const uint CS_HREDRAW = 0x0002;
        public const uint CS_VREDRAW = 0x0001;
        public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        public const int CW_USEDEFAULT = unchecked((int)0x80000000);
        public const int SW_SHOW = 5;
        public const uint PM_REMOVE = 0x0001;
        public const uint WM_CLOSE = 0x0010;
        public const uint WM_SIZE = 0x0005;
        public const uint WM_KEYDOWN = 0x0100;
        public const int VK_ESCAPE = 0x1B;

        [LibraryImport("user32.dll")]
        public static partial ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x, int y, int nWidth, int nHeight,
            nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [LibraryImport("user32.dll")]
        public static partial int DestroyWindow(nint hwnd);

        [LibraryImport("user32.dll")]
        public static partial int ShowWindow(nint hwnd, int nCmdShow);

        [LibraryImport("user32.dll")]
        public static partial int PeekMessageW(ref MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [LibraryImport("user32.dll")]
        public static partial int TranslateMessage(ref MSG lpMsg);

        [LibraryImport("user32.dll")]
        public static partial nint DispatchMessageW(ref MSG lpMsg);

        [LibraryImport("user32.dll")]
        public static partial nint DefWindowProcW(nint hWnd, uint Msg, nint wParam, nint lParam);

        [LibraryImport("user32.dll")]
        public static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

        [LibraryImport("user32.dll")]
        public static partial int AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, int bMenu, uint dwExStyle);

        [LibraryImport("user32.dll")]
        public static partial nint GetDC(nint hwnd);

        [LibraryImport("user32.dll")]
        public static partial int ReleaseDC(nint hwnd, nint hdc);

        [LibraryImport("user32.dll")]
        public static partial int GetSystemMetrics(int idx);

        [LibraryImport("user32.dll")]
        public static partial int GetClientRect(nint hwnd, out RECT rc);
    }

    private static partial class Gdi32
    {
        public const uint PFD_DRAW_TO_WINDOW = 0x00000004;
        public const uint PFD_SUPPORT_OPENGL = 0x00000020;
        public const uint PFD_DOUBLEBUFFER = 0x00000001;

        [LibraryImport("gdi32.dll")]
        public static partial int ChoosePixelFormat(nint hdc, ref PIXELFORMATDESCRIPTOR ppfd);

        [LibraryImport("gdi32.dll")]
        public static partial int SetPixelFormat(nint hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

        [LibraryImport("gdi32.dll")]
        public static partial int SwapBuffers(nint hdc);

        [LibraryImport("gdi32.dll")]
        public static partial nint CreateRectRgn(int x1, int y1, int x2, int y2);

        [LibraryImport("gdi32.dll")]
        public static partial int DeleteObject(nint obj);
    }

    private static partial class Dwmapi
    {
        [LibraryImport("dwmapi.dll")]
        public static partial int DwmEnableBlurBehindWindow(nint hwnd, ref DWM_BLURBEHIND bb);
    }

    private static partial class Kernel32
    {
        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        public static partial nint GetModuleHandleW(string? lpModuleName);
    }

    // ─── WGL Native ──────────────────────────────────────────────────────────

    private static partial class WGL
    {
        [LibraryImport("opengl32.dll")]
        public static partial nint wglCreateContext(nint hdc);

        [LibraryImport("opengl32.dll")]
        public static partial int wglMakeCurrent(nint hdc, nint hglrc);

        [LibraryImport("opengl32.dll")]
        public static partial int wglDeleteContext(nint hglrc);

        [LibraryImport("opengl32.dll", StringMarshalling = StringMarshalling.Utf8)]
        public static partial nint wglGetProcAddress(string lpszProc);
    }
}