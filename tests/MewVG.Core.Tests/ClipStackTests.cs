using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// Regression coverage for the clip stack's grow-only buffer pool (NVGContext's
/// _clipBufferPool / NVGClipPath). The invariant under test: _clipStack is always
/// a prefix of _clipBufferPool, and a pooled buffer slot that gets reused for a
/// new Clip() call at the same depth must never leak data from whatever clip
/// previously occupied that slot into a later retransmission (Restore's re-render
/// loop over the surviving prefix).
///
/// Identification strategy: each test clips a distinct axis-aligned rect with no
/// active transform, so NVGContext's flatten-pass Bounds ([minX, minY, maxX, maxY])
/// exactly equals [x, y, x+w, y+h] for that rect. Comparing captured Bounds is far
/// more robust than trying to predict exact tessellated vertex layout/order.
/// </summary>
public class ClipStackTests
{
    private static NVGContext NewContext(FakeRenderer renderer)
    {
        var context = new NVGContext(renderer, edgeAntiAlias: true);
        context.BeginFrame(200, 200, 1.0f);
        return context;
    }

    private static void ClipRect(NVGContext context, float x, float y, float w, float h)
    {
        context.BeginPath();
        context.Rect(x, y, w, h);
        context.Clip();
    }

    private static void AssertBounds(float[] bounds, float x, float y, float w, float h)
    {
        Assert.Equal(x, bounds[0], 3);
        Assert.Equal(y, bounds[1], 3);
        Assert.Equal(x + w, bounds[2], 3);
        Assert.Equal(y + h, bounds[3], 3);
    }

    [Fact]
    public void Restore_RetransmitsExactlySurvivingPrefix()
    {
        var renderer = new FakeRenderer();
        var context = NewContext(renderer);

        context.Save();
        ClipRect(context, 10, 10, 20, 20); // rectA

        context.Save();
        ClipRect(context, 100, 100, 30, 30); // rectB

        // Before Restore: two immediate RenderClip calls (rectA, rectB), no ResetClip yet.
        Assert.Equal(2, renderer.ClipCalls.Count);
        Assert.Equal(0, renderer.ResetClipCount);

        context.Restore();

        // Restore truncates the clip stack back to depth 1 (rectA only), which
        // must trigger exactly one ResetClip followed by exactly one RenderClip
        // for rectA - never rectB.
        Assert.Equal(1, renderer.ResetClipCount);
        Assert.Equal(3, renderer.ClipCalls.Count);

        var retransmitted = renderer.ClipCalls[^1];
        AssertBounds(retransmitted.Bounds, 10, 10, 20, 20);

        var lastTwoEvents = renderer.ClipEvents[^2..];
        Assert.IsType<FakeRenderer.ClipResetEvent>(lastTwoEvents[0]);
        var renderEvent = Assert.IsType<FakeRenderer.ClipRenderEvent>(lastTwoEvents[1]);
        AssertBounds(renderEvent.Data.Bounds, 10, 10, 20, 20);
    }

    [Fact]
    public void PooledBufferReuse_DoesNotLeakPriorClipIntoRetransmission()
    {
        var renderer = new FakeRenderer();
        var context = NewContext(renderer);

        context.Save();
        ClipRect(context, 10, 10, 20, 20); // rectA -> depth 0 buffer

        context.Save();
        ClipRect(context, 100, 100, 30, 30); // rectB -> depth 1 buffer

        context.Restore(); // depth-1 buffer (rectB) vacated from _clipStack, stays in pool

        ClipRect(context, 200, 5, 15, 15); // rectC -> reuses depth-1 pooled buffer

        // Force a full retransmission: Save captures current clip depth (2), a
        // further Clip pushes to depth 3, then Restore truncates back to 2 -
        // re-rendering the surviving prefix [rectA, rectC] from scratch.
        context.Save();
        ClipRect(context, 50, 50, 5, 5); // rectD -> depth 2, discarded by the next Restore

        var resetCountBeforeFinalRestore = renderer.ResetClipCount;
        var clipCallsBeforeFinalRestore = renderer.ClipCalls.Count;

        context.Restore();

        Assert.Equal(resetCountBeforeFinalRestore + 1, renderer.ResetClipCount);

        var retransmitted = renderer.ClipCalls.Skip(clipCallsBeforeFinalRestore).ToList();
        Assert.Equal(2, retransmitted.Count);

        // Exactly rectA then rectC - no residual rectB data from the reused buffer.
        AssertBounds(retransmitted[0].Bounds, 10, 10, 20, 20);
        AssertBounds(retransmitted[1].Bounds, 200, 5, 15, 15);
    }

