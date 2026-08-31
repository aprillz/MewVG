namespace Aprillz.MewVG;

/// <summary>
/// GL API surface the GL backend targets. Selected once per process via <see cref="NanoVGGL.Initialize"/>.
/// </summary>
public enum NanoVGGLProfile
{
    /// <summary>OpenGL 3.x core profile (GLSL 140).</summary>
    Gl3Core,

    /// <summary>OpenGL ES 3.0 / WebGL2 (GLSL ES 300). No BGRA upload, RGBA byte upload only.</summary>
    Gles3,
}
