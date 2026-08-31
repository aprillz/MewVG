using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using SkiaSharp;

namespace Skia.Demo.Browser;

[SupportedOSPlatform("browser")]
internal static partial class WebGLNative
{
    [LibraryImport("skia_webgl_shim", EntryPoint = "skia_webgl_init", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int InitializeContext(string selector);
}

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private const uint GL_RGBA8 = 0x8058;

    private static GRContext? _grContext;
    private static SKSurface? _surface;
    private static int _surfaceWidth;
    private static int _surfaceHeight;

    public static void Main()
    {
    }

    [JSExport]
    internal static void InitializeDemo()
    {
        var result = WebGLNative.InitializeContext("#canvas");
        if (result != 0)
        {
            throw new InvalidOperationException($"WebGL2 context creation failed (EMSCRIPTEN_RESULT {result}).");
        }

        var glInterface = GRGlInterface.Create();
        if (glInterface == null)
        {
            throw new InvalidOperationException("GRGlInterface.Create() returned null.");
        }

        _grContext = GRContext.CreateGl(glInterface);
        if (_grContext == null)
        {
            throw new InvalidOperationException("GRContext.CreateGl() returned null.");
        }

        Console.WriteLine("SkiaSharp WebGL2 demo initialized.");
    }

    [JSExport]
    internal static void RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight)
    {
        var grContext = _grContext;
        if (grContext == null)
        {
            return;
        }

        if (_surface == null || _surfaceWidth != pixelWidth || _surfaceHeight != pixelHeight)
        {
            _surface?.Dispose();
            var framebufferInfo = new GRGlFramebufferInfo(0, GL_RGBA8);
            var renderTarget = new GRBackendRenderTarget(pixelWidth, pixelHeight, 0, 8, framebufferInfo);
            _surface = SKSurface.Create(grContext, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
            if (_surface == null)
            {
                throw new InvalidOperationException("SKSurface.Create() returned null.");
            }

            _surfaceWidth = pixelWidth;
            _surfaceHeight = pixelHeight;
        }

        var canvas = _surface.Canvas;
        canvas.Clear(new SKColor(128, 128, 128));
        canvas.Save();
        canvas.Scale((float)devicePixelRatio);
        SkiaDemoScene.DrawDemo(canvas, (float)cssWidth, (float)cssHeight);
        canvas.Restore();
        canvas.Flush();
        grContext.Flush();
    }
}