    [Fact]
    public void ResetClip_ClearsStackAndAllowsFurtherClipping()
    {
        var renderer = new FakeRenderer();
        var context = NewContext(renderer);

        context.Save();
        ClipRect(context, 10, 10, 20, 20);
        ClipRect(context, 30, 30, 20, 20);

        Assert.Equal(2, renderer.ClipCalls.Count);
        Assert.Equal(0, renderer.ResetClipCount);

        context.ResetClip();

        Assert.Equal(1, renderer.ResetClipCount);

        // A further Clip call must work normally after ResetClip: fresh depth-0
        // buffer capture, immediate RenderClip, no leftover state from before.
        ClipRect(context, 60, 60, 10, 10); // rectE

        Assert.Equal(3, renderer.ClipCalls.Count);
        AssertBounds(renderer.ClipCalls[^1].Bounds, 60, 60, 10, 10);
    }

    /// <summary>Returns the RenderClip calls that immediately follow the last ResetClip event so far.</summary>
    private static List<FakeRenderer.ClipCall> LastRetransmitBatch(FakeRenderer renderer)
    {
        var events = renderer.ClipEvents;
        var lastResetEventIndex = events.FindLastIndex(clipEvent => clipEvent is FakeRenderer.ClipResetEvent);
        Assert.True(lastResetEventIndex >= 0);

        return events
            .Skip(lastResetEventIndex + 1)
            .TakeWhile(clipEvent => clipEvent is FakeRenderer.ClipRenderEvent)
            .Cast<FakeRenderer.ClipRenderEvent>()
            .Select(clipEvent => clipEvent.Data)
            .ToList();
    }

    [Fact]
    public void DeepNesting_PartialRestore_RetransmitsOnlyCorrectPrefix()
    {
        var renderer = new FakeRenderer();
        var context = NewContext(renderer);

        context.Save();
        ClipRect(context, 1, 1, 5, 5); // rectA, depth 0

        context.Save();
        ClipRect(context, 10, 10, 5, 5); // rectB, depth 1

        context.Save();
        ClipRect(context, 20, 20, 5, 5); // rectC, depth 2

        context.Save();
        ClipRect(context, 30, 30, 5, 5); // rectD, depth 3

        // Each Save/Clip pair bumped the clip stack by exactly one on top of the
        // depth its enclosing Save had recorded, so each of the following Restore
        // calls truncates by exactly one level and retransmits the surviving
        // prefix from scratch - this walks the pool-reuse invariant across every
        // depth in a single nested chain.

        context.Restore(); // undoes the rectD Save -> back to depth 3 -> retransmits [A, B, C]
        var batch1 = LastRetransmitBatch(renderer);
        Assert.Equal(3, batch1.Count);
        AssertBounds(batch1[0].Bounds, 1, 1, 5, 5);
        AssertBounds(batch1[1].Bounds, 10, 10, 5, 5);
        AssertBounds(batch1[2].Bounds, 20, 20, 5, 5);

        context.Restore(); // undoes the rectC Save -> back to depth 2 -> retransmits [A, B]
        var batch2 = LastRetransmitBatch(renderer);
        Assert.Equal(2, batch2.Count);
        AssertBounds(batch2[0].Bounds, 1, 1, 5, 5);
        AssertBounds(batch2[1].Bounds, 10, 10, 5, 5);

        context.Restore(); // undoes the rectB Save -> back to depth 1 -> retransmits [A]
        var batch3 = LastRetransmitBatch(renderer);
        Assert.Single(batch3);
        AssertBounds(batch3[0].Bounds, 1, 1, 5, 5);
    }
}
