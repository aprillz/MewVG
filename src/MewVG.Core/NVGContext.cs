// NanoVG Internal Context
// Ported from nanovg.c
// This file contains the core path rendering, state management, and tessellation logic

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

using Aprillz.MewVG.Tess;

using static Aprillz.MewVG.NVGMath;

namespace Aprillz.MewVG;

#region Internal Types

/// <summary>
/// Path command types
/// </summary>
internal enum NVGcommands
{
    MoveTo = 0,
    LineTo = 1,
    BezierTo = 2,
    Close = 3,
    Winding = 4,
}

/// <summary>
/// Point flags
/// </summary>
[Flags]
internal enum NVGpointFlags : byte
{
    Corner = 0x01,
    Left = 0x02,
    Bevel = 0x04,
    InnerBevel = 0x08,
}

/// <summary>
/// Internal point structure for path tessellation
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NVGpoint
{
    public float X, Y;
    public float DX, DY;
    public float Len;
    public float DMX, DMY;
    public NVGpointFlags Flags;
}

/// <summary>
/// Internal state structure
/// </summary>
internal unsafe struct NVGstate
{
    public NVGcompositeOperationState CompositeOperation;
    public bool ShapeAntiAlias;
    public NVGfillRule FillRule;
    public NVGpaint Fill;
    public NVGpaint Stroke;
    public float StrokeWidth;
    public float MiterLimit;
    public NVGlineCap LineCap;
    public NVGlineJoin LineJoin;
    public float Alpha;
    public Buffer6<float> Xform; // [6]
    public NVGscissorState Scissor;
    public float FontSize;
    public float LetterSpacing;
    public float LineHeight;
    public float FontBlur;
    public NVGalign TextAlign;
    public int FontId;
    public int ClipDepth;
}

[InlineArray(12)]
public struct Buffer12<T>
{
    private T _element0;
}

[InlineArray(2)]
public struct Buffer2<T>
{
    private T _element0;
}

/// <summary>

[InlineArray(6)]
public struct Buffer6<T>
{
    private T _element0;
}

/// <summary>
/// Internal scissor state
/// </summary>
internal struct NVGscissorState
{
    public Buffer6<float> Xform; // [6]
    public Buffer2<float> Extent; // [2]

    public NVGscissorState()
    {
        Xform = default;
        Extent = default;
        Extent[0] = -1.0f;
        Extent[1] = -1.0f;
    }
}

/// <summary>
/// Internal path cache for tessellation
/// </summary>
internal class NVGpathCache
{
    public NVGpoint[] Points;
    public int NPoints;
    public int CPoints;

    public NVGpathData[] Paths;
    public int NPaths;
    public int CPaths;

    public NVGvertex[] Verts;
    public int NVerts;
    public int CVerts;

    public float[] Bounds; // [4]

    public NVGpathCache()
    {
        Points = new NVGpoint[128];
        CPoints = 128;
        NPoints = 0;

        Paths = new NVGpathData[16];
        CPaths = 16;
        NPaths = 0;

        Verts = new NVGvertex[256];
        CVerts = 256;
        NVerts = 0;

        Bounds = new float[4];
    }
}

/// <summary>
/// Internal path data structure
/// </summary>
internal struct NVGpathData
{
    public int First;
    public int Count;
    public bool Closed;
    public int NBevel;
    public int FillOffset;
    public int NFill;
    public int StrokeOffset;
    public int NStroke;
    public NVGwinding Winding;
    public bool Convex;
}

#endregion

/// <summary>
/// Internal NanoVG context that implements path rendering from nanovg.c
/// </summary>
internal sealed class NVGContext
{
    private const int NVG_MAX_STATES = 32;
    private const int NVG_INIT_COMMANDS_SIZE = 256;
    private const float EDGE_AA_FRINGE_PX = 1f;

    // Render backend
    private readonly INVGRenderer _renderer;

    private readonly bool _edgeAntiAlias;
    private readonly bool _forceCoverageAaFringe = false;

    // Commands
    private float[] _commands;

    private int _ncommands;
    private int _ccommands;
    private float _commandx, _commandy;

    // State stack
    private readonly NVGstate[] _states;

    private int _nstates;

    // Path cache
    private readonly NVGpathCache _cache;
    private readonly Tessellator _fillTessellator = new();

    private readonly List<NVGClipPath> _clipStack = new();

    // Tolerances
    private float _tessTol;

    private float _distTol;
    private float _fringeWidth;
    private float _devicePxRatio;

    /// <summary>Current bezier flatten tolerance (depends on devicePxRatio).</summary>
    public float TessTol => _tessTol;

    // Statistics
    public int DrawCallCount;

    public int FillTriCount;
    public int StrokeTriCount;
    public int TextTriCount;

    private readonly struct NVGClipPath
    {
        public NVGClipPath(NVGscissorState scissor, float fringe, float[] bounds, NVGpathData[] paths, NVGvertex[] verts)
        {
            Scissor = scissor;
            Fringe = fringe;
            Bounds = bounds;
            Paths = paths;
            Verts = verts;
        }

        public NVGscissorState Scissor { get; }

        public float Fringe { get; }

        public float[] Bounds { get; }

        public NVGpathData[] Paths { get; }

        public NVGvertex[] Verts { get; }
    }

    public NVGContext(INVGRenderer renderer, bool edgeAntiAlias)
    {
        _renderer = renderer;
        _edgeAntiAlias = edgeAntiAlias;

        _commands = new float[NVG_INIT_COMMANDS_SIZE];
        _ccommands = NVG_INIT_COMMANDS_SIZE;
        _ncommands = 0;

        _states = new NVGstate[NVG_MAX_STATES];
        for (var i = 0; i < NVG_MAX_STATES; i++)
        {
            _states[i].Scissor = new NVGscissorState();
        }
        _nstates = 0;

        _cache = new NVGpathCache();

        SetDevicePixelRatio(1.0f);

        Save();
        Reset();
    }

    #region Device Pixel Ratio

    private void SetDevicePixelRatio(float ratio)
    {
        _tessTol = 0.25f / ratio;
        _distTol = 0.01f / ratio;
        _fringeWidth = EDGE_AA_FRINGE_PX / ratio;
        _devicePxRatio = ratio;
    }

    #endregion

    #region State Management

    private ref NVGstate GetState() => ref _states[_nstates - 1];

    public void Save()
    {
        if (_nstates >= NVG_MAX_STATES)
        {
            return;
        }

        if (_nstates > 0)
        {
            // Copy current state
            _states[_nstates] = _states[_nstates - 1];
            _states[_nstates].Xform = _states[_nstates - 1].Xform;
            _states[_nstates].Scissor.Xform = _states[_nstates - 1].Scissor.Xform;
            _states[_nstates].Scissor.Extent = _states[_nstates - 1].Scissor.Extent;
            ClonePaint(ref _states[_nstates].Fill, in _states[_nstates - 1].Fill);
            ClonePaint(ref _states[_nstates].Stroke, in _states[_nstates - 1].Stroke);
            _states[_nstates].ClipDepth = _states[_nstates - 1].ClipDepth;
        }
        _nstates++;
    }

    public void Restore()
    {
        if (_nstates <= 1)
        {
            return;
        }

        _nstates--;

        var desiredClipDepth = _states[_nstates - 1].ClipDepth;
        if (_clipStack.Count > desiredClipDepth)
        {
            _clipStack.RemoveRange(desiredClipDepth, _clipStack.Count - desiredClipDepth);
            _renderer.ResetClip();
            for (var i = 0; i < _clipStack.Count; i++)
            {
                var clip = _clipStack[i];
                var scissor = clip.Scissor;
                _renderer.RenderClip(ref scissor, clip.Fringe, clip.Bounds, clip.Paths, clip.Verts);
            }
        }
    }

    public void Reset()
    {
        ref var state = ref GetState();

        state.Fill = default;
        SetPaintColor(ref state.Fill, NVGcolor.RGBA(255, 255, 255, 255));

        state.Stroke = default;
        SetPaintColor(ref state.Stroke, NVGcolor.RGBA(0, 0, 0, 255));

        state.CompositeOperation = CompositeOperationState(NVGcompositeOperation.SourceOver);
        state.ShapeAntiAlias = true;
        state.FillRule = NVGfillRule.NonZero;
        state.StrokeWidth = 1.0f;
        state.MiterLimit = 10.0f;
        state.LineCap = NVGlineCap.Butt;
        state.LineJoin = NVGlineJoin.Miter;
        state.Alpha = 1.0f;

        TransformIdentity(state.Xform);

        state.Scissor.Extent[0] = -1.0f;
        state.Scissor.Extent[1] = -1.0f;

        state.FontSize = 16.0f;
        state.LetterSpacing = 0.0f;
        state.LineHeight = 1.0f;
        state.FontBlur = 0.0f;
        state.TextAlign = NVGalign.Left | NVGalign.Baseline;
        state.FontId = 0;
        state.ClipDepth = 0;

        if (_clipStack.Count > 0)
        {
            _renderer.ResetClip();
            _clipStack.Clear();
        }
    }

    private static void SetPaintColor(ref NVGpaint p, NVGcolor color)
    {
        p = default;
        TransformIdentity(p.Xform);
        p.Radius = 0.0f;
        p.Feather = 1.0f;
        p.InnerColor = color;
        p.OuterColor = color;
    }

    private static void ClonePaint(ref NVGpaint dst, in NVGpaint src)
    {
        dst = src;
        dst.Xform = src.Xform;
        dst.Extent = src.Extent;
    }

    private static NVGcompositeOperationState CompositeOperationState(NVGcompositeOperation op)
    {
        int sfactor, dfactor;

        switch (op)
        {
            case NVGcompositeOperation.SourceOver:
                sfactor = (int)NVGblendFactor.One;
                dfactor = (int)NVGblendFactor.OneMinusSrcAlpha;
                break;

            case NVGcompositeOperation.SourceIn:
                sfactor = (int)NVGblendFactor.DstAlpha;
                dfactor = (int)NVGblendFactor.Zero;
                break;

            case NVGcompositeOperation.SourceOut:
                sfactor = (int)NVGblendFactor.OneMinusDstAlpha;
                dfactor = (int)NVGblendFactor.Zero;
                break;

            case NVGcompositeOperation.Atop:
                sfactor = (int)NVGblendFactor.DstAlpha;
                dfactor = (int)NVGblendFactor.OneMinusSrcAlpha;
                break;

            case NVGcompositeOperation.DestinationOver:
                sfactor = (int)NVGblendFactor.OneMinusDstAlpha;
                dfactor = (int)NVGblendFactor.One;
                break;

            case NVGcompositeOperation.DestinationIn:
                sfactor = (int)NVGblendFactor.Zero;
                dfactor = (int)NVGblendFactor.SrcAlpha;
                break;

            case NVGcompositeOperation.DestinationOut:
                sfactor = (int)NVGblendFactor.Zero;
                dfactor = (int)NVGblendFactor.OneMinusSrcAlpha;
                break;

            case NVGcompositeOperation.DestinationAtop:
                sfactor = (int)NVGblendFactor.OneMinusDstAlpha;
                dfactor = (int)NVGblendFactor.SrcAlpha;
                break;

            case NVGcompositeOperation.Lighter:
                sfactor = (int)NVGblendFactor.One;
                dfactor = (int)NVGblendFactor.One;
                break;

            case NVGcompositeOperation.Copy:
                sfactor = (int)NVGblendFactor.One;
                dfactor = (int)NVGblendFactor.Zero;
                break;

            case NVGcompositeOperation.Xor:
                sfactor = (int)NVGblendFactor.OneMinusDstAlpha;
                dfactor = (int)NVGblendFactor.OneMinusSrcAlpha;
                break;

            default:
                sfactor = (int)NVGblendFactor.One;
                dfactor = (int)NVGblendFactor.Zero;
                break;
        }

        return new NVGcompositeOperationState
        {
            SrcRGB = sfactor,
            DstRGB = dfactor,
            SrcAlpha = sfactor,
            DstAlpha = dfactor
        };
    }

    #endregion

    #region State Setters

    public void ShapeAntiAlias(bool enabled)
    {
        ref var state = ref GetState();
        state.ShapeAntiAlias = enabled;
    }

    public void FillRule(NVGfillRule rule)
    {
        ref var state = ref GetState();
        state.FillRule = rule;
    }

    public void StrokeWidth(float width)
    {
        ref var state = ref GetState();
        state.StrokeWidth = width;
    }

    public void MiterLimit(float limit)
    {
        ref var state = ref GetState();
        state.MiterLimit = limit;
    }

    public void LineCap(NVGlineCap cap)
    {
        ref var state = ref GetState();
        state.LineCap = cap;
    }

    public void LineJoin(NVGlineJoin join)
    {
        ref var state = ref GetState();
        state.LineJoin = join;
    }

    public void GlobalAlpha(float alpha)
    {
        ref var state = ref GetState();
        state.Alpha = alpha;
    }

    public void GlobalCompositeOperation(NVGcompositeOperation op)
    {
        ref var state = ref GetState();
        state.CompositeOperation = CompositeOperationState(op);
    }

    public void GlobalCompositeBlendFunc(int sfactor, int dfactor) => GlobalCompositeBlendFuncSeparate(sfactor, dfactor, sfactor, dfactor);

