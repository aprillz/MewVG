# Aprillz.MewVG

A cross-platform, fully managed .NET vector graphics library - a C# port of NanoVG with no native dependencies.

- GitHub: https://github.com/aprillz/MewVG
- License: MIT

## Concept

- Pure C# / fully managed (no native binaries to ship)
- OpenGL on Windows/Linux, WebGL2 in browsers, Metal on macOS
- NanoVG drawing model (paths, fills, strokes, gradients, clipping)
- NativeAOT / Trim friendly (net8.0, net10.0)

## Packages

- `Aprillz.MewVG.Core` - platform-agnostic context, path, and paint API
- `Aprillz.MewVG.GL` - OpenGL / WebGL2 rendering backend
- `Aprillz.MewVG.Metal` - Metal rendering backend (macOS)

## Install

```sh
dotnet add package Aprillz.MewVG.Core
dotnet add package Aprillz.MewVG.GL      # OpenGL / WebGL2
# or
dotnet add package Aprillz.MewVG.Metal   # Metal (macOS)
```

## Quick start

```csharp
using Aprillz.MewVG;

// Create and make an OpenGL context current first.
MewVGGL.Initialize(getProcAddress);
using var vg = new MewVGGL();

vg.BeginFrame(width, height, devicePixelRatio);

vg.BeginPath();
vg.RoundedRect(20, 20, 200, 120, 12);
vg.FillColor(MewVGColor.RGBA(80, 160, 220, 255));
vg.Fill();

vg.EndFrame();
```
