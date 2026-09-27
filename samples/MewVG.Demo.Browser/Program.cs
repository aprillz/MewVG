using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Aprillz.MewVG;

namespace MewVG.Demo.Browser;

[SupportedOSPlatform("browser")]
internal static partial class WebGLNative
{
    [LibraryImport("mewvg_webgl_shim", EntryPoint = "mewvg_webgl_init", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int InitializeContext(string selector);

    [LibraryImport("mewvg_webgl_shim", EntryPoint = "mewvg_webgl_get_proc", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(string name);

    // Never called; see mewvg_sig_* in mewvg_webgl_shim.c. Declared so the wasm build emits
    // interp-to-native trampolines for MewVG's GL function-pointer signatures.
    [LibraryImport("mewvg_webgl_shim")]
    internal static partial void mewvg_sig_viiiiiiiii(int arg0, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8);

    [LibraryImport("mewvg_webgl_shim")]
    internal static partial void mewvg_sig_vif(int arg0, float arg1);

    [LibraryImport("mewvg_webgl_shim")]
    internal static partial void mewvg_sig_vffff(float arg0, float arg1, float arg2, float arg3);
}

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private static GLMinimal? _gl;
    private static MewVGGL? _vg;

    // Entry point only; initialization runs through InitializeDemo so the runtime is not torn
    // down when Main returns (dotnet.run() exits the runtime after Main).
    public static void Main()
    {
    }

    [JSExport]
    internal static void InitializeDemo()
    {
        // Never true at runtime, but the trimmer cannot prove it, so the signature-pinning
        // imports (see WebGLNative) survive into the pinvoke table.
        if (Environment.GetEnvironmentVariable("MEWVG_PIN_SIGNATURES") == "force")
        {
            WebGLNative.mewvg_sig_viiiiiiiii(0, 0, 0, 0, 0, 0, 0, 0, 0);
            WebGLNative.mewvg_sig_vif(0, 0f);
            WebGLNative.mewvg_sig_vffff(0f, 0f, 0f, 0f);
        }

        var result = WebGLNative.InitializeContext("#canvas");
        if (result != 0)
        {
            throw new InvalidOperationException($"WebGL2 context creation failed (EMSCRIPTEN_RESULT {result}).");
        }

        MewVGGL.Initialize(WebGLNative.GetProcAddress, MewVGGLProfile.Gles3);
        _gl = new GLMinimal(WebGLNative.GetProcAddress);
        _vg = new MewVGGL();

        Console.WriteLine("MewVG WebGL2 demo initialized.");
    }

    // Driven by requestAnimationFrame in main.js. Sizes come from JS because the canvas element
    // and devicePixelRatio live there.
    [JSExport]
    internal static void RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight)
    {
        var gl = _gl;
        var vg = _vg;
        if (gl == null || vg == null)
        {
            return;
        }

        gl.Viewport(0, 0, pixelWidth, pixelHeight);
        gl.ClearColor(0.5f, 0.5f, 0.5f, 1f);
        gl.Clear(GLMinimal.ColorBufferBit);

        vg.BeginFrame((float)cssWidth, (float)cssHeight, (float)devicePixelRatio);
        DemoScene.DrawDemo(vg, (float)cssWidth, (float)cssHeight);
        vg.EndFrame();
    }
}
