namespace MewVG.Demo;

internal sealed unsafe class GLMinimal
{
    public const uint ColorBufferBit = 0x00004000;

    private readonly delegate* unmanaged<int, int, int, int, void> _viewport;
    private readonly delegate* unmanaged<float, float, float, float, void> _clearColor;
    private readonly delegate* unmanaged<uint, void> _clear;
    private readonly delegate* unmanaged<int, int, int, int, uint, uint, void*, void> _readPixels;
    private readonly delegate* unmanaged<void> _finish;
    private readonly delegate* unmanaged<uint, byte*> _getString;

    public GLMinimal(Func<string, nint> getProcAddress)
    {
        _viewport = (delegate* unmanaged<int, int, int, int, void>)Get(getProcAddress, "glViewport");
        _clearColor = (delegate* unmanaged<float, float, float, float, void>)Get(getProcAddress, "glClearColor");
        _clear = (delegate* unmanaged<uint, void>)Get(getProcAddress, "glClear");
        _readPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)Get(getProcAddress, "glReadPixels");
        _finish = (delegate* unmanaged<void>)Get(getProcAddress, "glFinish");
        _getString = (delegate* unmanaged<uint, byte*>)Get(getProcAddress, "glGetString");
    }

    public void Viewport(int x, int y, int w, int h) => _viewport(x, y, w, h);
    public void ClearColor(float r, float g, float b, float a) => _clearColor(r, g, b, a);
    public void Clear(uint mask) => _clear(mask);

    /// <summary>The GL_RENDERER string of the current context.</summary>
    public string? Renderer => System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)_getString(0x1F01));

    /// <summary>Reads the bound framebuffer as RGBA8, bottom row first.</summary>
    public void ReadPixels(int x, int y, int w, int h, byte[] rgba)
    {
        _finish();
        fixed (byte* p = rgba)
        {
            _readPixels(x, y, w, h, 0x1908 /* GL_RGBA */, 0x1401 /* GL_UNSIGNED_BYTE */, p);
        }
    }

    /// <summary>Writes the bound framebuffer to <paramref name="path"/> as a binary PPM, top row first.</summary>
    public void SavePpm(string path, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        ReadPixels(0, 0, width, height, rgba);
        using var file = File.Create(path);
        file.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        var row = new byte[width * 3];
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var source = (y * width + x) * 4;
                row[x * 3] = rgba[source];
                row[x * 3 + 1] = rgba[source + 1];
                row[x * 3 + 2] = rgba[source + 2];
            }

            file.Write(row);
        }
    }

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
