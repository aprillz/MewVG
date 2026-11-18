using System.Diagnostics;

using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// A contour too thin for the fringe probe to land inside takes its fringe side from the sign of its area, the rule
/// every probed contour agrees with. A debug build checks a lone convex contour's side against probing it, so a
/// disagreement fails the fill there.
/// </summary>
public sealed class ThinFillFringeTests
{
    private sealed class ThrowingListener : TraceListener
    {
        public override void Fail(string? message, string? detailMessage) => throw new InvalidOperationException(message);

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
        }
    }

    [Theory]
    [InlineData(0.001f, 0.1f, 1f)]
    [InlineData(0.001f, 1f, 1f)]
    [InlineData(0.1f, 8f, 1.5f)]
    [InlineData(40f, 0.01f, 1f)]
    public void SubPixelThinRoundedRect_FillsWithoutASideDisagreement(float width, float height, float pixelRatio)
    {
        var listeners = Trace.Listeners.Cast<TraceListener>().ToArray();
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new ThrowingListener());
        try
        {
            var renderer = new FakeRenderer();
            using var vg = new TestMewVGContext(renderer);
            vg.BeginFrame(600, 600, pixelRatio);
            vg.BeginPath();
            vg.RoundedRect(10, 20, width, height, 1);
            vg.FillColor(0, 0, 0);
            vg.Fill();
            vg.EndFrame();
        }
        finally
        {
            Trace.Listeners.Clear();
            Trace.Listeners.AddRange(listeners);
        }
    }
}
