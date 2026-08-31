namespace Aprillz.MewVG;

/// <summary>
/// GLSL sources for the fill shader. The bodies are written in the common subset of
/// GLSL 140 and GLSL ES 300; only <see cref="Header"/> differs per profile.
/// </summary>
internal static class GLNVGShaderSource
{
    internal static string Header(NanoVGGLProfile profile)
    {
        // GLSL ES has no default float precision in fragment shaders.
        if (profile == NanoVGGLProfile.Gles3)
        {
            return "#version 300 es\n" +
                   "precision highp float;\n" +
                   "precision highp int;\n" +
                   "#define UNIFORMARRAY_SIZE 13\n\n";
        }
        else
        {
            return "#version 140\n" +
                   "#define UNIFORMARRAY_SIZE 13\n\n";
        }
    }

    internal const string FILL_VERT_SHADER =
            "\tuniform vec2 viewSize;\n" +
            "\tin vec2 vertex;\n" +
            "\tin vec2 tcoord;\n" +
            "\tout vec2 ftcoord;\n" +
            "\tout vec2 fpos;\n" +
            "void main(void) {\n" +
            "\tftcoord = tcoord;\n" +
            "\tfpos = vertex;\n" +
            "\tgl_Position = vec4(2.0*vertex.x/viewSize.x - 1.0, 1.0 - 2.0*vertex.y/viewSize.y, 0, 1);\n" +
            "}\n";

