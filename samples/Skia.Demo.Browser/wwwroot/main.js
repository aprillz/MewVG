import { dotnet } from './_framework/dotnet.js'

const canvas = document.getElementById('canvas');

function resize() {
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.round(canvas.clientWidth * dpr);
    canvas.height = Math.round(canvas.clientHeight * dpr);
}
resize();
window.addEventListener('resize', resize);

const { getAssemblyExports, getConfig } = await dotnet.create();
const exports = await getAssemblyExports(getConfig().mainAssemblyName);

// Creates the WebGL2 context on #canvas and initializes MewVG. Init lives in an export rather
// than Main so the runtime stays alive for the frame loop.
exports.Skia.Demo.Browser.Program.InitializeDemo();

function frame() {
    const dpr = window.devicePixelRatio || 1;
    exports.Skia.Demo.Browser.Program.RenderFrame(
        canvas.clientWidth, canvas.clientHeight, dpr, canvas.width, canvas.height);
    requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
