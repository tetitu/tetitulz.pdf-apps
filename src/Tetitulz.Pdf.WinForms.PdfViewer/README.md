# Tetitulz.Pdf.WinForms.PdfViewer

An embeddable **WinForms `UserControl`** PDF viewer (desktop-viewer style), built on the
pure-managed `Tetitulz.Pdf` library. Consumable from **.NET Framework 4.6.2** hosts and modern .NET.

**Targets:** `net462;net10.0-windows`
**Dependencies:** `Tetitulz.Pdf.Base`, `Tetitulz.Pdf.Core`, `Tetitulz.Pdf.Encryption` (all
netstandard2.0-capable; SkiaSharp + BouncyCastle ship net462-compatible assemblies).

---

## Features
- **A WinUI 3 look** — the chrome is drawn as a Windows 11 app's: WinUI's light and dark colours
  (`Theme`: `System`, the default, follows Windows' app mode; `Light`; `Dark`), the Windows accent
  (or the host's, `AccentColor`), Segoe UI Variable, icons from the font Windows installs (Segoe
  Fluent Icons, else Segoe MDL2 Assets — none shipped), a CommandBar toolbar of icon buttons
  (`ToolbarStyle`), a NavigationView pane, pages as cards with a shadow, a ProgressRing while a page
  renders, the find box as a flyout over the pages, the signatures' banner as an InfoBar, and optional
  floating page controls (`ShowFloatingControls`). See [The look](#the-look-winui-3).
- **Render** pages via SkiaSharp; continuous-scroll **and** single-page modes (toggle).
- **Zoom** — in/out, Fit Width, Fit Page, Actual Size, a `%` combo, and **Ctrl+mouse-wheel**. A fit
  stays a fit (`ZoomMode`): it refits when the window resizes, a document loads, or — in single-page
  layout — a page of another size is shown.
- **Two pages side by side** (`PageSpread`: `Facing`, or `FacingWithCover` with the first page
  alone), in the continuous and the single-page layout, from the page menu or the document's own
  `TwoColumn*` / `TwoPage*` layout under `PdfViewerFeatures.DocumentView`; the earlier page is on
  the right when the control is right to left, Next and Previous step a row, and a fit fits the row.
- **Rotate view** — Ctrl+Shift+Plus and Ctrl+Shift+Minus, or the page menu, turn every page a
  quarter (`ViewRotation`, `RotateClockwise`, `RotateCounterclockwise`) with the file unchanged:
  links, search hits, selection and form fields turn with the page; thumbnails and printing show the
  pages as the file has them; a document loads upright.
- **Sharp on scaled displays** — pages render at the monitor's dpi (`ScaleToDeviceDpi`, on by
  default), so 100% is the page's printed size and a 150% or 200% display gets a page rendered at
  144 or 192 dpi; on .NET 10 the scale follows the window to another monitor. A .NET Framework host
  gets the monitor's dpi only when its process is declared dpi-aware; otherwise Windows reports 96.
- **Text search** — find bar with on-page **highlight**, next/prev (F3 / Shift+F3), across all pages,
  searched in the background so typing never waits.
- **Reading** — arrows, Space and Shift+Space scroll; Home and End go to the first and last page;
  Left and Right and the wheel turn the page in single-page layout; **Back and Forward** (Alt+Left,
  Alt+Right and two buttons) return across link, bookmark, thumbnail and page-box jumps; the page box
  shows and accepts the document's page labels ("iii"); the zoom box offers Fit Width and Fit Page.
  `Features |= PdfViewerFeatures.DocumentView` opens each document as it asks: its opening page and
  zoom, its layout, its sidebar tab, its direction.
- **Text selection** — drag to select in reading order (Alt+drag for a rectangle), double-click a
  word, triple-click a line, Ctrl+A for the page, **Ctrl+C** or the right-click menu to copy. A host
  reads `SelectedText` and `Selection` (each page's text and glyph boxes, in user space, ready for a
  highlight annotation) and hears `SelectionChanged`. `CopyUsesLogicalOrder` (off by default)
  runs each copied line through the reading-order rule structured extraction applies, so a Latin
  phrase or a number inside Hebrew comes out the right way round; it costs nothing for text that is
  wholly one direction.
- **Clickable links** — URI links open in the browser (http/https/mailto allow-list); internal
  links jump to the destination's position (`/XYZ`, `/FitH`, `/FitBH`, `/FitR`), not just its page;
  the named actions NextPage, PrevPage, FirstPage and LastPage run; a link to another file or a
  program is reported and **never opened** (`LinkClicked` gives the host its `File`). Hovering shows
  the target in a tooltip (`ShowLinkToolTips`). `LinkClicked` lets the host override any of it.
- **Thumbnails sidebar** and **bookmarks/outline tree**: each item in the document's own style,
  colour and open state; a click lands at the item's position on its page; the section being read is
  selected as you scroll (`HighlightCurrentBookmark`).
- **Print / print-preview** at printer DPI, the dialog starting from the document's own copies,
  duplex and page range; fit or actual size (`PrintScaling`); all annotations, form fields only, or
  none (`PrintAnnotations`); filled-in values printed as shown.
- **Form filling** (opt-in: `Features |= PdfViewerFeatures.FormFilling`) — text, check box, radio
  and choice fields edited on the page; `SaveAs` appends the values to the original file as one
  revision (a signature over the original stays valid; an encrypted file stays encrypted under its
  own key). With `FormScripts` and a host-registered engine, the form's validation and
  calculation scripts run as values are committed.
- **Comments** (opt-in: `Features |= PdfViewerFeatures.Comments`) — a Comments tab by page, a click
  going to a comment and outlining it, its author, date and text on hover; `ShowComments = false`
  leaves comments off the page and keeps the form's fields.
- **Signatures** (opt-in: `Features |= PdfViewerFeatures.Signatures`) — the document's signatures
  verified on open (encrypted files under their password): a valid / unknown / invalid banner, a
  Signatures tab with each signer's details, signed fields described on hover. A click on a signed
  field selects its row and shows its validation status, as Acrobat does; `SignatureClicked` lets
  the host answer the click instead. Trust anchors and revocation options go in
  `SignatureVerification`; `SignaturesVerified` reports the results; `ShowSignatureBanner = false`
  hides the banner.
- **Signing** (opt-in: `Features |= PdfViewerFeatures.Signing`) — empty signature fields are marked
  on the page; a click raises `SignatureRequested`, whose handler supplies the certificate
  (`SignatureParameters`: a PFX, or an external signer such as a smart card), and `SignField` signs
  from code. Values typed into the form are written first, so the signature covers them; an earlier
  signature stays valid; an encrypted file is signed under its own key. The signed document is
  shown and unsaved until `SaveAs`.
- **Encrypted / password PDFs** — opened transparently after the host supplies a password.
- **Accessibility** — every glyph-only button and every box carries an accessible name from
  `Strings` (English and Hebrew), the page area names the page being read, thumbnails are reached and
  opened from the keyboard, and Windows' high-contrast setting draws the selection, the matches and the
  current thumbnail in the system's colours. On .NET 10 each status-line message is announced to a
  screen reader as it appears (`AnnounceStatusChanges`, on by default; not while the status bar is
  hidden); .NET Framework 4.6.2 has no such event.

---

## Quick start
```csharp
using Tetitulz.Pdf.WinForms.PdfViewer;

var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
Controls.Add(viewer);
viewer.LoadFile(@"C:\doc.pdf");      // or LoadStream(stream) / LoadBytes(byte[])
```

## The look: WinUI 3
The control is WinForms, drawn as a WinUI 3 (Fluent 2) app: the colours are WinUI's own (copied from
microsoft-ui-xaml's theme resources), the corners 4 px on controls and 8 px on cards, the text Segoe
UI Variable, the icons the system's icon font.
```csharp
var viewer = new PdfViewerControl
{
    Theme = PdfViewerTheme.Dark,                       // System (the default) follows Windows' app mode
    AccentColor = Color.FromArgb(0x10, 0x7C, 0x10),    // Color.Empty (the default) takes Windows' accent
    ToolbarStyle = PdfViewerToolbarStyle.IconsAndText, // Icons (the default): names in the tooltips
    ShowFloatingControls = true,                       // page and zoom over the pages, for a host without the toolbar
};
```
- **Windows' high-contrast setting overrides every theme**: the chrome is drawn in the system's colours.
- **The host's font wins.** When the control's font is not the default one (the host, or its form,
  set one), the chrome's text is in it; otherwise it is WinUI's.
- **No platform calls**, so a few parts stay Windows' own: the scroll bars and a combo box's drop-down
  list keep the system's look. On .NET 10, a host that calls `Application.SetColorMode` gets them in
  its mode, and `Theme = System` follows that mode too. Mica and acrylic belong to the host's window;
  the cards use WinUI's solid fallback colours. The floating controls appear and disappear rather than
  fade (a child window cannot be translucent without a platform call).
- **Without an icon font** (a system older than Windows 10), the buttons show their names and arrows.

## Encrypted PDFs (host supplies the password — no built-in dialog)
The empty (owner-only) password is tried automatically. For a user password, either preset it or
answer the event:
```csharp
// Option A: known password
viewer.Password = "secret";
viewer.LoadFile(@"C:\protected.pdf");

// Option B: prompt on demand
viewer.PasswordRequested += (s, e) =>
{
    // e.Attempt, e.V, e.R, e.KeyLengthBits describe the document
    e.Password = AskUserForPassword();   // your UI
    // e.Cancel = true;                  // to abort the load
};
viewer.LoadFile(@"C:\protected.pdf");    // throws PdfPasswordRequiredException if unanswered
```

## Filling forms
```csharp
viewer.Features |= PdfViewerFeatures.FormFilling;     // off by default
viewer.FieldValueChanging += (s, e) =>
{
    if (e.FieldName == "id" && e.NewValue.Length != 9) e.Cancel = true;   // the host validates
};
viewer.LoadFile(@"C:\form.pdf");
// … the user fills the form …
if (viewer.IsModified)
    viewer.SaveAs(@"C:\form-filled.pdf");             // the original + one appended revision
```

### Form scripts (opt-in twice)
With `PdfViewerFeatures.FormScripts` on as well, a committed value runs the field's Keystroke and
Validate scripts, then every calculation in the form's order (`PdfFieldScripts` in Base) — a script
can refuse the value, change it, or fill a total. The viewer references no script engine: the host
registers one, and with none registered the value is taken as typed and the status line says why.
```csharp
// Once at startup, with Tetitulz.Pdf.Plugins.JavaScript referenced:
PdfPluginHost.Instance.RegisterFromLoadedAssemblies();

viewer.Features |= PdfViewerFeatures.FormFilling | PdfViewerFeatures.FormScripts;
viewer.FormScriptBudget = TimeSpan.FromSeconds(5);    // shared by one commit's scripts
viewer.FormScriptMessage += (s, e) =>
{
    // A refusal (e.IsRejection) or an app.alert(); the status line shows it too.
    Console.WriteLine($"{e.FieldName}: {e.Message}");
};
```
A field's Format script runs too: a total holding `1234.5` shows `$1,234.50`, on the page and in the
saved file, while its value stays `1234.5`. Each character typed goes through the field's Keystroke
script, so a numeric field refuses a letter as it is typed; pasted text is checked when the field is left.

### Signatures: verifying and signing (opt-in)
`Signatures` verifies the document's signatures and shows the verdict; `Signing` lets the user sign
an empty signature field. The trust and the certificate are the host's.
```csharp
viewer.Features |= PdfViewerFeatures.Signatures | PdfViewerFeatures.Signing;
viewer.SignatureVerification = new SignatureVerificationOptions
{
    TrustAnchors = new[] { companyRoot },             // a BouncyCastle X509Certificate
};
viewer.SignatureRequested += (s, e) =>
{
    // Ask the user for the certificate's password, or leave e.Parameters null to sign nothing.
    e.Parameters = new SignatureParameters
    {
        CertificatePath = @"C:\me.pfx",
        CertificatePassword = AskForPassword(),
        Reason = "Approved",
    };
};
viewer.FieldSigned += (s, e) =>
{
    if (e.Error == null) viewer.SaveAs(@"C:\form-signed.pdf");   // the signature is kept only once saved
};
```

## Optional features, optional controls, overridable commands
Three levers, from coarse to fine:

- **`Features`** switches a capability off *everywhere*: its control is hidden, its shortcut passes to
  the host's form, and its public method does nothing. Default: `PdfViewerFeatures.Default`.
- **`Show*`** hides one control and keeps the capability, for a host that offers it from its own UI.
- **`CommandExecuting`** is raised before every button, shortcut, Ctrl+wheel, zoom-box choice and
  sidebar click; set `Handled` to replace the built-in action. The host's own calls (`Print()`,
  `GoToPage(…)`) do not raise it, so a handler may make them.

