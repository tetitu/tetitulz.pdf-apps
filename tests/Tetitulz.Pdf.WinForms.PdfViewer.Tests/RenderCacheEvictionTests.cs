// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Threading;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The render cache holds at most <c>MaxCachedPages</c> bitmaps and evicts the entry farthest from
/// the current page. When the paint loop walks forward from page 0 that victim is the page just
/// inserted — the one whose bitmap <c>EnsurePage</c> is about to return.
/// </summary>
public sealed class RenderCacheEvictionTests
{
    /// <summary>
    /// A viewport showing more than <c>MaxCachedPages</c> pages (a 30-page document of small pages
    /// in Continuous layout, or any document zoomed out) makes <c>PaintSurface</c> call
    /// <c>EnsurePage</c> for pages 0,1,2,… in ascending order while <c>_currentPage</c> is 0. Past
    /// the cache limit the freshly rendered bitmap was itself the farthest entry, so it was
    /// disposed and then handed to <c>Graphics.DrawImage</c> — an <c>ArgumentException</c> raised
    /// inside <c>OnPaint</c>, i.e. an unhandled exception on the UI thread.
    /// </summary>
    [Fact]
    public void EnsurePage_NeverReturnsABitmapItJustEvicted()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(30));
            Assert.Equal(0, ctrl.CurrentPage);

            Bitmap? last = null;
            for (int i = 0; i < 30; i++)
            {
                last = ctrl.EnsurePage(i);
                Assert.NotNull(last);

                // Reading a property of a disposed Bitmap throws, so this is the liveness check
                // AND the correctness one: a 100x100 pt page at 96 dpi is 134x134 px.
                Assert.Equal(134, last!.Width);
                Assert.Equal(134, last.Height);
            }

            // The exact call that crashed the paint loop.
            using var target = new Bitmap(16, 16);
            using var g = Graphics.FromImage(target);
            g.DrawImage(last!, new Rectangle(0, 0, 16, 16));
        });
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
