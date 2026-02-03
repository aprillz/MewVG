using System.Text;

namespace Aprillz.MewVG;

internal enum EnableCap : int
{
    Blend = 0x0BE2,
    CullFace = 0x0B44,
    DepthTest = 0x0B71,
    ScissorTest = 0x0C11,
    StencilTest = 0x0B90
}

internal enum CullFaceMode : int
{
    Back = 0x0405
}

internal enum FrontFaceDirection : int
{
    Ccw = 0x0901
}

internal enum StencilFunction : int
{
    Always = 0x0207,
    Equal = 0x0202,
    Notequal = 0x0205
}

internal enum StencilOp : int
{
    Keep = 0x1E00,
    Zero = 0,
    Incr = 0x1E02,
    IncrWrap = 0x8507,
    DecrWrap = 0x8508
}

internal enum StencilFace : int
{
    Front = 0x0404,
    Back = 0x0405
}

internal enum TextureUnit : int
{
    Texture0 = 0x84C0
}

internal enum TextureTarget : int
{
    Texture2D = 0x0DE1
}

internal enum BufferTarget : int
{
    ArrayBuffer = 0x8892
}

internal enum BufferUsageHint : int
{
    StreamDraw = 0x88E0
}

internal enum VertexAttribPointerType : int
{
    Float = 0x1406
}

internal enum PixelStoreParameter : int
{
    UnpackAlignment = 0x0CF5,
    UnpackRowLength = 0x0CF2,
    UnpackSkipPixels = 0x0CF4,
    UnpackSkipRows = 0x0CF3
}

internal enum PixelInternalFormat : int
{
    Rgba = 0x1908,
    R8 = 0x8229
}

internal enum PixelFormat : int
{
    Rgba = 0x1908,
    Red = 0x1903
}

internal enum PixelType : int
{
    UnsignedByte = 0x1401
}

internal enum TextureMinFilter : int
{
    NearestMipmapNearest = 0x2700,
    LinearMipmapLinear = 0x2703,
    Nearest = 0x2600,
    Linear = 0x2601
}

internal enum TextureMagFilter : int
{
    Nearest = 0x2600,
    Linear = 0x2601
}

internal enum TextureParameterName : int
{
    TextureMinFilter = 0x2801,
    TextureMagFilter = 0x2800,
    TextureWrapS = 0x2802,
    TextureWrapT = 0x2803
}

internal enum TextureWrapMode : int
{
    ClampToEdge = 0x812F,
    Repeat = 0x2901
}

internal enum GenerateMipmapTarget : int
{
    Texture2D = 0x0DE1
}

internal enum FramebufferTarget : int
{
    Framebuffer = 0x8D40
}

internal enum FramebufferAttachment : int
{
    ColorAttachment0 = 0x8CE0,
    StencilAttachment = 0x8D20,
    DepthStencilAttachment = 0x821A
}

internal enum RenderbufferTarget : int
{
    Renderbuffer = 0x8D41
}

internal enum RenderbufferStorage : int
{
    StencilIndex8 = 0x8D48,
    Depth24Stencil8 = 0x88F0
}

internal enum FramebufferErrorCode : int
{
    FramebufferComplete = 0x8CD5
}

internal enum PrimitiveType : int
{
    TriangleFan = 0x0006,
    TriangleStrip = 0x0005,
    Triangles = 0x0004
}

internal enum BlendingFactorSrc : int
{
    Zero = 0,
    One = 1,
    SrcColor = 0x0300,
    OneMinusSrcColor = 0x0301,
    DstColor = 0x0306,
    OneMinusDstColor = 0x0307,
    SrcAlpha = 0x0302,
    OneMinusSrcAlpha = 0x0303,
    DstAlpha = 0x0304,
    OneMinusDstAlpha = 0x0305,
    SrcAlphaSaturate = 0x0308
}

internal enum BlendingFactorDest : int
{
    Zero = 0,
    One = 1,
    SrcColor = 0x0300,
    OneMinusSrcColor = 0x0301,
    DstColor = 0x0306,
    OneMinusDstColor = 0x0307,
    SrcAlpha = 0x0302,
    OneMinusSrcAlpha = 0x0303,
    DstAlpha = 0x0304,
    OneMinusDstAlpha = 0x0305,
    SrcAlphaSaturate = 0x0308
}

