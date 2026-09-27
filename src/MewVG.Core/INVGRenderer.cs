// NanoVG renderer abstraction (core/backend split)

namespace Aprillz.MewVG;

internal interface INVGRenderer
{
    void BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio);

    void Cancel();

    void Flush();

    void RenderFill(
        ref MewVGPaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts);

    void RenderStroke(
        ref MewVGPaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        float strokeWidth,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts);

    void RenderClip(
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts);

    void ResetClip();

    /// <summary>Fills the device-space rectangle <paramref name="bounds"/> (x0, y0, x1, y1) with
    /// <paramref name="paint"/>, multiplying alpha by the 8-bit <paramref name="coverage"/> mask of
    /// <paramref name="maskWidth"/> x <paramref name="maskHeight"/> pixels whose top-left sits at
    /// (<paramref name="maskOriginX"/>, <paramref name="maskOriginY"/>) in frame units. The renderer
    /// owns every native resource behind the mask: with a <paramref name="cacheKey"/> it may keep
    /// the upload on the device and reuse it while <paramref name="cacheVersion"/> is unchanged; a
    /// null key is a mask for this draw only. <paramref name="coverage"/> is valid only during the call.</summary>
    void RenderMaskFill(
        ref MewVGPaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<byte> coverage,
        int maskWidth,
        int maskHeight,
        float maskOriginX,
        float maskOriginY,
        object? cacheKey,
        int cacheVersion);
}
