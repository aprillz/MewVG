// NanoVG shared public API (core wrapper)

namespace Aprillz.MewVG;

public abstract class NanoVG : IDisposable
{
    private bool _disposed;
    private readonly NVGContext _nvg;

    internal NanoVG(INVGRenderer renderer, bool edgeAntiAlias)
    {
        _nvg = new NVGContext(renderer, edgeAntiAlias);
    }

    #region Frame Management

    public void BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio = 1.0f) => _nvg.BeginFrame(windowWidth, windowHeight, devicePixelRatio);

    public void EndFrame() => _nvg.EndFrame();

    public void CancelFrame() => _nvg.CancelFrame();

    #endregion

    #region State Management

    public void Save() => _nvg.Save();

    public void Restore() => _nvg.Restore();

    public void FillColor(NVGcolor color) => _nvg.FillColor(color);

    public void FillColor(byte r, byte g, byte b, byte a = 255) => _nvg.FillColor(NVGcolor.RGBA(r, g, b, a));

    public void FillColor(float r, float g, float b, float a = 1.0f) => _nvg.FillColor(NVGcolor.RGBAf(r, g, b, a));

    public void StrokeColor(NVGcolor color) => _nvg.StrokeColor(color);

    public void StrokeColor(byte r, byte g, byte b, byte a = 255) => _nvg.StrokeColor(NVGcolor.RGBA(r, g, b, a));

    public void StrokeColor(float r, float g, float b, float a = 1.0f) => _nvg.StrokeColor(NVGcolor.RGBAf(r, g, b, a));

    public void StrokeWidth(float width) => _nvg.StrokeWidth(width);

    public void MiterLimit(float limit) => _nvg.MiterLimit(limit);

    public void LineCap(NVGlineCap cap) => _nvg.LineCap(cap);

    public void LineJoin(NVGlineJoin join) => _nvg.LineJoin(join);

    public void GlobalAlpha(float alpha) => _nvg.GlobalAlpha(alpha);

    public void GlobalCompositeOperation(NVGcompositeOperation op) => _nvg.GlobalCompositeOperation(op);

    #endregion

    #region Transforms

    public void ResetTransform() => _nvg.ResetTransform();

    public void Translate(float x, float y) => _nvg.Translate(x, y);

    public void Rotate(float angle) => _nvg.Rotate(angle);

    public void Scale(float x, float y) => _nvg.Scale(x, y);

    public void SkewX(float angle) => _nvg.SkewX(angle);

    public void SkewY(float angle) => _nvg.SkewY(angle);

    #endregion

    #region Scissoring

    public void Scissor(float x, float y, float w, float h) => _nvg.Scissor(x, y, w, h);

    public void ResetScissor() => _nvg.ResetScissor();

    public void IntersectScissor(float x, float y, float w, float h) => _nvg.IntersectScissor(x, y, w, h);

    #endregion

    #region Paths

    public void BeginPath() => _nvg.BeginPath();

    public void ClosePath() => _nvg.ClosePath();

    public void MoveTo(float x, float y) => _nvg.MoveTo(x, y);

    public void LineTo(float x, float y) => _nvg.LineTo(x, y);

    public void BezierTo(float c1x, float c1y, float c2x, float c2y, float x, float y) => _nvg.BezierTo(c1x, c1y, c2x, c2y, x, y);

    public void QuadTo(float cx, float cy, float x, float y) => _nvg.QuadTo(cx, cy, x, y);

    public void ArcTo(float x1, float y1, float x2, float y2, float radius) => _nvg.ArcTo(x1, y1, x2, y2, radius);

    public void Arc(float cx, float cy, float r, float a0, float a1, NVGwinding dir) => _nvg.Arc(cx, cy, r, a0, a1, dir);

    public void PathWinding(NVGwinding dir) => _nvg.PathWinding(dir);

    public void Rect(float x, float y, float w, float h)
    {
        MoveTo(x, y);
        LineTo(x + w, y);
        LineTo(x + w, y + h);
        LineTo(x, y + h);
        ClosePath();
    }

    public void RoundedRect(float x, float y, float w, float h, float r) => _nvg.RoundedRect(x, y, w, h, r);

    public void RoundedRectVarying(float x, float y, float w, float h, float radTopLeft, float radTopRight, float radBottomRight, float radBottomLeft)
        => _nvg.RoundedRectVarying(x, y, w, h, radTopLeft, radTopRight, radBottomRight, radBottomLeft);

    public void Ellipse(float cx, float cy, float rx, float ry) => _nvg.Ellipse(cx, cy, rx, ry);

    public void Circle(float cx, float cy, float r) => _nvg.Circle(cx, cy, r);

    public void Fill() => _nvg.Fill();

    public void Stroke() => _nvg.Stroke();

    #endregion

    #region Images

    public abstract int CreateImageRGBA(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data);

    public abstract int CreateImageAlpha(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data);

    public abstract bool UpdateImage(int image, ReadOnlySpan<byte> data);

    public abstract bool ImageSize(int image, out int width, out int height);

    public abstract void DeleteImage(int image);

    public abstract int CreateImageFromHandle(int textureId, int width, int height, NVGimageFlags flags);

    public abstract int ImageHandle(int image);

    #endregion

    #region Paints

    public NVGpaint LinearGradient(float sx, float sy, float ex, float ey, NVGcolor icol, NVGcolor ocol)
        => _nvg.LinearGradient(sx, sy, ex, ey, icol, ocol);

    public NVGpaint RadialGradient(float cx, float cy, float inr, float outr, NVGcolor icol, NVGcolor ocol)
        => _nvg.RadialGradient(cx, cy, inr, outr, icol, ocol);

    public NVGpaint BoxGradient(float x, float y, float w, float h, float r, float f, NVGcolor icol, NVGcolor ocol)
        => _nvg.BoxGradient(x, y, w, h, r, f, icol, ocol);

    public NVGpaint ImagePattern(float ox, float oy, float ex, float ey, float angle, int image, float alpha)
        => _nvg.ImagePattern(ox, oy, ex, ey, angle, image, alpha);

    public void FillPaint(NVGpaint paint) => _nvg.FillPaint(paint);

    public void StrokePaint(NVGpaint paint) => _nvg.StrokePaint(paint);

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeBackend();
    }

    protected abstract void DisposeBackend();
}