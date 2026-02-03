using System.Runtime.InteropServices;

using Aprillz.MewVG;
using Aprillz.MewVG.Interop;
using MetalInterop = Aprillz.MewVG.Interop.Metal;

namespace MewVG.Metal.Demo;

internal static unsafe partial class Program
{
    private const int Width = 800;
    private const int Height = 600;

    private static readonly NVGlineJoin[] LineJoins = { NVGlineJoin.Miter, NVGlineJoin.Round, NVGlineJoin.Bevel };
    private static readonly NVGlineCap[] LineCaps = { NVGlineCap.Butt, NVGlineCap.Round, NVGlineCap.Square };

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
    private static void Main()
    {
        _device = MetalDevice.CreateSystemDefaultDevice();
        if (_device == nint.Zero)
        {
            return;
        }

        _vg = new NanoVGMetal(_device);
        _vg.PixelFormat = MTLPixelFormat.BGRA8Unorm;
        _vg.StencilFormat = MTLPixelFormat.Stencil8;

        nint pool = CreateAutoreleasePool();

        _ = NSApplicationLoad();

        nint app = CreateApplication();
        if (app == nint.Zero)
        {
            return;
        }
        nint window = CreateWindow(app, Width, Height, "MewVG Metal Demo");
        if (window == nint.Zero)
        {
            return;
        }
        nint view = CreateContentView(window, Width, Height, _device, out nint metalLayer);
        if (view == nint.Zero)
        {
            return;
        }

        _contentView = view;
        _metalLayer = metalLayer;

        ObjCRuntime.SendMessageNoReturn(window, Sel.MakeKeyAndOrderFront, nint.Zero);
        ObjCRuntime.SendMessageNoReturn(window, Sel.Display);
        ObjCRuntime.SendMessage(app, Sel.ActivateIgnoringOtherApps, true);

        _commandQueue = ObjCRuntime.SendMessage(_device, MetalInterop.Sel.NewCommandQueue);
        if (_commandQueue == nint.Zero)
        {
            return;
        }

        SetupLayerDelegate(metalLayer);
        StartRenderTimer();
        ObjCRuntime.SendMessageNoReturn(app, Sel.Run);

        ObjCRuntime.SendMessageNoReturn(pool, ObjCRuntime.Selectors.release);
    }

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
        DrawScene(vg, width, height);
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


    private static void DrawScene(NanoVG vg, int width, int height)
    {
        DrawDemo(vg, width, height);
    }

    private static void DrawDemo(NanoVG vg, float width, float height)
    {
        // Background
        vg.BeginPath();
        vg.Rect(0, 0, width, height);
        vg.FillColor(28, 30, 34, 128);
        vg.Fill();

        var t = (float)(Environment.TickCount64 % 100000) / 1000.0f;
        var mx = width * 0.5f;
        var my = height * 0.5f;

        DrawEyes(vg, width - 250, 50, 150, 100, mx, my, t);
        DrawGraph(vg, 0, height / 2, width, height / 2, t);
        DrawColorWheel(vg, width - 300, height - 300, 250, 250, t);

        DrawLines(vg, 120, height - 50, 600, 50, t);
        DrawWidths(vg, 10, 50, 30);
        DrawCaps(vg, 10, 300, 30);
        DrawScissor(vg, 50, height - 80, t);

        // Widgets (no fonts/images)
        float wx = 50, wy = 50, ww = 300, wh = 400;
        DrawWindow(vg, wx, wy, ww, wh);

        float x = 60, y = 95;
        DrawSearchBox(vg, x, y, 280, 25);
        y += 40;
        DrawDropDown(vg, x, y, 280, 28);
        y += 45;

        DrawEditBox(vg, x, y, 280, 28);
        y += 35;
        DrawEditBox(vg, x, y, 280, 28);
        y += 38;
        DrawCheckBox(vg, x, y, 140, 28, true);
        DrawButton(vg, x + 138, y, 140, 28, NVGcolor.RGBA(0, 96, 128, 255));
        y += 45;

        DrawEditBoxBase(vg, x + 180, y, 100, 28);
        DrawSlider(vg, x, y, 170, 28, 0.4f);
        y += 55;

        DrawButton(vg, x, y, 160, 28, NVGcolor.RGBA(128, 16, 8, 255));
        DrawButton(vg, x + 170, y, 110, 28, NVGcolor.RGBA(0, 0, 0, 0));

        DrawThumbnailsNoImages(vg, 365, 95 + 14 - 30, 160, 300, 12, t);
    }

    private static float Clamp(float a, float mn, float mx) => a < mn ? mn : (a > mx ? mx : a);

