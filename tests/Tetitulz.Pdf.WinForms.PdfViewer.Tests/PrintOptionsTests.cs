// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Threading;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Rendering;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Printing's options (GAPS.md §10a step 4): the document's <c>/ViewerPreferences</c> starting the
/// dialog, fit or actual size, which annotations print, the pages a choice prints, and the filled
/// form on paper.
/// </summary>
public sealed class PrintOptionsTests
{
    [Fact]
    public void TheDocumentsPreferences_StartTheDialog()
    {
        var prefs = PdfDocumentSource.FromBytes(TestPdf.RedInchPages(
            "/PrintScaling /None /NumCopies 3 /Duplex /DuplexFlipLongEdge /PrintPageRange [2 7 0 R]")).PrintPreferences;
        Assert.True(prefs.ActualSize);
        Assert.Equal(3, prefs.Copies);
        Assert.Equal("DuplexFlipLongEdge", prefs.Duplex);
        Assert.Equal(new[] { (1, 2) }, prefs.Ranges);                    // through the indirect element

        var ps = new PrinterSettings();
        PdfViewerControl.ApplyDocumentPrintPreferences(ps, prefs, 3);
        Assert.Equal(3, ps.Copies);
        Assert.Equal(Duplex.Vertical, ps.Duplex);
        Assert.Equal(PrintRange.SomePages, ps.PrintRange);
        Assert.Equal((2, 3), (ps.FromPage, ps.ToPage));
        Assert.Equal((1, 3), (ps.MinimumPage, ps.MaximumPage));

        // The control: a document that asks nothing leaves the dialog as it was.
        var plain = PdfDocumentSource.FromBytes(TestPdf.RedInchPages(null)).PrintPreferences;
        var ps2 = new PrinterSettings();
        short copies = ps2.Copies;
        PdfViewerControl.ApplyDocumentPrintPreferences(ps2, plain, 3);
        Assert.Equal(copies, ps2.Copies);
        Assert.Equal(PrintRange.AllPages, ps2.PrintRange);
        Assert.False(plain.ActualSize);
    }

    [Theory]
    [InlineData("/PrintPageRange [0 2]")]           // pages count from 1
    [InlineData("/PrintPageRange [2 9]")]           // past the last page
    [InlineData("/PrintPageRange [3 2]")]           // backwards
    [InlineData("/PrintPageRange [1 2 3]")]         // not pairs
    [InlineData("/PrintPageRange [1.5 2]")]         // not integers
    public void ARangeTheDocumentCannotHave_IsIgnored(string entry)
        => Assert.Empty(PdfDocumentSource.FromBytes(TestPdf.RedInchPages(entry)).PrintPreferences.Ranges);

    [Fact]
    public void SeveralRanges_AreReadButLeftToTheUser()
    {
        var prefs = PdfDocumentSource.FromBytes(TestPdf.RedInchPages("/PrintPageRange [1 1 3 3]")).PrintPreferences;
        Assert.Equal(new[] { (0, 0), (2, 2) }, prefs.Ranges);

        var ps = new PrinterSettings();
        PdfViewerControl.ApplyDocumentPrintPreferences(ps, prefs, 3);
        Assert.Equal(PrintRange.AllPages, ps.PrintRange);               // a dialog holds one range
    }

    [Theory]
    [InlineData("/NumCopies 0", null)]
    [InlineData("/NumCopies 99999", 32767)]
    [InlineData("/Duplex /Sideways", null)]
    public void CopiesAndDuplex_TakeOnlyWhatAPrinterCan(string entry, int? copies)
    {
        var prefs = PdfDocumentSource.FromBytes(TestPdf.RedInchPages(entry)).PrintPreferences;
        Assert.Equal(copies, prefs.Copies);
        Assert.Null(prefs.Duplex);
    }

    [Fact]
    public void TheCurrentPage_IsThatPageAlone()
    {
        var ps = new PrinterSettings { PrintRange = PrintRange.CurrentPage };
        Assert.Equal((1, 1), PdfViewerControl.PagesToPrint(ps, 1, 3));

        ps.PrintRange = PrintRange.SomePages;
        ps.FromPage = 2;
        ps.ToPage = 9;
        Assert.Equal((1, 2), PdfViewerControl.PagesToPrint(ps, 0, 3));   // clamped to the document

        ps.PrintRange = PrintRange.AllPages;
        Assert.Equal((0, 2), PdfViewerControl.PagesToPrint(ps, 1, 3));
    }

