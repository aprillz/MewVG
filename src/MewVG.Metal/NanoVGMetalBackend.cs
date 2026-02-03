namespace Aprillz.MewVG;

public sealed class NanoVGMetalBackend : INanoVGBackend
{
    public string Name => "Metal";

    public bool IsSupported(NanoVGBackendOptions options)
    {
        return options.MetalDevice != IntPtr.Zero;
    }

    public NanoVG Create(NanoVGBackendOptions options)
    {
        if (options.MetalDevice == IntPtr.Zero)
        {
            throw new ArgumentException("MetalDevice must be provided for Metal backend.", nameof(options));
        }

        return new NanoVGMetal(options.MetalDevice, options.Flags);
    }
}
