namespace Aprillz.MewVG;

/// <summary>What besides the path itself decides the geometry a fill or stroke expands to.</summary>
internal readonly record struct PathExpansionSettings(
    bool IsStroke,
    float PixelRatio,
    float Fringe,
    float HalfWidth,
    MewVGLineCap LineCap,
    MewVGLineJoin LineJoin,
    float MiterLimit,
    MewVGFillRule FillRule)
{
    public static PathExpansionSettings ForFill(float pixelRatio, float fringe, MewVGFillRule fillRule)
        => new(false, pixelRatio, fringe, 0f, default, default, 0f, fillRule);

    public static PathExpansionSettings ForStroke(float pixelRatio, float fringe, float halfWidth, MewVGLineCap lineCap, MewVGLineJoin lineJoin, float miterLimit)
        => new(true, pixelRatio, fringe, halfWidth, lineCap, lineJoin, miterLimit, default);
}

/// <summary>
/// Keeps the device-space geometry of paths drawn more than once, so the same path drawn again at the same scale
/// but at another position is replayed shifted instead of flattened and expanded again.
/// </summary>
/// <remarks>
/// A path is identified by its device-space commands taken relative to its first point, compared exactly. Flattening
/// and expansion do not depend on where the path lies, so the geometry differs from the stored one only by that
/// shift. Geometry that follows the pixel grid (a coverage mask) is not vertex geometry and is never stored. A path
/// is stored on its second sighting, which keeps paths drawn once from evicting the repeated ones.
/// </remarks>
internal sealed class TranslatedPathCache
{
    private const int CAPACITY = 64;
    private const int SIGHTING_CAPACITY = 128;
    private const int MAX_COMMAND_FLOATS = 512;
    private const int MAX_VERTS = 2048;

    internal sealed class Entry
    {
        public ulong Hash;
        public PathExpansionSettings Settings;
        public float[] Commands = Array.Empty<float>();
        public int CommandCount;
        public float AnchorX;
        public float AnchorY;
        public NVGpathData[] Paths = Array.Empty<NVGpathData>();
        public int PathCount;
        public NVGvertex[] Verts = Array.Empty<NVGvertex>();
        public int VertCount;
        public readonly float[] Bounds = new float[4];
        public long LastUse;
        public bool Used;
        // Bumped whenever the entry takes another path's geometry.
        public int Version;
    }

    private readonly Entry[] _entries = new Entry[CAPACITY];
    private readonly ulong[] _sightings = new ulong[SIGHTING_CAPACITY];
    private int _nextSighting;
    private long _clock;

    // The lookup that missed, kept for the store that follows it.
    private ulong _missHash;
    private bool _missStores;

    public TranslatedPathCache()
    {
        for (var index = 0; index < CAPACITY; index++)
        {
            _entries[index] = new Entry();
        }
    }

    /// <summary>
    /// Finds the stored geometry of the path, returning it with the shift that moves it onto the path. On a miss,
    /// <see cref="Store"/> keeps the geometry computed next if the path has been seen before.
    /// </summary>
    public Entry? Find(ReadOnlySpan<float> commands, in PathExpansionSettings settings, out float shiftX, out float shiftY)
    {
        shiftX = 0f;
        shiftY = 0f;
        _missStores = false;
        if (!TryGetAnchor(commands, out var anchorX, out var anchorY))
        {
            return null;
        }

        var hash = Hash(commands, anchorX, anchorY, settings);
        foreach (var entry in _entries)
        {
            if (entry.Used && entry.Hash == hash && entry.Settings == settings && SameShape(entry, commands, anchorX, anchorY))
            {
                entry.LastUse = ++_clock;
                shiftX = anchorX - entry.AnchorX;
                shiftY = anchorY - entry.AnchorY;
                return entry;
            }
        }

        _missHash = hash;
        _missStores = Array.IndexOf(_sightings, hash) >= 0;
        if (!_missStores)
        {
            _sightings[_nextSighting] = hash;
            _nextSighting = (_nextSighting + 1) % SIGHTING_CAPACITY;
        }

        return null;
    }

