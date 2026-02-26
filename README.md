[![한국어](https://img.shields.io/badge/README.md-한국어-green.svg)](ko)

<p align="center">
  <img src="assets/logo/logo_h-960.png" alt="MewVG" height="120" />
</p>

# MewVG

![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-0078D4?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black)
![macOS](https://img.shields.io/badge/macOS-901DBA?logo=Apple&logoColor=white)
![License: MIT](https://img.shields.io/badge/License-MIT-000000)
[![NuGet](https://img.shields.io/nuget/v/Aprillz.MewVG.Core.svg?label=NuGet)](https://www.nuget.org/packages/Aprillz.MewVG.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aprillz.MewVG.Core.svg?label=Downloads)](https://www.nuget.org/packages/Aprillz.MewVG.Core/)

---

**MewVG** is a cross-platform, fully managed .NET vector graphics library based on [NanoVG](https://github.com/memononen/nanovg).

## Packages

| Package | Description |
|---|---|
| `Aprillz.MewVG.Core` | Platform-agnostic NanoVG context and path API |
| `Aprillz.MewVG.GL` | OpenGL rendering backend (Windows, Linux) |
| `Aprillz.MewVG.Metal` | Metal rendering backend (macOS) |

## Highlights

- **Pure C# / Fully Managed** &mdash; no native dependencies required
- **Cross-platform** &mdash; OpenGL (Win32, X11) and Metal (macOS) backends
- **NanoVG-compatible API** &mdash; familiar drawing model for paths, fills, strokes, and text
- **NativeAOT / Trim friendly** &mdash; targets `net8.0` and `net10.0`

## Getting Started

```bash
dotnet add package Aprillz.MewVG.Core
dotnet add package Aprillz.MewVG.GL      # for OpenGL
# or
dotnet add package Aprillz.MewVG.Metal    # for macOS Metal
```

## Project Structure

```
MewVG/
├── src/
│   ├── MewVG.Core/       Core NanoVG context, types, math
│   ├── MewVG.GL/         OpenGL rendering backend
│   └── MewVG.Metal/      Metal rendering backend
├── samples/
│   ├── MewVG.Demo.Windows/  Windows demo (GLFW + OpenGL)
│   ├── MewVG.Demo.Linux/    Linux demo (X11 + OpenGL)
│   └── MewVG.Demo.MacOS/    macOS demo (Metal)
└── build/
    └── MewVG.Common.props
```

## License

This project is licensed under the [MIT License](LICENSE).

### NanoVG

MewVG is based on the following projects:

- **[NanoVG](https://github.com/memononen/nanovg)** by Mikko Mononen &mdash; **zlib License**

  > Copyright (c) 2013 Mikko Mononen memon@inside.org
  >
  > This software is provided 'as-is', without any express or implied warranty.
  > In no event will the authors be held liable for any damages arising from the use of this software.
  >
  > Permission is granted to anyone to use this software for any purpose, including commercial applications,
  > and to alter it and redistribute it freely, subject to the following restrictions:
  >
  > 1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
  > 2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
  > 3. This notice may not be removed or altered from any source distribution.

- **[MetalNanoVG](https://github.com/ollix/MetalNanoVG)** by Ollix &mdash; **MIT License**

  > Copyright (c) 2017 Ollix
  >
  > Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
  >
  > The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
  >
  > THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
