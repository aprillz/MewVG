namespace Aprillz.MewVG;

public sealed class NanoVGBackendOptions
{
    public NVGcreateFlags Flags { get; set; } = NVGcreateFlags.NVG_ANTIALIAS | NVGcreateFlags.NVG_STENCIL_STROKES;

    public string? PreferredBackend { get; set; }

    public IntPtr MetalDevice { get; set; }
}

public interface INanoVGBackend
{
    string Name { get; }

    bool IsSupported(NanoVGBackendOptions options);

    NanoVG Create(NanoVGBackendOptions options);
}

public static class NanoVGFactory
{
    private static readonly object _sync = new();
    private static readonly Dictionary<string, INanoVGBackend> _backends = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterBackend(INanoVGBackend backend)
    {
        if (backend is null)
        {
            throw new ArgumentNullException(nameof(backend));
        }

        lock (_sync)
        {
            _backends[backend.Name] = backend;
        }
    }

    public static NanoVG Create(NanoVGBackendOptions? options = null)
    {
        options ??= new NanoVGBackendOptions();

        INanoVGBackend? backend = null;
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(options.PreferredBackend))
            {
                _backends.TryGetValue(options.PreferredBackend!, out backend);
            }
            else
            {
                var supported = _backends.Values.Where(b => b.IsSupported(options)).ToArray();
                if (supported.Length == 1)
                {
                    backend = supported[0];
                }
                else if (supported.Length > 1)
                {
                    throw new InvalidOperationException("Multiple NanoVG backends are available. Set PreferredBackend to select one: " +
                                                        string.Join(", ", supported.Select(b => b.Name)));
                }
            }
        }

        if (backend == null)
        {
            var names = string.Join(", ", GetRegisteredBackendNames());
            throw new InvalidOperationException("No suitable NanoVG backend found. Registered backends: " + names);
        }

        if (!backend.IsSupported(options))
        {
            throw new InvalidOperationException($"NanoVG backend '{backend.Name}' is not supported for the provided options.");
        }

        return backend.Create(options);
    }

    public static IReadOnlyCollection<string> GetRegisteredBackendNames()
    {
        lock (_sync)
        {
            return _backends.Keys.ToArray();
        }
    }
}