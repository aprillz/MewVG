using SkiaSharp;

namespace Skia.Demo.Browser;

// Port of MewVG.Demo.Shared/DemoScene.cs to SKCanvas. The scene issues the same shapes with
// Skia-idiomatic paints; NanoVG's box gradient has no Skia counterpart, so its SDF formula is
// reproduced as an SkSL runtime shader to keep pixel output and per-pixel work comparable.
internal static class SkiaDemoScene
{
    private static readonly SKStrokeCap[] LineCaps = { SKStrokeCap.Butt, SKStrokeCap.Round, SKStrokeCap.Square };
    private static readonly SKStrokeJoin[] LineJoins = { SKStrokeJoin.Miter, SKStrokeJoin.Round, SKStrokeJoin.Bevel };

    private static readonly SKRuntimeEffect _boxGradientEffect = CreateBoxGradientEffect();

    private static SKRuntimeEffect CreateBoxGradientEffect()
    {
        const string source = @"
uniform float2 center;
uniform float2 extent;
uniform float radius;
uniform float feather;
uniform half4 innerColor;
uniform half4 outerColor;

float sdroundrect(float2 pt, float2 ext, float rad) {
    float2 ext2 = ext - float2(rad, rad);
    float2 d = abs(pt) - ext2;
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0)) - rad;
}

