# Notices — Tetitulz.Pdf Windows packages

Copyright (c) 2026 Tamir Mordo. The packages, the demo and the tests in this repository are licensed
under the Apache License, Version 2.0 ([`LICENSE`](LICENSE)).

They are built on **Tetitulz.Pdf** ([github.com/tetitu/tetitulz.pdf](https://github.com/tetitu/tetitulz.pdf)),
included as the `lib/tetitulz.pdf` submodule. Its licences (Apache 2.0, and the Tetitulz.Pdf
Attribution License for `Plugins.PostScript`, `Plugins.Maintenance` and `Plugins.PageEdits`) and the
third-party components it carries are listed in its own `NOTICE`, `NOTICE.md` and `THIRD-PARTY.md`,
which every package redistribution carries. `Tetitulz.Pdf.Printing` depends on `Plugins.PostScript`,
so a program that ships it ships that plugin under the Attribution License.

## Used by the packages (the WinForms viewer control, Printing)

| Component | License | Used for |
|---|---|---|
| **Serilog** | Apache 2.0 (© Serilog Contributors) | The logging API of `Tetitulz.Pdf.Printing`; the host chooses the sinks. <br>https://github.com/serilog/serilog |
| **Microsoft.NETFramework.ReferenceAssemblies.net462** | MIT (© .NET Foundation) | Reference assemblies for the viewer control's `net462` target, at build time only. |

## Used by the test projects only

**xunit**, **xunit.runner.visualstudio**, **Microsoft.NET.Test.Sdk**, **coverlet.collector** — all
Apache 2.0 or MIT. Not distributed.