    internal const string FILL_FRAG_SHADER =
            "\tuniform vec4 frag[UNIFORMARRAY_SIZE];\n" +
            "\tuniform sampler2D tex;\n" +
            "\tuniform vec2 coverageOrigin;\n" +
            "\tuniform sampler2D clipTex;\n" +
            "\tuniform int clipEnabled;\n" +
            "\tuniform vec2 clipOrigin;\n" +
            "\tuniform vec2 clipSize;\n" +
            "\tuniform sampler2D maskTex;\n" +
            "\tuniform int maskEnabled;\n" +
            "\tuniform vec2 maskOrigin;\n" +
            "\tuniform vec2 maskSize;\n" +
            "\tuniform float maskScale;\n" +
            "\tin vec2 ftcoord;\n" +
            "\tin vec2 fpos;\n" +
            "\tout vec4 outColor;\n" +
            "\t#define scissorMat mat3(frag[0].xyz, frag[1].xyz, frag[2].xyz)\n" +
            "\t#define paintMat mat3(frag[3].xyz, frag[4].xyz, frag[5].xyz)\n" +
            "\t#define innerCol frag[6]\n" +
            "\t#define outerCol frag[7]\n" +
            "\t#define scissorExt frag[8].xy\n" +
            "\t#define scissorScale frag[8].zw\n" +
            "\t#define extent frag[9].xy\n" +
            "\t#define radius frag[9].z\n" +
            "\t#define feather frag[9].w\n" +
            "\t#define gradientCenter frag[10].xy\n" +
            "\t#define gradientRadii frag[10].zw\n" +
            "\t#define gradientFocal frag[11].xy\n" +
            "\t#define gradientSpread int(frag[11].z)\n" +
            "\t#define strokeMult frag[12].x\n" +
            "\t#define strokeThr frag[12].y\n" +
            "\t#define texType int(frag[12].z)\n" +
            "\t#define type int(frag[12].w)\n" +
            "\n" +
            "float sdroundrect(vec2 pt, vec2 ext, float rad) {\n" +
            "\tvec2 ext2 = ext - vec2(rad,rad);\n" +
            "\tvec2 d = abs(pt) - ext2;\n" +
            "\treturn min(max(d.x,d.y),0.0) + length(max(d,0.0)) - rad;\n" +
            "}\n" +
            "\n" +
            "float scissorMask(vec2 p) {\n" +
            "\tvec2 sc = (abs((scissorMat * vec3(p,1.0)).xy) - scissorExt);\n" +
            "\tsc = vec2(0.5,0.5) - sc * scissorScale;\n" +
            "\treturn clamp(sc.x,0.0,1.0) * clamp(sc.y,0.0,1.0);\n" +
            "}\n" +
            "float clipMask() {\n" +
            "\tif (clipEnabled == 0) return 1.0;\n" +
            "\tif (clipEnabled == 2) return 0.0;\n" +
            "\tvec2 p = gl_FragCoord.xy - clipOrigin;\n" +
            "\tif (p.x < 0.0 || p.y < 0.0 || p.x >= clipSize.x || p.y >= clipSize.y) return 0.0;\n" +
            "\treturn texelFetch(clipTex, ivec2(p), 0).r;\n" +
            "}\n" +
            "float maskCoverage() {\n" +
            "\tif (maskEnabled == 0) return 1.0;\n" +
            "\tvec2 p = (fpos - maskOrigin) * maskScale;\n" +
            "\tif (p.x < 0.0 || p.y < 0.0 || p.x >= maskSize.x || p.y >= maskSize.y) return 0.0;\n" +
            "\treturn texelFetch(maskTex, ivec2(p), 0).r;\n" +
            "}\n" +
            "\n" +
            "float gradientRadialT(vec2 p, vec2 center, vec2 focal, vec2 radii, int spread) {\n" +
            "\tvec2 np = (p - center) / radii;\n" +
            "\tvec2 nf = (focal - center) / radii;\n" +
            "\tvec2 d = np - nf;\n" +
            "\tfloat a = dot(d, d);\n" +
            "\tif (a <= 1e-6) return 0.0;\n" +
            "\tfloat b = 2.0 * dot(nf, d);\n" +
            "\tfloat c = dot(nf, nf) - 1.0;\n" +
            "\tfloat disc = b * b - 4.0 * a * c;\n" +
            "\tif (disc <= 0.0) return 1.0;\n" +
            "\tfloat u = (-b + sqrt(disc)) / (2.0 * a);\n" +
            "\tif (u <= 1e-6) return 1.0;\n" +
            "\tfloat t = 1.0 / u;\n" +
            "\tif (spread == 0) return clamp(t, 0.0, 1.0);\n" +
            "\tif (spread == 2) return fract(max(t, 0.0));\n" +
            "\tfloat r = mod(max(t, 0.0), 2.0);\n" +
            "\treturn r <= 1.0 ? r : 2.0 - r;\n" +
            "}\n" +
            "float gradientLinearT(vec2 p, vec2 startPt, vec2 endPt, int spread) {\n" +
            "\tvec2 axis = endPt - startPt;\n" +
            "\tfloat len2 = dot(axis, axis);\n" +
            "\tif (len2 <= 1e-6) return 0.0;\n" +
            "\tfloat t = dot(p - startPt, axis) / len2;\n" +
            "\tif (spread == 0) return clamp(t, 0.0, 1.0);\n" +
            "\tif (spread == 2) return fract(max(t, 0.0));\n" +
            "\tfloat r = mod(max(t, 0.0), 2.0);\n" +
            "\treturn r <= 1.0 ? r : 2.0 - r;\n" +
            "}\n" +
            "float strokeMask() {\n" +
            "\tif (strokeMult < 0.0) {\n" +
            "\t\treturn clamp(ftcoord.x + 0.5, 0.0, 1.0) * min(1.0, ftcoord.y);\n" +
            "\t}\n" +
            "\treturn clamp((1.0-abs(ftcoord.x*2.0-1.0))*strokeMult, 0.0, 1.0) * min(1.0, ftcoord.y);\n" +
            "}\n" +
            "\n" +
            "void main(void) {\n" +
            "\tvec4 result;\n" +
            "\tfloat scissor = scissorMask(fpos);\n" +
            "\tfloat strokeAlpha = strokeMask();\n" +
            "\tif (strokeAlpha < strokeThr) discard;\n" +
            "\tif (type == 0) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat d = clamp((sdroundrect(pt, extent, radius) + feather*0.5) / feather, 0.0, 1.0);\n" +
            "\t\tvec4 color = mix(innerCol,outerCol,d);\n" +
            "\t\tcolor *= strokeAlpha * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t} else if (type == 1) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy / extent;\n" +
            "\t\tvec4 color = texture(tex, pt);\n" +
            "\t\tif (texType == 1) color = vec4(color.xyz*color.w,color.w);\n" +
            "\t\tif (texType == 2) color = vec4(color.x);\n" +
            "\t\tcolor *= innerCol;\n" +
            "\t\tcolor *= strokeAlpha * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t} else if (type == 6) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat t = gradientRadialT(pt, gradientCenter, gradientFocal, gradientRadii, gradientSpread);\n" +
            "\t\tvec4 color = texture(tex, vec2(t, 0.5));\n" +
            "\t\tcolor *= innerCol;\n" +
            "\t\tcolor *= strokeAlpha * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t} else if (type == 7) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat t = gradientLinearT(pt, gradientCenter, gradientFocal, gradientSpread);\n" +
            "\t\tvec4 color = texture(tex, vec2(t, 0.5));\n" +
            "\t\tcolor *= innerCol;\n" +
            "\t\tcolor *= strokeAlpha * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t} else if (type == 2) {\n" +
            "\t\tresult = vec4(1,1,1,1);\n" +
            "\t} else if (type == 3) {\n" +
            "\t\tvec4 color = texture(tex, ftcoord);\n" +
            "\t\tif (texType == 1) color = vec4(color.xyz*color.w,color.w);\n" +
            "\t\tif (texType == 2) color = vec4(color.x);\n" +
            "\t\tcolor *= scissor;\n" +
            "\t\tresult = color * innerCol;\n" +
            "\t} else if (type == 4) {\n" +
            "\t\tresult = vec4(strokeAlpha);\n" +
            "\t} else if (type == 5) {\n" +
            "\t\tfloat coverage = texelFetch(tex, ivec2(gl_FragCoord.xy - coverageOrigin), 0).r;\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat d = clamp((sdroundrect(pt, extent, radius) + feather*0.5) / feather, 0.0, 1.0);\n" +
            "\t\tvec4 color = mix(innerCol,outerCol,d);\n" +
            "\t\tcolor *= coverage * scissor;\n" +
            "\t\tresult = color;\n" +
            "\t}\n" +
            "\toutColor = result * clipMask() * maskCoverage();\n" +
            "}\n";
}