    private static void DrawWindow(NanoVG vg, float x, float y, float w, float h)
    {
        var r = 3.0f;

        vg.Save();

        vg.BeginPath();
        vg.RoundedRect(x, y, w, h, r);
        vg.FillColor(28, 30, 34, 192);
        vg.Fill();

        var shadow = vg.BoxGradient(x, y + 2, w, h, r * 2, 10,
            NVGcolor.RGBA(0, 0, 0, 128), NVGcolor.RGBA(0, 0, 0, 0));
        vg.BeginPath();
        vg.Rect(x - 10, y - 10, w + 20, h + 30);
        vg.RoundedRect(x, y, w, h, r);
        vg.PathWinding(NVGwinding.CW);
        vg.FillPaint(shadow);
        vg.Fill();

        var header = vg.LinearGradient(x, y, x, y + 15,
            NVGcolor.RGBA(255, 255, 255, 8), NVGcolor.RGBA(0, 0, 0, 16));
        vg.BeginPath();
        vg.RoundedRect(x + 1, y + 1, w - 2, 30, r - 1);
        vg.FillPaint(header);
        vg.Fill();

        vg.BeginPath();
        vg.MoveTo(x + 0.5f, y + 0.5f + 30);
        vg.LineTo(x + 0.5f + w - 1, y + 0.5f + 30);
        vg.StrokeColor(0, 0, 0, 32);
        vg.Stroke();

        vg.Restore();
    }

    private static void DrawSearchBox(NanoVG vg, float x, float y, float w, float h)
    {
        var r = h / 2 - 1;
        var bg = vg.BoxGradient(x, y + 1.5f, w, h, h / 2, 5,
            NVGcolor.RGBA(0, 0, 0, 16), NVGcolor.RGBA(0, 0, 0, 92));
        vg.BeginPath();
        vg.RoundedRect(x, y, w, h, r);
        vg.FillPaint(bg);
        vg.Fill();
    }

    private static void DrawDropDown(NanoVG vg, float x, float y, float w, float h)
    {
        var r = 4.0f;
        var bg = vg.LinearGradient(x, y, x, y + h,
            NVGcolor.RGBA(255, 255, 255, 16), NVGcolor.RGBA(0, 0, 0, 16));
        vg.BeginPath();
        vg.RoundedRect(x + 1, y + 1, w - 2, h - 2, r - 1);
        vg.FillPaint(bg);
        vg.Fill();

        vg.BeginPath();
        vg.RoundedRect(x + 0.5f, y + 0.5f, w - 1, h - 1, r - 0.5f);
        vg.StrokeColor(0, 0, 0, 48);
        vg.Stroke();
    }

    private static void DrawEditBoxBase(NanoVG vg, float x, float y, float w, float h)
    {
        var bg = vg.BoxGradient(x + 1, y + 2.5f, w - 2, h - 2, 3, 4,
            NVGcolor.RGBA(255, 255, 255, 32), NVGcolor.RGBA(32, 32, 32, 32));
        vg.BeginPath();
        vg.RoundedRect(x + 1, y + 1, w - 2, h - 2, 3);
        vg.FillPaint(bg);
        vg.Fill();

        vg.BeginPath();
        vg.RoundedRect(x + 0.5f, y + 0.5f, w - 1, h - 1, 3.5f);
        vg.StrokeColor(0, 0, 0, 48);
        vg.Stroke();
    }

    private static void DrawEditBox(NanoVG vg, float x, float y, float w, float h)
        => DrawEditBoxBase(vg, x, y, w, h);

    private static void DrawCheckBox(NanoVG vg, float x, float y, float w, float h, bool checkedOn)
    {
        var bg = vg.BoxGradient(x + 1, y + (int)(h * 0.5f) - 9 + 1, 18, 18, 3, 3,
            NVGcolor.RGBA(0, 0, 0, 32), NVGcolor.RGBA(0, 0, 0, 92));
        vg.BeginPath();
        vg.RoundedRect(x + 1, y + (int)(h * 0.5f) - 9, 18, 18, 3);
        vg.FillPaint(bg);
        vg.Fill();

        if (checkedOn)
        {
            vg.BeginPath();
            vg.MoveTo(x + 4, y + h * 0.5f);
            vg.LineTo(x + 9, y + h * 0.5f + 5);
            vg.LineTo(x + 16, y + h * 0.5f - 6);
            vg.StrokeColor(255, 255, 255, 180);
            vg.StrokeWidth(2.5f);
            vg.Stroke();
            vg.StrokeWidth(1.0f);
        }
    }

    private static void DrawButton(NanoVG vg, float x, float y, float w, float h, NVGcolor col)
    {
        var r = 4.0f;
        var bg = vg.LinearGradient(x, y, x, y + h,
            NVGcolor.RGBA(255, 255, 255, IsBlack(col) ? (byte)16 : (byte)32),
            NVGcolor.RGBA(0, 0, 0, IsBlack(col) ? (byte)16 : (byte)32));
        vg.BeginPath();
        vg.RoundedRect(x + 1, y + 1, w - 2, h - 2, r - 1);
        if (!IsBlack(col))
        {
            vg.FillColor(col);
            vg.Fill();
        }
        vg.FillPaint(bg);
        vg.Fill();

        vg.BeginPath();
        vg.RoundedRect(x + 0.5f, y + 0.5f, w - 1, h - 1, r - 0.5f);
        vg.StrokeColor(0, 0, 0, 48);
        vg.Stroke();
    }

    private static bool IsBlack(NVGcolor col) => col.R == 0 && col.G == 0 && col.B == 0 && col.A == 0;

