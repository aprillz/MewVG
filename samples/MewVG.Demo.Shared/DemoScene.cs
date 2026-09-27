using Aprillz.MewVG;

namespace MewVG.Demo;

internal static class DemoScene
{
    private static readonly NVGlineJoin[] LineJoins = { NVGlineJoin.Miter, NVGlineJoin.Round, NVGlineJoin.Bevel };
    private static readonly NVGlineCap[] LineCaps = { NVGlineCap.Butt, NVGlineCap.Round, NVGlineCap.Square };

    public static void DrawDemo(NanoVG vg, float width, float height)
    {
        // Background
        vg.BeginPath();
        vg.Rect(0, 0, width, height);
        vg.FillColor(28, 30, 34, 128);
        vg.Fill();

        var t = FixedTime ?? (float)(Environment.TickCount64 % 100000) / 1000.0f;
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

    // MEWVG_DEMO_TIME freezes the animation clock so two runs draw the same frame (capture comparison).
    private static readonly float? FixedTime =
        float.TryParse(Environment.GetEnvironmentVariable("MEWVG_DEMO_TIME"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var fixedTime) ? fixedTime : null;

    // MEWVG_DEMO_FILL_LAYERS=<n> adds n translucent window-sized fills per frame (fill-rate benchmark).
    private static readonly int FillLayers =
        int.TryParse(Environment.GetEnvironmentVariable("MEWVG_DEMO_FILL_LAYERS"), out var fillLayers) ? fillLayers : 0;

    private static NanoVG? _checkImagesOwner;
    private static int _premultipliedImage;
    private static int _straightImage;
    private static int _alphaImage;
    private static int _gradientLut;

    /// <summary>
    /// Draws one of each paint the GL backend can route to a shader variant (solid, image of every
    /// texture kind, gradients, coverage mask fill), each plain, scissored, rotated, clipped and
    /// translucent, so a capture exercises every variant against the uber shader.
    /// </summary>
    public static void DrawShaderVariantChecks(NanoVG vg, float x, float y)
    {
        EnsureCheckImages(vg);

        int[] images = { 0, _premultipliedImage, _straightImage, _alphaImage };
        for (var row = 0; row < images.Length; row++)
        {
            for (var column = 0; column < 6; column++)
            {
                var cx = x + column * 130;
                var cy = y + row * 90;
                vg.Save();
                switch (column)
                {
                    case 1:
                        vg.Scissor(cx + 10, cy + 10, 70, 45);
                        break;
                    case 2:
                        vg.Translate(cx + 55, cy + 35);
                        vg.Rotate(0.35f);
                        vg.Translate(-(cx + 55), -(cy + 35));
                        vg.Scissor(cx + 5, cy + 5, 90, 55);
                        break;
                    case 3:
                        vg.BeginPath();
                        vg.Circle(cx + 55, cy + 35, 32);
                        vg.Clip();
                        break;
                    case 4:
                        vg.GlobalAlpha(0.45f);
                        break;
                    case 5:
                        vg.Scissor(cx + 15, cy, 80, 70);
                        vg.BeginPath();
                        vg.Ellipse(cx + 55, cy + 35, 50, 28);
                        vg.Clip();
                        vg.GlobalAlpha(0.7f);
                        break;
                }

                vg.BeginPath();
                vg.RoundedRect(cx, cy, 110, 70, 12);
                if (images[row] == 0)
                {
                    vg.FillColor(40, 170, 220, 200);
                }
                else
                {
                    vg.FillPaint(vg.ImagePattern(cx, cy, 32, 32, 0.0f, images[row], 1.0f));
                }

                vg.Fill();

                vg.BeginPath();
                vg.Circle(cx + 20, cy + 55, 9);
                vg.StrokeColor(250, 220, 40, 230);
                vg.StrokeWidth(2.5f);
                vg.Stroke();

                if (column == 3 || column == 5)
                {
                    vg.ResetClip();
                }

                vg.Restore();
            }
        }

        // Gradient paints: LUT linear (each spread), LUT radial with a focal point, and box gradients,
        // plain, scissored, inside a clip nested in another clip, and translucent.
        for (var column = 0; column < 6; column++)
        {
            var cx = x + column * 130;
            var cy = y + 4 * 90;
            vg.Save();
            switch (column)
            {
                case 3:
                    vg.Scissor(cx + 12, cy + 8, 80, 50);
                    break;
                case 4:
                    vg.BeginPath();
                    vg.Circle(cx + 55, cy + 35, 38);
                    vg.Clip();
                    vg.BeginPath();
                    vg.Rect(cx + 30, cy, 60, 70);
                    vg.Clip();
                    break;
                case 5:
                    vg.GlobalAlpha(0.5f);
                    break;
            }

            var gradient = System.Numerics.Matrix3x2.Identity;
            for (var band = 0; band < 3; band++)
            {
                vg.BeginPath();
                vg.Rect(cx, cy + band * 24, 110, 22);
                vg.FillPaint(band switch
                {
                    0 => vg.GradientLinear(gradient, cx + 30, cy, cx + 70, cy, column % 3, _gradientLut),
                    1 => vg.GradientRadial(gradient, cx + 55, cy + 35, cx + 40, cy + 30, 30, 20, column % 3, _gradientLut),
                    _ => vg.BoxGradient(cx + 10, cy + 50, 90, 14, 6, 10, NVGcolor.RGBA(250, 250, 250, 220), NVGcolor.RGBA(20, 60, 140, 160)),
                });
                vg.Fill();
            }

            if (column == 4)
            {
                vg.ResetClip();
            }

            vg.Restore();
        }

        // A dense self-intersecting polygon, which the core rasterizes into a coverage mask.
        // Passes: solid, image, then both again under a scissor and a clip.
        for (var pass = 0; pass < 4; pass++)
        {
            var cx = x + 820 + pass % 2 * 170;
            var cy = y + 90 + pass / 2 * 170;
            vg.Save();
            if (pass >= 2)
            {
                vg.Scissor(cx - 70, cy - 60, 150, 110);
                vg.BeginPath();
                vg.Circle(cx, cy, 62);
                vg.Clip();
            }

            vg.BeginPath();
            const int POINTS = 3000;
            for (var i = 0; i < POINTS; i++)
            {
                var angle = i * MathF.PI * 2 * 7 / POINTS;
                var radius = 75 * (0.55f + 0.45f * MathF.Sin(i * 0.37f));
                var px = cx + MathF.Cos(angle) * radius;
                var py = cy + MathF.Sin(angle) * radius;
                if (i == 0)
                {
                    vg.MoveTo(px, py);
                }
                else
                {
                    vg.LineTo(px, py);
                }
            }

            vg.ClosePath();
            if (pass % 2 == 0)
            {
                vg.FillColor(230, 90, 60, 220);
            }
            else
            {
                vg.FillPaint(vg.ImagePattern(cx, cy, 24, 24, 0.0f, _straightImage, 1.0f));
            }

            vg.Fill();
            if (pass >= 2)
            {
                vg.ResetClip();
            }

            vg.Restore();
        }
    }

    /// <summary>
    /// Draws the MEWVG_DEMO_FILL_LAYERS window-sized fills, alternating a solid colour and an image
    /// pattern, so a frame is bound by per-pixel shading rather than by draw submission.
    /// </summary>
    public static void DrawFillLayers(NanoVG vg, float width, float height)
    {
        if (FillLayers <= 0)
        {
            return;
        }

        EnsureCheckImages(vg);
        for (var layer = 0; layer < FillLayers; layer++)
        {
            vg.BeginPath();
            vg.Rect(0, 0, width, height);
            if (layer % 2 == 0)
            {
                vg.FillColor(20, 40, 80, 8);
            }
            else
            {
                vg.FillPaint(vg.ImagePattern(0, 0, 64, 64, 0.0f, _straightImage, 0.03f));
            }

            vg.Fill();
        }
    }

    private static void EnsureCheckImages(NanoVG vg)
    {
        if (ReferenceEquals(_checkImagesOwner, vg))
        {
            return;
        }

        _checkImagesOwner = vg;
        const int SIZE = 32;
        var rgba = new byte[SIZE * SIZE * 4];
        var alpha = new byte[SIZE * SIZE];
        for (var py = 0; py < SIZE; py++)
        {
            for (var px = 0; px < SIZE; px++)
            {
                var index = py * SIZE + px;
                var on = ((px / 8) + (py / 8)) % 2 == 0;
                var a = (byte)(on ? 255 : 110);
                rgba[index * 4] = (byte)(on ? 240 : 30);
                rgba[index * 4 + 1] = (byte)(px * 8);
                rgba[index * 4 + 2] = (byte)(py * 8);
                rgba[index * 4 + 3] = a;
                alpha[index] = (byte)((px + py) * 4);
            }
        }

        var premultiplied = (byte[])rgba.Clone();
        for (var i = 0; i < premultiplied.Length; i += 4)
        {
            var a = premultiplied[i + 3];
            premultiplied[i] = (byte)(premultiplied[i] * a / 255);
            premultiplied[i + 1] = (byte)(premultiplied[i + 1] * a / 255);
            premultiplied[i + 2] = (byte)(premultiplied[i + 2] * a / 255);
        }

        var repeat = NVGimageFlags.RepeatX | NVGimageFlags.RepeatY;
        _premultipliedImage = vg.CreateImageRGBA(SIZE, SIZE, repeat | NVGimageFlags.Premultiplied, premultiplied);
        _straightImage = vg.CreateImageRGBA(SIZE, SIZE, repeat, rgba);
        _alphaImage = vg.CreateImageAlpha(SIZE, SIZE, repeat, alpha);

        // Gradient lookup table (premultiplied, one row) as the LUT gradient paints expect.
        const int LUT_SIZE = 256;
        var lut = new byte[LUT_SIZE * 4];
        for (var i = 0; i < LUT_SIZE; i++)
        {
            var a = (byte)(120 + i / 2);
            lut[i * 4] = (byte)(i * a / 255);
            lut[i * 4 + 1] = (byte)((255 - i) * a / 255);
            lut[i * 4 + 2] = (byte)(90 * a / 255);
            lut[i * 4 + 3] = a;
        }

        _gradientLut = vg.CreateImageRGBA(LUT_SIZE, 1, NVGimageFlags.Premultiplied, lut);
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
}