half4 main(float2 fragCoord) {
    float d = clamp((sdroundrect(fragCoord - center, extent, radius) + feather * 0.5) / feather, 0.0, 1.0);
    half4 color = mix(innerColor, outerColor, half(d));
    return half4(color.rgb * color.a, color.a);
}";
        var effect = SKRuntimeEffect.CreateShader(source, out var errors);
        if (effect == null)
        {
            throw new InvalidOperationException($"Box gradient shader failed to compile: {errors}");
        }

        return effect;
    }

    private static SKColor Rgba(byte red, byte green, byte blue, byte alpha) => new SKColor(red, green, blue, alpha);

    private static SKColor Hsla(float hue, float saturation, float lightness, byte alpha)
    {
        var wrapped = ((hue % 1f) + 1f) % 1f;
        return SKColor.FromHsl(wrapped * 360f, saturation * 100f, lightness * 100f).WithAlpha(alpha);
    }

    private static SKShader Linear(float x0, float y0, float x1, float y1, SKColor c0, SKColor c1)
        => SKShader.CreateLinearGradient(new SKPoint(x0, y0), new SKPoint(x1, y1), new[] { c0, c1 }, null, SKShaderTileMode.Clamp);

    private static SKShader Radial(float cx, float cy, float innerRadius, float outerRadius, SKColor c0, SKColor c1)
        => SKShader.CreateRadialGradient(new SKPoint(cx, cy), outerRadius, new[] { c0, c1 },
            new[] { innerRadius / outerRadius, 1f }, SKShaderTileMode.Clamp);

    private static SKShader BoxGradient(float x, float y, float w, float h, float radius, float feather, SKColor inner, SKColor outer)
    {
        var uniforms = new SKRuntimeEffectUniforms(_boxGradientEffect)
        {
            ["center"] = new[] { x + w * 0.5f, y + h * 0.5f },
            ["extent"] = new[] { w * 0.5f, h * 0.5f },
            ["radius"] = radius,
            ["feather"] = feather,
            ["innerColor"] = new[] { inner.Red / 255f, inner.Green / 255f, inner.Blue / 255f, inner.Alpha / 255f },
            ["outerColor"] = new[] { outer.Red / 255f, outer.Green / 255f, outer.Blue / 255f, outer.Alpha / 255f },
        };
        return _boxGradientEffect.ToShader(uniforms);
    }

    private static void Fill(SKCanvas canvas, SKPath path, SKColor color)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true };
        canvas.DrawPath(path, paint);
    }

    private static void Fill(SKCanvas canvas, SKPath path, SKShader shader)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, Shader = shader, IsAntialias = true };
        canvas.DrawPath(path, paint);
        shader.Dispose();
    }

    private static void Stroke(SKCanvas canvas, SKPath path, SKColor color, float width,
        SKStrokeCap cap = SKStrokeCap.Butt, SKStrokeJoin join = SKStrokeJoin.Miter)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = width,
            StrokeCap = cap,
            StrokeJoin = join,
            IsAntialias = true,
        };
        canvas.DrawPath(path, paint);
    }

    private static SKPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new SKPath();
        path.AddRoundRect(SKRect.Create(x, y, w, h), r, r);
        return path;
    }

    private static SKPath Rect(float x, float y, float w, float h)
    {
        var path = new SKPath();
        path.AddRect(SKRect.Create(x, y, w, h));
        return path;
    }

    private static SKRect CircleOval(float cx, float cy, float r) => SKRect.Create(cx - r, cy - r, r * 2, r * 2);

    public static void DrawDemo(SKCanvas canvas, float width, float height)
    {
        using (var bg = Rect(0, 0, width, height))
        {
            Fill(canvas, bg, Rgba(28, 30, 34, 128));
        }

        var t = (float)(Environment.TickCount64 % 100000) / 1000.0f;
        var mx = width * 0.5f;
        var my = height * 0.5f;

        DrawEyes(canvas, width - 250, 50, 150, 100, mx, my, t);
        DrawGraph(canvas, 0, height / 2, width, height / 2, t);
        DrawColorWheel(canvas, width - 300, height - 300, 250, 250, t);

        DrawLines(canvas, 120, height - 50, 600, 50, t);
        DrawWidths(canvas, 10, 50, 30);
        DrawCaps(canvas, 10, 300, 30);
        DrawScissor(canvas, 50, height - 80, t);

        float wx = 50, wy = 50, ww = 300, wh = 400;
        DrawWindow(canvas, wx, wy, ww, wh);

        float x = 60, y = 95;
        DrawSearchBox(canvas, x, y, 280, 25);
        y += 40;
        DrawDropDown(canvas, x, y, 280, 28);
        y += 45;

        DrawEditBox(canvas, x, y, 280, 28);
        y += 35;
        DrawEditBox(canvas, x, y, 280, 28);
        y += 38;
        DrawCheckBox(canvas, x, y, 140, 28, true);
        DrawButton(canvas, x + 138, y, 140, 28, Rgba(0, 96, 128, 255));
        y += 45;

        DrawEditBox(canvas, x + 180, y, 100, 28);
        DrawSlider(canvas, x, y, 170, 28, 0.4f);
        y += 55;

        DrawButton(canvas, x, y, 160, 28, Rgba(128, 16, 8, 255));
        DrawButton(canvas, x + 170, y, 110, 28, Rgba(0, 0, 0, 0));

        DrawThumbnailsNoImages(canvas, 365, 95 + 14 - 30, 160, 300, 12, t);
    }

    private static float Clamp(float a, float mn, float mx) => a < mn ? mn : (a > mx ? mx : a);

    private static void DrawWindow(SKCanvas canvas, float x, float y, float w, float h)
    {
        var r = 3.0f;

        using (var body = RoundedRect(x, y, w, h, r))
        {
            Fill(canvas, body, Rgba(28, 30, 34, 192));
        }

        using (var shadowPath = new SKPath { FillType = SKPathFillType.EvenOdd })
        {
            shadowPath.AddRect(SKRect.Create(x - 10, y - 10, w + 20, h + 30));
            shadowPath.AddRoundRect(SKRect.Create(x, y, w, h), r, r);
            Fill(canvas, shadowPath, BoxGradient(x, y + 2, w, h, r * 2, 10, Rgba(0, 0, 0, 128), Rgba(0, 0, 0, 0)));
        }

        using (var header = RoundedRect(x + 1, y + 1, w - 2, 30, r - 1))
        {
            Fill(canvas, header, Linear(x, y, x, y + 15, Rgba(255, 255, 255, 8), Rgba(0, 0, 0, 16)));
        }

        using (var divider = new SKPath())
        {
            divider.MoveTo(x + 0.5f, y + 0.5f + 30);
            divider.LineTo(x + 0.5f + w - 1, y + 0.5f + 30);
            Stroke(canvas, divider, Rgba(0, 0, 0, 32), 1.0f);
        }
    }

    private static void DrawSearchBox(SKCanvas canvas, float x, float y, float w, float h)
    {
        var r = h / 2 - 1;
        using var path = RoundedRect(x, y, w, h, r);
        Fill(canvas, path, BoxGradient(x, y + 1.5f, w, h, h / 2, 5, Rgba(0, 0, 0, 16), Rgba(0, 0, 0, 92)));
    }

    private static void DrawDropDown(SKCanvas canvas, float x, float y, float w, float h)
    {
        var r = 4.0f;
        using (var body = RoundedRect(x + 1, y + 1, w - 2, h - 2, r - 1))
        {
            Fill(canvas, body, Linear(x, y, x, y + h, Rgba(255, 255, 255, 16), Rgba(0, 0, 0, 16)));
        }

        using (var border = RoundedRect(x + 0.5f, y + 0.5f, w - 1, h - 1, r - 0.5f))
        {
            Stroke(canvas, border, Rgba(0, 0, 0, 48), 1.0f);
        }
    }

    private static void DrawEditBox(SKCanvas canvas, float x, float y, float w, float h)
    {
        using (var body = RoundedRect(x + 1, y + 1, w - 2, h - 2, 3))
        {
            Fill(canvas, body, BoxGradient(x + 1, y + 2.5f, w - 2, h - 2, 3, 4, Rgba(255, 255, 255, 32), Rgba(32, 32, 32, 32)));
        }

        using (var border = RoundedRect(x + 0.5f, y + 0.5f, w - 1, h - 1, 3.5f))
        {
            Stroke(canvas, border, Rgba(0, 0, 0, 48), 1.0f);
        }
    }

    private static void DrawCheckBox(SKCanvas canvas, float x, float y, float w, float h, bool checkedOn)
    {
        using (var box = RoundedRect(x + 1, y + (int)(h * 0.5f) - 9, 18, 18, 3))
        {
            Fill(canvas, box, BoxGradient(x + 1, y + (int)(h * 0.5f) - 9 + 1, 18, 18, 3, 3, Rgba(0, 0, 0, 32), Rgba(0, 0, 0, 92)));
        }

        if (checkedOn)
        {
            using var mark = new SKPath();
            mark.MoveTo(x + 4, y + h * 0.5f);
            mark.LineTo(x + 9, y + h * 0.5f + 5);
            mark.LineTo(x + 16, y + h * 0.5f - 6);
            Stroke(canvas, mark, Rgba(255, 255, 255, 180), 2.5f);
        }
    }

    private static void DrawButton(SKCanvas canvas, float x, float y, float w, float h, SKColor col)
    {
        var r = 4.0f;
        var black = col.Red == 0 && col.Green == 0 && col.Blue == 0 && col.Alpha == 0;

        using (var body = RoundedRect(x + 1, y + 1, w - 2, h - 2, r - 1))
        {
            if (!black)
            {
                Fill(canvas, body, col);
            }

            Fill(canvas, body, Linear(x, y, x, y + h,
                Rgba(255, 255, 255, black ? (byte)16 : (byte)32),
                Rgba(0, 0, 0, black ? (byte)16 : (byte)32)));
        }

        using (var border = RoundedRect(x + 0.5f, y + 0.5f, w - 1, h - 1, r - 0.5f))
        {
            Stroke(canvas, border, Rgba(0, 0, 0, 48), 1.0f);
        }
    }

    private static void DrawEyes(SKCanvas canvas, float x, float y, float w, float h, float mx, float my, float t)
    {
        var ex = w * 0.23f;
        var ey = h * 0.5f;
        var lx = x + ex;
        var ly = y + ey;
        var rx = x + w - ex;
        var ry = y + ey;
        var br = (ex < ey ? ex : ey) * 0.5f;
        var blink = 1 - MathF.Pow(MathF.Sin(t * 0.5f), 200) * 0.8f;

        using (var sockets = new SKPath())
        {
            sockets.AddOval(SKRect.Create(lx + 3.0f - ex, ly + 16.0f - ey, ex * 2, ey * 2));
            sockets.AddOval(SKRect.Create(rx + 3.0f - ex, ry + 16.0f - ey, ex * 2, ey * 2));
            Fill(canvas, sockets, Linear(x, y + h * 0.5f, x + w * 0.1f, y + h, Rgba(0, 0, 0, 32), Rgba(0, 0, 0, 16)));
        }

        using (var whites = new SKPath())
        {
            whites.AddOval(SKRect.Create(lx - ex, ly - ey, ex * 2, ey * 2));
            whites.AddOval(SKRect.Create(rx - ex, ry - ey, ex * 2, ey * 2));
            Fill(canvas, whites, Linear(x, y + h * 0.25f, x + w * 0.1f, y + h, Rgba(220, 220, 220, 255), Rgba(128, 128, 128, 255)));
        }

        var dx = (mx - rx) / (ex * 10);
        var dy = (my - ry) / (ey * 10);
        var d = MathF.Sqrt(dx * dx + dy * dy);
        if (d > 1.0f) { dx /= d; dy /= d; }
        dx *= ex * 0.4f; dy *= ey * 0.5f;
        using (var pupil = new SKPath())
        {
            pupil.AddOval(SKRect.Create(lx + dx - br, ly + dy + ey * 0.25f * (1 - blink) - br * blink, br * 2, br * blink * 2));
            Fill(canvas, pupil, Rgba(32, 32, 32, 255));
        }

        using (var pupil = new SKPath())
        {
            pupil.AddOval(SKRect.Create(rx + dx - br, ry + dy + ey * 0.25f * (1 - blink) - br * blink, br * 2, br * blink * 2));
            Fill(canvas, pupil, Rgba(32, 32, 32, 255));
        }

        using (var glossLeft = new SKPath())
        {
            glossLeft.AddOval(SKRect.Create(lx - ex, ly - ey, ex * 2, ey * 2));
            Fill(canvas, glossLeft, Radial(lx - ex * 0.25f, ly - ey * 0.5f, ex * 0.1f, ex * 0.75f, Rgba(255, 255, 255, 128), Rgba(255, 255, 255, 0)));
        }

        using (var glossRight = new SKPath())
        {
            glossRight.AddOval(SKRect.Create(rx - ex, ry - ey, ex * 2, ey * 2));
            Fill(canvas, glossRight, Radial(rx - ex * 0.25f, ry - ey * 0.5f, ex * 0.1f, ex * 0.75f, Rgba(255, 255, 255, 128), Rgba(255, 255, 255, 0)));
        }
    }

    private static void DrawGraph(SKCanvas canvas, float x, float y, float w, float h, float t)
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

        using (var area = new SKPath())
        {
            area.MoveTo(sx[0], sy[0]);
            for (var i = 1; i < 6; i++)
            {
                area.CubicTo(sx[i - 1] + dx * 0.5f, sy[i - 1], sx[i] - dx * 0.5f, sy[i], sx[i], sy[i]);
            }

            area.LineTo(x + w, y + h);
            area.LineTo(x, y + h);
            Fill(canvas, area, Linear(x, y, x, y + h, Rgba(0, 160, 192, 0), Rgba(0, 160, 192, 64)));
        }

        using (var lineShadow = new SKPath())
        {
            lineShadow.MoveTo(sx[0], sy[0] + 2);
            for (var i = 1; i < 6; i++)
            {
                lineShadow.CubicTo(sx[i - 1] + dx * 0.5f, sy[i - 1] + 2, sx[i] - dx * 0.5f, sy[i] + 2, sx[i], sy[i] + 2);
            }

            Stroke(canvas, lineShadow, Rgba(0, 0, 0, 32), 3.0f);
        }

        using (var line = new SKPath())
        {
            line.MoveTo(sx[0], sy[0]);
            for (var i = 1; i < 6; i++)
            {
                line.CubicTo(sx[i - 1] + dx * 0.5f, sy[i - 1], sx[i] - dx * 0.5f, sy[i], sx[i], sy[i]);
            }

            Stroke(canvas, line, Rgba(0, 160, 192, 255), 3.0f);
        }

        for (var i = 0; i < 6; i++)
        {
            using var glow = Rect(sx[i] - 10, sy[i] - 10 + 2, 20, 20);
            Fill(canvas, glow, Radial(sx[i], sy[i] + 2, 3.0f, 8.0f, Rgba(0, 0, 0, 32), Rgba(0, 0, 0, 0)));
        }

        using (var dots = new SKPath())
        {
            for (var i = 0; i < 6; i++)
            {
                dots.AddCircle(sx[i], sy[i], 4.0f);
            }

            Fill(canvas, dots, Rgba(0, 160, 192, 255));
        }

        using (var cores = new SKPath())
        {
            for (var i = 0; i < 6; i++)
            {
                cores.AddCircle(sx[i], sy[i], 2.0f);
            }

            Fill(canvas, cores, Rgba(220, 220, 220, 255));
        }
    }

    private static void DrawSpinner(SKCanvas canvas, float cx, float cy, float r, float t)
    {
        var a0 = 0.0f + t * 6;
        var a1 = MathF.PI + t * 6;
        var r0 = r;
        var r1 = r * 0.75f;
        const float RAD_TO_DEG = 180f / MathF.PI;

        using var ring = new SKPath();
        ring.ArcTo(CircleOval(cx, cy, r0), a0 * RAD_TO_DEG, (a1 - a0) * RAD_TO_DEG, true);
        ring.ArcTo(CircleOval(cx, cy, r1), a1 * RAD_TO_DEG, (a0 - a1) * RAD_TO_DEG, false);
        ring.Close();

        var ax = cx + MathF.Cos(a0) * (r0 + r1) * 0.5f;
        var ay = cy + MathF.Sin(a0) * (r0 + r1) * 0.5f;
        var bx = cx + MathF.Cos(a1) * (r0 + r1) * 0.5f;
        var by = cy + MathF.Sin(a1) * (r0 + r1) * 0.5f;
        Fill(canvas, ring, Linear(ax, ay, bx, by, Rgba(0, 0, 0, 0), Rgba(0, 0, 0, 128)));
    }

    private static void DrawThumbnailsNoImages(SKCanvas canvas, float x, float y, float w, float h, int nimages, float t)
    {
        var cornerRadius = 3.0f;
        var thumb = 60.0f;
        var arry = 30.5f;
        var stackh = nimages / 2 * (thumb + 10) + 10;
        var u = (1 + MathF.Cos(t * 0.5f)) * 0.5f;
        var u2 = (1 - MathF.Cos(t * 0.2f)) * 0.5f;

        using (var shadowPath = new SKPath { FillType = SKPathFillType.EvenOdd })
        {
            shadowPath.AddRect(SKRect.Create(x - 10, y - 10, w + 20, h + 30));
            shadowPath.AddRoundRect(SKRect.Create(x, y, w, h), cornerRadius, cornerRadius);
            Fill(canvas, shadowPath, BoxGradient(x, y + 4, w, h, cornerRadius * 2, 20, Rgba(0, 0, 0, 128), Rgba(0, 0, 0, 0)));
        }

        using (var panel = new SKPath())
        {
            panel.AddRoundRect(SKRect.Create(x, y, w, h), cornerRadius, cornerRadius);
            panel.MoveTo(x - 10, y + arry);
            panel.LineTo(x + 1, y + arry - 11);
            panel.LineTo(x + 1, y + arry + 11);
            Fill(canvas, panel, Rgba(200, 200, 200, 255));
        }

        canvas.Save();
        canvas.ClipRect(SKRect.Create(x, y, w, h));
        canvas.Translate(0, -(stackh - h) * u);

        var dv = 1.0f / MathF.Max(nimages - 1, 1);
        for (var i = 0; i < nimages; i++)
        {
            var tx = x + 10 + i % 2 * (thumb + 10);
            var ty = y + 10 + i / 2 * (thumb + 10);
            var v = i * dv;
            var a = Clamp((u2 - v) / dv, 0, 1);

            if (a < 1.0f)
            {
                DrawSpinner(canvas, tx + thumb / 2, ty + thumb / 2, thumb * 0.25f, t);
            }

            using (var plate = RoundedRect(tx, ty, thumb, thumb, 5))
            {
                Fill(canvas, plate, BoxGradient(tx, ty, thumb, thumb, 5, 8, Rgba(255, 255, 255, 64), Rgba(0, 0, 0, 32)));
            }

            using (var edge = new SKPath { FillType = SKPathFillType.EvenOdd })
            {
                edge.AddRect(SKRect.Create(tx - 5, ty - 5, thumb + 10, thumb + 10));
                edge.AddRoundRect(SKRect.Create(tx, ty, thumb, thumb), 6, 6);
                Fill(canvas, edge, BoxGradient(tx - 1, ty, thumb + 2, thumb + 2, 5, 3, Rgba(0, 0, 0, 128), Rgba(0, 0, 0, 0)));
            }

            using (var border = RoundedRect(tx + 0.5f, ty + 0.5f, thumb - 1, thumb - 1, 3.5f))
            {
                Stroke(canvas, border, Rgba(255, 255, 255, 192), 1.0f);
            }
        }

        canvas.Restore();

        using (var fadeTop = Rect(x + 4, y, w - 8, 6))
        {
            Fill(canvas, fadeTop, Linear(x, y, x, y + 6, Rgba(200, 200, 200, 255), Rgba(200, 200, 200, 0)));
        }

        using (var fadeBottom = Rect(x + 4, y + h - 6, w - 8, 6))
        {
            Fill(canvas, fadeBottom, Linear(x, y + h, x, y + h - 6, Rgba(200, 200, 200, 255), Rgba(200, 200, 200, 0)));
        }

        using (var barTrack = RoundedRect(x + w - 12, y + 4, 8, h - 8, 3))
        {
            Fill(canvas, barTrack, BoxGradient(x + w - 12 + 1, y + 4 + 1, 8, h - 8, 3, 4, Rgba(0, 0, 0, 32), Rgba(0, 0, 0, 92)));
        }

        var scrollh = h / stackh * (h - 8);
        using (var barKnob = RoundedRect(x + w - 12 + 1, y + 4 + 1 + (h - 8 - scrollh) * u, 6, scrollh - 2, 2))
        {
            Fill(canvas, barKnob, BoxGradient(x + w - 12 - 1, y + 4 + (h - 8 - scrollh) * u - 1, 8, scrollh, 3, 4, Rgba(220, 220, 220, 255), Rgba(128, 128, 128, 255)));
        }
    }

    private static void DrawLines(SKCanvas canvas, float x, float y, float w, float h, float t)
    {
        var pad = 5.0f;
        var s = w / 9.0f - pad * 2;
        Span<float> pts = stackalloc float[8];

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

                using (var thick = new SKPath())
                {
                    thick.MoveTo(fx + pts[0], fy + pts[1]);
                    thick.LineTo(fx + pts[2], fy + pts[3]);
                    thick.LineTo(fx + pts[4], fy + pts[5]);
                    thick.LineTo(fx + pts[6], fy + pts[7]);
                    Stroke(canvas, thick, Rgba(0, 0, 0, 160), s * 0.3f, LineCaps[i], LineJoins[j]);
                }

                using (var thin = new SKPath())
                {
                    thin.MoveTo(fx + pts[0], fy + pts[1]);
                    thin.LineTo(fx + pts[2], fy + pts[3]);
                    thin.LineTo(fx + pts[4], fy + pts[5]);
                    thin.LineTo(fx + pts[6], fy + pts[7]);
                    Stroke(canvas, thin, Rgba(0, 192, 255, 255), 1.0f, SKStrokeCap.Butt, SKStrokeJoin.Bevel);
                }
            }
        }
    }

    private static void DrawWidths(SKCanvas canvas, float x, float y, float width)
    {
        for (var i = 0; i < 20; i++)
        {
            var w = (i + 0.5f) * 0.1f;
            using var line = new SKPath();
            line.MoveTo(x, y);
            line.LineTo(x + width, y + width * 0.3f);
            Stroke(canvas, line, Rgba(0, 0, 0, 255), w);
            y += 10;
        }
    }

    private static void DrawCaps(SKCanvas canvas, float x, float y, float width)
    {
        var lineWidth = 8.0f;

        using (var wide = Rect(x - lineWidth / 2, y, width + lineWidth, 40))
        {
            Fill(canvas, wide, Rgba(255, 255, 255, 32));
        }

        using (var tight = Rect(x, y, width, 40))
        {
            Fill(canvas, tight, Rgba(255, 255, 255, 32));
        }

        for (var i = 0; i < 3; i++)
        {
            using var line = new SKPath();
            line.MoveTo(x, y + i * 10 + 5);
            line.LineTo(x + width, y + i * 10 + 5);
            Stroke(canvas, line, Rgba(0, 0, 0, 255), lineWidth, LineCaps[i]);
        }
    }

    private static void DrawScissor(SKCanvas canvas, float x, float y, float t)
    {
        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateDegrees(5f);

        using (var red = Rect(-20, -20, 60, 40))
        {
            Fill(canvas, red, Rgba(255, 0, 0, 255));
        }

        // NanoVG resets the scissor for the translucent rect; Skia cannot un-clip, so the
        // unclipped and clipped draws are issued from separate save levels instead.
        canvas.Save();
        canvas.Translate(40, 0);
        canvas.RotateRadians(t);
        using (var ghost = Rect(-20, -10, 60, 30))
        {
            Fill(canvas, ghost, Rgba(255, 128, 0, 64));
        }

        canvas.Restore();

        canvas.Save();
        canvas.ClipRect(SKRect.Create(-20, -20, 60, 40));
        canvas.Translate(40, 0);
        canvas.RotateRadians(t);
        canvas.ClipRect(SKRect.Create(-20, -10, 60, 30));
        using (var lit = Rect(-20, -10, 60, 30))
        {
            Fill(canvas, lit, Rgba(255, 128, 0, 255));
        }

        canvas.Restore();
        canvas.Restore();
    }

    private static void DrawSlider(SKCanvas canvas, float x, float y, float w, float h, float pos)
    {
        var cy = y + (int)(h * 0.5f);
        float kr = (int)(h * 0.25f);

        using (var track = RoundedRect(x, cy - 2, w, 4, 2))
        {
            Fill(canvas, track, BoxGradient(x, cy - 2, w, 4, 2, 2, Rgba(0, 0, 0, 32), Rgba(0, 0, 0, 128)));
        }

        using (var knobShadow = new SKPath { FillType = SKPathFillType.EvenOdd })
        {
            knobShadow.AddRect(SKRect.Create(x + (int)(pos * w) - kr - 5, cy - kr - 5, kr * 2 + 10, kr * 2 + 13));
            knobShadow.AddCircle(x + (int)(pos * w), cy, kr);
            Fill(canvas, knobShadow, Radial(x + (int)(pos * w), cy + 1, kr - 3, kr + 3, Rgba(0, 0, 0, 64), Rgba(0, 0, 0, 0)));
        }

        using (var knob = new SKPath())
        {
            knob.AddCircle(x + (int)(pos * w), cy, kr - 1);
            Fill(canvas, knob, Rgba(40, 43, 48, 255));
            Fill(canvas, knob, Linear(x, cy - kr, x, cy + kr, Rgba(255, 255, 255, 32), Rgba(0, 0, 0, 48)));
        }

        using (var rim = new SKPath())
        {
            rim.AddCircle(x + (int)(pos * w), cy, kr - 0.5f);
            Stroke(canvas, rim, Rgba(0, 0, 0, 92), 1.0f);
        }
    }

    private static void DrawColorWheel(SKCanvas canvas, float x, float y, float w, float h, float t)
    {
        var cx = x + w * 0.5f;
        var cy = y + h * 0.5f;
        var r1 = (w < h ? w : h) * 0.5f - 5.0f;
        var r0 = r1 - 20.0f;
        var aeps = 0.5f / r1;
        var hue = MathF.Sin(t * 0.12f);
        const float RAD_TO_DEG = 180f / MathF.PI;

        for (var i = 0; i < 6; i++)
        {
            var a0 = i / 6.0f * MathF.PI * 2.0f - aeps;
            var a1 = (i + 1.0f) / 6.0f * MathF.PI * 2.0f + aeps;
            using var segment = new SKPath();
            segment.ArcTo(CircleOval(cx, cy, r0), a0 * RAD_TO_DEG, (a1 - a0) * RAD_TO_DEG, true);
            segment.ArcTo(CircleOval(cx, cy, r1), a1 * RAD_TO_DEG, (a0 - a1) * RAD_TO_DEG, false);
            segment.Close();

            var ax = cx + MathF.Cos(a0) * (r0 + r1) * 0.5f;
            var ay = cy + MathF.Sin(a0) * (r0 + r1) * 0.5f;
            var bx = cx + MathF.Cos(a1) * (r0 + r1) * 0.5f;
            var by = cy + MathF.Sin(a1) * (r0 + r1) * 0.5f;
            Fill(canvas, segment, Linear(ax, ay, bx, by,
                Hsla(a0 / (MathF.PI * 2), 1.0f, 0.55f, 255),
                Hsla(a1 / (MathF.PI * 2), 1.0f, 0.55f, 255)));
        }

        using (var rims = new SKPath())
        {
            rims.AddCircle(cx, cy, r0 - 0.5f);
            rims.AddCircle(cx, cy, r1 + 0.5f);
            Stroke(canvas, rims, Rgba(0, 0, 0, 64), 1.0f);
        }

        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateRadians(hue * MathF.PI * 2);

        using (var marker = Rect(r0 - 1, -3, r1 - r0 + 2, 6))
        {
            Stroke(canvas, marker, Rgba(255, 255, 255, 192), 2.0f);
        }

        using (var markerShadow = new SKPath { FillType = SKPathFillType.EvenOdd })
        {
            markerShadow.AddRect(SKRect.Create(r0 - 2 - 10, -4 - 10, r1 - r0 + 4 + 20, 8 + 20));
            markerShadow.AddRect(SKRect.Create(r0 - 2, -4, r1 - r0 + 4, 8));
            Fill(canvas, markerShadow, BoxGradient(r0 - 3, -5, r1 - r0 + 6, 10, 2, 4, Rgba(0, 0, 0, 128), Rgba(0, 0, 0, 0)));
        }

        var r = r0 - 6;
        var ax2 = MathF.Cos(120.0f / 180.0f * MathF.PI) * r;
        var ay2 = MathF.Sin(120.0f / 180.0f * MathF.PI) * r;
        var bx2 = MathF.Cos(-120.0f / 180.0f * MathF.PI) * r;
        var by2 = MathF.Sin(-120.0f / 180.0f * MathF.PI) * r;
        using (var triangle = new SKPath())
        {
            triangle.MoveTo(r, 0);
            triangle.LineTo(ax2, ay2);
            triangle.LineTo(bx2, by2);
            triangle.Close();
            Fill(canvas, triangle, Linear(r, 0, ax2, ay2, Hsla(hue, 1.0f, 0.5f, 255), Rgba(255, 255, 255, 255)));
            Fill(canvas, triangle, Linear((r + ax2) * 0.5f, (0 + ay2) * 0.5f, bx2, by2, Rgba(0, 0, 0, 0), Rgba(0, 0, 0, 255)));
            Stroke(canvas, triangle, Rgba(0, 0, 0, 64), 1.0f);
        }

        ax2 = MathF.Cos(120.0f / 180.0f * MathF.PI) * r * 0.3f;
        ay2 = MathF.Sin(120.0f / 180.0f * MathF.PI) * r * 0.4f;
        using (var cursor = new SKPath())
        {
            cursor.AddCircle(ax2, ay2, 5);
            Stroke(canvas, cursor, Rgba(255, 255, 255, 192), 2.0f);
        }

        using (var cursorShadow = new SKPath { FillType = SKPathFillType.EvenOdd })
        {
            cursorShadow.AddRect(SKRect.Create(ax2 - 20, ay2 - 20, 40, 40));
            cursorShadow.AddCircle(ax2, ay2, 7);
            Fill(canvas, cursorShadow, Radial(ax2, ay2, 7, 9, Rgba(0, 0, 0, 64), Rgba(0, 0, 0, 0)));
        }

        canvas.Restore();
    }
}
