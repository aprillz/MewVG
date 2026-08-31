using Xunit;

namespace Aprillz.MewVG.Tests;

// Pins the GLSL profile contract: the shader bodies must stay in the common subset of
// GLSL 140 and GLSL ES 300 so that only the header differs per profile.
public class GLShaderSourceTests
{
    [Fact]
    public void Gl3CoreHeaderTargetsGlsl140()
    {
        var header = GLNVGShaderSource.Header(NanoVGGLProfile.Gl3Core);

        Assert.StartsWith("#version 140\n", header);
        Assert.DoesNotContain("precision", header);
        Assert.Contains("#define UNIFORMARRAY_SIZE 13", header);
    }

    [Fact]
    public void Gles3HeaderTargetsGlslEs300WithExplicitPrecision()
    {
        var header = GLNVGShaderSource.Header(NanoVGGLProfile.Gles3);

        Assert.StartsWith("#version 300 es\n", header);
        Assert.Contains("precision highp float;", header);
        Assert.Contains("precision highp int;", header);
        Assert.Contains("#define UNIFORMARRAY_SIZE 13", header);
    }

    [Fact]
    public void ShaderBodiesStayInCommonGlslSubset()
    {
        var bodies = new[] { GLNVGShaderSource.FILL_VERT_SHADER, GLNVGShaderSource.FILL_FRAG_SHADER };

        foreach (var body in bodies)
        {
            // The version directive and precision qualifiers belong to the header only.
            Assert.DoesNotContain("#version", body);
            Assert.DoesNotContain("precision ", body);
            Assert.DoesNotContain("#extension", body);

            // Pre-GLSL-140 constructs rejected by GLSL ES 300.
            Assert.DoesNotContain("gl_FragColor", body);
            Assert.DoesNotContain("texture2D(", body);
            Assert.DoesNotContain("attribute ", body);
            Assert.DoesNotContain("varying ", body);
        }
    }

    [Fact]
    public void FragmentShaderDeclaresItsOwnOutput()
    {
        Assert.Contains("out vec4 outColor;", GLNVGShaderSource.FILL_FRAG_SHADER);
    }
}