internal enum ShaderType : int
{
    VertexShader = 0x8B31,
    FragmentShader = 0x8B30
}

internal enum ShaderParameter : int
{
    CompileStatus = 0x8B81
}

internal enum GetProgramParameterName : int
{
    LinkStatus = 0x8B82
}

internal enum GetPName : int
{
    FramebufferBinding = 0x8CA6,
    RenderbufferBinding = 0x8CA7
}

internal enum All : int
{
    True = 1
}

internal static unsafe class GL
{
    private static bool _initialized;
    private static Func<string, nint>? _getProcAddress;

    private static delegate* unmanaged<uint, void> _glUseProgram;
    private static delegate* unmanaged<uint, void> _glEnable;
    private static delegate* unmanaged<uint, void> _glDisable;
    private static delegate* unmanaged<uint, void> _glCullFace;
    private static delegate* unmanaged<uint, void> _glFrontFace;
    private static delegate* unmanaged<byte, byte, byte, byte, void> _glColorMask;
    private static delegate* unmanaged<uint, void> _glStencilMask;
    private static delegate* unmanaged<uint, uint, uint, void> _glStencilOp;
    private static delegate* unmanaged<uint, uint, uint, uint, void> _glStencilOpSeparate;
    private static delegate* unmanaged<uint, int, uint, void> _glStencilFunc;
    private static delegate* unmanaged<uint, void> _glActiveTexture;
    private static delegate* unmanaged<uint, uint, void> _glBindTexture;
    private static delegate* unmanaged<uint, void> _glBindVertexArray;
    private static delegate* unmanaged<uint, uint, void> _glBindBuffer;
    private static delegate* unmanaged<uint, nint, void*, uint, void> _glBufferData;
    private static delegate* unmanaged<uint, void> _glEnableVertexAttribArray;
    private static delegate* unmanaged<uint, void> _glDisableVertexAttribArray;
    private static delegate* unmanaged<uint, int, uint, byte, int, void*, void> _glVertexAttribPointer;
    private static delegate* unmanaged<int, int, void> _glUniform1i;
    private static delegate* unmanaged<int, int, float*, void> _glUniform2fv;
    private static delegate* unmanaged<int, int, float*, void> _glUniform4fv;
    private static delegate* unmanaged<int, uint*, void> _glGenTextures;
    private static delegate* unmanaged<int, uint*, void> _glDeleteTextures;
    private static delegate* unmanaged<uint, int, void> _glPixelStorei;
    private static delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> _glTexImage2D;
    private static delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> _glTexSubImage2D;
    private static delegate* unmanaged<uint, uint, int, void> _glTexParameteri;
    private static delegate* unmanaged<uint, void> _glGenerateMipmap;
    private static delegate* unmanaged<uint, int, int, void> _glDrawArrays;
    private static delegate* unmanaged<uint, uint, uint, uint, void> _glBlendFuncSeparate;
    private static delegate* unmanaged<uint, uint> _glCreateShader;
    private static delegate* unmanaged<uint, int, byte**, int*, void> _glShaderSource;
    private static delegate* unmanaged<uint, void> _glCompileShader;
    private static delegate* unmanaged<uint, uint, int*, void> _glGetShaderiv;
    private static delegate* unmanaged<uint, int, int*, byte*, void> _glGetShaderInfoLog;
    private static delegate* unmanaged<uint> _glCreateProgram;
    private static delegate* unmanaged<uint, uint, void> _glAttachShader;
    private static delegate* unmanaged<uint, uint, byte*, void> _glBindAttribLocation;
    private static delegate* unmanaged<uint, void> _glLinkProgram;
    private static delegate* unmanaged<uint, uint, int*, void> _glGetProgramiv;
    private static delegate* unmanaged<uint, int, int*, byte*, void> _glGetProgramInfoLog;
    private static delegate* unmanaged<uint, byte*, int> _glGetUniformLocation;
    private static delegate* unmanaged<uint, void> _glDeleteProgram;
    private static delegate* unmanaged<uint, void> _glDeleteShader;
    private static delegate* unmanaged<int, uint*, void> _glGenVertexArrays;
    private static delegate* unmanaged<int, uint*, void> _glGenBuffers;
    private static delegate* unmanaged<int, uint*, void> _glDeleteBuffers;
    private static delegate* unmanaged<int, uint*, void> _glDeleteVertexArrays;
    private static delegate* unmanaged<uint, int*, void> _glGetIntegerv;
    private static delegate* unmanaged<int, uint*, void> _glGenFramebuffers;
    private static delegate* unmanaged<uint, uint, void> _glBindFramebuffer;
    private static delegate* unmanaged<int, uint*, void> _glGenRenderbuffers;
    private static delegate* unmanaged<uint, uint, void> _glBindRenderbuffer;
    private static delegate* unmanaged<uint, uint, uint, uint, void> _glRenderbufferStorage;
    private static delegate* unmanaged<uint, uint, uint, uint, int, void> _glFramebufferTexture2D;
    private static delegate* unmanaged<uint, uint, uint, uint, void> _glFramebufferRenderbuffer;
    private static delegate* unmanaged<uint, uint> _glCheckFramebufferStatus;
    private static delegate* unmanaged<int, uint*, void> _glDeleteFramebuffers;
    private static delegate* unmanaged<int, uint*, void> _glDeleteRenderbuffers;
    private static delegate* unmanaged<void> _glFinish;

