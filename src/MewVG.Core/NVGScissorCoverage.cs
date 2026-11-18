namespace Aprillz.MewVG;

/// <summary>Decides whether a scissor changes any pixel of a draw, so a renderer can leave the scissor test out.</summary>
internal static class NVGScissorCoverage
{
    // How close to the edge of a scissor's full strength, in device pixels, a pixel centre counts as reaching it,
    // so that rounding never leaves the scissor out of a draw it cuts.
    private const float EDGE_TOLERANCE = 1.0f / 256.0f;

    /// <summary>
    /// False when the scissor leaves every device pixel the box can cover, inside a view of the given size and
    /// pixel ratio, at full strength.
    /// </summary>
    public static bool Cuts(ref NVGscissorState scissor, float minX, float minY, float maxX, float maxY, float fringe,
        float viewWidth, float viewHeight, float pixelRatio)
    {
        if (scissor.Extent[0] < -0.5f || scissor.Extent[1] < -0.5f)
        {
            return false;
        }

        // Only an unrotated scissor is a box.
        if (scissor.Xform[1] != 0.0f || scissor.Xform[2] != 0.0f)
        {
            return true;
        }

        // The shader fades the scissor over one fringe centred on its edge, so full strength starts half a fringe
        // inside; with no fringe the edge is hard.
        var halfX = (scissor.Extent[0] * MathF.Abs(scissor.Xform[0])) - (fringe * 0.5f);
        var halfY = (scissor.Extent[1] * MathF.Abs(scissor.Xform[3])) - (fringe * 0.5f);
        return PixelCentresReachPast(minX, maxX, viewWidth, scissor.Xform[4], halfX, pixelRatio) ||
               PixelCentresReachPast(minY, maxY, viewHeight, scissor.Xform[5], halfY, pixelRatio);
    }

    /// <summary>
    /// Whether the centre of a device pixel between the two ends, inside the view, lies at or beyond
    /// <paramref name="half"/> from the centre. All values but the ratio are in view units.
    /// </summary>
    private static bool PixelCentresReachPast(float low, float high, float view, float centre, float half, float ratio)
    {
        // Device pixel centres sit on half coordinates once the view units are scaled by the pixel ratio.
        low = MathF.Max(low, 0.0f) * ratio;
        high = MathF.Min(high, view) * ratio;
        var first = MathF.Ceiling(low - 0.5f) + 0.5f;
        var last = MathF.Floor(high - 0.5f) + 0.5f;
        var start = ((centre - half) * ratio) + EDGE_TOLERANCE;
        var end = ((centre + half) * ratio) - EDGE_TOLERANCE;
        return first <= last && (first < start || last > end);
    }
}
