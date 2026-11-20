namespace Aprillz.MewVG;

/// <summary>
/// GLSL sources for the fill shader. The bodies are written in the common subset of
/// GLSL 140 and GLSL ES 300; only <see cref="Header"/> differs per profile. Compiled as is, the
/// fragment shader is the uber shader that picks the paint by uniform; with <c>VARIANT</c> and a
/// <c>PAINT_*</c> define (plus <c>TEX_TYPE</c> for an image paint and <c>SCISSOR</c>, <c>CLIP</c>, <c>MASK</c>,
/// <c>FULL_COVERAGE</c> as needed) it compiles to a variant that draws one paint without the other branches.
/// </summary>
internal static class GLNVGShaderSource
{
    internal static string Header(MewVGGLProfile profile)
    {
        // GLSL ES has no default float precision in fragment shaders.
        if (profile == MewVGGLProfile.Gles3)
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

    // A paint whose colour is innerCol alone, or innerCol times what it samples, takes a per-vertex tint as well,
    // so draws that differ in that colour alone can go out in one call: the uber shader always (1 when the tint
    // array is off), a variant when compiled with TINT.
    private const string TINT_DEFINE =
            "#ifndef VARIANT\n" +
            "#define TINT\n" +
            "#endif\n" +
            "#ifdef TINT\n" +
            "#define TINTED(color) ((color) * ftint)\n" +
            "#else\n" +
            "#define TINTED(color) (color)\n" +
            "#endif\n";

    // A rounded rectangle is an instance of one of two meshes (see GLNVGContext.CreateRoundedRectMesh), whose vertices
    // name a quarter of the shape and, along each axis, where in it they sit. ftcoord carries the position against the
    // quarter's outer corner circle's centre. The patch mesh passes the outer and inner radii (the inner one far inside
    // out for a fill) in fradii, interpolated so that an edge patch between corners of different radii still measures
    // the distance to its edge; the quarter mesh passes them in fshape, with how far the inner corner circle's centre
    // lies from the outer one's along each axis.
    private const string ROUNDED_RECT_VERTEX =
            "#if !defined(VARIANT) || defined(PAINT_ROUNDED_RECT)\n" +
            "\tin vec4 roundedBox;\n" +
            "\tin vec4 roundedRadii;\n" +
            "\tin vec2 roundedEdge;\n" +
            "\tout vec2 fradii;\n" +
            "\tflat out vec3 fshape;\n" +
            "vec2 roundedRectVertex(bool quarters) {\n" +
            "\tvec2 side = vertex.x > 0.0 ? roundedRadii.zy : roundedRadii.wx;\n" +
            "\tfloat radius = vertex.y > 0.0 ? side.x : side.y;\n" +
            "\tbool outline = roundedEdge.x >= 0.0;\n" +
            "\tfloat strokeHalf = max(roundedEdge.x, 0.0);\n" +
            "\tvec2 outerHalf = roundedBox.zw + strokeHalf;\n" +
            "\tfloat outerRadius = radius > 0.0 ? radius + strokeHalf : 0.0;\n" +
            "\tfloat innerRadius = max(radius - strokeHalf, 0.0);\n" +
            "\tvec2 cover = outerHalf + roundedEdge.y;\n" +
            "\tvec2 local;\n" +
            "\tif (quarters) {\n" +
            "\t\tlocal = tcoord * cover;\n" +
            "\t} else {\n" +
            "\t\tvec2 centre = max(outerHalf - outerRadius, 0.0);\n" +
            "\t\tlocal = vec2(tcoord.x == 1.0 ? cover.x : centre.x, tcoord.y == 1.0 ? cover.y : centre.y);\n" +
            "\t\tif (outline && tcoord.x == 2.0) local = vec2(0.0);\n" +
            "\t}\n" +
            "\tftcoord = local - outerHalf + outerRadius;\n" +
            "\tfradii = vec2(outerRadius, outline ? innerRadius : -1.0e4);\n" +
            "\tfshape = vec3(outerRadius, outline ? innerRadius : -1.0e4, outline ? 2.0 * strokeHalf - outerRadius + innerRadius : 0.0);\n" +
            "\treturn roundedBox.xy + vertex * local;\n" +
            "}\n" +
            "#endif\n";

    internal const string FILL_VERT_SHADER =
            TINT_DEFINE +
            "\tuniform vec2 viewSize;\n" +
            "\tin vec2 vertex;\n" +
            "\tin vec2 tcoord;\n" +
            "\tin vec2 instanceOffset;\n" +
            "\tout vec2 ftcoord;\n" +
            "\tout vec2 fpos;\n" +
            "#ifdef TINT\n" +
            "\tin vec4 tint;\n" +
            "\tflat out vec4 ftint;\n" +
            "#endif\n" +
            // What is affine in the vertex position (the scissor and paint coordinates, the position along a
            // linear gradient) a variant works out per vertex, which interpolation carries exactly; the fragment
            // shader is left only the part that is not affine.
            "#if !defined(VARIANT) || defined(SCISSOR) || defined(PAINT_IMAGE) || defined(PAINT_BOX_GRADIENT) || defined(PAINT_RADIAL_GRADIENT) || defined(PAINT_LINEAR_GRADIENT) || defined(PAINT_COVERAGE_COMPOSITE)\n" +
            "\tuniform vec4 frag[UNIFORMARRAY_SIZE];\n" +
            "#endif\n" +
            "#ifdef SCISSOR\n" +
            "\tout vec2 fscissor;\n" +
            "#endif\n" +
            ROUNDED_RECT_VERTEX +
            "#if defined(PAINT_IMAGE) || defined(PAINT_BOX_GRADIENT) || defined(PAINT_RADIAL_GRADIENT) || defined(PAINT_COVERAGE_COMPOSITE)\n" +
            "\tout vec2 fpaint;\n" +
            "#endif\n" +
            "#ifdef PAINT_LINEAR_GRADIENT\n" +
            "\tout float fgradT;\n" +
            "#endif\n" +
            "void main(void) {\n" +
            "#if defined(VARIANT) && defined(PAINT_ROUNDED_RECT) && defined(ROUNDED_RECT_QUARTERS)\n" +
            "\tvec2 position = roundedRectVertex(true);\n" +
            "#elif defined(VARIANT) && defined(PAINT_ROUNDED_RECT)\n" +
            "\tvec2 position = roundedRectVertex(false);\n" +
            "#elif defined(VARIANT)\n" +
            "\tvec2 position = vertex + instanceOffset;\n" +
            "\tftcoord = tcoord;\n" +
            "#else\n" +
            "\tvec2 position;\n" +
            "\tif (int(frag[12].w) == 8) {\n" +
            "\t\tposition = roundedRectVertex(frag[12].z > 0.5);\n" +
            "\t} else {\n" +
            "\t\tposition = vertex + instanceOffset;\n" +
            "\t\tftcoord = tcoord;\n" +
            "\t\tfradii = vec2(0.0);\n" +
            "\t\tfshape = vec3(0.0);\n" +
            "\t}\n" +
            "#endif\n" +
            "\tfpos = position;\n" +
            "#ifdef TINT\n" +
            "\tftint = tint;\n" +
            "#endif\n" +
            "#ifdef SCISSOR\n" +
            "\tfscissor = (mat3(frag[0].xyz, frag[1].xyz, frag[2].xyz) * vec3(position,1.0)).xy;\n" +
            "#endif\n" +
            "#if defined(PAINT_IMAGE)\n" +
            "\tfpaint = (mat3(frag[3].xyz, frag[4].xyz, frag[5].xyz) * vec3(position,1.0)).xy / frag[9].xy;\n" +
            "#elif defined(PAINT_BOX_GRADIENT) || defined(PAINT_RADIAL_GRADIENT) || defined(PAINT_COVERAGE_COMPOSITE)\n" +
            "\tfpaint = (mat3(frag[3].xyz, frag[4].xyz, frag[5].xyz) * vec3(position,1.0)).xy;\n" +
            "#endif\n" +
            "#ifdef PAINT_LINEAR_GRADIENT\n" +
            "\tvec2 pt = (mat3(frag[3].xyz, frag[4].xyz, frag[5].xyz) * vec3(position,1.0)).xy;\n" +
            "\tvec2 axis = frag[11].xy - frag[10].xy;\n" +
            "\tfloat len2 = dot(axis, axis);\n" +
            "\tfgradT = len2 <= 1e-6 ? 0.0 : dot(pt - frag[10].xy, axis) / len2;\n" +
            "#endif\n" +
            "\tgl_Position = vec4(2.0*position.x/viewSize.x - 1.0, 1.0 - 2.0*position.y/viewSize.y, 0, 1);\n" +
            "}\n";

    internal const string FILL_FRAG_SHADER =
            TINT_DEFINE +
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
            "#ifdef SCISSOR\n" +
            "\tin vec2 fscissor;\n" +
            "#endif\n" +
            "#if defined(PAINT_IMAGE) || defined(PAINT_BOX_GRADIENT) || defined(PAINT_RADIAL_GRADIENT) || defined(PAINT_COVERAGE_COMPOSITE)\n" +
            "\tin vec2 fpaint;\n" +
            "#endif\n" +
            "#ifdef PAINT_LINEAR_GRADIENT\n" +
            "\tin float fgradT;\n" +
            "#endif\n" +
            "#ifdef TINT\n" +
            "\tflat in vec4 ftint;\n" +
            "#endif\n" +
            "#if !defined(VARIANT) || defined(PAINT_ROUNDED_RECT)\n" +
            "\tin vec2 fradii;\n" +
            "\tflat in vec3 fshape;\n" +
            "#endif\n" +
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
            "float scissorMaskAt(vec2 local) {\n" +
            "\tvec2 sc = (abs(local) - scissorExt);\n" +
            "\tsc = vec2(0.5,0.5) - sc * scissorScale;\n" +
            "\treturn clamp(sc.x,0.0,1.0) * clamp(sc.y,0.0,1.0);\n" +
            "}\n" +
            "float scissorMask(vec2 p) {\n" +
            "\treturn scissorMaskAt((scissorMat * vec3(p,1.0)).xy);\n" +
            "}\n" +
            // An image variant converts the texel by the texture type it was compiled for.
            "#ifdef TEX_TYPE\n" +
            "vec4 texelColor(vec4 color) {\n" +
            "#if TEX_TYPE == 1\n" +
            "\treturn vec4(color.xyz*color.w, color.w);\n" +
            "#elif TEX_TYPE == 2\n" +
            "\treturn vec4(color.x);\n" +
            "#else\n" +
            "\treturn color;\n" +
            "#endif\n" +
            "}\n" +
            "#endif\n" +
            "float clipMask() {\n" +
            "#if defined(VARIANT) && !defined(CLIP)\n" +
            "\treturn 1.0;\n" +
            "#else\n" +
            "#ifndef VARIANT\n" +
            "\tif (clipEnabled == 0) return 1.0;\n" +
            "\tif (clipEnabled == 2) return 0.0;\n" +
            "#endif\n" +
            "\tvec2 p = gl_FragCoord.xy - clipOrigin;\n" +
            "\tif (p.x < 0.0 || p.y < 0.0 || p.x >= clipSize.x || p.y >= clipSize.y) return 0.0;\n" +
            "\treturn texelFetch(clipTex, ivec2(p), 0).r;\n" +
            "#endif\n" +
            "}\n" +
            "float maskCoverage() {\n" +
            "#if defined(VARIANT) && !defined(MASK)\n" +
            "\treturn 1.0;\n" +
            "#else\n" +
            "#ifndef VARIANT\n" +
            "\tif (maskEnabled == 0) return 1.0;\n" +
            "#endif\n" +
            "\tvec2 p = (fpos - maskOrigin) * maskScale;\n" +
            "\tif (p.x < 0.0 || p.y < 0.0 || p.x >= maskSize.x || p.y >= maskSize.y) return 0.0;\n" +
            "\treturn texelFetch(maskTex, ivec2(p), 0).r;\n" +
            "#endif\n" +
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
            "float gradientSpreadT(float t, int spread) {\n" +
            "\tif (spread == 0) return clamp(t, 0.0, 1.0);\n" +
            "\tif (spread == 2) return fract(max(t, 0.0));\n" +
            "\tfloat r = mod(max(t, 0.0), 2.0);\n" +
            "\treturn r <= 1.0 ? r : 2.0 - r;\n" +
            "}\n" +
            // Given the position against a corner circle's centre, the distance that, less the circle's radius, is the
            // signed distance to the rounded rectangle's edge.
            "float cornerDistance(vec2 q) {\n" +
            "\treturn min(max(q.x, q.y), 0.0) + length(max(q, 0.0));\n" +
            "}\n" +
            // A rounded rectangle covers what its outer edge covers and its inner edge does not; a fill's inner edge lies
            // so far inside out that it covers nothing. In the patch mesh the distance to a corner circle is the length
            // of the position against its centre; in the quarter mesh it is the corner distance, against the inner
            // circle's own centre for the inner edge.
            "#if !defined(VARIANT) || defined(PAINT_ROUNDED_RECT)\n" +
            "float roundedRectCoverage(bool quarters) {\n" +
            "\tfloat outerDistance;\n" +
            "\tfloat innerDistance;\n" +
            "\tif (quarters) {\n" +
            "\t\touterDistance = cornerDistance(ftcoord) - fshape.x;\n" +
            "\t\tinnerDistance = cornerDistance(ftcoord + fshape.z) - fshape.y;\n" +
            "\t} else {\n" +
            "\t\tfloat circleDistance = length(ftcoord);\n" +
            "\t\touterDistance = circleDistance - fradii.x;\n" +
            "\t\tinnerDistance = circleDistance - fradii.y;\n" +
            "\t}\n" +
            "\tfloat outer = clamp(0.5 - outerDistance * frag[9].x, 0.0, 1.0);\n" +
            "\tfloat inner = clamp(0.5 - innerDistance * frag[9].x, 0.0, 1.0);\n" +
            "\treturn max(outer - inner, 0.0);\n" +
            "}\n" +
            "#endif\n" +
            "float strokeMask() {\n" +
            "\tif (strokeMult < 0.0) {\n" +
            "\t\treturn clamp(ftcoord.x + 0.5, 0.0, 1.0) * min(1.0, ftcoord.y);\n" +
            "\t}\n" +
            "\treturn clamp((1.0-abs(ftcoord.x*2.0-1.0))*strokeMult, 0.0, 1.0) * min(1.0, ftcoord.y);\n" +
            "}\n" +
            "\n" +
            "void main(void) {\n" +
            "\tvec4 result;\n" +
            "#ifdef VARIANT\n" +
            "#ifdef SCISSOR\n" +
            "\tfloat scissor = scissorMaskAt(fscissor);\n" +
            "#else\n" +
            "\tfloat scissor = 1.0;\n" +
            "#endif\n" +
            // Every vertex of a fill drawn without a fringe carries the body texcoord, for which strokeMask is 1.
            "#ifdef FULL_COVERAGE\n" +
            "\tfloat strokeAlpha = 1.0;\n" +
            "#else\n" +
            "\tfloat strokeAlpha = strokeMask();\n" +
            "#endif\n" +
            "#if defined(PAINT_SOLID)\n" +
            "\tvec4 color = TINTED(innerCol);\n" +
            "\tcolor *= strokeAlpha * scissor;\n" +
            "\tresult = color;\n" +
            "#elif defined(PAINT_IMAGE)\n" +
            "\tvec4 color = texelColor(texture(tex, fpaint));\n" +
            "\tcolor *= innerCol;\n" +
            "\tcolor *= strokeAlpha * scissor;\n" +
            "\tresult = color;\n" +
            "#elif defined(PAINT_IMAGE_TRIANGLES)\n" +
            "\tvec4 color = texelColor(texture(tex, ftcoord));\n" +
            "\tcolor *= scissor;\n" +
            "\tresult = color * TINTED(innerCol);\n" +
            "#elif defined(PAINT_BOX_GRADIENT)\n" +
            "\tfloat d = clamp((sdroundrect(fpaint, extent, radius) + feather*0.5) / feather, 0.0, 1.0);\n" +
            "\tvec4 color = mix(innerCol,outerCol,d);\n" +
            "\tcolor *= strokeAlpha * scissor;\n" +
            "\tresult = color;\n" +
            "#elif defined(PAINT_RADIAL_GRADIENT)\n" +
            "\tfloat t = gradientRadialT(fpaint, gradientCenter, gradientFocal, gradientRadii, gradientSpread);\n" +
            "\tvec4 color = texture(tex, vec2(t, 0.5));\n" +
            "\tcolor *= innerCol;\n" +
            "\tcolor *= strokeAlpha * scissor;\n" +
            "\tresult = color;\n" +
            "#elif defined(PAINT_LINEAR_GRADIENT)\n" +
            "\tfloat t = gradientSpreadT(fgradT, gradientSpread);\n" +
            "\tvec4 color = texture(tex, vec2(t, 0.5));\n" +
            "\tcolor *= innerCol;\n" +
            "\tcolor *= strokeAlpha * scissor;\n" +
            "\tresult = color;\n" +
            "#elif defined(PAINT_ROUNDED_RECT) && defined(ROUNDED_RECT_QUARTERS)\n" +
            "\tresult = TINTED(innerCol) * (roundedRectCoverage(true) * scissor);\n" +
            "#elif defined(PAINT_ROUNDED_RECT)\n" +
            "\tresult = TINTED(innerCol) * (roundedRectCoverage(false) * scissor);\n" +
            "#elif defined(PAINT_SIMPLE)\n" +
            "\tresult = vec4(1,1,1,1);\n" +
            "#elif defined(PAINT_COVERAGE_OUTPUT)\n" +
            "\tresult = vec4(strokeAlpha);\n" +
            "#elif defined(PAINT_COVERAGE_COMPOSITE)\n" +
            "\tfloat coverage = texelFetch(tex, ivec2(gl_FragCoord.xy - coverageOrigin), 0).r;\n" +
            "\tfloat d = clamp((sdroundrect(fpaint, extent, radius) + feather*0.5) / feather, 0.0, 1.0);\n" +
            "\tvec4 color = mix(innerCol,outerCol,d);\n" +
            "\tcolor *= coverage * scissor;\n" +
            "\tresult = color;\n" +
            "#elif defined(PAINT_COVERAGE_SOLID)\n" +
            "\tfloat coverage = texelFetch(tex, ivec2(gl_FragCoord.xy - coverageOrigin), 0).r;\n" +
            "\tvec4 color = innerCol;\n" +
            "\tcolor *= coverage * scissor;\n" +
            "\tresult = color;\n" +
            "#endif\n" +
            "#else\n" +
            "\tfloat scissor = scissorMask(fpos);\n" +
            "\tfloat strokeAlpha = strokeMask();\n" +
            "\tif (strokeAlpha < strokeThr) discard;\n" +
            "\tif (type == 0) {\n" +
            "\t\tvec2 pt = (paintMat * vec3(fpos,1.0)).xy;\n" +
            "\t\tfloat d = clamp((sdroundrect(pt, extent, radius) + feather*0.5) / feather, 0.0, 1.0);\n" +
            "\t\tvec4 color = TINTED(mix(innerCol,outerCol,d));\n" +
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
            "\t} else if (type == 8) {\n" +
            "\t\tresult = TINTED(innerCol) * (roundedRectCoverage(frag[12].z > 0.5) * scissor);\n" +
            "\t} else if (type == 2) {\n" +
            "\t\tresult = vec4(1,1,1,1);\n" +
            "\t} else if (type == 3) {\n" +
            "\t\tvec4 color = texture(tex, ftcoord);\n" +
            "\t\tif (texType == 1) color = vec4(color.xyz*color.w,color.w);\n" +
            "\t\tif (texType == 2) color = vec4(color.x);\n" +
            "\t\tcolor *= scissor;\n" +
            "\t\tresult = color * TINTED(innerCol);\n" +
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
            "#endif\n" +
            "\toutColor = result * clipMask() * maskCoverage();\n" +
            "}\n";
}
