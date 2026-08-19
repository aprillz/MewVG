using Aprillz.MewVG;

namespace MewVG.Core.Tests;

/// <summary>
/// Captures RenderFill/RenderStroke/RenderClip calls (paths + vertex data) for
/// inspection in tests. Vertex/path spans are transient in INVGRenderer, so
/// every call is snapshotted into owned arrays.
/// </summary>
internal sealed class FakeRenderer : INVGRenderer
{
    public sealed record Call(NVGpathData[] Paths, NVGvertex[] Verts, float Fringe);

    public sealed record StrokeCallData(NVGpathData[] Paths, NVGvertex[] Verts, float Fringe, float StrokeWidth);

    /// <summary>
    /// One RenderClip invocation, fully snapshotted (paths/verts/bounds copied into
    /// owned arrays, scissor copied by value) at call time so later buffer reuse in
    /// NVGContext's clip pool cannot retroactively corrupt an already-recorded call.
    /// </summary>
    public sealed record ClipCall(NVGpathData[] Paths, NVGvertex[] Verts, float Fringe, float[] Bounds, NVGscissorState Scissor);

    /// <summary>Ordered log entry: either a ResetClip call or a RenderClip call, in call order.</summary>
    public abstract record ClipEvent;

    public sealed record ClipResetEvent : ClipEvent;

    public sealed record ClipRenderEvent(ClipCall Data) : ClipEvent;

    public List<Call> FillCalls { get; } = new();

    public List<StrokeCallData> StrokeCalls { get; } = new();

    public List<ClipCall> ClipCalls { get; } = new();

    /// <summary>Interleaved ResetClip/RenderClip log, in call order, for verifying exact retransmission sequences.</summary>
    public List<ClipEvent> ClipEvents { get; } = new();

    public int ResetClipCount { get; private set; }

    public int FlushCount { get; private set; }

    public int CancelCount { get; private set; }

    public void BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio)
    {
    }

    public void Cancel() => CancelCount++;

    public void Flush() => FlushCount++;

    public void RenderFill(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        FillCalls.Add(new Call(paths.ToArray(), verts.ToArray(), fringe));
    }

    public void RenderStroke(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        float strokeWidth,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        StrokeCalls.Add(new StrokeCallData(paths.ToArray(), verts.ToArray(), fringe, strokeWidth));
    }

    public void RenderClip(
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts)
    {
        // Deep copy at call time: paths/verts/bounds arrays here are grow-only
        // buffers owned by NVGContext's clip pool and may be mutated or reused
        // for a different clip depth right after this call returns.
        var call = new ClipCall(paths.ToArray(), verts.ToArray(), fringe, bounds.ToArray(), scissor);
        ClipCalls.Add(call);
        ClipEvents.Add(new ClipRenderEvent(call));
    }

    public void ResetClip()
    {
        ResetClipCount++;
        ClipEvents.Add(new ClipResetEvent());
    }
}

/// <summary>
/// Minimal concrete NanoVG subclass so tests can drive the public facade without
/// a real GL/Metal backend. Image operations are no-ops; tests only exercise
/// path/fill/stroke.
/// </summary>
internal sealed class TestNanoVG : NanoVG
{
    public TestNanoVG(INVGRenderer renderer, bool edgeAntiAlias) : base(renderer, edgeAntiAlias)
    {
    }

    public override int CreateImageRGBA(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data) => 0;

    public override int CreateImageAlpha(int width, int height, NVGimageFlags imageFlags, ReadOnlySpan<byte> data) => 0;

    public override bool UpdateImage(int image, ReadOnlySpan<byte> data) => false;

    public override bool ImageSize(int image, out int width, out int height)
    {
        width = 0;
        height = 0;
        return false;
    }

    public override void DeleteImage(int image)
    {
    }

    public override int CreateImageFromHandle(int textureId, int width, int height, NVGimageFlags flags) => 0;

    public override int ImageHandle(int image) => 0;

    protected override void DisposeBackend()
    {
    }
}
