namespace Aprillz.MewVG;

public sealed class NanoVGGLBackend : INanoVGBackend
{
    public string Name => "OpenGL";

    public bool IsSupported(NanoVGBackendOptions options) => true;

    public MewVGContext Create(NanoVGBackendOptions options)
        => new MewVGGL();
}