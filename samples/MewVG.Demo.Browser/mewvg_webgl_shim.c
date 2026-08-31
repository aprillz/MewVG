// Statically linked WebGL2 bootstrap for the MewVG browser demo: creates the context on the
// canvas and exposes Emscripten's GL proc-address lookup to managed code.
#include <emscripten/html5.h>
#include <emscripten/html5_webgl.h>

static EMSCRIPTEN_WEBGL_CONTEXT_HANDLE mewvg_context;

// Returns 0 on success, a negative EMSCRIPTEN_RESULT on failure.
int mewvg_webgl_init(const char* selector)
{
    EmscriptenWebGLContextAttributes attrs;
    emscripten_webgl_init_context_attributes(&attrs);
    attrs.majorVersion = 2;
    attrs.minorVersion = 0;
    attrs.alpha = 0;
    attrs.depth = 0;
    attrs.stencil = 0;
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

void* mewvg_webgl_get_proc(const char* name)
{
    return emscripten_webgl_get_proc_address(name);
}

// Never called. The interp-to-native trampoline generator only harvests DllImport signatures,
// not calli signatures, so these pin the shapes MewVG invokes through GL function pointers:
// glTexImage2D/glTexSubImage2D (9 ints), glUniform1f, glClearColor.
void mewvg_sig_viiiiiiiii(int arg0, int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8) {}
void mewvg_sig_vif(int arg0, float arg1) {}
void mewvg_sig_vffff(float arg0, float arg1, float arg2, float arg3) {}
