using Aprillz.MewVG;

using Xunit;

namespace MewVG.Core.Tests;

/// <summary>
/// The renderer-side mask image cache: a keyed mask is uploaded once per version, a keyless
/// one is uploaded per draw, an image is rewritten in place only after every frame that may
/// read it has passed, an image the current frame draws with is deleted only after that
/// frame, and images leave when idle or over budget.
/// </summary>
public sealed class MaskImageCacheTests
{
    private sealed class Images
    {
        public readonly List<int> Created = new();
        public readonly List<int> Updated = new();
        public readonly List<int> Deleted = new();
        private int _next = 1;

        public MaskImageCache NewCache(int framesInFlight = 1) => new(
            (width, height, coverage) => { var image = _next++; Created.Add(image); return image; },
            (image, width, height, coverage) => { Updated.Add(image); return true; },
            image => Deleted.Add(image),
            framesInFlight);
    }

    private static readonly byte[] _pixels = new byte[4096 * 4096];

    [Fact]
    public void KeyedMask_UploadsOncePerVersion()
    {
        var images = new Images();
        var cache = images.NewCache();
        var key = new object();

        cache.BeginFrame();
        var first = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key, version: 1);
        var again = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key, version: 1);
        cache.BeginFrame();
        var nextFrame = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key, version: 1);

        Assert.Equal(first, again);
        Assert.Equal(first, nextFrame);
        Assert.Single(images.Created);
        Assert.Empty(images.Updated);
    }

    [Fact]
    public void KeyedMask_NewVersionRewritesInPlaceOnlyAfterItsReadersAreDone()
    {
        var images = new Images();
        var cache = images.NewCache();
        var key = new object();

        // Drawn this frame, then rasterized again this frame: the queued draw still needs the old
        // pixels, so the new version gets a new image and the old one goes after the frame.
        cache.BeginFrame();
        var first = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key, version: 1);
        var second = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key, version: 2);
        Assert.NotEqual(first, second);
        Assert.Empty(images.Updated);
        Assert.Empty(images.Deleted);

        cache.BeginFrame();
        Assert.Equal(new[] { first }, images.Deleted);

        // A frame later the image is free to take the next version in place.
        var third = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key, version: 3);
        Assert.Equal(second, third);
        Assert.Equal(new[] { second }, images.Updated);

        // A new size never fits the old image.
        var resized = cache.Acquire(_pixels.AsSpan(0, 400), 20, 20, key, version: 4);
        Assert.NotEqual(second, resized);
    }

    [Fact]
    public void TransientMasks_GetTheirOwnImagePerDrawAndRecycleOncePastTheFramesInFlight()
    {
        var images = new Images();
        var cache = images.NewCache(framesInFlight: 3);

        cache.BeginFrame();
        var a = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key: null, version: 0);
        var b = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key: null, version: 0);
        Assert.NotEqual(a, b);

        // Frames 2 and 3 may still be executed alongside frame 1's commands.
        cache.BeginFrame();
        var c = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key: null, version: 0);
        cache.BeginFrame();
        var d = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key: null, version: 0);
        Assert.DoesNotContain(c, new[] { a, b });
        Assert.DoesNotContain(d, new[] { a, b, c });

        // Frame 4 is three frames past frame 1, so its images are writable again.
        cache.BeginFrame();
        var e = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key: null, version: 0);
        Assert.Equal(a, e);
        Assert.Equal(new[] { a }, images.Updated);
    }

    [Fact]
    public void Masks_LeaveAfterSittingIdle()
    {
        var images = new Images();
        var cache = images.NewCache();

        cache.BeginFrame();
        var keyed = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, new object(), version: 1);
        var transient = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key: null, version: 0);
        for (var frame = 0; frame < 200; frame++)
        {
            cache.BeginFrame();
        }

        Assert.Equal(2, images.Deleted.Count);
        Assert.Contains(keyed, images.Deleted);
        Assert.Contains(transient, images.Deleted);
        Assert.Equal(0, cache.KeyedCount);
    }

    [Fact]
    public void KeyedMasks_OverBudgetEvictTheLeastRecentlyUsedButNeverThisFrame()
    {
        var images = new Images();
        var cache = images.NewCache();
        const int side = 4096; // 16 MiB per mask against a 64 MiB budget
        var keys = new object[5];
        var imagesByKey = new int[5];

        for (var i = 0; i < 4; i++)
        {
            cache.BeginFrame();
            keys[i] = new object();
            imagesByKey[i] = cache.Acquire(_pixels, side, side, keys[i], version: 1);
        }
        Assert.Empty(images.Deleted);

        cache.BeginFrame();
        keys[4] = new object();
        imagesByKey[4] = cache.Acquire(_pixels, side, side, keys[4], version: 1);

        Assert.Equal(new[] { imagesByKey[0] }, images.Deleted);
        Assert.Equal(4, cache.KeyedCount);

        // Everything acquired this frame stays even when that leaves the cache over budget.
        cache.Acquire(_pixels, side, side, new object(), version: 1);
        cache.Acquire(_pixels, side, side, new object(), version: 1);
        Assert.DoesNotContain(imagesByKey[4], images.Deleted);
        Assert.Equal(4, cache.KeyedCount);
    }

    [Fact]
    public void Dispose_DeletesEveryImage()
    {
        var images = new Images();
        var cache = images.NewCache();

        cache.BeginFrame();
        var keyed = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, new object(), version: 1);
        var transient = cache.Acquire(_pixels.AsSpan(0, 100), 10, 10, key: null, version: 0);
        cache.Dispose();

        Assert.Equal(2, images.Deleted.Count);
        Assert.Contains(keyed, images.Deleted);
        Assert.Contains(transient, images.Deleted);
    }
}
