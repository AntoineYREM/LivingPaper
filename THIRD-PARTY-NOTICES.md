# Third-party notices

LivingPaper is under the AGPL-3.0 (see `LICENSE`). It uses the components below, under licenses that can be combined with it.
The continuous integration fails if a NuGet package, direct or transitive, has a license that is not on a list of permissive
licenses (`.github/scripts/check-licenses.py`).

## In the application

| Component | License | Use |
|---|---|---|
| [.NET and ASP.NET Core](https://github.com/dotnet/aspnetcore) (Microsoft) | MIT | Runtime, web framework, Data Protection, logging |
| [PdfPig](https://github.com/UglyToad/PdfPig) 0.1.16 | Apache-2.0 | Reads the text of PDF books. Pure .NET. |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) 11.0.0 (Microsoft) | MIT | Access to the database of the passages of the books |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) 3.0.5 (Eric Sink) | Apache-2.0 | Binding of .NET to SQLite, brought by Microsoft.Data.Sqlite |
| [SQLite](https://sqlite.org/copyright.html) 3.53.4 | Public domain | Database engine embedded in the application. Native library. |
| [sqlite-vec](https://github.com/asg017/sqlite-vec) 0.1.7-alpha.2.1 (Alex Garcia) | MIT or Apache-2.0 | SQLite extension that compares vectors. Native library, prerelease. |
| [EB Garamond](https://github.com/octaviopardo/EBGaramond12) (The EB Garamond Project Authors) | SIL OFL 1.1 | Typeface, served by the application itself (`wwwroot/fonts`, license in `OFL.txt`) |

The Docker image is built on the Microsoft .NET images (`mcr.microsoft.com/dotnet/aspnet`), which hold Debian packages under
their own licenses.

## For development and tests only (not shipped)

| Component | License |
|---|---|
| [xUnit v3](https://github.com/xunit/xunit) | Apache-2.0 |
| [Shouldly](https://github.com/shouldly/shouldly) | BSD-3-Clause |
| [Playwright for .NET](https://github.com/microsoft/playwright-dotnet) (drives Chromium in the end-to-end tests; downloads Chromium and the Playwright driver on first run) | MIT |
| [Microsoft.Testing.Platform](https://github.com/microsoft/testfx), `Microsoft.AspNetCore.Mvc.Testing` | MIT |

## The examples

The files of `examples/` are under CC0 1.0 (see `examples/LICENSE`). The books they describe are in the public domain.