    public void GlobalCompositeBlendFuncSeparate(int srcRGB, int dstRGB, int srcAlpha, int dstAlpha)
    {
        ref var state = ref GetState();
        state.CompositeOperation = new NVGcompositeOperationState
        {
            SrcRGB = srcRGB,
            DstRGB = dstRGB,
            SrcAlpha = srcAlpha,
            DstAlpha = dstAlpha
        };
    }

    #endregion

    #region Transform

    public void ResetTransform()
    {
        ref var state = ref GetState();
        TransformIdentity(state.Xform);
    }

    public void Transform(float a, float b, float c, float d, float e, float f)
    {
        ref var state = ref GetState();
        Span<float> t = stackalloc float[6] { a, b, c, d, e, f };
        TransformPremultiply(state.Xform, t);
    }

    public void Translate(float x, float y)
    {
        ref var state = ref GetState();
        Span<float> t = stackalloc float[6];
        TransformTranslate(t, x, y);
        TransformPremultiply(state.Xform, t);
    }

    public void Rotate(float angle)
    {
        ref var state = ref GetState();
        Span<float> t = stackalloc float[6];
        TransformRotate(t, angle);
        TransformPremultiply(state.Xform, t);
    }

    public void SkewX(float angle)
    {
        ref var state = ref GetState();
        Span<float> t = stackalloc float[6];
        TransformSkewX(t, angle);
        TransformPremultiply(state.Xform, t);
    }

    public void SkewY(float angle)
    {
        ref var state = ref GetState();
        Span<float> t = stackalloc float[6];
        TransformSkewY(t, angle);
        TransformPremultiply(state.Xform, t);
    }

    public void Scale(float x, float y)
    {
        ref var state = ref GetState();
        Span<float> t = stackalloc float[6];
        TransformScale(t, x, y);
        TransformPremultiply(state.Xform, t);
    }

    public Matrix3x2 GetTransformMatrix()
    {
        ref readonly var state = ref GetState();
        var x = state.Xform;
        return new Matrix3x2(x[0], x[1], x[2], x[3], x[4], x[5]);
    }

    public void SetTransformMatrix(in Matrix3x2 matrix)
    {
        ref var state = ref GetState();
        state.Xform[0] = matrix.M11;
        state.Xform[1] = matrix.M12;
        state.Xform[2] = matrix.M21;
        state.Xform[3] = matrix.M22;
        state.Xform[4] = matrix.M31;
        state.Xform[5] = matrix.M32;
    }

    #endregion

    #region Fill & Stroke Style

    public void StrokeColor(NVGcolor color)
    {
        ref var state = ref GetState();
        SetPaintColor(ref state.Stroke, color);
    }

    public void StrokePaint(NVGpaint paint)
    {
        ref var state = ref GetState();
        state.Stroke = paint;
        TransformMultiply(state.Stroke.Xform, state.Xform);
    }

    public void FillColor(NVGcolor color)
    {
        ref var state = ref GetState();
        SetPaintColor(ref state.Fill, color);
    }

    public void FillPaint(NVGpaint paint)
    {
        ref var state = ref GetState();
        state.Fill = paint;
        TransformMultiply(state.Fill.Xform, state.Xform);
    }

    #endregion

    #region Scissor

    public void Scissor(float x, float y, float w, float h)
    {
        ref var state = ref GetState();

        w = Maxf(0.0f, w);
        h = Maxf(0.0f, h);

        TransformIdentity(state.Scissor.Xform);
        state.Scissor.Xform[4] = x + w * 0.5f;
        state.Scissor.Xform[5] = y + h * 0.5f;
        TransformMultiply(state.Scissor.Xform, state.Xform);

        state.Scissor.Extent[0] = w * 0.5f;
        state.Scissor.Extent[1] = h * 0.5f;
    }

    public void IntersectScissor(float x, float y, float w, float h)
    {
        ref var state = ref GetState();

        // If no previous scissor has been set, set the scissor as current scissor.
        if (state.Scissor.Extent[0] < 0)
        {
            Scissor(x, y, w, h);
            return;
        }

        // Transform the current scissor rect into current transform space.
        Span<float> pxform = stackalloc float[6];
        Span<float> invxform = stackalloc float[6];
        for (var i = 0; i < 6; i++)
        {
            pxform[i] = state.Scissor.Xform[i];
        }

        var ex = state.Scissor.Extent[0];
        var ey = state.Scissor.Extent[1];
        TransformInverse(invxform, state.Xform);
        TransformMultiply(pxform, invxform);
        var tex = ex * Absf(pxform[0]) + ey * Absf(pxform[2]);
        var tey = ex * Absf(pxform[1]) + ey * Absf(pxform[3]);

        // Intersect rects.
        Span<float> rect = stackalloc float[4];
        IsectRects(rect, pxform[4] - tex, pxform[5] - tey, tex * 2, tey * 2, x, y, w, h);

        Scissor(rect[0], rect[1], rect[2], rect[3]);
    }

    private static void IsectRects(Span<float> dst, float ax, float ay, float aw, float ah,
                                   float bx, float by, float bw, float bh)
    {
        var minx = Maxf(ax, bx);
        var miny = Maxf(ay, by);
        var maxx = Minf(ax + aw, bx + bw);
        var maxy = Minf(ay + ah, by + bh);
        dst[0] = minx;
        dst[1] = miny;
        dst[2] = Maxf(0.0f, maxx - minx);
        dst[3] = Maxf(0.0f, maxy - miny);
    }

    public void ResetScissor()
    {
        ref var state = ref GetState();
        state.Scissor.Xform = default;
        state.Scissor.Extent[0] = -1.0f;
        state.Scissor.Extent[1] = -1.0f;
    }

    #endregion

    #region Clip Path

    public void Clip()
    {
        ref var state = ref GetState();

        ApplyPathTolerances(in state, forStroke: false);
        FlattenPaths(enforceWinding: false);
        if (_cache.NPaths == 0)
        {
            return;
        }

        var fillFringe = _fringeWidth;

        // Clip uses stencil (binary inside/outside) — pass fringe=0 so fill
        // triangles stay at geometric boundary (no inset that would shrink clip).
        ExpandFill(0.0f, NVGlineJoin.Miter, 2.4f, MapFillRuleToTess(state.FillRule));

        var clip = CaptureClipSnapshot(state.Scissor, fillFringe);
        _clipStack.Add(clip);
        state.ClipDepth = _clipStack.Count;

        var scissor = clip.Scissor;
        _renderer.RenderClip(ref scissor, clip.Fringe, clip.Bounds, clip.Paths, clip.Verts);
    }

    public void ResetClip()
    {
        ref var state = ref GetState();
        state.ClipDepth = 0;
        _clipStack.Clear();
        _renderer.ResetClip();
    }

    private NVGClipPath CaptureClipSnapshot(NVGscissorState scissor, float fringe)
    {
        int totalFillVerts = 0;
        for (var i = 0; i < _cache.NPaths; i++)
        {
            totalFillVerts += _cache.Paths[i].NFill;
        }

        var verts = totalFillVerts > 0 ? new NVGvertex[totalFillVerts] : Array.Empty<NVGvertex>();
        var paths = _cache.NPaths > 0 ? new NVGpathData[_cache.NPaths] : Array.Empty<NVGpathData>();

        int vertOffset = 0;
        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var srcPath = ref _cache.Paths[i];
            var dstPath = srcPath;
            dstPath.FillOffset = vertOffset;
            dstPath.NStroke = 0;
            dstPath.StrokeOffset = 0;

            if (srcPath.NFill > 0)
            {
                _cache.Verts.AsSpan(srcPath.FillOffset, srcPath.NFill)
                    .CopyTo(verts.AsSpan(vertOffset));
                vertOffset += srcPath.NFill;
            }

            paths[i] = dstPath;
        }

        var bounds = new float[4];
        Array.Copy(_cache.Bounds, bounds, 4);