    public static void Initialize(Func<string, nint> getProcAddress)
    {
        if (_initialized)
        {
            return;
        }

        _getProcAddress = getProcAddress ?? throw new ArgumentNullException(nameof(getProcAddress));

        Load();
        _initialized = true;
    }

    public static void EnsureLoaded()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("OpenGL is not initialized. Call NanoVGGL.Initialize(...) after creating a GL context.");
        }
    }

    private static nint LoadProc(string name)
    {
        var proc = _getProcAddress!(name);
        if (proc == nint.Zero)
        {
            throw new InvalidOperationException($"Missing GL entry point: {name}");
        }

        return proc;
    }

    private static void Load()
    {
        _glUseProgram = (delegate* unmanaged<uint, void>)LoadProc("glUseProgram");
        _glEnable = (delegate* unmanaged<uint, void>)LoadProc("glEnable");
        _glDisable = (delegate* unmanaged<uint, void>)LoadProc("glDisable");
        _glCullFace = (delegate* unmanaged<uint, void>)LoadProc("glCullFace");
        _glFrontFace = (delegate* unmanaged<uint, void>)LoadProc("glFrontFace");
        _glColorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)LoadProc("glColorMask");
        _glStencilMask = (delegate* unmanaged<uint, void>)LoadProc("glStencilMask");
        _glStencilOp = (delegate* unmanaged<uint, uint, uint, void>)LoadProc("glStencilOp");
        _glStencilOpSeparate = (delegate* unmanaged<uint, uint, uint, uint, void>)LoadProc("glStencilOpSeparate");
        _glStencilFunc = (delegate* unmanaged<uint, int, uint, void>)LoadProc("glStencilFunc");
        _glActiveTexture = (delegate* unmanaged<uint, void>)LoadProc("glActiveTexture");
        _glBindTexture = (delegate* unmanaged<uint, uint, void>)LoadProc("glBindTexture");
        _glBindVertexArray = (delegate* unmanaged<uint, void>)LoadProc("glBindVertexArray");
        _glBindBuffer = (delegate* unmanaged<uint, uint, void>)LoadProc("glBindBuffer");
        _glBufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)LoadProc("glBufferData");
        _glEnableVertexAttribArray = (delegate* unmanaged<uint, void>)LoadProc("glEnableVertexAttribArray");
        _glDisableVertexAttribArray = (delegate* unmanaged<uint, void>)LoadProc("glDisableVertexAttribArray");
        _glVertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)LoadProc("glVertexAttribPointer");
        _glUniform1i = (delegate* unmanaged<int, int, void>)LoadProc("glUniform1i");
        _glUniform2fv = (delegate* unmanaged<int, int, float*, void>)LoadProc("glUniform2fv");
        _glUniform4fv = (delegate* unmanaged<int, int, float*, void>)LoadProc("glUniform4fv");
        _glGenTextures = (delegate* unmanaged<int, uint*, void>)LoadProc("glGenTextures");
        _glDeleteTextures = (delegate* unmanaged<int, uint*, void>)LoadProc("glDeleteTextures");
        _glPixelStorei = (delegate* unmanaged<uint, int, void>)LoadProc("glPixelStorei");
        _glTexImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)LoadProc("glTexImage2D");
        _glTexSubImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)LoadProc("glTexSubImage2D");
        _glTexParameteri = (delegate* unmanaged<uint, uint, int, void>)LoadProc("glTexParameteri");
        _glGenerateMipmap = (delegate* unmanaged<uint, void>)LoadProc("glGenerateMipmap");
        _glDrawArrays = (delegate* unmanaged<uint, int, int, void>)LoadProc("glDrawArrays");
        _glBlendFuncSeparate = (delegate* unmanaged<uint, uint, uint, uint, void>)LoadProc("glBlendFuncSeparate");
        _glCreateShader = (delegate* unmanaged<uint, uint>)LoadProc("glCreateShader");
        _glShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)LoadProc("glShaderSource");
        _glCompileShader = (delegate* unmanaged<uint, void>)LoadProc("glCompileShader");
        _glGetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)LoadProc("glGetShaderiv");
        _glGetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)LoadProc("glGetShaderInfoLog");
        _glCreateProgram = (delegate* unmanaged<uint>)LoadProc("glCreateProgram");
        _glAttachShader = (delegate* unmanaged<uint, uint, void>)LoadProc("glAttachShader");
        _glBindAttribLocation = (delegate* unmanaged<uint, uint, byte*, void>)LoadProc("glBindAttribLocation");
        _glLinkProgram = (delegate* unmanaged<uint, void>)LoadProc("glLinkProgram");
        _glGetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)LoadProc("glGetProgramiv");
        _glGetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)LoadProc("glGetProgramInfoLog");
        _glGetUniformLocation = (delegate* unmanaged<uint, byte*, int>)LoadProc("glGetUniformLocation");
        _glDeleteProgram = (delegate* unmanaged<uint, void>)LoadProc("glDeleteProgram");
        _glDeleteShader = (delegate* unmanaged<uint, void>)LoadProc("glDeleteShader");
        _glGenVertexArrays = (delegate* unmanaged<int, uint*, void>)LoadProc("glGenVertexArrays");
        _glGenBuffers = (delegate* unmanaged<int, uint*, void>)LoadProc("glGenBuffers");
        _glDeleteBuffers = (delegate* unmanaged<int, uint*, void>)LoadProc("glDeleteBuffers");
        _glDeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)LoadProc("glDeleteVertexArrays");
        _glGetIntegerv = (delegate* unmanaged<uint, int*, void>)LoadProc("glGetIntegerv");
        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)LoadProc("glGenFramebuffers");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)LoadProc("glBindFramebuffer");
        _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)LoadProc("glGenRenderbuffers");
        _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)LoadProc("glBindRenderbuffer");
        _glRenderbufferStorage = (delegate* unmanaged<uint, uint, uint, uint, void>)LoadProc("glRenderbufferStorage");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)LoadProc("glFramebufferTexture2D");
        _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)LoadProc("glFramebufferRenderbuffer");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)LoadProc("glCheckFramebufferStatus");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)LoadProc("glDeleteFramebuffers");
        _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)LoadProc("glDeleteRenderbuffers");
        _glFinish = (delegate* unmanaged<void>)LoadProc("glFinish");
    }

    public static void UseProgram(int program) => _glUseProgram((uint)program);

    public static void Enable(EnableCap cap) => _glEnable((uint)cap);

    public static void Disable(EnableCap cap) => _glDisable((uint)cap);

    public static void CullFace(CullFaceMode mode) => _glCullFace((uint)mode);

    public static void FrontFace(FrontFaceDirection mode) => _glFrontFace((uint)mode);

    public static void ColorMask(bool r, bool g, bool b, bool a) => _glColorMask((byte)(r ? 1 : 0), (byte)(g ? 1 : 0), (byte)(b ? 1 : 0), (byte)(a ? 1 : 0));

    public static void StencilMask(int mask) => _glStencilMask((uint)mask);

    public static void StencilOp(StencilOp fail, StencilOp zfail, StencilOp zpass) => _glStencilOp((uint)fail, (uint)zfail, (uint)zpass);

    public static void StencilOpSeparate(StencilFace face, StencilOp sfail, StencilOp dpfail, StencilOp dppass)
        => _glStencilOpSeparate((uint)face, (uint)sfail, (uint)dpfail, (uint)dppass);

    public static void StencilFunc(StencilFunction func, int reference, int mask) => _glStencilFunc((uint)func, reference, (uint)mask);

    public static void ActiveTexture(TextureUnit texture) => _glActiveTexture((uint)texture);

    public static void BindTexture(TextureTarget target, int texture) => _glBindTexture((uint)target, (uint)texture);

    public static void BindVertexArray(int array) => _glBindVertexArray((uint)array);

    public static void BindBuffer(BufferTarget target, int buffer) => _glBindBuffer((uint)target, (uint)buffer);

    public static void BufferData(BufferTarget target, int size, ReadOnlySpan<NVGvertex> data, BufferUsageHint usage)
    {
        fixed (NVGvertex* ptr = data)
        {
            _glBufferData((uint)target, size, ptr, (uint)usage);
        }
    }

    public static void EnableVertexAttribArray(int index) => _glEnableVertexAttribArray((uint)index);

    public static void DisableVertexAttribArray(int index) => _glDisableVertexAttribArray((uint)index);

    public static void VertexAttribPointer(int index, int size, VertexAttribPointerType type, bool normalized, int stride, int pointer)
        => _glVertexAttribPointer((uint)index, size, (uint)type, (byte)(normalized ? 1 : 0), stride, (void*)(nint)pointer);

    public static void Uniform1(int location, int v0) => _glUniform1i(location, v0);

    public static void Uniform2(int location, int count, ReadOnlySpan<float> value)
    {
        fixed (float* ptr = value)
        {
            _glUniform2fv(location, count, ptr);
        }
    }

    public static void Uniform4(int location, int count, ReadOnlySpan<float> value)
    {
        fixed (float* ptr = value)
        {
            _glUniform4fv(location, count, ptr);
        }
    }

    public static int GenTexture()
    {
        uint tex;
        _glGenTextures(1, &tex);
        return (int)tex;
    }

    public static void DeleteTexture(int texture)
    {
        var tex = (uint)texture;
        _glDeleteTextures(1, &tex);
    }

    public static void PixelStore(PixelStoreParameter pname, int param) => _glPixelStorei((uint)pname, param);

    public static void TexImage2D(TextureTarget target, int level, PixelInternalFormat internalFormat, int width, int height, int border, PixelFormat format, PixelType type, ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data.Length == 0 ? null : data)
        {
            _glTexImage2D((uint)target, level, (int)internalFormat, width, height, border, (uint)format, (uint)type, ptr);
        }
    }

    public static void TexSubImage2D(TextureTarget target, int level, int xoffset, int yoffset, int width, int height, PixelFormat format, PixelType type, ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data.Length == 0 ? null : data)
        {
            _glTexSubImage2D((uint)target, level, xoffset, yoffset, width, height, (uint)format, (uint)type, ptr);
        }
    }

    public static void TexParameter(TextureTarget target, TextureParameterName pname, int param) => _glTexParameteri((uint)target, (uint)pname, param);

    public static void GenerateMipmap(GenerateMipmapTarget target) => _glGenerateMipmap((uint)target);

    public static void DrawArrays(PrimitiveType mode, int first, int count) => _glDrawArrays((uint)mode, first, count);

    public static void BlendFuncSeparate(BlendingFactorSrc srcRGB, BlendingFactorDest dstRGB, BlendingFactorSrc srcAlpha, BlendingFactorDest dstAlpha)
        => _glBlendFuncSeparate((uint)srcRGB, (uint)dstRGB, (uint)srcAlpha, (uint)dstAlpha);

    public static int CreateShader(ShaderType type) => (int)_glCreateShader((uint)type);

    public static void ShaderSource(int shader, int count, string[] sources, int[]? lengths)
    {
        var source = string.Concat(sources);
        var utf8 = Encoding.UTF8.GetBytes(source);
        fixed (byte* pSource = utf8)
        {
            var strings = stackalloc byte*[1];
            strings[0] = pSource;
            var len = utf8.Length;
            _glShaderSource((uint)shader, 1, strings, &len);
        }
    }

    public static void CompileShader(int shader) => _glCompileShader((uint)shader);

    public static void GetShader(int shader, ShaderParameter pname, out int value)
    {
        fixed (int* p = &value)
        {
            _glGetShaderiv((uint)shader, (uint)pname, p);
        }
    }

    public static string GetShaderInfoLog(int shader)
    {
        var length = 0;
        _glGetShaderiv((uint)shader, 0x8B84, &length);
        if (length <= 1)
        {
            return string.Empty;
        }

        var buffer = stackalloc byte[length];
        var written = 0;
        _glGetShaderInfoLog((uint)shader, length, &written, buffer);
        return Encoding.UTF8.GetString(buffer, Math.Max(0, written));
    }

    public static int CreateProgram() => (int)_glCreateProgram();

    public static void AttachShader(int program, int shader) => _glAttachShader((uint)program, (uint)shader);

    public static void BindAttribLocation(int program, int index, string name)
    {
        var utf8 = Encoding.UTF8.GetBytes(name);
        fixed (byte* pName = utf8)
        {
            _glBindAttribLocation((uint)program, (uint)index, pName);
        }
    }

    public static void LinkProgram(int program) => _glLinkProgram((uint)program);

    public static void GetProgram(int program, GetProgramParameterName pname, out int value)
    {
        fixed (int* p = &value)
        {
            _glGetProgramiv((uint)program, (uint)pname, p);
        }
    }

    public static string GetProgramInfoLog(int program)
    {
        var length = 0;
        _glGetProgramiv((uint)program, 0x8B84, &length);
        if (length <= 1)
        {
            return string.Empty;
        }

        var buffer = stackalloc byte[length];
        var written = 0;
        _glGetProgramInfoLog((uint)program, length, &written, buffer);
        return Encoding.UTF8.GetString(buffer, Math.Max(0, written));
    }

    public static int GetUniformLocation(int program, string name)
    {
        var utf8 = Encoding.UTF8.GetBytes(name);
        fixed (byte* pName = utf8)
        {
            return _glGetUniformLocation((uint)program, pName);
        }
    }

    public static void DeleteProgram(int program) => _glDeleteProgram((uint)program);

    public static void DeleteShader(int shader) => _glDeleteShader((uint)shader);

    public static int GenVertexArray()
    {
        uint vao;
        _glGenVertexArrays(1, &vao);
        return (int)vao;
    }

    public static int GenBuffer()
    {
        uint buffer;
        _glGenBuffers(1, &buffer);
        return (int)buffer;
    }

    public static void DeleteBuffer(int buffer)
    {
        var b = (uint)buffer;
        _glDeleteBuffers(1, &b);
    }

    public static void DeleteVertexArray(int vao)
    {
        var v = (uint)vao;
        _glDeleteVertexArrays(1, &v);
    }

    public static int GetInteger(GetPName pname)
    {
        var value = 0;
        _glGetIntegerv((uint)pname, &value);
        return value;
    }

    public static int GenFramebuffer()
    {
        uint fb;
        _glGenFramebuffers(1, &fb);
        return (int)fb;
    }

    public static void BindFramebuffer(FramebufferTarget target, int framebuffer) => _glBindFramebuffer((uint)target, (uint)framebuffer);

    public static int GenRenderbuffer()
    {
        uint rb;
        _glGenRenderbuffers(1, &rb);
        return (int)rb;
    }

    public static void BindRenderbuffer(RenderbufferTarget target, int renderbuffer) => _glBindRenderbuffer((uint)target, (uint)renderbuffer);

    public static void RenderbufferStorage(RenderbufferTarget target, RenderbufferStorage internalformat, int width, int height)
        => _glRenderbufferStorage((uint)target, (uint)internalformat, (uint)width, (uint)height);

    public static void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment, TextureTarget textarget, int texture, int level)
        => _glFramebufferTexture2D((uint)target, (uint)attachment, (uint)textarget, (uint)texture, level);

    public static void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment, RenderbufferTarget renderbuffertarget, int renderbuffer)
        => _glFramebufferRenderbuffer((uint)target, (uint)attachment, (uint)renderbuffertarget, (uint)renderbuffer);

    public static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget target)
        => (FramebufferErrorCode)(int)_glCheckFramebufferStatus((uint)target);

    public static void DeleteFramebuffer(int framebuffer)
    {
        var fb = (uint)framebuffer;
        _glDeleteFramebuffers(1, &fb);
    }

    public static void DeleteRenderbuffer(int renderbuffer)
    {
        var rb = (uint)renderbuffer;
        _glDeleteRenderbuffers(1, &rb);
    }

    public static void Finish() => _glFinish();
}