    private static void DrawEyes(NanoVG vg, float x, float y, float w, float h, float mx, float my, float t)
    {
        var ex = w * 0.23f;
        var ey = h * 0.5f;
        var lx = x + ex;
        var ly = y + ey;
        var rx = x + w - ex;
        var ry = y + ey;
        var br = (ex < ey ? ex : ey) * 0.5f;
        var blink = 1 - MathF.Pow(MathF.Sin(t * 0.5f), 200) * 0.8f;

        var bg = vg.LinearGradient(x, y + h * 0.5f, x + w * 0.1f, y + h,
            NVGcolor.RGBA(0, 0, 0, 32), NVGcolor.RGBA(0, 0, 0, 16));
        vg.BeginPath();
        vg.Ellipse(lx + 3.0f, ly + 16.0f, ex, ey);
        vg.Ellipse(rx + 3.0f, ry + 16.0f, ex, ey);
        vg.FillPaint(bg);
        vg.Fill();

        bg = vg.LinearGradient(x, y + h * 0.25f, x + w * 0.1f, y + h,
            NVGcolor.RGBA(220, 220, 220, 255), NVGcolor.RGBA(128, 128, 128, 255));
        vg.BeginPath();
        vg.Ellipse(lx, ly, ex, ey);
        vg.Ellipse(rx, ry, ex, ey);
        vg.FillPaint(bg);
        vg.Fill();

        var dx = (mx - rx) / (ex * 10);
        var dy = (my - ry) / (ey * 10);
        var d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > 1.0f) { dx /= d; dy /= d; }
        dx *= ex * 0.4f; dy *= ey * 0.5f;
        vg.BeginPath();
        vg.Ellipse(lx + dx, ly + dy + ey * 0.25f * (1 - blink), br, br * blink);
        vg.FillColor(32, 32, 32, 255);
        vg.Fill();

