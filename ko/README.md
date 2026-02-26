# MewVG

![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-0078D4?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black)
![macOS](https://img.shields.io/badge/macOS-901DBA?logo=Apple&logoColor=white)
![License: MIT](https://img.shields.io/badge/License-MIT-000000)
[![NuGet](https://img.shields.io/nuget/v/Aprillz.MewVG.Core.svg?label=NuGet)](https://www.nuget.org/packages/Aprillz.MewVG.Core/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aprillz.MewVG.Core.svg?label=Downloads)](https://www.nuget.org/packages/Aprillz.MewVG.Core/)

---

**MewVG**는 [NanoVG](https://github.com/memononen/nanovg) 기반의 크로스플랫폼, 완전 관리형(fully managed) .NET 벡터 그래픽스 라이브러리입니다.

## 패키지

| 패키지 | 설명 |
|---|---|
| `Aprillz.MewVG.Core` | 플랫폼 독립적인 NanoVG 컨텍스트 및 경로 API |
| `Aprillz.MewVG.GL` | OpenGL 렌더링 백엔드 (Windows, Linux) |
| `Aprillz.MewVG.Metal` | Metal 렌더링 백엔드 (macOS) |

## 주요 특징

- **순수 C# / 완전 관리형** &mdash; 네이티브 의존성 없음
- **크로스플랫폼** &mdash; OpenGL (Win32, X11) 및 Metal (macOS) 백엔드
- **NanoVG 호환 API** &mdash; 경로, 채우기, 스트로크, 텍스트를 위한 친숙한 드로잉 모델
- **NativeAOT / Trim 지원** &mdash; `net8.0` 및 `net10.0` 대상

## 시작하기

```bash
dotnet add package Aprillz.MewVG.Core
dotnet add package Aprillz.MewVG.GL      # OpenGL용
# 또는
dotnet add package Aprillz.MewVG.Metal    # macOS Metal용
```

## 프로젝트 구조

```
MewVG/
├── src/
│   ├── MewVG.Core/       코어 NanoVG 컨텍스트, 타입, 수학 유틸리티
│   ├── MewVG.GL/         OpenGL 렌더링 백엔드
│   └── MewVG.Metal/      Metal 렌더링 백엔드
├── samples/
│   ├── MewVG.GL.Demo/    OpenGL 데모 앱
│   └── MewVG.Metal.Demo/ Metal 데모 앱
└── build/
    └── MewVG.Common.props
```

## 라이선스

이 프로젝트는 [MIT 라이선스](../LICENSE)로 제공됩니다.

### 원본 프로젝트

MewVG는 다음 프로젝트를 기반으로 합니다:

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
