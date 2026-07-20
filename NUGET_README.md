# Aprillz.MewVG

A cross-platform, fully managed .NET vector graphics library - a C# port of NanoVG with no native dependencies.

- GitHub: https://github.com/aprillz/MewVG
- License: MIT

## Concept

- Pure C# / fully managed (no native binaries to ship)
- OpenGL on Windows/Linux, Metal on macOS
- NanoVG drawing model (paths, fills, strokes, gradients, clipping)
- NativeAOT / Trim friendly (net8.0, net10.0)

## Packages

- `Aprillz.MewVG.Core` - platform-agnostic context, path, and paint API
- `Aprillz.MewVG.GL` - OpenGL rendering backend (Windows, Linux)
- `Aprillz.MewVG.Metal` - Metal rendering backend (macOS)

## Install

```sh
dotnet add package Aprillz.MewVG.Core
dotnet add package Aprillz.MewVG.GL      # OpenGL (Windows, Linux)
# or
dotnet add package Aprillz.MewVG.Metal   # Metal (macOS)
```

## Quick start

```csharp
using Aprillz.MewVG;

// vg is created through a backend (Aprillz.MewVG.GL / .Metal).
vg.BeginFrame(width, height, devicePixelRatio);

vg.BeginPath();
vg.RoundedRect(20, 20, 200, 120, 12);
vg.FillColor(NVGcolor.RGBA(80, 160, 220, 255));
vg.Fill();

vg.EndFrame();
```