        dx = (mx - rx) / (ex * 10);
        dy = (my - ry) / (ey * 10);
        d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > 1.0f) { dx /= d; dy /= d; }
        dx *= ex * 0.4f; dy *= ey * 0.5f;
        vg.BeginPath();
        vg.Ellipse(rx + dx, ry + dy + ey * 0.25f * (1 - blink), br, br * blink);
        vg.FillColor(32, 32, 32, 255);
        vg.Fill();

        var gloss = vg.RadialGradient(lx - ex * 0.25f, ly - ey * 0.5f, ex * 0.1f, ex * 0.75f,
            NVGcolor.RGBA(255, 255, 255, 128), NVGcolor.RGBA(255, 255, 255, 0));
        vg.BeginPath();
        vg.Ellipse(lx, ly, ex, ey);
        vg.FillPaint(gloss);
        vg.Fill();

        gloss = vg.RadialGradient(rx - ex * 0.25f, ry - ey * 0.5f, ex * 0.1f, ex * 0.75f,
            NVGcolor.RGBA(255, 255, 255, 128), NVGcolor.RGBA(255, 255, 255, 0));
        vg.BeginPath();
        vg.Ellipse(rx, ry, ex, ey);
        vg.FillPaint(gloss);
        vg.Fill();
    }

    private static void DrawGraph(NanoVG vg, float x, float y, float w, float h, float t)
    {
        Span<float> samples = stackalloc float[6];
        Span<float> sx = stackalloc float[6];
        Span<float> sy = stackalloc float[6];
        var dx = w / 5.0f;

        samples[0] = (1 + MathF.Sin(t * 1.2345f + MathF.Cos(t * 0.33457f) * 0.44f)) * 0.5f;
        samples[1] = (1 + MathF.Sin(t * 0.68363f + MathF.Cos(t * 1.3f) * 1.55f)) * 0.5f;
        samples[2] = (1 + MathF.Sin(t * 1.1642f + MathF.Cos(t * 0.33457f) * 1.24f)) * 0.5f;
        samples[3] = (1 + MathF.Sin(t * 0.56345f + MathF.Cos(t * 1.63f) * 0.14f)) * 0.5f;
        samples[4] = (1 + MathF.Sin(t * 1.6245f + MathF.Cos(t * 0.254f) * 0.3f)) * 0.5f;
        samples[5] = (1 + MathF.Sin(t * 0.345f + MathF.Cos(t * 0.03f) * 0.6f)) * 0.5f;

        for (var i = 0; i < 6; i++)
        {
            sx[i] = x + i * dx;
            sy[i] = y + h * samples[i] * 0.8f;
        }

        var bg = vg.LinearGradient(x, y, x, y + h, NVGcolor.RGBA(0, 160, 192, 0), NVGcolor.RGBA(0, 160, 192, 64));
        vg.BeginPath();
        vg.MoveTo(sx[0], sy[0]);
        for (var i = 1; i < 6; i++)
        {
            vg.BezierTo(sx[i - 1] + dx * 0.5f, sy[i - 1], sx[i] - dx * 0.5f, sy[i], sx[i], sy[i]);
        }

        vg.LineTo(x + w, y + h);
        vg.LineTo(x, y + h);
        vg.FillPaint(bg);
        vg.Fill();

        vg.BeginPath();
        vg.MoveTo(sx[0], sy[0] + 2);
        for (var i = 1; i < 6; i++)
        {
            vg.BezierTo(sx[i - 1] + dx * 0.5f, sy[i - 1] + 2, sx[i] - dx * 0.5f, sy[i] + 2, sx[i], sy[i] + 2);
        }

        vg.StrokeColor(0, 0, 0, 32);
        vg.StrokeWidth(3.0f);
        vg.Stroke();

        vg.BeginPath();
        vg.MoveTo(sx[0], sy[0]);
        for (var i = 1; i < 6; i++)
        {
            vg.BezierTo(sx[i - 1] + dx * 0.5f, sy[i - 1], sx[i] - dx * 0.5f, sy[i], sx[i], sy[i]);
        }

        vg.StrokeColor(0, 160, 192, 255);
        vg.StrokeWidth(3.0f);
        vg.Stroke();

        for (var i = 0; i < 6; i++)
        {
            bg = vg.RadialGradient(sx[i], sy[i] + 2, 3.0f, 8.0f, NVGcolor.RGBA(0, 0, 0, 32), NVGcolor.RGBA(0, 0, 0, 0));
            vg.BeginPath();
            vg.Rect(sx[i] - 10, sy[i] - 10 + 2, 20, 20);
            vg.FillPaint(bg);
            vg.Fill();
        }

        vg.BeginPath();
        for (var i = 0; i < 6; i++)
        {
            vg.Circle(sx[i], sy[i], 4.0f);
        }

        vg.FillColor(0, 160, 192, 255);
        vg.Fill();

        vg.BeginPath();
        for (var i = 0; i < 6; i++)
        {
            vg.Circle(sx[i], sy[i], 2.0f);
        }

        vg.FillColor(220, 220, 220, 255);
        vg.Fill();

        vg.StrokeWidth(1.0f);
    }

    private static void DrawSpinner(NanoVG vg, float cx, float cy, float r, float t)
    {
        var a0 = 0.0f + t * 6;
        var a1 = MathF.PI + t * 6;
        var r0 = r;
        var r1 = r * 0.75f;

        vg.Save();
        vg.BeginPath();
        vg.Arc(cx, cy, r0, a0, a1, NVGwinding.CW);
        vg.Arc(cx, cy, r1, a1, a0, NVGwinding.CCW);
        vg.ClosePath();
        var ax = cx + MathF.Cos(a0) * (r0 + r1) * 0.5f;
        var ay = cy + MathF.Sin(a0) * (r0 + r1) * 0.5f;
        var bx = cx + MathF.Cos(a1) * (r0 + r1) * 0.5f;
        var by = cy + MathF.Sin(a1) * (r0 + r1) * 0.5f;
        var paint = vg.LinearGradient(ax, ay, bx, by, NVGcolor.RGBA(0, 0, 0, 0), NVGcolor.RGBA(0, 0, 0, 128));
        vg.FillPaint(paint);
        vg.Fill();
        vg.Restore();
    }

    private static void DrawColorWheel(NanoVG vg, float x, float y, float w, float h, float t)
    {
        vg.Save();

        var cx = x + w * 0.5f;
        var cy = y + h * 0.5f;
        var r1 = (w < h ? w : h) * 0.5f - 5.0f;
        var r0 = r1 - 20.0f;
        var aeps = 0.5f / r1;
        var hue = MathF.Sin(t * 0.12f);

        for (var i = 0; i < 6; i++)
        {
            var a0 = i / 6.0f * MathF.PI * 2.0f - aeps;
            var a1 = (i + 1.0f) / 6.0f * MathF.PI * 2.0f + aeps;
            vg.BeginPath();
            vg.Arc(cx, cy, r0, a0, a1, NVGwinding.CW);
            vg.Arc(cx, cy, r1, a1, a0, NVGwinding.CCW);
            vg.ClosePath();
            var ax = cx + MathF.Cos(a0) * (r0 + r1) * 0.5f;
            var ay = cy + MathF.Sin(a0) * (r0 + r1) * 0.5f;
            var bx = cx + MathF.Cos(a1) * (r0 + r1) * 0.5f;
            var by = cy + MathF.Sin(a1) * (r0 + r1) * 0.5f;
            var paint = vg.LinearGradient(ax, ay, bx, by,
                NVGcolor.HSLA(a0 / (MathF.PI * 2), 1.0f, 0.55f, 255),
                NVGcolor.HSLA(a1 / (MathF.PI * 2), 1.0f, 0.55f, 255));
            vg.FillPaint(paint);
            vg.Fill();
        }

        vg.BeginPath();
        vg.Circle(cx, cy, r0 - 0.5f);
        vg.Circle(cx, cy, r1 + 0.5f);
        vg.StrokeColor(0, 0, 0, 64);
        vg.StrokeWidth(1.0f);
        vg.Stroke();

        vg.Save();
        vg.Translate(cx, cy);
        vg.Rotate(hue * MathF.PI * 2);

        vg.StrokeWidth(2.0f);
        vg.BeginPath();
        vg.Rect(r0 - 1, -3, r1 - r0 + 2, 6);
        vg.StrokeColor(255, 255, 255, 192);
        vg.Stroke();

        var paint2 = vg.BoxGradient(r0 - 3, -5, r1 - r0 + 6, 10, 2, 4,
            NVGcolor.RGBA(0, 0, 0, 128), NVGcolor.RGBA(0, 0, 0, 0));
        vg.BeginPath();
        vg.Rect(r0 - 2 - 10, -4 - 10, r1 - r0 + 4 + 20, 8 + 20);
        vg.Rect(r0 - 2, -4, r1 - r0 + 4, 8);
        vg.PathWinding(NVGwinding.CW);
        vg.FillPaint(paint2);
        vg.Fill();

        var r = r0 - 6;
        var ax2 = MathF.Cos(120.0f / 180.0f * MathF.PI) * r;
        var ay2 = MathF.Sin(120.0f / 180.0f * MathF.PI) * r;
        var bx2 = MathF.Cos(-120.0f / 180.0f * MathF.PI) * r;
        var by2 = MathF.Sin(-120.0f / 180.0f * MathF.PI) * r;
        vg.BeginPath();
        vg.MoveTo(r, 0);
        vg.LineTo(ax2, ay2);
        vg.LineTo(bx2, by2);
        vg.ClosePath();
        paint2 = vg.LinearGradient(r, 0, ax2, ay2, NVGcolor.HSLA(hue, 1.0f, 0.5f, 255), NVGcolor.RGBA(255, 255, 255, 255));
        vg.FillPaint(paint2);
        vg.Fill();
        paint2 = vg.LinearGradient((r + ax2) * 0.5f, (0 + ay2) * 0.5f, bx2, by2, NVGcolor.RGBA(0, 0, 0, 0), NVGcolor.RGBA(0, 0, 0, 255));
        vg.FillPaint(paint2);
        vg.Fill();
        vg.StrokeColor(0, 0, 0, 64);
        vg.Stroke();

        ax2 = MathF.Cos(120.0f / 180.0f * MathF.PI) * r * 0.3f;
        ay2 = MathF.Sin(120.0f / 180.0f * MathF.PI) * r * 0.4f;
        vg.StrokeWidth(2.0f);
        vg.BeginPath();
        vg.Circle(ax2, ay2, 5);
        vg.StrokeColor(255, 255, 255, 192);
        vg.Stroke();

        paint2 = vg.RadialGradient(ax2, ay2, 7, 9, NVGcolor.RGBA(0, 0, 0, 64), NVGcolor.RGBA(0, 0, 0, 0));
        vg.BeginPath();
        vg.Rect(ax2 - 20, ay2 - 20, 40, 40);
        vg.Circle(ax2, ay2, 7);
        vg.PathWinding(NVGwinding.CW);
        vg.FillPaint(paint2);
        vg.Fill();

        vg.Restore();
        vg.Restore();
    }

    private static void DrawLines(NanoVG vg, float x, float y, float w, float h, float t)
    {
        var pad = 5.0f;
        var s = w / 9.0f - pad * 2;
        Span<float> pts = stackalloc float[8];

        vg.Save();

        pts[0] = -s * 0.25f + MathF.Cos(t * 0.3f) * s * 0.5f;
        pts[1] = MathF.Sin(t * 0.3f) * s * 0.5f;
        pts[2] = -s * 0.25f;
        pts[3] = 0;
        pts[4] = s * 0.25f;
        pts[5] = 0;
        pts[6] = s * 0.25f + MathF.Cos(-t * 0.3f) * s * 0.5f;
        pts[7] = MathF.Sin(-t * 0.3f) * s * 0.5f;

        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                var fx = x + s * 0.5f + (i * 3 + j) / 9.0f * w + pad;
                var fy = y - s * 0.5f + pad;

                vg.LineCap(LineCaps[i]);
                vg.LineJoin(LineJoins[j]);

                vg.StrokeWidth(s * 0.3f);
                vg.StrokeColor(0, 0, 0, 160);
                vg.BeginPath();
                vg.MoveTo(fx + pts[0], fy + pts[1]);
                vg.LineTo(fx + pts[2], fy + pts[3]);
                vg.LineTo(fx + pts[4], fy + pts[5]);
                vg.LineTo(fx + pts[6], fy + pts[7]);
                vg.Stroke();

                vg.LineCap(NVGlineCap.Butt);
                vg.LineJoin(NVGlineJoin.Bevel);
                vg.StrokeWidth(1.0f);
                vg.StrokeColor(0, 192, 255, 255);
                vg.BeginPath();
                vg.MoveTo(fx + pts[0], fy + pts[1]);
                vg.LineTo(fx + pts[2], fy + pts[3]);
                vg.LineTo(fx + pts[4], fy + pts[5]);
                vg.LineTo(fx + pts[6], fy + pts[7]);
                vg.Stroke();
            }
        }

        vg.Restore();
    }

    private static void DrawWidths(NanoVG vg, float x, float y, float width)
    {
        vg.Save();
        vg.StrokeColor(0, 0, 0, 255);
        for (var i = 0; i < 20; i++)
        {
            var w = (i + 0.5f) * 0.1f;
            vg.StrokeWidth(w);
            vg.BeginPath();
            vg.MoveTo(x, y);
            vg.LineTo(x + width, y + width * 0.3f);
            vg.Stroke();
            y += 10;
        }
        vg.Restore();
    }

    private static void DrawCaps(NanoVG vg, float x, float y, float width)
    {
        var lineWidth = 8.0f;

        vg.Save();
        vg.BeginPath();
        vg.Rect(x - lineWidth / 2, y, width + lineWidth, 40);
        vg.FillColor(255, 255, 255, 32);
        vg.Fill();

        vg.BeginPath();
        vg.Rect(x, y, width, 40);
        vg.FillColor(255, 255, 255, 32);
        vg.Fill();

        vg.StrokeWidth(lineWidth);
        for (var i = 0; i < 3; i++)
        {
            vg.LineCap(LineCaps[i]);
            vg.StrokeColor(0, 0, 0, 255);
            vg.BeginPath();
            vg.MoveTo(x, y + i * 10 + 5);
            vg.LineTo(x + width, y + i * 10 + 5);
            vg.Stroke();
        }
        vg.Restore();
    }

    private static void DrawScissor(NanoVG vg, float x, float y, float t)
    {
        vg.Save();
        vg.Translate(x, y);
        vg.Rotate(5f * (MathF.PI / 180f));
        vg.BeginPath();
        vg.Rect(-20, -20, 60, 40);
        vg.FillColor(255, 0, 0, 255);
        vg.Fill();
        vg.Scissor(-20, -20, 60, 40);

        vg.Translate(40, 0);
        vg.Rotate(t);

        vg.Save();
        vg.ResetScissor();
        vg.BeginPath();
        vg.Rect(-20, -10, 60, 30);
        vg.FillColor(255, 128, 0, 64);
        vg.Fill();
        vg.Restore();

        vg.IntersectScissor(-20, -10, 60, 30);
        vg.BeginPath();
        vg.Rect(-20, -10, 60, 30);
        vg.FillColor(255, 128, 0, 255);
        vg.Fill();

        vg.Restore();
    }

    private static void DrawSlider(NanoVG vg, float x, float y, float w, float h, float pos)
    {
        vg.Save();
        var cy = y + (int)(h * 0.5f);
        float kr = (int)(h * 0.25f);

        var bg = vg.BoxGradient(x, cy - 2, w, 4, 2, 2,
            NVGcolor.RGBA(0, 0, 0, 32), NVGcolor.RGBA(0, 0, 0, 128));

        vg.BeginPath();
        vg.RoundedRect(x, cy - 2, w, 4, 2);
        vg.FillPaint(bg);
        vg.Fill();

        var knobShadow = vg.RadialGradient(x + (int)(pos * w), cy + 1, kr - 3, kr + 3,
            NVGcolor.RGBA(0, 0, 0, 64), NVGcolor.RGBA(0, 0, 0, 0));

        vg.BeginPath();
        vg.Rect(x + (int)(pos * w) - kr - 5, cy - kr - 5, kr * 2 + 10, kr * 2 + 13);
        vg.Circle(x + (int)(pos * w), cy, kr);
        vg.PathWinding(NVGwinding.CW);
        vg.FillPaint(knobShadow);
        vg.Fill();

        var knob = vg.LinearGradient(x, cy - kr, x, cy + kr,
            NVGcolor.RGBA(255, 255, 255, 32), NVGcolor.RGBA(0, 0, 0, 48));

        vg.BeginPath();
        vg.Circle(x + (int)(pos * w), cy, kr - 1);
        vg.FillColor(40, 43, 48, 255);
        vg.Fill();
        vg.FillPaint(knob);
        vg.Fill();

        vg.BeginPath();
        vg.Circle(x + (int)(pos * w), cy, kr - 0.5f);
        vg.StrokeColor(0, 0, 0, 92);
        vg.StrokeWidth(1.0f);
        vg.Stroke();
        vg.Restore();
    }

    private static void DrawThumbnailsNoImages(NanoVG vg, float x, float y, float w, float h, int nimages, float t)
    {
        var cornerRadius = 3.0f;
        var thumb = 60.0f;
        var arry = 30.5f;
        var stackh = nimages / 2 * (thumb + 10) + 10;
        var u = (1 + MathF.Cos(t * 0.5f)) * 0.5f;
        var u2 = (1 - MathF.Cos(t * 0.2f)) * 0.5f;

        vg.Save();

        var shadow = vg.BoxGradient(x, y + 4, w, h, cornerRadius * 2, 20,
            NVGcolor.RGBA(0, 0, 0, 128), NVGcolor.RGBA(0, 0, 0, 0));
        vg.BeginPath();
        vg.Rect(x - 10, y - 10, w + 20, h + 30);
        vg.RoundedRect(x, y, w, h, cornerRadius);
        vg.PathWinding(NVGwinding.CW);
        vg.FillPaint(shadow);
        vg.Fill();

        vg.BeginPath();
        vg.RoundedRect(x, y, w, h, cornerRadius);
        vg.MoveTo(x - 10, y + arry);
        vg.LineTo(x + 1, y + arry - 11);
        vg.LineTo(x + 1, y + arry + 11);
        vg.FillColor(200, 200, 200, 255);
        vg.Fill();

        vg.Save();
        vg.Scissor(x, y, w, h);
        vg.Translate(0, -(stackh - h) * u);

        var dv = 1.0f / MathF.Max(nimages - 1, 1);
        for (var i = 0; i < nimages; i++)
        {
            var tx = x + 10 + i % 2 * (thumb + 10);
            var ty = y + 10 + i / 2 * (thumb + 10);
            var v = i * dv;
            var a = Clamp((u2 - v) / dv, 0, 1);

            if (a < 1.0f)
            {
                DrawSpinner(vg, tx + thumb / 2, ty + thumb / 2, thumb * 0.25f, t);
            }

            var imgPaint = vg.BoxGradient(tx, ty, thumb, thumb, 5, 8,
                NVGcolor.RGBA(255, 255, 255, 64), NVGcolor.RGBA(0, 0, 0, 32));
            vg.BeginPath();
            vg.RoundedRect(tx, ty, thumb, thumb, 5);
            vg.FillPaint(imgPaint);
            vg.Fill();

            var edge = vg.BoxGradient(tx - 1, ty, thumb + 2, thumb + 2, 5, 3,
                NVGcolor.RGBA(0, 0, 0, 128), NVGcolor.RGBA(0, 0, 0, 0));
            vg.BeginPath();
            vg.Rect(tx - 5, ty - 5, thumb + 10, thumb + 10);
            vg.RoundedRect(tx, ty, thumb, thumb, 6);
            vg.PathWinding(NVGwinding.CW);
            vg.FillPaint(edge);
            vg.Fill();

            vg.BeginPath();
            vg.RoundedRect(tx + 0.5f, ty + 0.5f, thumb - 1, thumb - 1, 3.5f);
            vg.StrokeWidth(1.0f);
            vg.StrokeColor(255, 255, 255, 192);
            vg.Stroke();
        }

        vg.Restore();

        var fade = vg.LinearGradient(x, y, x, y + 6, NVGcolor.RGBA(200, 200, 200, 255), NVGcolor.RGBA(200, 200, 200, 0));
        vg.BeginPath();
        vg.Rect(x + 4, y, w - 8, 6);
        vg.FillPaint(fade);
        vg.Fill();

        fade = vg.LinearGradient(x, y + h, x, y + h - 6, NVGcolor.RGBA(200, 200, 200, 255), NVGcolor.RGBA(200, 200, 200, 0));
        vg.BeginPath();
        vg.Rect(x + 4, y + h - 6, w - 8, 6);
        vg.FillPaint(fade);
        vg.Fill();

        var barBg = vg.BoxGradient(x + w - 12 + 1, y + 4 + 1, 8, h - 8, 3, 4,
            NVGcolor.RGBA(0, 0, 0, 32), NVGcolor.RGBA(0, 0, 0, 92));
        vg.BeginPath();
        vg.RoundedRect(x + w - 12, y + 4, 8, h - 8, 3);
        vg.FillPaint(barBg);
        vg.Fill();

        var scrollh = h / stackh * (h - 8);
        var bar = vg.BoxGradient(x + w - 12 - 1, y + 4 + (h - 8 - scrollh) * u - 1, 8, scrollh, 3, 4,
            NVGcolor.RGBA(220, 220, 220, 255), NVGcolor.RGBA(128, 128, 128, 255));
        vg.BeginPath();
        vg.RoundedRect(x + w - 12 + 1, y + 4 + 1 + (h - 8 - scrollh) * u, 6, scrollh - 2, 2);
        vg.FillPaint(bar);
        vg.Fill();

        vg.Restore();
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

    private static void SetClearColor(nint attachment, MTLClearColor color)
    {
        objc_msgSend(attachment, Sel.SetClearColor, color);
    }

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

    private static float GetBackingScaleFactor(nint window)
        => window == nint.Zero ? 1.0f : (float)ObjCRuntime.SendMessageDouble(window, Sel.BackingScaleFactor);

    private static NSSize GetWindowContentSize(nint window)
    {
        if (window == nint.Zero)
        {
            return new NSSize(Width, Height);
        }

        nint contentView = ObjCRuntime.SendMessage(window, Sel.ContentView);
        if (contentView == nint.Zero)
        {
            return new NSSize(Width, Height);
        }

        var bounds = objc_msgSend_rect(contentView, Sel.Bounds);
        return bounds.Size;
    }

    private static NSRect GetWindowContentBounds(nint window)
    {
        if (window == nint.Zero)
        {
            return new NSRect(0, 0, Width, Height);
        }

        nint contentView = ObjCRuntime.SendMessage(window, Sel.ContentView);
        if (contentView == nint.Zero)
        {
            return new NSRect(0, 0, Width, Height);
        }

        return objc_msgSend_rect(contentView, Sel.Bounds);
    }

    private static bool RespondsToSelector(nint obj, nint selector)
        => obj != nint.Zero && ObjCRuntime.SendMessage(obj, Sel.RespondsToSelector, selector) != nint.Zero;

    [LibraryImport("/System/Library/Frameworks/AppKit.framework/AppKit", EntryPoint = "NSApplicationLoad")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool NSApplicationLoad();

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

    [Flags]
    private enum NSViewAutoresizingMask : ulong
    {
        None = 0,
        MinXMargin = 1 << 0,
        WidthSizable = 1 << 1,
        MaxXMargin = 1 << 2,
        MinYMargin = 1 << 3,
        HeightSizable = 1 << 4,
        MaxYMargin = 1 << 5
    }

    private enum NSViewLayerContentsRedrawPolicy : ulong
    {
        Never = 0,
        OnSetNeedsDisplay = 1,
        DuringViewResize = 2
    }

    [Flags]
    private enum CALayerAutoresizingMask : ulong
    {
        None = 0,
        MinXMargin = 1 << 0,
        WidthSizable = 1 << 1,
        MaxXMargin = 1 << 2,
        MinYMargin = 1 << 3,
        HeightSizable = 1 << 4,
        MaxYMargin = 1 << 5
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

    private enum NSApplicationActivationPolicy : ulong
    {
        Regular = 0,
        Accessory = 1,
        Prohibited = 2
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
        public static readonly nint IsVisible = ObjCRuntime.RegisterSelector("isVisible");

        public static readonly nint InitWithFrame = ObjCRuntime.RegisterSelector("initWithFrame:");
        public static readonly nint SetWantsLayer = ObjCRuntime.RegisterSelector("setWantsLayer:");
        public static readonly nint SetLayer = ObjCRuntime.RegisterSelector("setLayer:");
        public static readonly nint SetAutoresizingMask = ObjCRuntime.RegisterSelector("setAutoresizingMask:");
        public static readonly nint SetLayerContentsRedrawPolicy = ObjCRuntime.RegisterSelector("setLayerContentsRedrawPolicy:");

        public static readonly nint SetPixelFormat = ObjCRuntime.RegisterSelector("setPixelFormat:");
        public static readonly nint SetColorPixelFormat = ObjCRuntime.RegisterSelector("setColorPixelFormat:");
        public static readonly nint SetFramebufferOnly = ObjCRuntime.RegisterSelector("setFramebufferOnly:");
        public static readonly nint SetNeedsDisplayOnBoundsChange = ObjCRuntime.RegisterSelector("setNeedsDisplayOnBoundsChange:");
        public static readonly nint SetPresentsWithTransaction = ObjCRuntime.RegisterSelector("setPresentsWithTransaction:");
        public static readonly nint SetAllowsNextDrawableTimeout = ObjCRuntime.RegisterSelector("setAllowsNextDrawableTimeout:");
        public static readonly nint SetNextDrawableTimeout = ObjCRuntime.RegisterSelector("setNextDrawableTimeout:");
        public static readonly nint SetMaximumDrawableCount = ObjCRuntime.RegisterSelector("setMaximumDrawableCount:");
        public static readonly nint SetDisplaySyncEnabled = ObjCRuntime.RegisterSelector("setDisplaySyncEnabled:");
        public static readonly nint SetDrawableSize = ObjCRuntime.RegisterSelector("setDrawableSize:");
        public static readonly nint SetContentsScale = ObjCRuntime.RegisterSelector("setContentsScale:");
        public static readonly nint SetFrame = ObjCRuntime.RegisterSelector("setFrame:");
        public static readonly nint Texture = ObjCRuntime.RegisterSelector("texture");
        public static readonly nint TextureWidth = ObjCRuntime.RegisterSelector("width");
        public static readonly nint TextureHeight = ObjCRuntime.RegisterSelector("height");
        public static readonly nint SetDelegate = ObjCRuntime.RegisterSelector("setDelegate:");
        public static readonly nint DisplayLayer = ObjCRuntime.RegisterSelector("displayLayer:");
        public static readonly nint SetNeedsDisplay = ObjCRuntime.RegisterSelector("setNeedsDisplay");
        public static readonly nint ScheduledTimer = ObjCRuntime.RegisterSelector("scheduledTimerWithTimeInterval:target:selector:userInfo:repeats:");
        public static readonly nint Tick = ObjCRuntime.RegisterSelector("tick:");

        public static readonly nint BackingScaleFactor = ObjCRuntime.RegisterSelector("backingScaleFactor");
        public static readonly nint ContentView = ObjCRuntime.RegisterSelector("contentView");
        public static readonly nint Window = ObjCRuntime.RegisterSelector("window");
        public static readonly nint Layer = ObjCRuntime.RegisterSelector("layer");
        public static readonly nint Bounds = ObjCRuntime.RegisterSelector("bounds");
        public static readonly nint InLiveResize = ObjCRuntime.RegisterSelector("inLiveResize");
        public static readonly nint RespondsToSelector = ObjCRuntime.RegisterSelector("respondsToSelector:");

        public static readonly nint NextEventMatchingMask = ObjCRuntime.RegisterSelector("nextEventMatchingMask:untilDate:inMode:dequeue:");
        public static readonly nint SendEvent = ObjCRuntime.RegisterSelector("sendEvent:");
        public static readonly nint UpdateWindows = ObjCRuntime.RegisterSelector("updateWindows");
        public static readonly nint DateWithTimeIntervalSinceNow = ObjCRuntime.RegisterSelector("dateWithTimeIntervalSinceNow:");

        public static readonly nint SetClearColor = ObjCRuntime.RegisterSelector("setClearColor:");
        public static readonly nint ConvertSizeToBacking = ObjCRuntime.RegisterSelector("convertSizeToBacking:");
        public static readonly nint Present = ObjCRuntime.RegisterSelector("present");
    }
}