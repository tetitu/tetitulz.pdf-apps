# Tetitulz.Pdf Windows packages

Windows packages built on [Tetitulz.Pdf](https://github.com/tetitu/tetitulz.pdf), the pure-managed
.NET PDF library: an embeddable WinForms PDF viewer control, `PdfViewerControl`, with a minimal host
to try it, and the machinery of a Windows virtual PDF printer, with their tests. The library is the
`lib/tetitulz.pdf` submodule and everything here builds against its sources, so a change to the
library is tried here by moving the submodule.

## The packages

| Package | What it is | Target |
|---|---|---|
| [`Tetitulz.Pdf.WinForms.PdfViewer`](src/Tetitulz.Pdf.WinForms.PdfViewer/README.md) | An embeddable WinForms viewer control (`PdfViewerControl`): navigation, zoom, search and selection, forms, annotations, signatures, printing, RTL | `net462`, `net10.0-windows` |
| [`Tetitulz.Pdf.Printing`](src/Tetitulz.Pdf.Printing/README.md) | A Windows virtual PDF printer's machinery: a PostScript printer on a named-pipe port, its jobs converted to PDF by the library's PostScript plugin, the printer installed and removed | `net10.0-windows` |

Each is held to the library's rules for a shipping assembly (`src/Directory.Build.props` imports the
library's), with its public API pinned under `tests/ApiSnapshots`.

## The demos

Each is a small program that does one job with the library, and each runs with `dotnet run --project
demo/<name> -- [output-directory]`, writing its files there (the current directory by default). Every
one but `Viewer` makes its own input, so none needs a file to start from.

| Demo | What it shows |
|---|---|
| [`QuickStart`](demo/QuickStart/Program.cs) | Start here: build a PDF in code (`DocumentBuilder`), render a page to PNG, extract its text, merge two documents (`PdfMerger`) |
| [`FormFill`](demo/FormFill/Program.cs) | Add AcroForm fields (`PdfFormFieldBuilder`), list them, fill them with a Hebrew value drawn in a bundled font (`PdfOperations.FillForm`), then flatten |
| [`SignAndVerify`](demo/SignAndVerify/Program.cs) | Sign at PAdES B-B (`PdfSigner`), or B-T with `--tsa <url>`; verify without and with a trust anchor, then watch an edit made after signing be caught |
| [`PdfGallery`](demo/PdfGallery/Program.cs) | A folder of images (`--images <folder>`, or six it draws) into a PDF: one per A4 page, fitted and numbered (`PdfOperations.CreatePdfFromImages`, `AddHeaderFooter`), one per page at the image's own size, and a contact sheet of captioned thumbnails |
| [`HtmlInvoice`](demo/HtmlInvoice/Program.cs) | HTML to PDF: an English invoice and a long right-to-left Hebrew one, by the managed engine (no browser) and by Chromium; `--install` first downloads the Chromium build Playwright drives |
| [`Viewer`](demo/Viewer/MainForm.cs) | A minimal WinForms host for `PdfViewerControl` |

CI runs `QuickStart`, `FormFill`, `SignAndVerify` and `PdfGallery` on every build, so they keep compiling and working
as the library moves.

[`samples/edge-to-edge-driver`](samples/edge-to-edge-driver/README.md) is a template printer driver
package for a product built on `Printing`, whose pages print to the paper's edges.

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

A `v*` tag that builds and tests green becomes a GitHub release with every package's `.nupkg`, all
at the tag's version.

## License

Copyright (c) 2026 Tamir Mordo. Licensed under the [Apache License 2.0](LICENSE). The library the
packages are built on keeps its own licences, listed in its `NOTICE` and `NOTICE.md`. See
[`NOTICE.md`](NOTICE.md).
