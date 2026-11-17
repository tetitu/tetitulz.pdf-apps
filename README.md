# Tetitulz.Pdf WinForms viewer

An embeddable WinForms PDF viewer control, `PdfViewerControl`, built on
[Tetitulz.Pdf](https://github.com/tetitu/tetitulz.pdf), the pure-managed .NET PDF library, with a
minimal host to try it and its tests. The library is the `lib/tetitulz.pdf` submodule and everything
here builds against its sources, so a change to the library is tried here by moving the submodule.

## The package

| Package | What it is | Target |
|---|---|---|
| [`Tetitulz.Pdf.WinForms.PdfViewer`](src/Tetitulz.Pdf.WinForms.PdfViewer/README.md) | An embeddable WinForms viewer control (`PdfViewerControl`): navigation, zoom, search and selection, forms, annotations, signatures, printing, RTL | `net462`, `net10.0-windows` |

It is held to the library's rules for a shipping assembly (`src/Directory.Build.props` imports the
library's), with its public API pinned under `tests/ApiSnapshots`.

`demo/Viewer` is a minimal WinForms host for the control.

## Building

Windows, with the .NET 10 SDK (`global.json` pins the feature band):

```bash
git clone --recurse-submodules https://github.com/tetitu/tetitulz.pdf-apps.git
cd tetitulz.pdf-apps
dotnet build tetitulz.pdf-apps.sln
dotnet test  tetitulz.pdf-apps.sln
```

A clone made without `--recurse-submodules` gets the library with `git submodule update --init`.
To build against a newer library, move the submodule and commit it:

```bash
git -C lib/tetitulz.pdf pull origin main
git add lib/tetitulz.pdf && git commit -m "Library: move to <commit>"
```

Package versions are the library's `Directory.Packages.props`, imported by this repository's.

## Releases

A `v*` tag that builds and tests green becomes a GitHub release with the package's `.nupkg`.

## License

Copyright (c) 2026 Tamir Mordo. Licensed under the [Apache License 2.0](LICENSE). The library the
control is built on keeps its own licences, listed in its `NOTICE` and `NOTICE.md`. See
[`NOTICE.md`](NOTICE.md).
