// NanoVG renderer abstraction (core/backend split)

namespace Aprillz.MewVG;

internal interface INVGRenderer
{
    void BeginFrame(float windowWidth, float windowHeight, float devicePixelRatio);

    void Cancel();

    void Flush();

    void RenderFill(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        ReadOnlySpan<float> bounds,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts);

    void RenderStroke(
        ref NVGpaint paint,
        NVGcompositeOperationState compositeOperation,
        ref NVGscissorState scissor,
        float fringe,
        float strokeWidth,
        ReadOnlySpan<NVGpathData> paths,
        ReadOnlySpan<NVGvertex> verts);
}