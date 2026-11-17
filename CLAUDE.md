# Tetitulz.Pdf Windows packages — Contributor & Agent Guide

The Windows packages (the WinForms viewer control, `Printing`), the viewer's demo host and their
tests, built on the library in the `lib/tetitulz.pdf` submodule.

**The library's rulebook applies here too**: [`lib/tetitulz.pdf/CLAUDE.md`](lib/tetitulz.pdf/CLAUDE.md),
with its reasoning in `lib/tetitulz.pdf/CONTRIBUTING.md` — the coding conventions, the diagnostics
pattern, the testing rules and the banned patterns. What follows is only what is particular to this
repository.

## Layout

| Path | Contents |
|---|---|
| `src/` | The packages: `Tetitulz.Pdf.WinForms.PdfViewer` (`net462;net10.0-windows`), `Tetitulz.Pdf.Printing` (`net10.0-windows`) |
| `demo/` | `Viewer`, a minimal host for the control; never packed |
| `tests/` | One test project per package; `tests/ApiSnapshots` pins the packages' public API |
| `lib/tetitulz.pdf` | The library, a submodule; every project references its sources |

- **A change a package needs in the library is made in the library's repository**, pushed, then the
  submodule moved and committed here. Nothing here edits `lib/`.
- **`src/Directory.Build.props` and `.targets` import the library's `src/` ones**: a package here is
  held to the library's rules for a shipping assembly (packing, doc-comment and nullable errors, the
  licence files). Only the repository it names is this one.
- **Package versions**: `Directory.Packages.props` imports the library's, then adds the few only this
  repository uses. A package both use has one version, the library's.
- **`Printing` references `Plugins.PostScript`** (a print job is PostScript); the library's rule that
  the plugin references no feature keeps that edge acyclic.
- The library's guard tests scan the library's `src/` only. Here the packages are guarded by their own
  tests and API snapshots; the library's float-to-integer cast rule (`Core.Util.SaturatingCast`, never
  a cast) and its no-P/Invoke rule hold here by review.

## Testing the viewer

- **A viewer test runs its body through `tests/Tetitulz.Pdf.WinForms.PdfViewer.Tests/StaThread.Run`.**
  WinForms needs a single-threaded apartment, and an exception a window procedure raises there — a
  layout, a paint, a message handler — is outside the body's `try`: WinForms answers it with its
  thread-exception dialog, which nothing in a test host dismisses. The helper sets the thread's
  exception mode to catch and rethrows that exception as the test's failure, after the body's own when
  both occurred.
- **A test that shows a window** (`form.Show()`, `DrawToBitmap` of a shown control) **carries
  `[DesktopFact]`**: its one-time probe shows a form holding a `SplitContainer` off-screen, and where
  that throws (GDI+ refuses to paint a shown window on a locked screen or in a service session) the
  test is skipped with the reason, since no change to the control can prevent it.

## Build & test

```bash
git submodule update --init
git -C lib/tetitulz.pdf fetch --tags     # the library's versions, which a packed package depends on
dotnet build tetitulz.pdf-apps.sln
dotnet test  tetitulz.pdf-apps.sln
```
