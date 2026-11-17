// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Tetitulz.Pdf.Base.Operations.Extraction;
using Xunit;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Regressions for the viewer defects found by the 2026-08 assembly scan: the page box, the zoom
/// anchor, the render cache's budget and lifetime, the selection band, and the thumbnail strip.
/// </summary>
public sealed class ViewerGapClosureTests
{
    private static void RunSta(Action action) => StaThread.Run(action);

    // ================================================================ the page box

    /// <summary>
    /// A <c>/CropBox</c> larger than its <c>/MediaBox</c> lays out at the size the renderer
    /// actually produces.
    /// </summary>
    /// <remarks>
    /// <c>ReadGeometry</c> read <c>page.CropBox</c> while every bitmap came from
    /// <c>ResolveRenderBox</c>, which clips to the <c>/MediaBox</c> per §14.11.2. The page was laid
    /// out 1584 pt tall and rendered 792 pt tall, so the image was stretched to double height and
    /// every overlay landed at half its proper distance down the page.
    /// </remarks>
    [Fact]
    public void CropBoxExceedingMediaBox_IsClippedToTheMediaBox()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.CropBoxLargerThanMediaBox());
        var geo = src.Geometry(0);

        Assert.Equal(612f, geo.WidthPts, 1);
        Assert.Equal(792f, geo.HeightPts, 1);
    }

    /// <summary>
    /// The layout rectangle and the rendered bitmap describe the same rectangle of the page.
    /// </summary>
    /// <remarks>
    /// This is the assertion the divergence could not survive, and it is deliberately a
    /// <b>parity</b> check rather than a restatement of the expected numbers: it fails for any
    /// future box rule the two sides come to disagree on, not just for this one.
    /// </remarks>
    [Theory]
    [InlineData(96)]
    [InlineData(150)]
    public void LaidOutSizeMatchesTheRenderedBitmap(int dpi)
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.CropBoxLargerThanMediaBox());
        var geo = src.Geometry(0);
        float scale = PdfPixelMapper.ScaleForDpi(dpi);

        using var bmp = src.RenderBitmap(0, dpi);

        Assert.Equal((int)Math.Round(geo.DisplayWidthPts * scale), bmp.Width);
        Assert.Equal((int)Math.Round(geo.DisplayHeightPts * scale), bmp.Height);
    }

    [Fact]
    public void PlainPageGeometryIsUnchanged()
    {
        // The control: routing through the renderer must not move an ordinary page.
        var geo = PdfDocumentSource.FromBytes(TestPdf.OnePage()).Geometry(0);

        Assert.Equal(200f, geo.WidthPts, 1);
        Assert.Equal(200f, geo.HeightPts, 1);
        Assert.Equal(0f, geo.CropX0, 1);
        Assert.Equal(0f, geo.CropY0, 1);
    }

    // ================================================================ zoom anchoring

    /// <summary>
    /// Zoom keeps the reader where they were, expressed as the fraction of the canvas at the
    /// viewport centre.
    /// </summary>
    /// <remarks>
    /// <c>SetZoom</c> hard-coded that fraction to <c>0.5</c>, so in the Continuous layout — where
    /// the canvas is the whole document — every zoom step landed on the middle page of the file.
    /// </remarks>
    [Fact]
    public void CenterFraction_ReadsTheViewportCentreNotTheCanvasMiddle()
    {
        // Viewport 600 px tall at the very top of a 20 000 px canvas: the centre is 300/20000.
        Assert.Equal(0.015, ZoomMath.CenterFraction(0, 600, 20000), 6);
        // Scrolled to the middle, it IS 0.5 — the old constant was right for exactly this case.
        Assert.Equal(0.5, ZoomMath.CenterFraction(9700, 600, 20000), 6);
    }

    [Fact]
    public void ScrollForCenterFraction_RoundTripsAndScalesWithTheCanvas()
    {
        const int viewport = 600, canvas = 20000, offset = 1400;
        double f = ZoomMath.CenterFraction(offset, viewport, canvas);

        Assert.Equal(offset, ZoomMath.ScrollForCenterFraction(f, viewport, canvas));
        // Zooming to 2x doubles the canvas; the same fraction is twice as far down it.
        Assert.Equal((int)Math.Round(f * 2 * canvas - viewport / 2.0),
                     ZoomMath.ScrollForCenterFraction(f, viewport, 2 * canvas));
    }

    [Fact]
    public void ScrollForCenterFraction_StaysInsideTheScrollableRange()
    {
        Assert.Equal(0, ZoomMath.ScrollForCenterFraction(0.0, 600, 20000));
        Assert.Equal(19400, ZoomMath.ScrollForCenterFraction(1.0, 600, 20000));
        Assert.Equal(0, ZoomMath.ScrollForCenterFraction(0.5, 800, 400));   // canvas fits the viewport
    }

    [Fact]
    public void AnchorFraction_IsTheCentreFractionAtTheCentreAndMovesWithTheAnchor()
    {
        Assert.Equal(ZoomMath.CenterFraction(1400, 600, 20000), ZoomMath.AnchorFraction(1400, 300, 20000), 9);
        Assert.Equal((1400 + 50) / 20000.0, ZoomMath.AnchorFraction(1400, 50, 20000), 9);

        // Round trip at a non-centre anchor, then the same fraction on a doubled canvas.
        double f = ZoomMath.AnchorFraction(1400, 50, 20000);
        Assert.Equal(1400, ZoomMath.ScrollForAnchorFraction(f, 50, 600, 20000));
        Assert.Equal((int)Math.Round(f * 40000 - 50), ZoomMath.ScrollForAnchorFraction(f, 50, 600, 40000));
    }

    /// <summary>Ctrl+wheel keeps what is under the cursor under the cursor.</summary>
    /// <remarks>
    /// <para><b>The two cursors are the test.</b> Anchored at the centre, zooming in with the pointer
    /// near the top and with it near the bottom asks for the same scroll; anchored at the cursor each
    /// asks for the offset that keeps its own point's fraction of the canvas under it. The fraction
    /// rather than the pixel is what is held, because the page margins do not scale — the same
    /// approximation the centre anchor always made.</para>
    ///
    /// <para><b>It asserts the requested scroll, not the applied one.</b> A control that has never
    /// been shown has no window handle and drops <c>AutoScrollPosition</c> — a first version of this
    /// test read the applied offset and got 0 for both cursors, anchored or not.</para>
    /// </remarks>
    [Fact]
    public void WheelZoom_AsksForTheScrollThatKeepsTheCursorsPoint()
    {
        RunSta(() =>
        {
            void Check(int cursorY)
            {
                using var ctrl = new PdfViewerControl();
                ctrl.Size = new Size(900, 700);
                ctrl.LoadBytes(TestPdf.ManyPages(40));

                var surface = (ScrollableControl)ctrl.PageSurface;
                int oldCanvas = surface.AutoScrollMinSize.Height;
                double fraction = ZoomMath.AnchorFraction(-surface.AutoScrollPosition.Y, cursorY, oldCanvas);

                ctrl.ZoomStepAt(zoomIn: true, new Point(200, cursorY));

                int newCanvas = surface.AutoScrollMinSize.Height;
                Assert.True(newCanvas > oldCanvas, "the zoom did not grow the canvas");
                Assert.Equal(ZoomMath.ScrollForAnchorFraction(fraction, cursorY, surface.ClientSize.Height, newCanvas),
                             ctrl.LastAnchoredScroll.Y);
            }

            Check(40);
            Check(560);
        });
    }

    [Fact]
    public void WheelZoom_AtTwoCursorsAsksForTwoDifferentScrolls()
    {
        RunSta(() =>
        {
            int ScrollFor(int cursorY)
            {
                using var ctrl = new PdfViewerControl();
                ctrl.Size = new Size(900, 700);
                ctrl.LoadBytes(TestPdf.ManyPages(40));

                // Through the wheel handler itself, so the cursor's location is what reaches the zoom.
                var wheel = new MouseEventArgs(MouseButtons.None, 0, 200, cursorY, delta: 120);
                Assert.True(ctrl.SurfaceMouseWheel(wheel, Keys.Control));
                return ctrl.LastAnchoredScroll.Y;
            }

            // From the top of the canvas a 1.25x step moves the point under the cursor down by about a
            // quarter of the cursor's height — less, since the page margins do not grow — so the two
            // requests are ~10 and ~130 px apart. The centre anchor asks for one scroll for both.
            int top = ScrollFor(40), bottom = ScrollFor(560);
            Assert.True(bottom - top > 100, $"cursor near the top asked for {top}, near the bottom for {bottom}");
        });
    }
    /// <summary>Zooming near the front of a long document does not teleport to its middle.</summary>
    [Fact]
    public void ZoomIn_NearTheStartOfALongDocument_StaysNearTheStart()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.Size = new Size(900, 700);
            ctrl.LoadBytes(TestPdf.ManyPages(200));
            ctrl.GoToPage(3);

            ctrl.ZoomIn();

            Assert.InRange(ctrl.CurrentPage, 0, 20);
        });
    }

    // ================================================================ render cache

    /// <summary>
    /// The page just rendered is never the eviction victim.
    /// </summary>
    /// <remarks>
    /// The victim was the cached page farthest from <c>_currentPage</c>, and on load
    /// <c>_currentPage</c> is 0 while the paint loop walks visible pages upward — so the 17th page
    /// rendered was itself the farthest, was disposed, and was handed to
    /// <c>Graphics.DrawImage</c> anyway.
    /// </remarks>
    [Fact]
    public void EvictionVictim_IsNeverThePageJustRendered()
    {
        var cached = Enumerable.Range(0, 17).ToList();

        int victim = PdfViewerControl.ChooseEvictionVictim(cached, currentPage: 0, keepPage: 16);

        Assert.NotEqual(16, victim);
        Assert.Equal(15, victim);      // the farthest of the rest
    }

    [Fact]
    public void EvictionVictim_IsNeverAPageTheViewKeeps()
    {
        var cached = Enumerable.Range(0, 6).ToList();

        int victim = PdfViewerControl.ChooseEvictionVictim(cached, currentPage: 0, keepPage: 5,
            kept: new HashSet<int> { 3, 4 });

        Assert.Equal(2, victim);       // the farthest of the rest
        Assert.Equal(4, PdfViewerControl.ChooseEvictionVictim(cached, currentPage: 0, keepPage: 5));   // control: 4 without the set
        Assert.Equal(-1, PdfViewerControl.ChooseEvictionVictim(new List<int> { 3, 4, 5 }, currentPage: 0, keepPage: 5,
            kept: new HashSet<int> { 3, 4 }));
    }

    [Fact]
    public void EvictionVictim_IsMinusOneWhenOnlyTheKeptPageRemains()
        => Assert.Equal(-1, PdfViewerControl.ChooseEvictionVictim(
            new List<int> { 7 }, currentPage: 0, keepPage: 7));

    /// <summary>
    /// Rendering more pages than the cache holds still returns usable bitmaps for every one.
    /// </summary>
    /// <remarks>
    /// Reading <see cref="Bitmap.Width"/> on a disposed bitmap throws, so this asserts the guard's
    /// own evidence: before the fix the last page's bitmap came back disposed and the throw
    /// happened inside <c>Surface.OnPaint</c>, where nothing catches it.
    /// </remarks>
    [Fact]
    public void EnsurePage_ReturnsAUsableBitmapForEveryVisiblePage()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(24));

            for (int p = 0; p < 24; p++)
            {
                Bitmap? bmp = ctrl.EnsurePage(p);
                Assert.NotNull(bmp);
                Assert.True(bmp!.Width > 0 && bmp.Height > 0,
                    $"page {p} came back as an unusable bitmap");
            }
        });
    }

    /// <summary>
    /// A page whose pixel extent is file-controlled is measured before it is allocated, and one
    /// over the ceiling is refused rather than being rendered into a 400 MB bitmap (twice, once
    /// GDI+ has its own copy).
    /// </summary>
    [Fact]
    public void OversizedUserUnitPage_IsMeasuredOverTheSingleBitmapCeiling()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.HugeUserUnitPage());

        long bytes = src.EstimatedBitmapBytes(0, 96);

        Assert.True(bytes > PdfViewerControl.MaxSingleBitmapBytes,
            $"a /UserUnit 75000 page should measure over the ceiling, got {bytes} bytes");
    }

    [Fact]
    public void OversizedUserUnitPage_IsRefusedRatherThanRendered()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.HugeUserUnitPage());

            Assert.Null(ctrl.EnsurePage(0));
        });
    }

    [Fact]
    public void OrdinaryPage_IsWellUnderTheCeilingAndStillRenders()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePage());

        long bytes = src.EstimatedBitmapBytes(0, 96);

        Assert.InRange(bytes, 1, PdfViewerControl.MaxSingleBitmapBytes);
    }

    // ================================================================ flat-drag selection

    /// <summary>
    /// A horizontal sweep along a line's baseline selects <b>that</b> line, not the one below it.
    /// </summary>
    /// <remarks>
    /// <para>Glyph rectangles are anchored to the baseline and the flat-drag band hung
    /// <i>downward</i> from the pointer, so the centres inside the band belonged to the next line
    /// down: the user saw the following line highlighted and Ctrl+C copied its text.</para>
    /// <para>Built from the extractor's real coordinates for a two-line page rather than from
    /// invented numbers, so the two halves — where a glyph's box is, and where the band is — are
    /// asserted against each other.</para>
    /// </remarks>
    [Fact]
    public void FlatDragAlongABaseline_CoversThatLineAndNotTheNextOne()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.TwoLinesOfText());
        var geo = src.Geometry(0);
        float scale = PdfPixelMapper.ScaleForDpi(96);

        var lines = LineChars(src);
        Assert.True(lines.Count >= 2, "fixture must extract two lines of text");

        PdfTextChar first = lines[0], second = lines[1];
        Assert.True(Math.Abs(first.Y - second.Y) > 1f, "fixture's two lines must have distinct baselines");

        RectangleF r1 = PdfPixelMapper.MapGlyph(geo, scale, first.X, first.Y, first.Width, first.Height);
        RectangleF r2 = PdfPixelMapper.MapGlyph(geo, scale, second.X, second.Y, second.Width, second.Height);

        // Sweep along line 1's baseline: the pointer sits exactly on it, which is how a line gets
        // selected by hand.
        float baseline1 = PdfPixelMapper.MapRect(geo, scale, first.X, first.Y, 0f, 0f).Y;
        var band = PdfViewerControl.SelectionBand(
            new Point((int)r1.Left, (int)baseline1),
            new Point((int)r1.Right + 120, (int)baseline1 + 1),
            scale);

        Assert.True(band.Contains(r1.X + r1.Width / 2, r1.Y + r1.Height / 2),
            $"the swept line must be selected (band {band}, glyph {r1})");
        Assert.False(band.Contains(r2.X + r2.Width / 2, r2.Y + r2.Height / 2),
            $"the line below must not be (band {band}, glyph {r2})");
    }

    /// <summary>A tall drag is untouched — only the flat-drag heuristic moved.</summary>
    [Fact]
    public void TallDrag_IsTheRectangleBetweenThePointers()
    {
        var r = PdfViewerControl.SelectionBand(new Point(10, 20), new Point(60, 220), 1.3333f);

        Assert.Equal(10f, r.X, 3);
        Assert.Equal(20f, r.Y, 3);
        Assert.Equal(50f, r.Width, 3);
        Assert.Equal(200f, r.Height, 3);
    }

    private static List<PdfTextChar> LineChars(PdfDocumentSource src)
    {
        var layout = src.LayoutFor(0);
        var firstOfLine = new List<PdfTextChar>();
        if (layout == null) return firstOfLine;
        foreach (var line in layout.Lines)
            foreach (var word in line.Words)
                if (word.Chars.Count > 0) { firstOfLine.Add(word.Chars[0]); break; }
        return firstOfLine;
    }

    // ================================================================ thumbnail strip

    /// <summary>
    /// The thumbnail strip is capped, so a file-controlled page count cannot spend the process's
    /// USER handle quota one <see cref="Panel"/> at a time.
    /// </summary>
    [Fact]
    public void ThumbnailStrip_IsCappedForALongDocument()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(PdfViewerControl.MaxThumbnails + 80));

            Assert.Equal(PdfViewerControl.MaxThumbnails, ctrl.ThumbnailItemCount);
        });
    }

    [Fact]
    public void ThumbnailStrip_IsCompleteForAShortDocument()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(9));

            Assert.Equal(9, ctrl.ThumbnailItemCount);
        });
    }
}
