// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Drawing;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The page view, the thumbnails and printing render through one guard, and printing renders as
/// print. Only the page view asked what a render would cost; a thumbnail rendered a page the view
/// refused at the renderer's 400 MB ceiling, and a print job had no guard and no catch (Wave 22,
/// SmallPlugins+Viewer-1:1). Printing used the screen intent, so annotations followed NoView
/// rather than their Print flag (1:8).
/// </summary>
public sealed class GuardedRenderTests
{
    /// <summary>A page whose bitmap would pass the budget is refused before it is rendered; the control renders.</summary>
    [Fact]
    public void RenderWithin_RefusesAPageOverTheBudget_AndRendersOneUnder()
    {
        var huge = PdfDocumentSource.FromBytes(TestPdf.HugeUserUnitPage());
        Assert.Null(huge.RenderWithin(0, 18, PdfViewerControl.MaxSingleBitmapBytes, null, out string? why));
        Assert.Contains("too large", why, StringComparison.Ordinal);

        var plain = PdfDocumentSource.FromBytes(TestPdf.OnePage());
        using var bmp = plain.RenderWithin(0, 72, PdfViewerControl.MaxSingleBitmapBytes, null, out string? none);
        Assert.NotNull(bmp);
        Assert.Equal(200, bmp!.Width);
        Assert.Null(none);
    }

    /// <summary>
    /// A thumbnail's dpi fills its box from the page's own size: a US-Letter page in a 140-pixel
    /// box at 17 dpi, a 60 000-point page at 1, never the fixed 18 that made the latter 400 MB.
    /// </summary>
    [Theory]
    [InlineData(140, 181, 612, 792, 17)]
    [InlineData(140, 140, 60000, 60000, 1)]
    [InlineData(140, 140, 10, 10, 1008)]
    [InlineData(0, 140, 612, 792, 1)]
    public void DpiToFill_FillsTheBoxFromThePagesSize(int w, int h, double pw, double ph, int expected)
        => Assert.Equal(expected, ZoomMath.DpiToFill(w, h, pw, ph));

    /// <summary>
    /// A printed page follows the Print flag: an annotation without it is on screen and not on
    /// paper, one with Print | NoView the reverse, one with Print on both.
    /// </summary>
    [Theory]
    [InlineData(null, true, false)]
    [InlineData(36, false, true)]
    [InlineData(4, true, true)]
    public void APrintedPage_FollowsTheAnnotationsPrintFlag(int? flags, bool onScreen, bool onPaper)
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.RedAnnotationPage(flags));

        using (var screen = src.RenderBitmap(0, 72))
            Assert.Equal(onScreen, IsRed(screen.GetPixel(50, 50)));

        using var paper = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(paper))
        {
            g.Clear(Color.White);
            Assert.Null(PdfViewerControl.PrintPage(src, 0, 72, g, new Rectangle(0, 0, 100, 100)));
        }
        Assert.Equal(onPaper, IsRed(paper.GetPixel(50, 50)));
    }

    /// <summary>
    /// A page too large to print, and a page that cannot be rendered at all, each come back as
    /// this page's failure with a placeholder drawn — never an exception out of the print handler.
    /// </summary>
    [Fact]
    public void APageThatCannotPrint_IsReportedAndTheJobGoesOn()
    {
        using var paper = new Bitmap(100, 100);
        using var g = Graphics.FromImage(paper);

        string? huge = PdfViewerControl.PrintPage(PdfDocumentSource.FromBytes(TestPdf.HugeUserUnitPage()), 0, 600, g, new Rectangle(0, 0, 100, 100));
        Assert.Contains("too large", huge, StringComparison.Ordinal);

        string? missing = PdfViewerControl.PrintPage(PdfDocumentSource.FromBytes(TestPdf.OnePage()), 5, 600, g, new Rectangle(0, 0, 100, 100));
        Assert.NotNull(missing);
        Assert.NotEqual("", missing);
    }

    private static bool IsRed(Color c) => c.R > 200 && c.G < 80 && c.B < 80;
}
