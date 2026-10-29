# Third-party notices

Overboard is licensed under the MIT License (see [LICENSE](LICENSE)).
Its releases include the following third-party components. Full license texts are in [licenses/](licenses).

| Component | Version | License | Copyright |
|---|---|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (Avalonia, Desktop, Win32, X11, Native, FreeDesktop, FreeDesktop.AtSpi, Skia, HarfBuzz, Themes.Fluent, Remote.Protocol) | 12.1.4 | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| [ANGLE](https://chromium.googlesource.com/angle/angle) (Avalonia.Angle.Windows.Natives, Windows only) | 2.1.27548.20260419 | BSD-3-Clause, [licenses/ANGLE-LICENSE.txt](licenses/ANGLE-LICENSE.txt) | Copyright 2018 The ANGLE Project Authors |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | 3.119.4 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp) | 8.3.1.3 | MIT | Copyright (c) 2015-2016 Xamarin, Inc.; Copyright (c) 2017-2018 Microsoft Corporation |
| Skia, HarfBuzz and their native dependencies | as shipped with the two packages above | various, [licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt](licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt) | see that file |
| [MicroCom.Runtime](https://github.com/kekekeks/MicroCom) | 0.11.6 | MIT | Copyright 2021 © Nikita Tsukanov |
| [Tmds.DBus.Protocol](https://github.com/tmds/Tmds.DBus) | 0.94.1 | MIT | Copyright Tom Deseyn |
| [SharpHook](https://github.com/TolikPylypchuk/SharpHook) | 8.0.1 | MIT | (c) 2021 Anatoliy Pylypchuk |
| [libuiohook](https://github.com/TolikPylypchuk/libuiohook) (fork used by SharpHook) | commit `3890543` | **LGPL-3.0**, see below | Alexander Barker and contributors; fork by Anatoliy Pylypchuk |
| [.NET runtime](https://github.com/dotnet/runtime) (self-contained builds) | 10.0 | MIT, with [licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt](licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt) | Copyright (c) .NET Foundation and Contributors |

## libuiohook (LGPL-3.0)

Overboard uses libuiohook, unmodified, for its system-wide shortcuts. It is a separate dynamic library
shipped next to the executable, and is not built into it:

- Windows: `uiohook.dll`
- Linux: `libuiohook.so`, `libuiohook-x11.so`, `libuiohook-xrecord.so`, `libuiohook-wayland.so`
- macOS: `Overboard.app/Contents/MacOS/libuiohook.dylib`

You may replace this library with a modified version built from its source. Overboard loads whichever copy sits next to it.

- License: GNU Lesser General Public License version 3, [licenses/LGPL-3.0.txt](licenses/LGPL-3.0.txt),
  which supplements the GNU General Public License version 3, [licenses/GPL-3.0.txt](licenses/GPL-3.0.txt).
- Corresponding source, at the exact version shipped:
  https://github.com/TolikPylypchuk/libuiohook/tree/3890543a1aa09f20966b1784650487093daad97e

## MIT License

The components marked MIT above are provided under the following terms, with their respective copyright notices:

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Keeping this file up to date

When a dependency is upgraded, update its version above and refresh the matching file in `licenses/`
from the NuGet package (`THIRD-PARTY-NOTICES.txt`, `LICENSE`), and the libuiohook commit from the
`libuiohook` submodule of the SharpHook release in use.
