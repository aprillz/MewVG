// Statically linked WebGL2 bootstrap for the Skia comparison demo: creates the context on the
// canvas and exposes Emscripten's GL proc-address lookup to managed code.
#include <emscripten/html5.h>
#include <emscripten/html5_webgl.h>

static EMSCRIPTEN_WEBGL_CONTEXT_HANDLE mewvg_context;

// Returns 0 on success, a negative EMSCRIPTEN_RESULT on failure.
int skia_webgl_init(const char* selector)
{
    EmscriptenWebGLContextAttributes attrs;
    emscripten_webgl_init_context_attributes(&attrs);
    attrs.majorVersion = 2;
    attrs.minorVersion = 0;
    attrs.alpha = 0;
    attrs.depth = 0;
    attrs.stencil = 1;
    // MewVG does its own analytic anti-aliasing; MSAA would only add cost.
    attrs.antialias = 0;
    attrs.preserveDrawingBuffer = 0;

    mewvg_context = emscripten_webgl_create_context(selector, &attrs);
    if (mewvg_context <= 0)
    {
        return mewvg_context != 0 ? (int)mewvg_context : -1;
    }

    return (int)emscripten_webgl_make_context_current(mewvg_context);
}

void* skia_webgl_get_proc(const char* name)
{
    return emscripten_webgl_get_proc_address(name);
}

