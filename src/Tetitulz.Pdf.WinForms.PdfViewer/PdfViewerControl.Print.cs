// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Rendering;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    public sealed partial class PdfViewerControl
    {
        // Bounds on the printer's reported dpi. The floor is one device pixel per PDF point
        // (a printer claiming less would render illegibly); the ceiling keeps a 2400-dpi
        // photo printer from rasterising a whole page at that resolution.
        private const int MinPrintDpi = 72;
        private const int MaxPrintDpi = 600;

        private PdfPrintScaling _printScaling = PdfPrintScaling.Document;
        private AnnotationRendering _printAnnotations = AnnotationRendering.All;

        /// <summary>
        /// How a page is sized on the paper. <see cref="PdfPrintScaling.Document"/> (the default)
        /// follows the document's <c>/ViewerPreferences /PrintScaling</c>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a member of the enum.</exception>
        [Category("PDF"), DefaultValue(PdfPrintScaling.Document), Description("How a printed page is sized on the paper.")]
        public PdfPrintScaling PrintScaling
        {
            get => _printScaling;
            set
            {
                if (value != PdfPrintScaling.Document && value != PdfPrintScaling.FitToPage && value != PdfPrintScaling.ActualSize)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Not a print scaling.");
                _printScaling = value;
            }
        }

        /// <summary>
        /// Which annotations are printed — all those flagged to print (the default), the form's
        /// fields alone, or none: a print dialog's "comments and forms".
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a member of the enum.</exception>
        [Category("PDF"), DefaultValue(AnnotationRendering.All), Description("Which annotations are printed.")]
        public AnnotationRendering PrintAnnotations
        {
            get => _printAnnotations;
            set
            {
                // The render options refuse a value outside the enum; asked here, so the refusal is
                // the caller's assignment and not the next print job.
                _ = new PdfRenderOptions { Annotations = value };
                _printAnnotations = value;
            }
        }

        /// <summary>Shows a print-preview dialog for the document. Nothing happens when the document's permissions withhold printing (<see cref="HonourPermissions"/>).</summary>
        public void PrintPreview()
        {
            if (_doc == null || !Has(PdfViewerFeatures.Print) || !Permits(Tetitulz.Pdf.Base.Encryption.PdfPermissions.Print)) return;
            using (var pd = BuildPrintDocument())
            using (var dlg = new PrintPreviewDialog { Document = pd, Width = 900, Height = 700 })
                dlg.ShowDialog(this);
        }

        /// <summary>
        /// Shows the printer-selection dialog and prints the document. The dialog opens with what the
        /// document's <c>/ViewerPreferences</c> asks for: copies, duplex, and a page range. Nothing
        /// happens when the document's permissions withhold printing (<see cref="HonourPermissions"/>).
        /// </summary>
        public void Print()
        {
            if (_doc == null || !Has(PdfViewerFeatures.Print) || !Permits(Tetitulz.Pdf.Base.Encryption.PdfPermissions.Print)) return;
            using (var pd = BuildPrintDocument())
            using (var dlg = new PrintDialog { Document = pd, AllowSomePages = true, AllowCurrentPage = true, UseEXDialog = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                pd.Print();
            }
        }

        internal PrintDocument BuildPrintDocument()
        {
            int printPage = 0;
            int lastPage = _pageCount - 1;

            var pd = new PrintDocument { DocumentName = _documentTitle ?? _strings.PrintJobName };
            string failedFormat = _strings.PagePrintFailedFormat, notPrintedFormat = _strings.PageNotPrintedFormat;
            ApplyDocumentPrintPreferences(pd.PrinterSettings, _doc!.PrintPreferences, _pageCount);

            pd.BeginPrint += (s, e) => (printPage, lastPage) = PagesToPrint(pd.PrinterSettings, _currentPage, _pageCount);

            // Read when the job is built, so a setting changed during a preview applies to the next one.
            PdfRenderOptions options = PrintRenderOptions;
            bool actualSize = PrintsAtActualSize;

            pd.PrintPage += (s, e) =>
            {
                if (e.Graphics == null || _doc == null) return;

                string? failure = PrintPage(_doc, printPage, SaturatingCast.ToInt(e.Graphics.DpiX), e.Graphics, e.MarginBounds,
                    options, actualSize, failedFormat);
                if (failure != null)
                    _statusLabel.Text = PdfViewerStrings.Format(notPrintedFormat, printPage + 1, failure);

                printPage++;
                e.HasMorePages = printPage <= lastPage;
            };

            return pd;
        }

        /// <summary>Whether this job prints at the page's own size: asked for, or the document's <c>/PrintScaling /None</c>.</summary>
        internal bool PrintsAtActualSize
            => _printScaling == PdfPrintScaling.ActualSize
               || (_printScaling == PdfPrintScaling.Document && _doc?.PrintPreferences.ActualSize == true);

        /// <summary>What a print job renders its pages under: the print intent and <see cref="PrintAnnotations"/>.</summary>
        internal PdfRenderOptions PrintRenderOptions
            => new PdfRenderOptions { Intent = RenderIntent.Print, Annotations = _printAnnotations };

        /// <summary>The document shown — the filled form included — as printing reads it.</summary>
        internal PdfDocumentSource? DocumentSource => _doc;

        /// <summary>
        /// The first and last page index the dialog's choice prints. "Current page" is that page
        /// alone — it printed from the current page to the end of the document.
        /// </summary>
        internal static (int First, int Last) PagesToPrint(PrinterSettings ps, int currentPage, int pageCount)
        {
            int last = pageCount - 1;
            switch (ps.PrintRange)
            {
                case PrintRange.CurrentPage:
                    int current = Clamp(currentPage, 0, last);
                    return (current, current);
                case PrintRange.SomePages:
                    return (Clamp(ps.FromPage - 1, 0, last), Clamp(ps.ToPage - 1, 0, last));
                default:
                    return (0, last);
            }
        }

        /// <summary>
        /// Starts the print dialog from what the document asks (§12.2 Table 150): <c>/NumCopies</c>,
        /// <c>/Duplex</c>, and a <c>/PrintPageRange</c> of one sub-range — a dialog holds one
        /// range, and several are left to the user rather than widened into pages not asked for.
        /// </summary>
        internal static void ApplyDocumentPrintPreferences(PrinterSettings ps, DocumentPrintPreferences prefs, int pageCount)
        {
            ps.MinimumPage = 1;
            ps.MaximumPage = Math.Max(1, pageCount);
            if (prefs.Copies is int copies) ps.Copies = (short)copies;
            switch (prefs.Duplex)
            {
                case "Simplex": ps.Duplex = Duplex.Simplex; break;
                case "DuplexFlipLongEdge": ps.Duplex = Duplex.Vertical; break;
                case "DuplexFlipShortEdge": ps.Duplex = Duplex.Horizontal; break;
            }
            if (prefs.Ranges.Count == 1)
            {
                ps.PrintRange = PrintRange.SomePages;
                ps.FromPage = prefs.Ranges[0].From + 1;
                ps.ToPage = prefs.Ranges[0].To + 1;
            }
        }

        /// <summary>
        /// Draws one page onto a printer's graphics, centred in <paramref name="area"/> — fitted to
        /// it, or at its own size when <paramref name="actualSize"/>; a page that cannot be rendered
        /// is drawn as a placeholder saying so, and the reason is returned. Null when the page was printed.
        /// </summary>
        /// <remarks>
        /// <para>The page renders under the print intent (<paramref name="options"/>, by default
        /// <see cref="PdfDocumentSource.PrintOptions"/>): under the screen intent annotations would
        /// print without their Print flag, print-only ones would be left off, and <c>/ViewArea</c>
        /// would stand for <c>/PrintArea</c>.</para>
        /// <para>It renders through the page view's guard, at a resolution lowered until the bitmap
        /// fits <see cref="MaxSingleBitmapBytes"/> — a US-Letter page at 600 dpi is 134 MB — and an
        /// exception is this page's alone, rather than escaping the handler and aborting the whole
        /// job or the preview dialog's message loop.</para>
        /// <para>At actual size the page measures what the bitmap measures at the dpi it was
        /// rendered at — the print box, in the printer's hundredths of an inch.</para>
        /// </remarks>
        internal static string? PrintPage(PdfDocumentSource doc, int pageIndex, int printerDpi, Graphics g, Rectangle area,
            PdfRenderOptions? options = null, bool actualSize = false, string? failedFormat = null)
        {
            options ??= PdfDocumentSource.PrintOptions;
            failedFormat ??= PdfViewerStrings.English().PagePrintFailedFormat;
            string? failure;
            try
            {
                int dpi = Clamp(printerDpi, MinPrintDpi, MaxPrintDpi);
                long bytes = doc.EstimatedBitmapBytes(pageIndex, dpi, options);
                if (bytes > MaxSingleBitmapBytes)
                    dpi = Math.Max(1, SaturatingCast.ToInt(dpi * Math.Sqrt((double)MaxSingleBitmapBytes / bytes)));

                using (var bmp = doc.RenderWithin(pageIndex, dpi, MaxSingleBitmapBytes, options, out failure))
                {
                    if (bmp != null)
                    {
                        float scale = actualSize
                            ? 100f / dpi                                   // pixels → hundredths of an inch
                            : Math.Min((float)area.Width / bmp.Width, (float)area.Height / bmp.Height);
                        int w = SaturatingCast.ToInt(Math.Round(bmp.Width * scale));
                        int h = SaturatingCast.ToInt(Math.Round(bmp.Height * scale));
                        var dest = new Rectangle(area.Left + (area.Width - w) / 2,
                                                 area.Top + (area.Height - h) / 2, w, h);
                        g.DrawImage(bmp, dest);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            using (var font = new Font(FontFamily.GenericSansSerif, 10f))
            using (var ink = new SolidBrush(ViewerPalette.PrintedText))
                g.DrawString(PdfViewerStrings.Format(failedFormat, pageIndex + 1, failure ?? ""), font, ink, area);
            return failure ?? "the page could not be rendered.";
        }
    }
}
