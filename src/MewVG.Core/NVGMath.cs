// NanoVG Math Utilities
// Ported from nanovg.c

using System.Runtime.CompilerServices;

namespace Aprillz.MewVG;

/// <summary>
/// Math helper functions used internally by NanoVG
/// </summary>
internal static class NVGMath
{
    public const float NVG_PI = 3.14159265358979323846264338327f;
    public const float NVG_KAPPA90 = 0.5522847493f; // Length proportional to radius of a cubic bezier handle for 90deg arcs.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sqrtf(float a) => MathF.Sqrt(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Modf(float a, float b) => a % b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sinf(float a) => MathF.Sin(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cosf(float a) => MathF.Cos(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Tanf(float a) => MathF.Tan(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Atan2f(float a, float b) => MathF.Atan2(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Acosf(float a) => MathF.Acos(a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Mini(int a, int b) => a < b ? a : b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Maxi(int a, int b) => a > b ? a : b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clampi(int a, int mn, int mx) => a < mn ? mn : (a > mx ? mx : a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Minf(float a, float b) => a < b ? a : b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Maxf(float a, float b) => a > b ? a : b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Absf(float a) => a >= 0.0f ? a : -a;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Signf(float a) => a >= 0.0f ? 1.0f : -1.0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clampf(float a, float mn, float mx) => a < mn ? mn : (a > mx ? mx : a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cross(float dx0, float dy0, float dx1, float dy1) => dx1 * dy0 - dx0 * dy1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Normalize(ref float x, ref float y)
    {
        var d = Sqrtf(x * x + y * y);
        if (d > 1e-6f)
        {
            var id = 1.0f / d;
            x *= id;
            y *= id;
        }
        return d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DegToRad(float deg) => deg / 180.0f * NVG_PI;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float RadToDeg(float rad) => rad / NVG_PI * 180.0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool PtEquals(float x1, float y1, float x2, float y2, float tol)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return dx * dx + dy * dy < tol * tol;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DistPtSeg(float x, float y, float px, float py, float qx, float qy)
    {
        var pqx = qx - px;
        var pqy = qy - py;
        var dx = x - px;
        var dy = y - py;
        var d = pqx * pqx + pqy * pqy;
        var t = pqx * dx + pqy * dy;
        if (d > 0)
        {
            t /= d;
        }

        if (t < 0)
        {
            t = 0;
        }
        else if (t > 1)
        {
            t = 1;
        }

        dx = px + t * pqx - x;
        dy = py + t * pqy - y;
        return dx * dx + dy * dy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetAverageScale(ReadOnlySpan<float> t)
    {
        var sx = Sqrtf(t[0] * t[0] + t[2] * t[2]);
        var sy = Sqrtf(t[1] * t[1] + t[3] * t[3]);
        return (sx + sy) * 0.5f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float TriArea2(float ax, float ay, float bx, float by, float cx, float cy)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var acx = cx - ax;
        var acy = cy - ay;
        return acx * aby - abx * acy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CurveDivs(float r, float arc, float tol)
    {
        var da = Acosf(r / (r + tol)) * 2.0f;
        return Maxi(2, (int)MathF.Ceiling(arc / da));
    }

    #region Transform Operations

    public static void TransformIdentity(Span<float> t)
    {
        t[0] = 1.0f; t[1] = 0.0f;
        t[2] = 0.0f; t[3] = 1.0f;
        t[4] = 0.0f; t[5] = 0.0f;
    }

    public static unsafe void TransformIdentity(float* t)
    {
        t[0] = 1.0f; t[1] = 0.0f;
        t[2] = 0.0f; t[3] = 1.0f;
        t[4] = 0.0f; t[5] = 0.0f;
    }

    public static void TransformTranslate(Span<float> t, float tx, float ty)
    {
        t[0] = 1.0f; t[1] = 0.0f;
        t[2] = 0.0f; t[3] = 1.0f;
        t[4] = tx; t[5] = ty;
    }

    public static void TransformScale(Span<float> t, float sx, float sy)
    {
        t[0] = sx; t[1] = 0.0f;
        t[2] = 0.0f; t[3] = sy;
        t[4] = 0.0f; t[5] = 0.0f;
    }

    public static void TransformRotate(Span<float> t, float a)
    {
        var cs = Cosf(a);
        var sn = Sinf(a);
        t[0] = cs; t[1] = sn;
        t[2] = -sn; t[3] = cs;
        t[4] = 0.0f; t[5] = 0.0f;
    }

    public static void TransformSkewX(Span<float> t, float a)
    {
        t[0] = 1.0f; t[1] = 0.0f;
        t[2] = Tanf(a); t[3] = 1.0f;
        t[4] = 0.0f; t[5] = 0.0f;
    }

    public static void TransformSkewY(Span<float> t, float a)
    {
        t[0] = 1.0f; t[1] = Tanf(a);
        t[2] = 0.0f; t[3] = 1.0f;
        t[4] = 0.0f; t[5] = 0.0f;
    }

    public static void TransformMultiply(Span<float> t, ReadOnlySpan<float> s)
    {
        var t0 = t[0] * s[0] + t[1] * s[2];
        var t2 = t[2] * s[0] + t[3] * s[2];
        var t4 = t[4] * s[0] + t[5] * s[2] + s[4];
        t[1] = t[0] * s[1] + t[1] * s[3];
        t[3] = t[2] * s[1] + t[3] * s[3];
        t[5] = t[4] * s[1] + t[5] * s[3] + s[5];
        t[0] = t0;
        t[2] = t2;
        t[4] = t4;
    }

    public static void TransformPremultiply(Span<float> t, ReadOnlySpan<float> s)
    {
        Span<float> s2 = stackalloc float[6];
        s.CopyTo(s2);
        TransformMultiply(s2, t);
        s2.CopyTo(t);
    }

    public static bool TransformInverse(Span<float> inv, ReadOnlySpan<float> t)
    {
        var det = (double)t[0] * t[3] - (double)t[2] * t[1];
        if (det > -1e-6 && det < 1e-6)
        {
            TransformIdentity(inv);
            return false;
        }
        var invdet = 1.0 / det;
        inv[0] = (float)(t[3] * invdet);
        inv[2] = (float)(-t[2] * invdet);
        inv[4] = (float)(((double)t[2] * t[5] - (double)t[3] * t[4]) * invdet);
        inv[1] = (float)(-t[1] * invdet);
        inv[3] = (float)(t[0] * invdet);
        inv[5] = (float)(((double)t[1] * t[4] - (double)t[0] * t[5]) * invdet);
        return true;
    }

    public static void TransformPoint(out float dx, out float dy, ReadOnlySpan<float> t, float sx, float sy)
    {
        dx = sx * t[0] + sy * t[2] + t[4];
        dy = sx * t[1] + sy * t[3] + t[5];
    }

    #endregion
}