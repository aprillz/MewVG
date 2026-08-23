namespace Aprillz.MewVG;

/// <summary>
/// Device-side copies of coverage masks, owned by the renderer that draws them. A frozen
/// geometry's mask is keyed on its cache object and re-uploaded only when the version the
/// core hands over changes; a keyless mask is uploaded for one draw. Images are written in
/// place only once every frame that may still read them has passed, and an image a draw of
/// the current frame references is never deleted before that frame flushes. Entries expire
/// after sitting idle and under a byte budget, so nothing here needs a finalizer.
/// </summary>
internal sealed class MaskImageCache
{
    internal delegate int CreateImage(int width, int height, ReadOnlySpan<byte> coverage);
    internal delegate bool UpdateImage(int image, int width, int height, ReadOnlySpan<byte> coverage);

    private const long DEVICE_BUDGET_BYTES = 64L * 1024 * 1024;
    // Frames a mask may go unused before its device copy is released. The core keeps the CPU
    // pixels of a keyed mask, so a release costs one re-upload, not a rasterization.
    private const int IDLE_FRAMES = 120;

    private sealed class Entry
    {
        public int Image;
        public int Width;
        public int Height;
        public int Version;
        public int LastUsedFrame;
        public long Bytes => (long)Width * Height;
    }

    private readonly CreateImage _create;
    private readonly UpdateImage _update;
    private readonly Action<int> _delete;
    // Frames whose draws may still be reading an image: 1 when the backend finishes a frame's
    // commands before the next frame records (GL), the in-flight depth otherwise (Metal).
    private readonly int _framesInFlight;
    private readonly Dictionary<object, Entry> _keyed = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entry> _transient = new();
    // Replaced while a draw of the current frame still references them; deleted once that frame is out.
    private readonly List<int> _retired = new();
    private readonly List<object> _evictScratch = new();
    private int _frame;
    private long _keyedBytes;

    public MaskImageCache(CreateImage create, UpdateImage update, Action<int> delete, int framesInFlight)
    {
        _create = create;
        _update = update;
        _delete = delete;
        _framesInFlight = Math.Max(1, framesInFlight);
    }

    /// <summary>Images held for frozen geometries and their device bytes, for diagnostics.</summary>
    public int KeyedCount => _keyed.Count;
    public long KeyedBytes => _keyedBytes;

    /// <summary>Starts a frame: retired images and idle entries are released, and over-budget keyed entries evicted.</summary>
    public void BeginFrame()
    {
        _frame++;

        foreach (var image in _retired)
        {
            _delete(image);
        }
        _retired.Clear();

        _evictScratch.Clear();
        foreach (var pair in _keyed)
        {
            if (_frame - pair.Value.LastUsedFrame > IDLE_FRAMES)
            {
                _evictScratch.Add(pair.Key);
            }
        }
        foreach (var key in _evictScratch)
        {
            Release(key);
        }

        for (var i = _transient.Count - 1; i >= 0; i--)
        {
            if (_frame - _transient[i].LastUsedFrame > IDLE_FRAMES)
            {
                _delete(_transient[i].Image);
                _transient.RemoveAt(i);
            }
        }
    }

    /// <summary>Returns the image holding <paramref name="coverage"/>, uploading when this renderer has no current copy. Returns 0 when the upload fails.</summary>
    public int Acquire(ReadOnlySpan<byte> coverage, int width, int height, object? key, int version)
    {
        if (key == null)
        {
            return AcquireTransient(coverage, width, height);
        }

        _keyed.TryGetValue(key, out var entry);
        if (entry != null)
        {
            if (entry.Version == version && entry.Width == width && entry.Height == height)
            {
                entry.LastUsedFrame = _frame;
                return entry.Image;
            }

            if (entry.Width == width && entry.Height == height && IsWritable(entry) && _update(entry.Image, width, height, coverage))
            {
                entry.Version = version;
                entry.LastUsedFrame = _frame;
                return entry.Image;
            }
        }

        var image = _create(width, height, coverage);
        if (image == 0)
        {
            return 0;
        }

        if (entry != null)
        {
            Retire(entry);
            _keyedBytes -= entry.Bytes;
        }

        entry = new Entry { Image = image, Width = width, Height = height, Version = version, LastUsedFrame = _frame };
        _keyed[key] = entry;
        _keyedBytes += entry.Bytes;
        EvictToBudget();
        return image;
    }

    private int AcquireTransient(ReadOnlySpan<byte> coverage, int width, int height)
    {
        foreach (var entry in _transient)
        {
            if (entry.Width == width && entry.Height == height && IsWritable(entry) && _update(entry.Image, width, height, coverage))
            {
                entry.LastUsedFrame = _frame;
                return entry.Image;
            }
        }

        var image = _create(width, height, coverage);
        if (image == 0)
        {
            return 0;
        }

        _transient.Add(new Entry { Image = image, Width = width, Height = height, LastUsedFrame = _frame });
        return image;
    }

    // An image may be overwritten once no frame that drew with it can still be executing.
    private bool IsWritable(Entry entry) => _frame - entry.LastUsedFrame >= _framesInFlight;

    // Least recently used first, never an image a draw of this frame references.
    private void EvictToBudget()
    {
        while (_keyedBytes > DEVICE_BUDGET_BYTES)
        {
            object? victim = null;
            var oldest = int.MaxValue;
            foreach (var pair in _keyed)
            {
                if (pair.Value.LastUsedFrame < _frame && pair.Value.LastUsedFrame < oldest)
                {
                    oldest = pair.Value.LastUsedFrame;
                    victim = pair.Key;
                }
            }

            if (victim == null)
            {
                return;
            }

            Release(victim);
        }
    }

    private void Release(object key)
    {
        if (_keyed.Remove(key, out var entry))
        {
            _keyedBytes -= entry.Bytes;
            Retire(entry);
        }
    }

    // Deleting an image the current frame already queued a draw against must wait for that
    // frame's flush; anything older is done with on GL and retained by Metal's command buffers.
    private void Retire(Entry entry)
    {
        if (entry.LastUsedFrame == _frame)
        {
            _retired.Add(entry.Image);
        }
        else
        {
            _delete(entry.Image);
        }
    }

    /// <summary>Deletes every image; the owning renderer is going away.</summary>
    public void Dispose()
    {
        foreach (var image in _retired)
        {
            _delete(image);
        }
        _retired.Clear();

        foreach (var entry in _keyed.Values)
        {
            _delete(entry.Image);
        }
        _keyed.Clear();
        _keyedBytes = 0;

        foreach (var entry in _transient)
        {
            _delete(entry.Image);
        }
        _transient.Clear();
    }
}