        return new NVGClipPath(scissor, fringe, bounds, paths, verts);
    }

    #endregion

    #region Frame Management

    public void BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio)
    {
        _nstates = 0;
        Save();
        Reset();

        SetDevicePixelRatio(devicePixelRatio);

        _renderer.BeginFrame(windowWidth, windowHeight, devicePixelRatio);

        DrawCallCount = 0;
        FillTriCount = 0;
        StrokeTriCount = 0;
        TextTriCount = 0;
    }

    public void CancelFrame() => _renderer.Cancel();

    public void EndFrame() => _renderer.Flush();

    #endregion

    #region Path Commands

    private void AppendCommands(Span<float> vals)
    {
        ref var state = ref GetState();

        var nvals = vals.Length;

        if (_ncommands + nvals > _ccommands)
        {
            var ccommands = _ncommands + nvals + _ccommands / 2;
            Array.Resize(ref _commands, ccommands);
            _ccommands = ccommands;
        }

        if ((int)vals[0] != (int)NVGcommands.Close && (int)vals[0] != (int)NVGcommands.Winding)
        {
            _commandx = vals[nvals - 2];
            _commandy = vals[nvals - 1];
        }

        // Transform commands
        var i = 0;
        while (i < nvals)
        {
            var cmd = (int)vals[i];
            switch ((NVGcommands)cmd)
            {
                case NVGcommands.MoveTo:
                    TransformPoint(out vals[i + 1], out vals[i + 2], state.Xform, vals[i + 1], vals[i + 2]);
                    i += 3;
                    break;

                case NVGcommands.LineTo:
                    TransformPoint(out vals[i + 1], out vals[i + 2], state.Xform, vals[i + 1], vals[i + 2]);
                    i += 3;
                    break;

                case NVGcommands.BezierTo:
                    TransformPoint(out vals[i + 1], out vals[i + 2], state.Xform, vals[i + 1], vals[i + 2]);
                    TransformPoint(out vals[i + 3], out vals[i + 4], state.Xform, vals[i + 3], vals[i + 4]);
                    TransformPoint(out vals[i + 5], out vals[i + 6], state.Xform, vals[i + 5], vals[i + 6]);
                    i += 7;
                    break;

                case NVGcommands.Close:
                    i++;
                    break;

                case NVGcommands.Winding:
                    i += 2;
                    break;

                default:
                    i++;
                    break;
            }
        }

        vals.CopyTo(_commands.AsSpan(_ncommands));
        _ncommands += nvals;
    }

    public void BeginPath()
    {
        _ncommands = 0;
        ClearPathCache();
    }

    public void MoveTo(float x, float y)
    {
        Span<float> vals = stackalloc float[] { (float)NVGcommands.MoveTo, x, y };
        AppendCommands(vals);
    }

    public void LineTo(float x, float y)
    {
        Span<float> vals = stackalloc float[] { (float)NVGcommands.LineTo, x, y };
        AppendCommands(vals);
    }

    public void BezierTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
    {
        Span<float> vals = stackalloc float[] { (float)NVGcommands.BezierTo, c1x, c1y, c2x, c2y, x, y };
        AppendCommands(vals);
    }

    public void QuadTo(float cx, float cy, float x, float y)
    {
        var x0 = _commandx;
        var y0 = _commandy;
        Span<float> vals = stackalloc float[]
        {
            (float)NVGcommands.BezierTo,
            x0 + 2.0f / 3.0f * (cx - x0), y0 + 2.0f / 3.0f * (cy - y0),
            x + 2.0f / 3.0f * (cx - x), y + 2.0f / 3.0f * (cy - y),
            x, y
        };
        AppendCommands(vals);
    }

    public void ArcTo(float x1, float y1, float x2, float y2, float radius)
    {
        var x0 = _commandx;
        var y0 = _commandy;

        if (_ncommands == 0)
        {
            return;
        }

        // Handle degenerate cases
        if (PtEquals(x0, y0, x1, y1, _distTol) ||
            PtEquals(x1, y1, x2, y2, _distTol) ||
            DistPtSeg(x1, y1, x0, y0, x2, y2) < _distTol * _distTol ||
            radius < _distTol)
        {
            LineTo(x1, y1);
            return;
        }

        // Calculate tangential circle to lines (x0,y0)-(x1,y1) and (x1,y1)-(x2,y2).
        var dx0 = x0 - x1;
        var dy0 = y0 - y1;
        var dx1 = x2 - x1;
        var dy1 = y2 - y1;
        Normalize(ref dx0, ref dy0);
        Normalize(ref dx1, ref dy1);
        var a = Acosf(dx0 * dx1 + dy0 * dy1);
        var d = radius / Tanf(a / 2.0f);

        if (d > 10000.0f)
        {
            LineTo(x1, y1);
            return;
        }

        float cx, cy, a0, a1;
        NVGwinding dir;

        if (Cross(dx0, dy0, dx1, dy1) > 0.0f)
        {
            cx = x1 + dx0 * d + dy0 * radius;
            cy = y1 + dy0 * d + -dx0 * radius;
            a0 = Atan2f(dx0, -dy0);
            a1 = Atan2f(-dx1, dy1);
            dir = NVGwinding.CW;
        }
        else
        {
            cx = x1 + dx0 * d + -dy0 * radius;
            cy = y1 + dy0 * d + dx0 * radius;
            a0 = Atan2f(-dx0, dy0);
            a1 = Atan2f(dx1, -dy1);
            dir = NVGwinding.CCW;
        }

        Arc(cx, cy, radius, a0, a1, dir);
    }

    public void ClosePath()
    {
        Span<float> vals = stackalloc float[] { (float)NVGcommands.Close };
        AppendCommands(vals);
    }

    public void PathWinding(NVGwinding dir)
    {
        Span<float> vals = stackalloc float[] { (float)NVGcommands.Winding, (float)dir };
        AppendCommands(vals);
    }

    public void Arc(float cx, float cy, float r, float a0, float a1, NVGwinding dir)
    {
        var da = a1 - a0;
        if (dir == NVGwinding.CW)
        {
            if (Absf(da) >= NVG_PI * 2)
            {
                da = NVG_PI * 2;
            }
            else
            {
                while (da < 0.0f)
                {
                    da += NVG_PI * 2;
                }
            }
        }
        else
        {
            if (Absf(da) >= NVG_PI * 2)
            {
                da = -NVG_PI * 2;
            }
            else
            {
                while (da > 0.0f)
                {
                    da -= NVG_PI * 2;
                }
            }
        }

        // Split arc into max 90 degree segments.
        var ndivs = Maxi(1, Mini((int)(Absf(da) / (NVG_PI * 0.5f) + 0.5f), 5));
        var hda = da / (float)ndivs / 2.0f;
        var kappa = Absf(4.0f / 3.0f * (1.0f - Cosf(hda)) / Sinf(hda));

        if (dir == NVGwinding.CCW)
        {
            kappa = -kappa;
        }

        Span<float> vals = stackalloc float[3 + 5 * 7 + 100];
        var nvals = 0;

        var move = _ncommands > 0 ? (int)NVGcommands.LineTo : (int)NVGcommands.MoveTo;

        float px = 0, py = 0, ptanx = 0, ptany = 0;
        for (var i = 0; i <= ndivs; i++)
        {
            var a = a0 + da * (i / (float)ndivs);
            var dx = Cosf(a);
            var dy = Sinf(a);
            var x = cx + dx * r;
            var y = cy + dy * r;
            var tanx = -dy * r * kappa;
            var tany = dx * r * kappa;

            if (i == 0)
            {
                vals[nvals++] = (float)move;
                vals[nvals++] = x;
                vals[nvals++] = y;
            }
            else
            {
                vals[nvals++] = (float)NVGcommands.BezierTo;
                vals[nvals++] = px + ptanx;
                vals[nvals++] = py + ptany;
                vals[nvals++] = x - tanx;
                vals[nvals++] = y - tany;
                vals[nvals++] = x;
                vals[nvals++] = y;
            }
            px = x;
            py = y;
            ptanx = tanx;
            ptany = tany;
        }

        AppendCommands(vals.Slice(0, nvals));
    }

    public void Rect(float x, float y, float w, float h)
    {
        Span<float> vals = stackalloc float[]
        {
            (float)NVGcommands.MoveTo, x, y,
            (float)NVGcommands.LineTo, x, y + h,
            (float)NVGcommands.LineTo, x + w, y + h,
            (float)NVGcommands.LineTo, x + w, y,
            (float)NVGcommands.Close
        };
        AppendCommands(vals);
    }

    public void RoundedRect(float x, float y, float w, float h, float r) => RoundedRectVarying(x, y, w, h, r, r, r, r);

    public void RoundedRectVarying(float x, float y, float w, float h,
        float radTopLeft, float radTopRight, float radBottomRight, float radBottomLeft)
    {
        if (radTopLeft < 0.1f && radTopRight < 0.1f && radBottomRight < 0.1f && radBottomLeft < 0.1f)
        {
            Rect(x, y, w, h);
            return;
        }

        var halfw = Absf(w) * 0.5f;
        var halfh = Absf(h) * 0.5f;
        var rxBL = Minf(radBottomLeft, halfw) * Signf(w);
        var ryBL = Minf(radBottomLeft, halfh) * Signf(h);
        var rxBR = Minf(radBottomRight, halfw) * Signf(w);
        var ryBR = Minf(radBottomRight, halfh) * Signf(h);
        var rxTR = Minf(radTopRight, halfw) * Signf(w);
        var ryTR = Minf(radTopRight, halfh) * Signf(h);
        var rxTL = Minf(radTopLeft, halfw) * Signf(w);
        var ryTL = Minf(radTopLeft, halfh) * Signf(h);

        Span<float> vals = stackalloc float[]
        {
            (float)NVGcommands.MoveTo, x, y + ryTL,
            (float)NVGcommands.LineTo, x, y + h - ryBL,
            (float)NVGcommands.BezierTo, x, y + h - ryBL * (1 - NVG_KAPPA90), x + rxBL * (1 - NVG_KAPPA90), y + h, x + rxBL, y + h,
            (float)NVGcommands.LineTo, x + w - rxBR, y + h,
            (float)NVGcommands.BezierTo, x + w - rxBR * (1 - NVG_KAPPA90), y + h, x + w, y + h - ryBR * (1 - NVG_KAPPA90), x + w, y + h - ryBR,
            (float)NVGcommands.LineTo, x + w, y + ryTR,
            (float)NVGcommands.BezierTo, x + w, y + ryTR * (1 - NVG_KAPPA90), x + w - rxTR * (1 - NVG_KAPPA90), y, x + w - rxTR, y,
            (float)NVGcommands.LineTo, x + rxTL, y,
            (float)NVGcommands.BezierTo, x + rxTL * (1 - NVG_KAPPA90), y, x, y + ryTL * (1 - NVG_KAPPA90), x, y + ryTL,
            (float)NVGcommands.Close
        };
        AppendCommands(vals);
    }

    public void Ellipse(float cx, float cy, float rx, float ry)
    {
        Span<float> vals = stackalloc float[]
        {
            (float)NVGcommands.MoveTo, cx - rx, cy,
            (float)NVGcommands.BezierTo, cx - rx, cy + ry * NVG_KAPPA90, cx - rx * NVG_KAPPA90, cy + ry, cx, cy + ry,
            (float)NVGcommands.BezierTo, cx + rx * NVG_KAPPA90, cy + ry, cx + rx, cy + ry * NVG_KAPPA90, cx + rx, cy,
            (float)NVGcommands.BezierTo, cx + rx, cy - ry * NVG_KAPPA90, cx + rx * NVG_KAPPA90, cy - ry, cx, cy - ry,
            (float)NVGcommands.BezierTo, cx - rx * NVG_KAPPA90, cy - ry, cx - rx, cy - ry * NVG_KAPPA90, cx - rx, cy,
            (float)NVGcommands.Close
        };
        AppendCommands(vals);
    }

    public void Circle(float cx, float cy, float r) => Ellipse(cx, cy, r, r);

    #endregion

    #region Path Cache Operations

    private void ClearPathCache()
    {
        _cache.NPoints = 0;
        _cache.NPaths = 0;
    }

    private ref NVGpathData LastPath()
    {
        if (_cache.NPaths > 0)
        {
            return ref _cache.Paths[_cache.NPaths - 1];
        }

        throw new InvalidOperationException("No paths in cache");
    }

    private void AddPath()
    {
        if (_cache.NPaths + 1 > _cache.CPaths)
        {
            var cpaths = _cache.NPaths + 1 + _cache.CPaths / 2;
            Array.Resize(ref _cache.Paths, cpaths);
            _cache.CPaths = cpaths;
        }

        _cache.Paths[_cache.NPaths] = new NVGpathData
        {
            First = _cache.NPoints,
            Winding = NVGwinding.CCW
        };
        _cache.NPaths++;
    }

    private ref NVGpoint LastPoint()
    {
        if (_cache.NPoints > 0)
        {
            return ref _cache.Points[_cache.NPoints - 1];
        }

        throw new InvalidOperationException("No points in cache");
    }

    private void AddPoint(float x, float y, NVGpointFlags flags)
    {
        if (_cache.NPaths <= 0)
        {
            return;
        }

        ref var path = ref _cache.Paths[_cache.NPaths - 1];

        if (path.Count > 0 && _cache.NPoints > 0)
        {
            ref var pt = ref LastPoint();
            if (PtEquals(pt.X, pt.Y, x, y, _distTol))
            {
                pt.Flags |= flags;
                return;
            }
        }

        if (_cache.NPoints + 1 > _cache.CPoints)
        {
            var cpoints = _cache.NPoints + 1 + _cache.CPoints / 2;
            Array.Resize(ref _cache.Points, cpoints);
            _cache.CPoints = cpoints;
        }

        _cache.Points[_cache.NPoints] = new NVGpoint
        {
            X = x,
            Y = y,
            Flags = flags
        };
        _cache.NPoints++;
        path.Count++;
    }

    private void ClosePathInternal()
    {
        if (_cache.NPaths <= 0)
        {
            return;
        }

        ref var path = ref _cache.Paths[_cache.NPaths - 1];
        path.Closed = true;
    }

    private void PathWindingInternal(NVGwinding winding)
    {
        if (_cache.NPaths <= 0)
        {
            return;
        }

        ref var path = ref _cache.Paths[_cache.NPaths - 1];
        path.Winding = winding;
    }

    #endregion

    #region Path Tessellation

    private void TesselateBezier(float x1, float y1, float x2, float y2,
                                 float x3, float y3, float x4, float y4,
                                 int level, NVGpointFlags type)
    {
        if (level > 14)
        {
            return;
        }

        var x12 = (x1 + x2) * 0.5f;
        var y12 = (y1 + y2) * 0.5f;
        var x23 = (x2 + x3) * 0.5f;
        var y23 = (y2 + y3) * 0.5f;
        var x34 = (x3 + x4) * 0.5f;
        var y34 = (y3 + y4) * 0.5f;
        var x123 = (x12 + x23) * 0.5f;
        var y123 = (y12 + y23) * 0.5f;

        var dx = x4 - x1;
        var dy = y4 - y1;
        var d2 = Absf((x2 - x4) * dy - (y2 - y4) * dx);
        var d3 = Absf((x3 - x4) * dy - (y3 - y4) * dx);

        if ((d2 + d3) * (d2 + d3) < _tessTol * (dx * dx + dy * dy))
        {
            AddPoint(x4, y4, type);
            return;
        }

        var x234 = (x23 + x34) * 0.5f;
        var y234 = (y23 + y34) * 0.5f;
        var x1234 = (x123 + x234) * 0.5f;
        var y1234 = (y123 + y234) * 0.5f;

        TesselateBezier(x1, y1, x12, y12, x123, y123, x1234, y1234, level + 1, 0);
        TesselateBezier(x1234, y1234, x234, y234, x34, y34, x4, y4, level + 1, type);
    }

    private void FlattenPaths(bool enforceWinding = true)
    {
        if (_cache.NPaths > 0)
        {
            return;
        }

        // Flatten
        var i = 0;
        while (i < _ncommands)
        {
            var cmd = (int)_commands[i];
            switch ((NVGcommands)cmd)
            {
                case NVGcommands.MoveTo:
                    AddPath();
                    AddPoint(_commands[i + 1], _commands[i + 2], NVGpointFlags.Corner);
                    i += 3;
                    break;

                case NVGcommands.LineTo:
                    AddPoint(_commands[i + 1], _commands[i + 2], NVGpointFlags.Corner);
                    i += 3;
                    break;

                case NVGcommands.BezierTo:
                    if (_cache.NPoints > 0)
                    {
                        ref var last = ref LastPoint();
                        TesselateBezier(last.X, last.Y,
                            _commands[i + 1], _commands[i + 2],
                            _commands[i + 3], _commands[i + 4],
                            _commands[i + 5], _commands[i + 6], 0, NVGpointFlags.Corner);
                    }
                    i += 7;
                    break;

                case NVGcommands.Close:
                    ClosePathInternal();
                    i++;
                    break;

                case NVGcommands.Winding:
                    PathWindingInternal((NVGwinding)(int)_commands[i + 1]);
                    i += 2;
                    break;

                default:
                    i++;
                    break;
            }
        }

        _cache.Bounds[0] = _cache.Bounds[1] = 1e6f;
        _cache.Bounds[2] = _cache.Bounds[3] = -1e6f;

        // Calculate the direction and length of line segments.
        for (var j = 0; j < _cache.NPaths; j++)
        {
            ref var path = ref _cache.Paths[j];
            var pts = _cache.Points.AsSpan(path.First, path.Count);

            // If the first and last points are the same, remove the last, mark as closed path.
            if (path.Count > 0)
            {
                ref var p0 = ref pts[path.Count - 1];
                ref var p1 = ref pts[0];
                if (PtEquals(p0.X, p0.Y, p1.X, p1.Y, _distTol))
                {
                    path.Count--;
                    path.Closed = true;
                }
            }

            if (path.Count < 1)
            {
                continue;
            }

            pts = _cache.Points.AsSpan(path.First, path.Count);

            // Legacy NanoVG winding normalization is optional for CPU-resolved fills.
            if (enforceWinding && path.Count > 2)
            {
                var area = PolyArea(pts);
                if (path.Winding == NVGwinding.CCW && area < 0.0f)
                {
                    PolyReverse(pts);
                }

                if (path.Winding == NVGwinding.CW && area > 0.0f)
                {
                    PolyReverse(pts);
                }
            }

            for (var k = 0; k < path.Count; k++)
            {
                ref var p0 = ref pts[k];
                ref var p1 = ref pts[(k + 1) % path.Count];

                // Calculate segment direction and length
                p0.DX = p1.X - p0.X;
                p0.DY = p1.Y - p0.Y;
                p0.Len = Normalize(ref p0.DX, ref p0.DY);

                // Update bounds
                _cache.Bounds[0] = Minf(_cache.Bounds[0], p0.X);
                _cache.Bounds[1] = Minf(_cache.Bounds[1], p0.Y);
                _cache.Bounds[2] = Maxf(_cache.Bounds[2], p0.X);
                _cache.Bounds[3] = Maxf(_cache.Bounds[3], p0.Y);
            }
        }
    }

    private static float PolyArea(Span<NVGpoint> pts)
    {
        float area = 0;
        for (var i = 2; i < pts.Length; i++)
        {
            ref var a = ref pts[0];
            ref var b = ref pts[i - 1];
            ref var c = ref pts[i];
            area += TriArea2(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        }
        return area * 0.5f;
    }

    private static bool IsConvexContour(ReadOnlySpan<NVGpoint> pts)
    {
        if (pts.Length < 3)
        {
            return false;
        }

        // Skia-style convex test: consecutive turns must have the same sign AND
        // the total signed turn angle must equal ±2π (exactly one revolution).
        // The first condition alone is necessary but not sufficient — a self-
        // intersecting pentagram has all turns in one direction but winds ±4π.
        float sign = 0f;
        float turnSum = 0f;
        for (var i = 0; i < pts.Length; i++)
        {
            ref readonly var a = ref pts[i];
            ref readonly var b = ref pts[(i + 1) % pts.Length];
            ref readonly var c = ref pts[(i + 2) % pts.Length];

            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var bcx = c.X - b.X;
            var bcy = c.Y - b.Y;
            var cross = abx * bcy - aby * bcx;
            if (MathF.Abs(cross) <= 1e-8f)
            {
                continue;
            }

            if (sign == 0f)
            {
                sign = cross;
            }
            else if (cross * sign < 0f)
            {
                return false;
            }

            var dot = abx * bcx + aby * bcy;
            turnSum += MathF.Atan2(cross, dot);
        }

        if (sign == 0f)
        {
            return false;
        }

        // Allow small numerical slack; a simple convex polygon winds once (±2π),
        // self-intersecting contours wind ±4π or more.
        const float revolution = MathF.PI * 2f;
        return MathF.Abs(MathF.Abs(turnSum) - revolution) <= 1e-3f;
    }

    // Simple (non-self-intersecting) polygon test: sign of turn may flip (concave
    // ok), but total turn angle must equal ±2π. Self-intersecting contours wind
    // ±4π or more. Used to decide whether per-contour fringe AA is safe — the
    // NanoVG-style fringe strip assumes a simple boundary; along a pentagram's
    // self-intersecting edges the strip would cross itself at inner vertices
    // and leave visible seams across the fill interior.
    private static bool IsSimpleContour(ReadOnlySpan<NVGpoint> pts)
    {
        if (pts.Length < 3)
        {
            return false;
        }

        float turnSum = 0f;
        for (var i = 0; i < pts.Length; i++)
        {
            ref readonly var a = ref pts[i];
            ref readonly var b = ref pts[(i + 1) % pts.Length];
            ref readonly var c = ref pts[(i + 2) % pts.Length];

            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var bcx = c.X - b.X;
            var bcy = c.Y - b.Y;
            var cross = abx * bcy - aby * bcx;
            var dot = abx * bcx + aby * bcy;
            turnSum += MathF.Atan2(cross, dot);
        }

        const float revolution = MathF.PI * 2f;
        return MathF.Abs(MathF.Abs(turnSum) - revolution) <= 1e-3f;
    }

    private static void PolyReverse(Span<NVGpoint> pts)
    {
        int i = 0, j = pts.Length - 1;
        while (i < j)
        {
            (pts[i], pts[j]) = (pts[j], pts[i]);
            i++;
            j--;
        }
    }

    #endregion

    #region Fill & Stroke Rendering

    private void ApplyPathTolerances(in NVGstate state, bool forStroke = false)
    {
        var ratio = Maxf(_devicePxRatio, 0.0001f);
        _tessTol = 0.25f / ratio;
        _distTol = 0.01f / ratio;
        _fringeWidth = EDGE_AA_FRINGE_PX / ratio;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TessWindingRule MapFillRuleToTess(NVGfillRule rule)
        => rule == NVGfillRule.EvenOdd ? TessWindingRule.Odd : TessWindingRule.NonZero;

    public void Fill()
    {
        ref var state = ref GetState();
        var fillPaint = state.Fill;

        ApplyPathTolerances(in state, forStroke: false);
        FlattenPaths(enforceWinding: false);

        var useFringeAa = (_edgeAntiAlias && state.ShapeAntiAlias) || _forceCoverageAaFringe;
        // miterLimit 4.0 (SVG default) keeps ~36° tips on miter instead of beveling.
        // Bevel at a sharp convex tip emits outside-normal vertices that poke past
        // the tip along both edge normals, producing visible 1–2px spikes at the
        // corner in the fringe strip. Miter collapses those two vertices to a
        // single point on the bisector, inside the fill body's coverage.
        const float fillFringeMiterLimit = 4.0f;
        if (useFringeAa)
        {
            var fillFringe = _fringeWidth;
            ExpandFill(fillFringe, NVGlineJoin.Miter, fillFringeMiterLimit, MapFillRuleToTess(state.FillRule));
        }
        else
        {
            ExpandFill(0.0f, NVGlineJoin.Miter, fillFringeMiterLimit, MapFillRuleToTess(state.FillRule));
        }

        // Apply global alpha
        fillPaint.InnerColor.A *= state.Alpha;
        fillPaint.OuterColor.A *= state.Alpha;

        // Submit to renderer
        _renderer.RenderFill(
            ref fillPaint,
            state.CompositeOperation,
            ref state.Scissor,
            useFringeAa ? _fringeWidth : 0.0f,
            _cache.Bounds,
            _cache.Paths.AsSpan(0, _cache.NPaths),
            _cache.Verts);

        // Count triangles
        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var path = ref _cache.Paths[i];
            FillTriCount += path.NFill - 2;
            FillTriCount += path.NStroke - 2;
            DrawCallCount += 2;
        }
    }

    /// <summary>
    /// Flatten + tessellate current path commands in object-space (identity transform).
    /// Call after BeginPath + path commands with identity transform active.
    /// The returned cache can be reused across frames via <see cref="FillFromCache"/>.
    /// </summary>
    public FrozenFillCache BuildFillCache(TessWindingRule windingRule)
    {
        ref var state = ref GetState();
        ApplyPathTolerances(in state, forStroke: false);

        var cache = new FrozenFillCache();
        cache.TessTol = _tessTol;

        // Commands are in object-space (identity transform) but tolerances are
        // calibrated for screen-space. Compensate by the current transform's
        // scale factor so bezier subdivision matches screen-space visual quality.
        var xf = state.Xform;
        var sx = MathF.Sqrt(xf[0] * xf[0] + xf[1] * xf[1]);
        var sy = MathF.Sqrt(xf[2] * xf[2] + xf[3] * xf[3]);
        var scale = Maxf(Maxf(sx, sy), 0.0001f);
        var fringeWidthObj = _fringeWidth / scale;

        var savedTessTol = _tessTol;
        var savedDistTol = _distTol;
        var savedFringeWidth = _fringeWidth;
        if (scale > 1.0f)
        {
            _tessTol /= scale;
            _distTol /= scale;
        }

        FlattenPaths(enforceWinding: false);

        // Restore standard tolerances (compensated values only needed for flattening)
        _tessTol = savedTessTol;
        _distTol = savedDistTol;
        _fringeWidth = savedFringeWidth;

        // Check for single convex contour (directConvexFill fast path)
        if (_cache.NPaths == 1)
        {
            ref var onlyPath = ref _cache.Paths[0];
            if (onlyPath.Count >= 3)
            {
                var pts = _cache.Points.AsSpan(onlyPath.First, onlyPath.Count);
                if (IsConvexContour(pts))
                {
                    cache.IsDirectConvex = true;
                    onlyPath.Closed = true;
                    onlyPath.Winding = PolyArea(pts) >= 0.0f ? NVGwinding.CCW : NVGwinding.CW;
                }
            }
        }

        if (!cache.IsDirectConvex)
        {
            NormalizeContoursForFill(_distTol / (scale > 1.0f ? scale : 1.0f), 0.0f);
        }

        if (_cache.NPaths == 0)
        {
            ClearPathCache();
            return cache;
        }

        if (!cache.IsDirectConvex)
        {
            // Compute joins + fringe signs in object-space for inset tessellation.
            // fringeSigns are topology-dependent (inside/outside), transform-invariant.
            CalculateJoins(fringeWidthObj, NVGlineJoin.Miter, 2.4f);

            var sourcePathCount = _cache.NPaths;
            Span<float> fringeSigns = sourcePathCount <= 128
                ? stackalloc float[sourcePathCount]
                : new float[sourcePathCount];

            _fringeWidth = fringeWidthObj;
            ComputeFillFringeSigns(fringeSigns, sourcePathCount, windingRule);
            _fringeWidth = savedFringeWidth;

            // Snapshot contour data (after CalculateJoins — DM vectors included)
            cache.NContourPaths = _cache.NPaths;
            cache.ContourPaths = _cache.Paths.AsSpan(0, _cache.NPaths).ToArray();
            cache.NContourPoints = _cache.NPoints;
            cache.ContourPoints = _cache.Points.AsSpan(0, _cache.NPoints).ToArray();

            // Tessellate with fringe inset in object-space.
            // Inset = fringeWidthObj * 0.5 * fringeSign. After uniform scale S at
            // render time this becomes _fringeWidth * 0.5 * fringeSign, matching
            // the screen-space fringe strip inner edge exactly.
            _fillTessellator.Clear();
            Span<Vector2> stackContour = stackalloc Vector2[128];
            for (var i = 0; i < sourcePathCount; i++)
            {
                ref var path = ref _cache.Paths[i];
                if (path.Count < 3) continue;
                var pts = _cache.Points.AsSpan(path.First, path.Count);
                var woff = fringeWidthObj * 0.5f * fringeSigns[i];

                var contour = path.Count <= 128
                    ? stackContour.Slice(0, path.Count)
                    : new Vector2[path.Count].AsSpan();

                for (var j = 0; j < path.Count; j++)
                    InsetPoint(ref contour[j], in pts[j], woff);
                _fillTessellator.AddContour(contour);
            }

            var tessResult = _fillTessellator.Tessellate(windingRule, TessElementType.Polygons, 3);
            if (tessResult.Status == TessStatus.Ok)
            {
                cache.TessVertices = tessResult.Vertices;
                cache.TessIndices = tessResult.Indices;
                cache.TriangleCount = tessResult.Indices.Length / 3;
            }
        }
        else
        {
            // Convex: just snapshot contour data (fan is computed at render time)
            cache.NContourPaths = _cache.NPaths;
            cache.ContourPaths = _cache.Paths.AsSpan(0, _cache.NPaths).ToArray();
            cache.NContourPoints = _cache.NPoints;
            cache.ContourPoints = _cache.Points.AsSpan(0, _cache.NPoints).ToArray();
        }

        ClearPathCache();
        return cache;
    }

    /// <summary>
    /// Render a fill using cached object-space tessellation + current transform.
    /// Restores cached contour points, transforms to screen-space, generates fringe,
    /// and submits to the renderer.
    /// </summary>
    public void FillFromCache(FrozenFillCache cache, TessWindingRule windingRule)
    {
        if (cache.NContourPaths == 0) return;

        ref var state = ref GetState();
        var fillPaint = state.Fill;
        ApplyPathTolerances(in state, forStroke: false);

        // Restore contour points and transform to screen-space
        RestoreAndTransformContours(cache, state.Xform);

        // Run ExpandFill with cached tessellation (skips NormalizeContoursForFill + tessellation)
        var useFringeAa = (_edgeAntiAlias && state.ShapeAntiAlias) || _forceCoverageAaFringe;
        const float fillFringeMiterLimit = 4.0f;
        if (useFringeAa)
        {
            ExpandFill(_fringeWidth, NVGlineJoin.Miter, fillFringeMiterLimit, windingRule, tessCache: cache);
        }
        else
        {
            ExpandFill(0.0f, NVGlineJoin.Miter, fillFringeMiterLimit, windingRule, tessCache: cache);
        }

        // Submit to renderer
        fillPaint.InnerColor.A *= state.Alpha;
        fillPaint.OuterColor.A *= state.Alpha;

        _renderer.RenderFill(
            ref fillPaint,
            state.CompositeOperation,
            ref state.Scissor,
            useFringeAa ? _fringeWidth : 0.0f,
            _cache.Bounds,
            _cache.Paths.AsSpan(0, _cache.NPaths),
            _cache.Verts);

        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var path = ref _cache.Paths[i];
            FillTriCount += path.NFill - 2;
            FillTriCount += path.NStroke - 2;
            DrawCallCount += 2;
        }
    }

    private void RestoreAndTransformContours(FrozenFillCache cache, Buffer6<float> xform)
    {
        // Ensure capacity
        EnsurePathCapacity(cache.NContourPaths);
        if (cache.NContourPoints > _cache.CPoints)
        {
            var cpoints = (cache.NContourPoints + 0x7f) & ~0x7f;
            Array.Resize(ref _cache.Points, cpoints);
            _cache.CPoints = cpoints;
        }

        _cache.NPaths = cache.NContourPaths;
        _cache.NPoints = cache.NContourPoints;

        // Copy path metadata
        cache.ContourPaths.AsSpan(0, cache.NContourPaths).CopyTo(_cache.Paths);

        // Copy and transform points
        for (var i = 0; i < cache.NContourPoints; i++)
        {
            ref var src = ref cache.ContourPoints[i];
            ref var dst = ref _cache.Points[i];
            dst = src; // copy all fields (flags etc.)
            TransformPoint(out dst.X, out dst.Y, xform, src.X, src.Y);
        }

        // Recalculate edge directions + lengths (screen-space)
        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var path = ref _cache.Paths[i];
            var pts = _cache.Points.AsSpan(path.First, path.Count);
            for (var k = 0; k < path.Count; k++)
            {
                ref var p0 = ref pts[k];
                ref var p1 = ref pts[(k + 1) % path.Count];
                p0.DX = p1.X - p0.X;
                p0.DY = p1.Y - p0.Y;
                p0.Len = Normalize(ref p0.DX, ref p0.DY);
            }
        }

        // Recompute bounds
        _cache.Bounds[0] = _cache.Bounds[1] = 1e6f;
        _cache.Bounds[2] = _cache.Bounds[3] = -1e6f;
        for (var i = 0; i < _cache.NPoints; i++)
        {
            var x = _cache.Points[i].X;
            var y = _cache.Points[i].Y;
            _cache.Bounds[0] = Math.Min(_cache.Bounds[0], x);
            _cache.Bounds[1] = Math.Min(_cache.Bounds[1], y);
            _cache.Bounds[2] = Math.Max(_cache.Bounds[2], x);
            _cache.Bounds[3] = Math.Max(_cache.Bounds[3], y);
        }
    }

    public void Stroke()
    {
        ref var state = ref GetState();
        var scale = GetAverageScale(state.Xform);
        var strokeWidth = Clampf(state.StrokeWidth * scale, 0.0f, 200.0f);
        var strokePaint = state.Stroke;

        if (strokeWidth < _fringeWidth)
        {
            // If the stroke width is less than pixel size, use alpha to emulate coverage.
            var alpha = Clampf(strokeWidth / _fringeWidth, 0.0f, 1.0f);
            strokePaint.InnerColor.A *= alpha * alpha;
            strokePaint.OuterColor.A *= alpha * alpha;
            strokeWidth = _fringeWidth;
        }

        // Apply global alpha
        strokePaint.InnerColor.A *= state.Alpha;
        strokePaint.OuterColor.A *= state.Alpha;

        ApplyPathTolerances(in state, forStroke: true);
        FlattenPaths();

        if (_edgeAntiAlias && state.ShapeAntiAlias)
        {
            ExpandStroke(strokeWidth * 0.5f, _fringeWidth, state.LineCap, state.LineJoin, state.MiterLimit);
        }
        else
        {
            ExpandStroke(strokeWidth * 0.5f, 0.0f, state.LineCap, state.LineJoin, state.MiterLimit);
        }

        // Submit to renderer
        _renderer.RenderStroke(
            ref strokePaint,
            state.CompositeOperation,
            ref state.Scissor,
            _fringeWidth,
            strokeWidth,
            _cache.Paths.AsSpan(0, _cache.NPaths),
            _cache.Verts);

        // Count triangles
        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var path = ref _cache.Paths[i];
            StrokeTriCount += path.NStroke - 2;
            DrawCallCount++;
        }
    }

    #endregion

    #region Expand Fill/Stroke (Simplified)

    private void ExpandFill(float w, NVGlineJoin lineJoin, float miterLimit, TessWindingRule tessWindingRule,
        FrozenFillCache? tessCache = null)
    {
        var fastSingleConvex = false;
        if (_cache.NPaths == 1)
        {
            ref var onlyPath = ref _cache.Paths[0];
            if (onlyPath.Count >= 3)
            {
                var pts = _cache.Points.AsSpan(onlyPath.First, onlyPath.Count);
                if (IsConvexContour(pts))
                {
                    // Keep original flattened contour as SSOT for the common UI case
                    // (single convex shape), skipping normalization+tessellation work.
                    fastSingleConvex = true;
                    onlyPath.Closed = true;
                    onlyPath.Winding = PolyArea(pts) >= 0.0f ? NVGwinding.CCW : NVGwinding.CW;
                }
            }
        }

        if (!fastSingleConvex && tessCache == null)
        {
            NormalizeContoursForFill(_distTol, 0.0f);
        }
        if (_cache.NPaths == 0)
        {
            _cache.NVerts = 0;
            return;
        }

        CalculateJoins(w, lineJoin, miterLimit);

        var aa = _fringeWidth;
        var fringe = w > 0.0f;
        var sourcePathCount = _cache.NPaths;
        bool directConvexFill = false;
        int directFirst = 0;
        int directCount = 0;
        Vector2[] tessVertices = Array.Empty<Vector2>();
        int[] tessIndices = Array.Empty<int>();
        int triangleCount;
        bool tessNeedsTransform = false;

        if (sourcePathCount == 1)
        {
            ref readonly var onlyPath = ref _cache.Paths[0];
            if (onlyPath.Count >= 3)
            {
                var pts = _cache.Points.AsSpan(onlyPath.First, onlyPath.Count);
                if (IsConvexContour(pts))
                {
                    directConvexFill = true;
                    directFirst = onlyPath.First;
                    directCount = onlyPath.Count;
                }
            }
        }

        // Compute fringe signs early so tessellator contours can be inset to match fringe.
        Span<float> fringeSigns = sourcePathCount <= 128
            ? stackalloc float[sourcePathCount]
            : new float[sourcePathCount];
        if (fringe && !directConvexFill)
        {
            ComputeFillFringeSigns(fringeSigns, sourcePathCount, tessWindingRule);
        }

        if (!directConvexFill)
        {
            if (tessCache is { TessVertices: not null, TessIndices: not null })
            {
                // Use cached object-space tessellation (will be transformed during fill body emit)
                tessVertices = tessCache.TessVertices;
                tessIndices = tessCache.TessIndices;
                triangleCount = tessCache.TriangleCount;
                tessNeedsTransform = true;
            }
            else
            {
                _fillTessellator.Clear();
                Span<Vector2> stackContour = stackalloc Vector2[128];
                Vector2[]? rentedContour = null;
                try
                {
                    for (var i = 0; i < sourcePathCount; i++)
                    {
                        ref var path = ref _cache.Paths[i];
                        if (path.Count < 3)
                        {
                            continue;
                        }

                        var pts = _cache.Points.AsSpan(path.First, path.Count);
                        var woff = fringe ? aa * 0.5f * fringeSigns[i] : 0.0f;
                        if (path.Count <= stackContour.Length)
                        {
                            var contour = stackContour.Slice(0, path.Count);
                            for (var j = 0; j < path.Count; j++)
                            {
                                InsetPoint(ref contour[j], in pts[j], woff);
                            }

                            _fillTessellator.AddContour(contour);
                        }
                        else
                        {
                            rentedContour = System.Buffers.ArrayPool<Vector2>.Shared.Rent(path.Count);
                            var contour = rentedContour.AsSpan(0, path.Count);
                            for (var j = 0; j < path.Count; j++)
                            {
                                InsetPoint(ref contour[j], in pts[j], woff);
                            }

                            _fillTessellator.AddContour(contour);
                            System.Buffers.ArrayPool<Vector2>.Shared.Return(rentedContour, clearArray: false);
                            rentedContour = null;
                        }
                    }
                }
                finally
                {
                    if (rentedContour is not null)
                    {
                        System.Buffers.ArrayPool<Vector2>.Shared.Return(rentedContour, clearArray: false);
                    }
                }

                var tessResult = _fillTessellator.Tessellate(tessWindingRule, TessElementType.Polygons, 3);
                if (tessResult.Status != TessStatus.Ok)
                {
                    _cache.NPaths = 0;
                    _cache.NVerts = 0;
                    return;
                }

                tessVertices = tessResult.Vertices;
                tessIndices = tessResult.Indices;
                triangleCount = tessIndices.Length / 3;
            }
        }
        else
        {
            triangleCount = directCount - 2;
        }

        if (triangleCount == 0)
        {
            _cache.NPaths = 0;
            _cache.NVerts = 0;
            return;
        }

        var fringePathCount = 0;
        if (fringe)
        {
            for (var i = 0; i < sourcePathCount; i++)
            {
                if (_cache.Paths[i].Count > 0)
                {
                    fringePathCount++;
                }
            }
        }
        var fillPathCount = triangleCount > 0 ? 1 : 0;
        var finalPathCount = fringePathCount + fillPathCount;
        EnsurePathCapacity(finalPathCount);

        var cverts = triangleCount * 3;
        if (directConvexFill && fringe)
        {
            // Bevel vertices split into 2 fan vertices (DL0 + DL1), adding 1 extra triangle each
            cverts += _cache.Paths[0].NBevel * 3;
        }
        if (fringe)
        {
            for (var i = 0; i < sourcePathCount; i++)
            {
                ref var path = ref _cache.Paths[i];
                if (path.Count > 0)
                {
                    cverts += (path.Count + path.NBevel * 5 + 1) * 2;
                }
            }
        }

        EnsureVerts(cverts);

        if (fringe && directConvexFill)
        {
            ComputeFillFringeSigns(fringeSigns, sourcePathCount, tessWindingRule);
        }

        var vertOffset = 0;
        var pathOffset = 0;

        // Flag fills that need the coverage-AA pipeline instead of the stencil-
        // fill + fringe-overlay pipeline. A self-intersecting source contour
        // (pentagram) — its fringe strip crosses itself at inner vertices and
        // leaves seams across the fill; coverage AA's MAX-blended accumulation
        // collapses those overlaps to a clean boundary.
        bool sourceHasNonSimple = false;
        if (fringe)
        {
            for (var i = 0; i < sourcePathCount; i++)
            {
                ref var srcPath = ref _cache.Paths[i];
                if (srcPath.Count <= 0)
                {
                    continue;
                }

                var pts = _cache.Points.AsSpan(srcPath.First, srcPath.Count);
                if (!IsSimpleContour(pts))
                {
                    sourceHasNonSimple = true;
                }

                var outPath = srcPath;
                outPath.FillOffset = 0;
                outPath.NFill = 0;
                outPath.Convex = false;
                outPath.StrokeOffset = vertOffset;

                var fringeDir = fringeSigns[i];
                var woff = aa * 0.5f;
                var lw = woff * fringeDir;
                var rw = woff * fringeDir;
                float lu = 0.5f;
                float ru = -0.5f;

                for (var j = 0; j < srcPath.Count; j++)
                {
                    ref var p0 = ref pts[(j + srcPath.Count - 1) % srcPath.Count];
                    ref var p1 = ref pts[j];

                    // Ignore InnerBevel for fill fringe. InnerBevel is set in
                    // CalculateJoins to protect strokes from miter overlap at
                    // interior corners, but for a thin filled rect its bevel
                    // corners leave a small triangle at the original vertex
                    // that's covered by neither the tess inset (which places
                    // the boundary further inward along DM) nor the fringe
                    // strip (which only fills between bevel corners and outside
                    // vertices). The uncovered triangle flips to alpha<1 after
                    // pixel-grid snapping and appears as a 1px speckle at the
                    // contour's start vertex. The simple miter path gives a
                    // vertex coincident with the tess inset and closes the gap.
                    // Outer Bevel (at sharp convex corners) is still honored.
                    var joinFlags = p1.Flags & ~NVGpointFlags.InnerBevel;
                    if ((joinFlags & NVGpointFlags.Bevel) != 0)
                    {
                        BevelJoin(ref vertOffset, ref p0, ref p1, lw, rw, lu, ru, aa);
                    }
                    else
                    {
                        SetVert(ref _cache.Verts[vertOffset++], p1.X + p1.DMX * lw, p1.Y + p1.DMY * lw, lu, 1);
                        SetVert(ref _cache.Verts[vertOffset++], p1.X - p1.DMX * rw, p1.Y - p1.DMY * rw, ru, 1);
                    }
                }

                SetVert(ref _cache.Verts[vertOffset++], _cache.Verts[outPath.StrokeOffset].X, _cache.Verts[outPath.StrokeOffset].Y, lu, 1);
                SetVert(ref _cache.Verts[vertOffset++], _cache.Verts[outPath.StrokeOffset + 1].X, _cache.Verts[outPath.StrokeOffset + 1].Y, ru, 1);

                outPath.NStroke = vertOffset - outPath.StrokeOffset;
                _cache.Paths[pathOffset++] = outPath;
            }
        }

        if (triangleCount > 0)
        {
            ref var outPath = ref _cache.Paths[pathOffset++];
            outPath = default;
            outPath.FillOffset = vertOffset;
            outPath.StrokeOffset = 0;
            outPath.NStroke = 0;
            // Non-simple sources produce a tessellated body that is not a single
            // convex fan; flag it so the GL renderer routes to the coverage AA
            // path (which handles the self-intersecting fringe correctly via
            // Max blending) instead of the stencil-fill + fringe-overlay path.
            outPath.Convex = !sourceHasNonSimple;

            if (directConvexFill)
            {
                var pts = _cache.Points.AsSpan(directFirst, directCount);
                if (fringe)
                {
                    // Bevel-aware inset fan (mirrors NanoVG's nvg__expandFill fill fan).
                    // At sharp corners (bevel), DM magnitude >> 1 causes overshoot.
                    // Split to two edge normals (DL, magnitude=1) to match fringe's BevelJoin.
                    var woff = aa * 0.5f * fringeSigns[0];
                    float cx = 0, cy = 0;
                    float px = 0, py = 0;
                    var fanIdx = 0;

                    for (var j = 0; j < directCount; j++)
                    {
                        ref readonly var p1 = ref pts[j];

                        if ((p1.Flags & (NVGpointFlags.Bevel | NVGpointFlags.InnerBevel)) != 0)
                        {
                            ref readonly var p0 = ref pts[j > 0 ? j - 1 : directCount - 1];
                            var dlx0 = p0.DY;
                            var dly0 = -p0.DX;
                            var dlx1 = p1.DY;
                            var dly1 = -p1.DX;

                            // First edge normal vertex
                            var fx = p1.X + dlx0 * woff;
                            var fy = p1.Y + dly0 * woff;
                            if (fanIdx == 0) { cx = fx; cy = fy; }
                            else if (fanIdx == 1) { px = fx; py = fy; }
                            else
                            {
                                SetVert(ref _cache.Verts[vertOffset++], cx, cy, 0.5f, 1);
                                SetVert(ref _cache.Verts[vertOffset++], px, py, 0.5f, 1);
                                SetVert(ref _cache.Verts[vertOffset++], fx, fy, 0.5f, 1);
                                px = fx; py = fy;
                            }
                            fanIdx++;

                            // Second edge normal vertex
                            fx = p1.X + dlx1 * woff;
                            fy = p1.Y + dly1 * woff;
                            if (fanIdx == 1) { px = fx; py = fy; }
                            else
                            {
                                SetVert(ref _cache.Verts[vertOffset++], cx, cy, 0.5f, 1);
                                SetVert(ref _cache.Verts[vertOffset++], px, py, 0.5f, 1);
                                SetVert(ref _cache.Verts[vertOffset++], fx, fy, 0.5f, 1);
                                px = fx; py = fy;
                            }
                            fanIdx++;
                        }
                        else
                        {
                            // Smooth vertex: DM magnitude ≈ 1, safe to use.
                            var fx = p1.X + p1.DMX * woff;
                            var fy = p1.Y + p1.DMY * woff;
                            if (fanIdx == 0) { cx = fx; cy = fy; }
                            else if (fanIdx == 1) { px = fx; py = fy; }
                            else
                            {
                                SetVert(ref _cache.Verts[vertOffset++], cx, cy, 0.5f, 1);
                                SetVert(ref _cache.Verts[vertOffset++], px, py, 0.5f, 1);
                                SetVert(ref _cache.Verts[vertOffset++], fx, fy, 0.5f, 1);
                                px = fx; py = fy;
                            }
                            fanIdx++;
                        }
                    }
                }
                else
                {
                    // No fringe: simple fan without inset
                    for (var i = 1; i < directCount - 1; i++)
                    {
                        SetVert(ref _cache.Verts[vertOffset++], pts[0].X, pts[0].Y, 0.5f, 1);
                        SetVert(ref _cache.Verts[vertOffset++], pts[i].X, pts[i].Y, 0.5f, 1);
                        SetVert(ref _cache.Verts[vertOffset++], pts[i + 1].X, pts[i + 1].Y, 0.5f, 1);
                    }
                }
            }
            else
            {
                if (tessNeedsTransform)
                {
                    // Cached object-space tessellation: transform to screen-space
                    ref readonly var xform = ref GetState().Xform;
                    for (var i = 0; i < triangleCount; i++)
                    {
                        var i0 = tessIndices[i * 3];
                        var i1 = tessIndices[i * 3 + 1];
                        var i2 = tessIndices[i * 3 + 2];

                        TransformPoint(out var x0, out var y0, xform, tessVertices[i0].X, tessVertices[i0].Y);
                        TransformPoint(out var x1, out var y1, xform, tessVertices[i1].X, tessVertices[i1].Y);
                        TransformPoint(out var x2, out var y2, xform, tessVertices[i2].X, tessVertices[i2].Y);

                        SetVert(ref _cache.Verts[vertOffset++], x0, y0, 0.5f, 1);
                        SetVert(ref _cache.Verts[vertOffset++], x1, y1, 0.5f, 1);
                        SetVert(ref _cache.Verts[vertOffset++], x2, y2, 0.5f, 1);
                    }
                }
                else
                {
                    for (var i = 0; i < triangleCount; i++)
                    {
                        var i0 = tessIndices[i * 3];
                        var i1 = tessIndices[i * 3 + 1];
                        var i2 = tessIndices[i * 3 + 2];

                        var v0 = tessVertices[i0];
                        var v1 = tessVertices[i1];
                        var v2 = tessVertices[i2];

                        SetVert(ref _cache.Verts[vertOffset++], v0.X, v0.Y, 0.5f, 1);
                        SetVert(ref _cache.Verts[vertOffset++], v1.X, v1.Y, 0.5f, 1);
                        SetVert(ref _cache.Verts[vertOffset++], v2.X, v2.Y, 0.5f, 1);
                    }
                }
            }

            outPath.NFill = vertOffset - outPath.FillOffset;
        }

        _cache.NPaths = finalPathCount;
        _cache.NVerts = vertOffset;
    }

    private void ComputeFillFringeSigns(Span<float> signs, int sourcePathCount, TessWindingRule windingRule)
    {
        for (var i = 0; i < sourcePathCount; i++)
        {
            signs[i] = 1f;
            ref readonly var path = ref _cache.Paths[i];
            if (path.Count < 2)
            {
                continue;
            }

            var pts = _cache.Points.AsSpan(path.First, path.Count);
            var probe = Maxf(_fringeWidth * 0.75f, _distTol * 4.0f);

            if (TryProbeFringeSign(pts, 0, probe, sourcePathCount, windingRule, out var sign))
            {
                signs[i] = sign;
            }
            else if (path.Count > 2 &&
                     TryProbeFringeSign(pts, path.Count / 2, probe, sourcePathCount, windingRule, out sign))
            {
                // Retry at a different vertex (midpoint of contour).
                signs[i] = sign;
            }
            else
            {
                // Ambiguous — fall back to signed-area winding.
                signs[i] = path.Winding == NVGwinding.CW ? 1f : -1f;
            }
        }
    }

    private bool TryProbeFringeSign(ReadOnlySpan<NVGpoint> pts, int vertexIndex,
        float probe, int sourcePathCount, TessWindingRule windingRule, out float sign)
    {
        ref readonly var p = ref pts[vertexIndex];

        // Use normalized DM direction for probing.
        // DM (average of adjacent edge normals) reliably points toward/away from
        // the polygon interior, even at sharp corners where a single edge normal
        // (DL) may miss. Normalizing prevents magnitude explosion (DM up to 600x).
        var dmLen = p.DMX * p.DMX + p.DMY * p.DMY;
        float pdx, pdy;
        if (dmLen > 1e-6f)
        {
            dmLen = MathF.Sqrt(dmLen);
            pdx = p.DMX / dmLen;
            pdy = p.DMY / dmLen;
        }
        else
        {
            // DM degenerate — fall back to edge normal (DL)
            pdx = p.DY;
            pdy = -p.DX;
        }

        var plusX = p.X + pdx * probe;
        var plusY = p.Y + pdy * probe;
        var minusX = p.X - pdx * probe;
        var minusY = p.Y - pdy * probe;

        var plusInside = IsPointInsideFill(plusX, plusY, sourcePathCount, windingRule);
        var minusInside = IsPointInsideFill(minusX, minusY, sourcePathCount, windingRule);

        if (plusInside && !minusInside)
        {
            // +DM direction is inside → DM points inward → positive woff insets
            sign = 1f;
            return true;
        }

        if (!plusInside && minusInside)
        {
            // -DM direction is inside → DM points outward → negative woff insets
            sign = -1f;
            return true;
        }

        sign = 0f;
        return false;
    }

    private bool IsPointInsideFill(float x, float y, int sourcePathCount, TessWindingRule windingRule)
    {
        var winding = 0;
        var crossings = 0;

        for (var i = 0; i < sourcePathCount; i++)
        {
            ref readonly var path = ref _cache.Paths[i];
            if (path.Count < 2)
            {
                continue;
            }

            var pts = _cache.Points.AsSpan(path.First, path.Count);
            for (var j = 0; j < path.Count; j++)
            {
                ref readonly var a = ref pts[j];
                ref readonly var b = ref pts[(j + 1) % path.Count];

                var ayAbove = a.Y > y;
                var byAbove = b.Y > y;
                if (ayAbove == byAbove)
                {
                    continue;
                }

                var t = (y - a.Y) / (b.Y - a.Y);
                var xHit = a.X + t * (b.X - a.X);
                if (xHit <= x)
                {
                    continue;
                }

                crossings++;
                if (windingRule == TessWindingRule.NonZero)
                {
                    winding += b.Y > a.Y ? 1 : -1;
                }
            }
        }

        return windingRule == TessWindingRule.NonZero
            ? winding != 0
            : (crossings & 1) != 0;
    }

    private void ExpandStroke(float w, float fringe, NVGlineCap lineCap, NVGlineJoin lineJoin, float miterLimit)
    {
        var aa = fringe;
        float u0 = 0.0f, u1 = 1.0f;
        var ncap = CurveDivs(w, NVG_PI, _tessTol);
        if (lineJoin == NVGlineJoin.Round || lineCap == NVGlineCap.Round)
        {
            ncap = Maxi(ncap, 12);
        }

        w += aa * 0.5f;

        // Disable the gradient used for antialiasing when antialiasing is not used.
        if (aa == 0.0f)
        {
            u0 = 0.5f;
            u1 = 0.5f;
        }

        CalculateJoins(w, lineJoin, miterLimit);

        // Calculate max vertex usage
        var cverts = 0;
        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var path = ref _cache.Paths[i];
            var loop = path.Closed;
            if (lineJoin == NVGlineJoin.Round)
            {
                cverts += (path.Count + path.NBevel * (ncap + 2) + 1) * 2;
            }
            else
            {
                cverts += (path.Count + path.NBevel * 5 + 1) * 2;
            }

            if (!loop)
            {
                // Space for caps
                if (lineCap == NVGlineCap.Round)
                {
                    cverts += (ncap * 2 + 2) * 2;
                }
                else
                {
                    cverts += (3 + 3) * 2;
                }
            }
        }

        EnsureVerts(cverts);

        var vertOffset = 0;
        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var path = ref _cache.Paths[i];
            var pts = _cache.Points.AsSpan(path.First, path.Count);

            path.FillOffset = 0;
            path.NFill = 0;

            // Calculate stroke
            var loop = path.Closed;
            path.StrokeOffset = vertOffset;

            int s, e;
            if (loop)
            {
                s = 0;
                e = path.Count;
            }
            else
            {
                s = 1;
                e = path.Count - 1;
            }

            if (!loop && path.Count > 0)
            {
                // Add cap
                ref var p0 = ref pts[0];
                ref var p1 = ref pts[1];
                var dx = p1.X - p0.X;
                var dy = p1.Y - p0.Y;
                Normalize(ref dx, ref dy);

                if (lineCap == NVGlineCap.Butt)
                {
                    ButtCapStart(ref vertOffset, ref p0, dx, dy, w, -aa * 0.5f, aa, u0, u1);
                }
                else if (lineCap == NVGlineCap.Square)
                {
                    ButtCapStart(ref vertOffset, ref p0, dx, dy, w, w - aa, aa, u0, u1);
                }
                else if (lineCap == NVGlineCap.Round)
                {
                    RoundCapStart(ref vertOffset, ref p0, dx, dy, w, ncap, aa, u0, u1);
                }
            }

            for (var j = s; j < e; j++)
            {
                ref var p0 = ref pts[(j + path.Count - 1) % path.Count];
                ref var p1 = ref pts[j];

                if ((p1.Flags & (NVGpointFlags.Bevel | NVGpointFlags.InnerBevel)) != 0)
                {
                    if (lineJoin == NVGlineJoin.Round)
                    {
                        RoundJoin(ref vertOffset, ref p0, ref p1, w, w, u0, u1, ncap, aa);
                    }
                    else
                    {
                        BevelJoin(ref vertOffset, ref p0, ref p1, w, w, u0, u1, aa);
                    }
                }
                else
                {
                    SetVert(ref _cache.Verts[vertOffset++], p1.X + p1.DMX * w, p1.Y + p1.DMY * w, u0, 1);
                    SetVert(ref _cache.Verts[vertOffset++], p1.X - p1.DMX * w, p1.Y - p1.DMY * w, u1, 1);
                }
            }

            if (loop)
            {
                // Loop it
                SetVert(ref _cache.Verts[vertOffset++], _cache.Verts[path.StrokeOffset].X, _cache.Verts[path.StrokeOffset].Y, u0, 1);
                SetVert(ref _cache.Verts[vertOffset++], _cache.Verts[path.StrokeOffset + 1].X, _cache.Verts[path.StrokeOffset + 1].Y, u1, 1);
            }
            else if (path.Count > 1)
            {
                // Add cap
                ref var p0 = ref pts[path.Count - 2];
                ref var p1 = ref pts[path.Count - 1];
                var dx = p1.X - p0.X;
                var dy = p1.Y - p0.Y;
                Normalize(ref dx, ref dy);

                if (lineCap == NVGlineCap.Butt)
                {
                    ButtCapEnd(ref vertOffset, ref p1, dx, dy, w, -aa * 0.5f, aa, u0, u1);
                }
                else if (lineCap == NVGlineCap.Square)
                {
                    ButtCapEnd(ref vertOffset, ref p1, dx, dy, w, w - aa, aa, u0, u1);
                }
                else if (lineCap == NVGlineCap.Round)
                {
                    RoundCapEnd(ref vertOffset, ref p1, dx, dy, w, ncap, aa, u0, u1);
                }
            }

            path.NStroke = vertOffset - path.StrokeOffset;
        }
    }

    private void CalculateJoins(float w, NVGlineJoin lineJoin, float miterLimit)
    {
        var iw = w > 0.0f ? 1.0f / w : 0.0f;

        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var path = ref _cache.Paths[i];
            var pts = _cache.Points.AsSpan(path.First, path.Count);
            var nleft = 0;
            path.NBevel = 0;

            for (var j = 0; j < path.Count; j++)
            {
                ref var p0 = ref pts[(j + path.Count - 1) % path.Count];
                ref var p1 = ref pts[j];

                var dlx0 = p0.DY;
                var dly0 = -p0.DX;
                var dlx1 = p1.DY;
                var dly1 = -p1.DX;

                // Calculate extrusions
                p1.DMX = (dlx0 + dlx1) * 0.5f;
                p1.DMY = (dly0 + dly1) * 0.5f;
                var dmr2 = p1.DMX * p1.DMX + p1.DMY * p1.DMY;
                if (dmr2 > 0.000001f)
                {
                    var scale = 1.0f / dmr2;
                    if (scale > 600.0f)
                    {
                        scale = 600.0f;
                    }

                    p1.DMX *= scale;
                    p1.DMY *= scale;
                }

                // Clear flags, but keep the corner
                p1.Flags = (p1.Flags & NVGpointFlags.Corner) != 0 ? NVGpointFlags.Corner : 0;

                // Keep track of left turns
                var cross = p1.DX * p0.DY - p0.DX * p1.DY;
                if (cross > 0.0f)
                {
                    nleft++;
                    p1.Flags |= NVGpointFlags.Left;
                }

                // Calculate if we should use bevel or miter for inner join
                var limit = Maxf(1.01f, Minf(p0.Len, p1.Len) * iw);
                if (dmr2 * limit * limit < 1.0f)
                {
                    p1.Flags |= NVGpointFlags.InnerBevel;
                }

                // Check to see if the corner needs to be beveled
                if ((p1.Flags & NVGpointFlags.Corner) != 0)
                {
                    if (dmr2 * miterLimit * miterLimit < 1.0f || lineJoin == NVGlineJoin.Bevel || lineJoin == NVGlineJoin.Round)
                    {
                        p1.Flags |= NVGpointFlags.Bevel;
                    }
                }

                if ((p1.Flags & (NVGpointFlags.Bevel | NVGpointFlags.InnerBevel)) != 0)
                {
                    path.NBevel++;
                }

                // Store back
                pts[j] = p1;
            }

            path.Convex = nleft == path.Count;
        }
    }

    private void EnsureVerts(int count)
    {
        if (count > _cache.CVerts)
        {
            var cverts = (count + 0xff) & ~0xff;
            Array.Resize(ref _cache.Verts, cverts);
            _cache.CVerts = cverts;
        }
    }

    private void EnsurePathCapacity(int count)
    {
        if (count > _cache.CPaths)
        {
            var cpaths = (count + 0x0f) & ~0x0f;
            Array.Resize(ref _cache.Paths, cpaths);
            _cache.CPaths = cpaths;
        }
    }

    private void NormalizeContoursForFill(float epsWorld, float collinearTol)
    {
        if (_cache.NPaths == 0)
        {
            return;
        }

        var writePath = 0;
        var writePoint = 0;

        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var srcPath = ref _cache.Paths[i];
            if (srcPath.Count < 2)
            {
                continue;
            }

            var src = _cache.Points.AsSpan(srcPath.First, srcPath.Count);
            var dstStart = writePoint;

            var hasPrev = false;
            NVGpoint prev = default;
            for (var j = 0; j < src.Length; j++)
            {
                var p = src[j];
                if (!hasPrev || !PtEquals(prev.X, prev.Y, p.X, p.Y, epsWorld))
                {
                    _cache.Points[writePoint++] = p;
                    prev = p;
                    hasPrev = true;
                }
            }

            var count = writePoint - dstStart;
            if (count > 1 &&
                PtEquals(
                    _cache.Points[dstStart].X, _cache.Points[dstStart].Y,
                    _cache.Points[dstStart + count - 1].X, _cache.Points[dstStart + count - 1].Y,
                    epsWorld))
            {
                count--;
                writePoint--;
            }

            if (count < 3)
            {
                writePoint = dstStart;
                continue;
            }

            RemoveCollinearVerticesInPlace(dstStart, ref count, collinearTol);
            if (count < 3)
            {
                writePoint = dstStart;
                continue;
            }

            var dstPath = srcPath;
            dstPath.First = dstStart;
            dstPath.Count = count;
            dstPath.Closed = true;
            RecomputePathSegmentData(dstStart, count);
            var signedArea2 = ComputeSignedArea2(dstStart, count);
            dstPath.Winding = signedArea2 >= 0.0 ? NVGwinding.CCW : NVGwinding.CW;
            _cache.Paths[writePath++] = dstPath;
            writePoint = dstStart + count;
        }

        _cache.NPaths = writePath;
        _cache.NPoints = writePoint;
        RecalculatePathBounds();
    }

    private void RemoveCollinearVerticesInPlace(int start, ref int count, float collinearTol)
    {
        if (count < 3)
        {
            return;
        }

        var rentedA = System.Buffers.ArrayPool<NVGpoint>.Shared.Rent(count);
        var rentedB = System.Buffers.ArrayPool<NVGpoint>.Shared.Rent(count);
        try
        {
            var src = rentedA.AsSpan(0, count);
            var dst = rentedB.AsSpan(0, count);
            _cache.Points.AsSpan(start, count).CopyTo(src);
            var srcCount = count;

            while (srcCount >= 3)
            {
                var removed = false;
                var dstCount = 0;
                for (var i = 0; i < srcCount; i++)
                {
                    var iPrev = (i + srcCount - 1) % srcCount;
                    var iNext = (i + 1) % srcCount;

                    ref readonly var a = ref src[iPrev];
                    ref readonly var b = ref src[i];
                    ref readonly var c = ref src[iNext];
                    if (IsNearlyCollinear(in a, in b, in c, collinearTol))
                    {
                        removed = true;
                        continue;
                    }

                    dst[dstCount++] = b;
                }

                srcCount = dstCount;
                if (!removed)
                {
                    break;
                }

                var tmp = src;
                src = dst;
                dst = tmp;
            }

            count = srcCount;
            if (count > 0)
            {
                src.Slice(0, count).CopyTo(_cache.Points.AsSpan(start, count));
            }
        }
        finally
        {
            System.Buffers.ArrayPool<NVGpoint>.Shared.Return(rentedA, clearArray: false);
            System.Buffers.ArrayPool<NVGpoint>.Shared.Return(rentedB, clearArray: false);
        }
    }

    private static bool IsNearlyCollinear(in NVGpoint a, in NVGpoint b, in NVGpoint c, float collinearTol)
    {
        var abx = (double)b.X - a.X;
        var aby = (double)b.Y - a.Y;
        var bcx = (double)c.X - b.X;
        var bcy = (double)c.Y - b.Y;

        var ab2 = abx * abx + aby * aby;
        var bc2 = bcx * bcx + bcy * bcy;
        var tol2 = (double)collinearTol * collinearTol;
        if (ab2 <= tol2 || bc2 <= tol2)
        {
            return true;
        }

        var cross = abx * bcy - aby * bcx;
        var cross2 = cross * cross;
        // (|ab|+|bc|)^2 <= 2*(|ab|^2+|bc|^2). Avoids sqrt in hot path.
        var rhs = tol2 * 2.0 * (ab2 + bc2);
        return cross2 <= rhs;
    }

    private void RecomputePathSegmentData(int start, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            ref var p0 = ref _cache.Points[start + i];
            ref var p1 = ref _cache.Points[start + next];
            p0.DX = p1.X - p0.X;
            p0.DY = p1.Y - p0.Y;
            p0.Len = Normalize(ref p0.DX, ref p0.DY);
        }
    }

    private double ComputeSignedArea2(int start, int count)
    {
        double area2 = 0.0;
        for (var i = 0; i < count; i++)
        {
            var j = (i + 1) % count;
            ref readonly var a = ref _cache.Points[start + i];
            ref readonly var b = ref _cache.Points[start + j];
            area2 += (double)a.X * b.Y - (double)b.X * a.Y;
        }

        return area2;
    }

    private void RecalculatePathBounds()
    {
        if (_cache.NPaths == 0)
        {
            _cache.Bounds[0] = 0;
            _cache.Bounds[1] = 0;
            _cache.Bounds[2] = 0;
            _cache.Bounds[3] = 0;
            return;
        }

        _cache.Bounds[0] = _cache.Bounds[1] = 1e6f;
        _cache.Bounds[2] = _cache.Bounds[3] = -1e6f;

        for (var i = 0; i < _cache.NPaths; i++)
        {
            ref var path = ref _cache.Paths[i];
            var pts = _cache.Points.AsSpan(path.First, path.Count);
            for (var j = 0; j < pts.Length; j++)
            {
                ref var p = ref pts[j];
                _cache.Bounds[0] = Minf(_cache.Bounds[0], p.X);
                _cache.Bounds[1] = Minf(_cache.Bounds[1], p.Y);
                _cache.Bounds[2] = Maxf(_cache.Bounds[2], p.X);
                _cache.Bounds[3] = Maxf(_cache.Bounds[3], p.Y);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetVert(ref NVGvertex vtx, float x, float y, float u, float v)
    {
        vtx.X = x;
        vtx.Y = y;
        vtx.U = u;
        vtx.V = v;
    }

    /// <summary>
    /// Offset a point along its DM vector for tessellator inset, clamping DM magnitude
    /// to 1 to prevent overshoot at sharp corners (where DM can be up to 600x).
    /// </summary>
    private static void InsetPoint(ref Vector2 dst, in NVGpoint pt, float woff)
    {
        var dmx = pt.DMX;
        var dmy = pt.DMY;
        var dmSq = dmx * dmx + dmy * dmy;
        if (dmSq > 1.0f)
        {
            var invLen = 1.0f / MathF.Sqrt(dmSq);
            dmx *= invLen;
            dmy *= invLen;
        }
        dst = new Vector2(pt.X + dmx * woff, pt.Y + dmy * woff);
    }

    private void BevelJoin(ref int dst, ref NVGpoint p0, ref NVGpoint p1, float lw, float rw, float lu, float ru, float fringe)
    {
        var dlx0 = p0.DY;
        var dly0 = -p0.DX;
        var dlx1 = p1.DY;
        var dly1 = -p1.DX;

        if ((p1.Flags & NVGpointFlags.Left) != 0)
        {
            float lx0, ly0, lx1, ly1;
            ChooseBevel((p1.Flags & NVGpointFlags.InnerBevel) != 0, ref p0, ref p1, lw, out lx0, out ly0, out lx1, out ly1);

            SetVert(ref _cache.Verts[dst++], lx0, ly0, lu, 1);
            SetVert(ref _cache.Verts[dst++], p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1);

            if ((p1.Flags & NVGpointFlags.Bevel) != 0)
            {
                SetVert(ref _cache.Verts[dst++], lx0, ly0, lu, 1);
                SetVert(ref _cache.Verts[dst++], p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1);
                SetVert(ref _cache.Verts[dst++], lx1, ly1, lu, 1);
                SetVert(ref _cache.Verts[dst++], p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1);
            }
            else
            {
                var rx0 = p1.X - p1.DMX * rw;
                var ry0 = p1.Y - p1.DMY * rw;

                SetVert(ref _cache.Verts[dst++], p1.X, p1.Y, 0.5f, 1);
                SetVert(ref _cache.Verts[dst++], p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1);
                SetVert(ref _cache.Verts[dst++], rx0, ry0, ru, 1);
                SetVert(ref _cache.Verts[dst++], rx0, ry0, ru, 1);
                SetVert(ref _cache.Verts[dst++], p1.X, p1.Y, 0.5f, 1);
                SetVert(ref _cache.Verts[dst++], p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1);
            }

            SetVert(ref _cache.Verts[dst++], lx1, ly1, lu, 1);
            SetVert(ref _cache.Verts[dst++], p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1);
        }
        else
        {
            float rx0, ry0, rx1, ry1;
            ChooseBevel((p1.Flags & NVGpointFlags.InnerBevel) != 0, ref p0, ref p1, -rw, out rx0, out ry0, out rx1, out ry1);

            SetVert(ref _cache.Verts[dst++], p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1);
            SetVert(ref _cache.Verts[dst++], rx0, ry0, ru, 1);

            if ((p1.Flags & NVGpointFlags.Bevel) != 0)
            {
                SetVert(ref _cache.Verts[dst++], p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1);
                SetVert(ref _cache.Verts[dst++], rx0, ry0, ru, 1);
                SetVert(ref _cache.Verts[dst++], p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1);
                SetVert(ref _cache.Verts[dst++], rx1, ry1, ru, 1);
            }
            else
            {
                var lx0 = p1.X + p1.DMX * lw;
                var ly0 = p1.Y + p1.DMY * lw;

                SetVert(ref _cache.Verts[dst++], p1.X + dlx0 * lw, p1.Y + dly0 * lw, lu, 1);
                SetVert(ref _cache.Verts[dst++], p1.X, p1.Y, 0.5f, 1);
                SetVert(ref _cache.Verts[dst++], lx0, ly0, lu, 1);
                SetVert(ref _cache.Verts[dst++], lx0, ly0, lu, 1);
                SetVert(ref _cache.Verts[dst++], p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1);
                SetVert(ref _cache.Verts[dst++], p1.X, p1.Y, 0.5f, 1);
            }

            SetVert(ref _cache.Verts[dst++], p1.X + dlx1 * lw, p1.Y + dly1 * lw, lu, 1);
            SetVert(ref _cache.Verts[dst++], rx1, ry1, ru, 1);
        }
    }

    private void RoundJoin(ref int dst, ref NVGpoint p0, ref NVGpoint p1, float lw, float rw, float lu, float ru, int ncap, float fringe)
    {
        var dlx0 = p0.DY;
        var dly0 = -p0.DX;
        var dlx1 = p1.DY;
        var dly1 = -p1.DX;

        if ((p1.Flags & NVGpointFlags.Left) != 0)
        {
            float lx0, ly0, lx1, ly1;
            ChooseBevel((p1.Flags & NVGpointFlags.InnerBevel) != 0, ref p0, ref p1, lw, out lx0, out ly0, out lx1, out ly1);
            var a0 = Atan2f(-dly0, -dlx0);
            var a1 = Atan2f(-dly1, -dlx1);
            if (a1 > a0)
            {
                a1 -= NVG_PI * 2;
            }

            SetVert(ref _cache.Verts[dst++], lx0, ly0, lu, 1);
            SetVert(ref _cache.Verts[dst++], p1.X - dlx0 * rw, p1.Y - dly0 * rw, ru, 1);

            var n = Clampi((int)MathF.Ceiling((a0 - a1) / NVG_PI * ncap), 2, ncap);
            for (var i = 0; i < n; i++)
            {
                var u = i / (float)(n - 1);
                var a = a0 + u * (a1 - a0);
                var rx = p1.X + Cosf(a) * rw;
                var ry = p1.Y + Sinf(a) * rw;
                SetVert(ref _cache.Verts[dst++], p1.X, p1.Y, 0.5f, 1);
                SetVert(ref _cache.Verts[dst++], rx, ry, ru, 1);
            }

            SetVert(ref _cache.Verts[dst++], lx1, ly1, lu, 1);
            SetVert(ref _cache.Verts[dst++], p1.X - dlx1 * rw, p1.Y - dly1 * rw, ru, 1);
        }
        else
        {
            float rx0, ry0, rx1, ry1;
            ChooseBevel((p1.Flags & NVGpointFlags.InnerBevel) != 0, ref p0, ref p1, -rw, out rx0, out ry0, out rx1, out ry1);
            var a0 = Atan2f(dly0, dlx0);
            var a1 = Atan2f(dly1, dlx1);
            if (a1 < a0)
            {
                a1 += NVG_PI * 2;
            }

            SetVert(ref _cache.Verts[dst++], p1.X + dlx0 * rw, p1.Y + dly0 * rw, lu, 1);
            SetVert(ref _cache.Verts[dst++], rx0, ry0, ru, 1);

            var n = Clampi((int)MathF.Ceiling((a1 - a0) / NVG_PI * ncap), 2, ncap);
            for (var i = 0; i < n; i++)
            {
                var u = i / (float)(n - 1);
                var a = a0 + u * (a1 - a0);
                var lx = p1.X + Cosf(a) * lw;
                var ly = p1.Y + Sinf(a) * lw;
                SetVert(ref _cache.Verts[dst++], lx, ly, lu, 1);
                SetVert(ref _cache.Verts[dst++], p1.X, p1.Y, 0.5f, 1);
            }

            SetVert(ref _cache.Verts[dst++], p1.X + dlx1 * rw, p1.Y + dly1 * rw, lu, 1);
            SetVert(ref _cache.Verts[dst++], rx1, ry1, ru, 1);
        }
    }

    private static void ChooseBevel(bool bevel, ref NVGpoint p0, ref NVGpoint p1, float w, out float x0, out float y0, out float x1, out float y1)
    {
        if (bevel)
        {
            x0 = p1.X + p0.DY * w;
            y0 = p1.Y - p0.DX * w;
            x1 = p1.X + p1.DY * w;
            y1 = p1.Y - p1.DX * w;
        }
        else
        {
            x0 = p1.X + p1.DMX * w;
            y0 = p1.Y + p1.DMY * w;
            x1 = p1.X + p1.DMX * w;
            y1 = p1.Y + p1.DMY * w;
        }
    }

    private void ButtCapStart(ref int dst, ref NVGpoint p, float dx, float dy, float w, float d, float aa, float u0, float u1)
    {
        var px = p.X - dx * d;
        var py = p.Y - dy * d;
        var dlx = dy;
        var dly = -dx;
        SetVert(ref _cache.Verts[dst++], px + dlx * w - dx * aa, py + dly * w - dy * aa, u0, 0);
        SetVert(ref _cache.Verts[dst++], px - dlx * w - dx * aa, py - dly * w - dy * aa, u1, 0);
        SetVert(ref _cache.Verts[dst++], px + dlx * w, py + dly * w, u0, 1);
        SetVert(ref _cache.Verts[dst++], px - dlx * w, py - dly * w, u1, 1);
    }

    private void ButtCapEnd(ref int dst, ref NVGpoint p, float dx, float dy, float w, float d, float aa, float u0, float u1)
    {
        var px = p.X + dx * d;
        var py = p.Y + dy * d;
        var dlx = dy;
        var dly = -dx;
        SetVert(ref _cache.Verts[dst++], px + dlx * w, py + dly * w, u0, 1);
        SetVert(ref _cache.Verts[dst++], px - dlx * w, py - dly * w, u1, 1);
        SetVert(ref _cache.Verts[dst++], px + dlx * w + dx * aa, py + dly * w + dy * aa, u0, 0);
        SetVert(ref _cache.Verts[dst++], px - dlx * w + dx * aa, py - dly * w + dy * aa, u1, 0);
    }

    private void RoundCapStart(ref int dst, ref NVGpoint p, float dx, float dy, float w, int ncap, float aa, float u0, float u1)
    {
        var px = p.X;
        var py = p.Y;
        var dlx = dy;
        var dly = -dx;

        for (var i = 0; i < ncap; i++)
        {
            var a = i / (float)(ncap - 1) * NVG_PI;
            float ax = Cosf(a) * w, ay = Sinf(a) * w;
            SetVert(ref _cache.Verts[dst++], px - dlx * ax - dx * ay, py - dly * ax - dy * ay, u0, 1);
            SetVert(ref _cache.Verts[dst++], px, py, 0.5f, 1);
        }
        SetVert(ref _cache.Verts[dst++], px + dlx * w, py + dly * w, u0, 1);
        SetVert(ref _cache.Verts[dst++], px - dlx * w, py - dly * w, u1, 1);
    }

    private void RoundCapEnd(ref int dst, ref NVGpoint p, float dx, float dy, float w, int ncap, float aa, float u0, float u1)
    {
        var px = p.X;
        var py = p.Y;
        var dlx = dy;
        var dly = -dx;

        SetVert(ref _cache.Verts[dst++], px + dlx * w, py + dly * w, u0, 1);
        SetVert(ref _cache.Verts[dst++], px - dlx * w, py - dly * w, u1, 1);

        for (var i = 0; i < ncap; i++)
        {
            var a = i / (float)(ncap - 1) * NVG_PI;
            float ax = Cosf(a) * w, ay = Sinf(a) * w;
            SetVert(ref _cache.Verts[dst++], px, py, 0.5f, 1);
            SetVert(ref _cache.Verts[dst++], px - dlx * ax + dx * ay, py - dly * ax + dy * ay, u0, 1);
        }
    }

    #endregion

    #region Gradients

    public NVGpaint LinearGradient(float sx, float sy, float ex, float ey, NVGcolor icol, NVGcolor ocol)
    {
        const float large = 1e5f;

        NVGpaint p = default;

        var dx = ex - sx;
        var dy = ey - sy;
        var d = Sqrtf(dx * dx + dy * dy);
        if (d > 0.0001f)
        {
            dx /= d;
            dy /= d;
        }
        else
        {
            dx = 0;
            dy = 1;
        }

        p.Xform[0] = dy; p.Xform[1] = -dx;
        p.Xform[2] = dx; p.Xform[3] = dy;
        p.Xform[4] = sx - dx * large; p.Xform[5] = sy - dy * large;

        p.Extent[0] = large;
        p.Extent[1] = large + d * 0.5f;

        p.Radius = 0.0f;
        p.Feather = Maxf(1.0f, d);
        p.InnerColor = icol;
        p.OuterColor = ocol;

        return p;
    }

    public NVGpaint RadialGradient(float cx, float cy, float inr, float outr, NVGcolor icol, NVGcolor ocol)
    {
        NVGpaint p = default;

        var r = (inr + outr) * 0.5f;
        var f = outr - inr;

        TransformIdentity(p.Xform);
        p.Xform[4] = cx;
        p.Xform[5] = cy;

        p.Extent[0] = r;
        p.Extent[1] = r;

        p.Radius = r;
        p.Feather = Maxf(1.0f, f);
        p.InnerColor = icol;
        p.OuterColor = ocol;

        return p;
    }

    public NVGpaint GradientRadial(in Matrix3x2 gradientTransform, float centerX, float centerY, float focalX, float focalY, float radiusX, float radiusY, int spreadMethod, int image)
    {
        NVGpaint p = default;
        p.PaintKind = (int)NVGpaintKind.GradientRadial;
        p.Image = image;
        p.Center[0] = centerX;
        p.Center[1] = centerY;
        p.Focal[0] = focalX;
        p.Focal[1] = focalY;
        p.Radius2[0] = radiusX;
        p.Radius2[1] = radiusY;
        p.SpreadMethod = spreadMethod;
        p.InnerColor = NVGcolor.White;
        p.OuterColor = NVGcolor.White;
        p.Xform[0] = gradientTransform.M11;
        p.Xform[1] = gradientTransform.M12;
        p.Xform[2] = gradientTransform.M21;
        p.Xform[3] = gradientTransform.M22;
        p.Xform[4] = gradientTransform.M31;
        p.Xform[5] = gradientTransform.M32;
        return p;
    }

    public NVGpaint GradientLinear(in Matrix3x2 gradientTransform, float startX, float startY, float endX, float endY, int spreadMethod, int image)
    {
        NVGpaint p = default;
        p.PaintKind = (int)NVGpaintKind.GradientLinear;
        p.Image = image;
        p.Center[0] = startX;
        p.Center[1] = startY;
        p.Focal[0] = endX;
        p.Focal[1] = endY;
        p.SpreadMethod = spreadMethod;
        p.InnerColor = NVGcolor.White;
        p.OuterColor = NVGcolor.White;
        p.Xform[0] = gradientTransform.M11;
        p.Xform[1] = gradientTransform.M12;
        p.Xform[2] = gradientTransform.M21;
        p.Xform[3] = gradientTransform.M22;
        p.Xform[4] = gradientTransform.M31;
        p.Xform[5] = gradientTransform.M32;
        return p;
    }

    public NVGpaint BoxGradient(float x, float y, float w, float h, float r, float f, NVGcolor icol, NVGcolor ocol)
    {
        NVGpaint p = default;

        TransformIdentity(p.Xform);
        p.Xform[4] = x + w * 0.5f;
        p.Xform[5] = y + h * 0.5f;

        p.Extent[0] = w * 0.5f;
        p.Extent[1] = h * 0.5f;

        p.Radius = r;
        p.Feather = Maxf(1.0f, f);
        p.InnerColor = icol;
        p.OuterColor = ocol;

        return p;
    }

    public NVGpaint ImagePattern(float cx, float cy, float w, float h, float angle, int image, float alpha)
    {
        NVGpaint p = default;

        TransformRotate(p.Xform, angle);
        p.Xform[4] = cx;
        p.Xform[5] = cy;

        p.Extent[0] = w;
        p.Extent[1] = h;

        p.Image = image;
        p.InnerColor = p.OuterColor = NVGcolor.RGBAf(1, 1, 1, alpha);

        return p;
    }

    #endregion
}
