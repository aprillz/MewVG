namespace MewVG.Demo;

internal sealed unsafe class GLMinimal
{
    public const uint ColorBufferBit = 0x00004000;
    public const uint StencilBufferBit = 0x00000400;

    private readonly delegate* unmanaged<int, int, int, int, void> _viewport;
    private readonly delegate* unmanaged<float, float, float, float, void> _clearColor;
    private readonly delegate* unmanaged<int, void> _clearStencil;
    private readonly delegate* unmanaged<uint, void> _clear;

    public GLMinimal(Func<string, nint> getProcAddress)
    {
        _viewport = (delegate* unmanaged<int, int, int, int, void>)Get(getProcAddress, "glViewport");
        _clearColor = (delegate* unmanaged<float, float, float, float, void>)Get(getProcAddress, "glClearColor");
        _clearStencil = (delegate* unmanaged<int, void>)Get(getProcAddress, "glClearStencil");
        _clear = (delegate* unmanaged<uint, void>)Get(getProcAddress, "glClear");
    }

    public void Viewport(int x, int y, int w, int h) => _viewport(x, y, w, h);
    public void ClearColor(float r, float g, float b, float a) => _clearColor(r, g, b, a);
    public void ClearStencil(int s) => _clearStencil(s);
    public void Clear(uint mask) => _clear(mask);

    private static nint Get(Func<string, nint> getProcAddress, string name)
    {
        var proc = getProcAddress(name);
        if (proc == nint.Zero)
        {
            throw new InvalidOperationException($"Missing GL entry point: {name}");
        }

        return proc;
    }
}