    /// <summary>Keeps the geometry computed for the path of the last missed <see cref="Find"/>, when that path qualified.</summary>
    public void Store(ReadOnlySpan<float> commands, in PathExpansionSettings settings,
        ReadOnlySpan<NVGpathData> paths, ReadOnlySpan<NVGvertex> verts, ReadOnlySpan<float> bounds)
    {
        if (!_missStores || verts.Length > MAX_VERTS || !TryGetAnchor(commands, out var anchorX, out var anchorY))
        {
            return;
        }

        _missStores = false;
        var entry = _entries[0];
        foreach (var candidate in _entries)
        {
            if (!candidate.Used)
            {
                entry = candidate;
                break;
            }

            if (candidate.LastUse < entry.LastUse)
            {
                entry = candidate;
            }
        }

        entry.Used = true;
        entry.Version++;
        entry.Hash = _missHash;
        entry.Settings = settings;
        entry.AnchorX = anchorX;
        entry.AnchorY = anchorY;
        entry.LastUse = ++_clock;
        entry.CommandCount = commands.Length;
        Fit(ref entry.Commands, commands.Length);
        commands.CopyTo(entry.Commands);
        entry.PathCount = paths.Length;
        Fit(ref entry.Paths, paths.Length);
        paths.CopyTo(entry.Paths);
        entry.VertCount = verts.Length;
        Fit(ref entry.Verts, verts.Length);
        verts.CopyTo(entry.Verts);
        bounds.CopyTo(entry.Bounds);
    }

    private static void Fit<T>(ref T[] array, int length)
    {
        if (array.Length < length)
        {
            array = new T[Math.Max(length, array.Length * 2)];
        }
    }

    /// <summary>The first point of a path that starts with a move and is small enough to be kept.</summary>
    private static bool TryGetAnchor(ReadOnlySpan<float> commands, out float anchorX, out float anchorY)
    {
        if (commands.Length < 3 || commands.Length > MAX_COMMAND_FLOATS || (int)commands[0] != (int)NVGcommands.MoveTo)
        {
            anchorX = 0f;
            anchorY = 0f;
            return false;
        }

        anchorX = commands[1];
        anchorY = commands[2];
        return true;
    }

    private static ulong Hash(ReadOnlySpan<float> commands, float anchorX, float anchorY, in PathExpansionSettings settings)
    {
        var hash = 14695981039346656037UL ^ (ulong)settings.GetHashCode();
        var index = 0;
        while (index < commands.Length)
        {
            var command = (int)commands[index];
            hash = Mix(hash, (uint)command);
            var coordinates = CoordinateCount(command);
            for (var offset = 1; offset <= coordinates; offset += 2)
            {
                hash = Mix(hash, (uint)BitConverter.SingleToInt32Bits(commands[index + offset] - anchorX));
                hash = Mix(hash, (uint)BitConverter.SingleToInt32Bits(commands[index + offset + 1] - anchorY));
            }

            index += 1 + Math.Max(coordinates, ValueCount(command));
        }

        return hash;
    }

    private static bool SameShape(Entry entry, ReadOnlySpan<float> commands, float anchorX, float anchorY)
    {
        if (entry.CommandCount != commands.Length)
        {
            return false;
        }

        var stored = entry.Commands;
        var index = 0;
        while (index < commands.Length)
        {
            var command = (int)commands[index];
            if ((int)stored[index] != command)
            {
                return false;
            }

            var coordinates = CoordinateCount(command);
            for (var offset = 1; offset <= coordinates; offset += 2)
            {
                if (!SameBits(stored[index + offset] - entry.AnchorX, commands[index + offset] - anchorX) ||
                    !SameBits(stored[index + offset + 1] - entry.AnchorY, commands[index + offset + 1] - anchorY))
                {
                    return false;
                }
            }

            for (var offset = coordinates + 1; offset <= ValueCount(command); offset++)
            {
                if (!SameBits(stored[index + offset], commands[index + offset]))
                {
                    return false;
                }
            }

            index += 1 + Math.Max(coordinates, ValueCount(command));
        }

        return true;
    }

    private static bool SameBits(float first, float second)
        => BitConverter.SingleToInt32Bits(first) == BitConverter.SingleToInt32Bits(second);

    private static ulong Mix(ulong hash, uint value) => (hash ^ value) * 1099511628211UL;

    /// <summary>How many of the values after a command are point coordinates.</summary>
    private static int CoordinateCount(int command) => (NVGcommands)command switch
    {
        NVGcommands.MoveTo or NVGcommands.LineTo => 2,
        NVGcommands.BezierTo => 6,
        _ => 0,
    };

    /// <summary>How many values follow a command, coordinates or not.</summary>
    private static int ValueCount(int command) => (NVGcommands)command switch
    {
        NVGcommands.MoveTo or NVGcommands.LineTo => 2,
        NVGcommands.BezierTo => 6,
        NVGcommands.Winding => 1,
        _ => 0,
    };
}