    /// <summary>
    /// A one-inch page on a 1000-unit area: fitted it fills the area; at actual size it is 100
    /// hundredths of an inch, centred — whatever dpi it was rendered at.
    /// </summary>
    [Theory]
    [InlineData(72)]
    [InlineData(300)]
    public void ActualSize_IsThePagesOwnSize_AndFitFillsTheArea(int dpi)
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.RedInchPages(null));

        using Bitmap fit = PrintOnto(src, dpi, actualSize: false);
        Assert.True(IsRed(fit.GetPixel(20, 500)));

        using Bitmap actual = PrintOnto(src, dpi, actualSize: true);
        Assert.True(IsRed(actual.GetPixel(500, 500)));
        Assert.True(IsRed(actual.GetPixel(452, 452)));
        Assert.False(IsRed(actual.GetPixel(440, 500)));                  // just outside the inch
        Assert.False(IsRed(actual.GetPixel(20, 500)));
    }

    [Fact]
    public void TheScalingDefault_FollowsTheDocument()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.RedInchPages("/PrintScaling /None"));
            Assert.Equal(PdfPrintScaling.Document, viewer.PrintScaling);
            Assert.True(viewer.PrintsAtActualSize);
            viewer.PrintScaling = PdfPrintScaling.FitToPage;
            Assert.False(viewer.PrintsAtActualSize);

            viewer.LoadBytes(TestPdf.RedInchPages(null));
            viewer.PrintScaling = PdfPrintScaling.Document;
            Assert.False(viewer.PrintsAtActualSize);
            viewer.PrintScaling = PdfPrintScaling.ActualSize;
            Assert.True(viewer.PrintsAtActualSize);

            Assert.Throws<ArgumentOutOfRangeException>(() => viewer.PrintScaling = (PdfPrintScaling)7);
            Assert.Throws<ArgumentOutOfRangeException>(() => viewer.PrintAnnotations = (AnnotationRendering)7);
            Assert.Equal(AnnotationRendering.All, viewer.PrintAnnotations);

            // A job renders under the print intent and the chosen annotations.
            Assert.Equal((RenderIntent.Print, AnnotationRendering.All),
                (viewer.PrintRenderOptions.Intent, viewer.PrintRenderOptions.Annotations));
            viewer.PrintAnnotations = AnnotationRendering.FormFieldsOnly;
            Assert.Equal(AnnotationRendering.FormFieldsOnly, viewer.PrintRenderOptions.Annotations);
        });
    }

    [Theory]
    [InlineData(AnnotationRendering.All, true)]
    [InlineData(AnnotationRendering.FormFieldsOnly, false)]
    [InlineData(AnnotationRendering.None, false)]
    public void PrintAnnotations_ChoosesWhichAnnotationsReachThePaper(AnnotationRendering scope, bool printed)
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.RedAnnotationPage(4));   // a square flagged Print
        using var paper = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(paper))
        {
            g.Clear(Color.White);
            Assert.Null(PdfViewerControl.PrintPage(src, 0, 72, g, new Rectangle(0, 0, 100, 100),
                new PdfRenderOptions { Intent = RenderIntent.Print, Annotations = scope }));
        }
        Assert.Equal(printed, IsRed(paper.GetPixel(50, 50)));
    }

    /// <summary>What is printed is what is shown: a value filled in prints, before any save.</summary>
    [Fact]
    public void AFilledInValue_IsPrinted()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(TestPdf.ScriptedForm(null, null, null));

            int before = InkInField(viewer);
            Assert.True(viewer.SetFieldValue("a", "WWWWWWWW"));
            int after = InkInField(viewer);

            Assert.True(after > before + 50, $"the value did not print: {before} → {after} dark pixels");
        });

        // The field "a" sits at [20 300 220 330] on a 400-point page; its box is printed at 72 dpi
        // onto a 400 × 400 sheet and its dark pixels counted.
        static int InkInField(PdfViewerControl viewer)
        {
            using var paper = new Bitmap(400, 400);
            using (var g = Graphics.FromImage(paper))
            {
                g.Clear(Color.White);
                Assert.Null(PdfViewerControl.PrintPage(viewer.DocumentSource!, 0, 72, g, new Rectangle(0, 0, 400, 400)));
            }
            int dark = 0;
            for (int x = 25; x < 215; x++)
                for (int y = 72; y < 98; y++)          // 400 − 328 … 400 − 302
                    if (paper.GetPixel(x, y).GetBrightness() < 0.4f) dark++;
            return dark;
        }
    }

    private static Bitmap PrintOnto(PdfDocumentSource src, int dpi, bool actualSize)
    {
        var paper = new Bitmap(1000, 1000);
        using (var g = Graphics.FromImage(paper))
        {
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Pixel;
            Assert.Null(PdfViewerControl.PrintPage(src, 0, dpi, g, new Rectangle(0, 0, 1000, 1000),
                PdfDocumentSource.PrintOptions, actualSize));
        }
        return paper;
    }

    private static bool IsRed(Color c) => c.R > 200 && c.G < 80 && c.B < 80;

    private static void RunSta(Action action) => StaThread.Run(action);
}
