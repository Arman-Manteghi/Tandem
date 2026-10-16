# Third-party notices

The released `Tandem.exe` is a self-contained .NET application. It includes the following
components, which are distributed under their own licenses.

## .NET Runtime, Windows Presentation Foundation and Windows Forms

- Copyright (c) .NET Foundation and Contributors
- License: MIT
- Source: <https://github.com/dotnet/runtime>, <https://github.com/dotnet/wpf>, <https://github.com/dotnet/winforms>

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

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

The .NET components contain further third-party code, listed in the
[.NET Runtime third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT).

## Used only to build and test (not included in `Tandem.exe`)

| Component | License |
|---|---|
| [xUnit.net](https://github.com/xunit/xunit) | Apache License 2.0 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | MIT |
| [Pillow](https://github.com/python-pillow/Pillow) (README images only) | MIT-CMU |

## Fonts and icons

Tandem uses fonts that ship with Windows (Segoe UI Variable, Segoe Fluent Icons, Sitka). They are
not included in Tandem. The Tandem icon is drawn from code in this repository.

## Trademarks

"Claude" and "Anthropic" are trademarks of Anthropic, PBC. Tandem is not affiliated with or
endorsed by Anthropic.