```csharp
// No opening files from inside the viewer, no text selection.
viewer.Features &= ~(PdfViewerFeatures.OpenFile | PdfViewerFeatures.TextSelection);

// The host has its own zoom slider and status line.
viewer.ShowZoomControls = false;
viewer.ShowStatusBar = false;

// Print through the host's own dialog.
viewer.CommandExecuting += (s, e) =>
{
    if (e.Command == PdfViewerCommand.Print)
    {
        e.Handled = true;
        viewer.Print();   // or the host's own print flow
    }
};
```

## Selected public API
| Member | Purpose |
|---|---|
| `LoadFile/LoadBytes/LoadStream` | Open a document (path / bytes / stream) |
| `PageCount`, `CurrentPage`, `GoToPage(index)`, `GoToPage(index, top)`, `NextPage`, `PreviousPage` | Navigation |
| `Zoom`, `ZoomMode`, `ZoomIn/ZoomOut`, `FitWidth`, `FitPage`, `ActualSize` | Zoom |
| `LayoutMode` (`Continuous`/`SinglePage`), `PageSpread` (`None`/`Facing`/`FacingWithCover`) | Page layout |
| `ViewRotation`, `RotateClockwise`, `RotateCounterclockwise` | Turn the view (the file is unchanged) |
| `Find(text)` | Open the find bar / run a search |
| `GoBack`, `GoForward`, `CanGoBack`, `CanGoForward`, `ShowHistoryButtons` | Navigation history |
| `SelectedText`, `Selection`, `SelectionChanged`, `SelectPageText`, `CopySelection`, `ClearSelection`, `TextSelectionMode` | Text selection |
| `RenderPagesInBackground`, `ScaleToDeviceDpi`, `ShowContextMenu` | Rendering, the page menu |
| `AnnounceStatusChanges` | Status messages spoken by a screen reader (.NET 10) |
| `Print()`, `PrintPreview()` | Printing |
| `Password`, `PasswordRequested` | Encrypted-document password input |
| `CopyUsesLogicalOrder` | Copy mixed-direction lines in reading order |
| `Features` | Switch capabilities off (open, search, selection, links, thumbnails, bookmarks, print) |
| `ShowToolbar`, `ShowSidebar`, `ShowStatusBar`, `ShowOpenButton`, `ShowNavigationControls`, `ShowZoomControls`, `ShowLayoutButton`, `ShowSidebarButton`, `ShowFindButton`, `ShowPrintButton` | Hide one control, keep its command |
| `SetFieldValue`, `SaveAs`, `IsModified`, `ChangedFieldValues`, `HighlightFormFields`, `ShowSaveButton` | Form filling |
| `FormScriptBudget` | Form scripts |
| `DocumentLoaded`, `PageChanged`, `LinkClicked`, `CommandExecuting`, `FieldValueChanging`, `FieldValueChanged`, `FormScriptMessage` | Events |

---

## .NET Framework 4.6.2 notes
- The control consumes the `netstandard2.0` builds of `Tetitulz.Pdf.*`. SkiaSharp's native
  `libSkiaSharp.dll` and BouncyCastle deploy automatically for **SDK-style (PackageReference)**
  hosts; legacy `packages.config` hosts may need to reference the SkiaSharp NuGet directly so the
  native `win-x64/x86` binaries copy to output.
- Overlay alignment (links / search / selection) handles page `/Rotate` 0/90/180/270.
- Pages and thumbnails render on a background thread through one held render session, under a
  bounded bitmap cache (16 pages, 128 MB); while a page renders at a new zoom its previous bitmap is
  shown scaled. `RenderPagesInBackground = false` renders each page in the paint that shows it — for
  a host that captures the control with `DrawToBitmap` straight after loading. The find box searches
  in the background too, its count growing as it goes.
- Selection spans pages: a drag from one page into the next selects on both.

## Licence and provenance

Apache License 2.0. Copyright (c) 2026 Tamir Mordo. Credit it as "Uses Tetitulz.Pdf by Tamir Mordo" (the package's NOTICE file). See the repository for the full guide, the open-gap register and the third-party notices.
