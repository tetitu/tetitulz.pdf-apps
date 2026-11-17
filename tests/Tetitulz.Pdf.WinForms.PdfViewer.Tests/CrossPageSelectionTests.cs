// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// A drag that crosses from one page into the next selects on every page it crosses, and Ctrl+C copies
/// both pages' text in page order. It used to select on the page the drag began on alone.
/// </summary>
public sealed class CrossPageSelectionTests
{
    private static void RunSta(Action action) => StaThread.Run(action);

    [Fact]
    public void ADragAcrossTwoPages_SelectsOnBoth_AndCopiesThemInOrder()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(600, 500) };
            ctrl.LoadBytes(TestPdf.TwoPagesOfText());
            var canvas = ((ScrollableControl)ctrl.PageSurface).AutoScrollMinSize;
            Assert.True(canvas.Height > 0, "the pages were not laid out");

            string both = ctrl.SelectBetween(new Point(0, 0), new Point(canvas.Width, canvas.Height));
            Assert.Equal(2, ctrl.SelectedPageCount);
            int one = both.IndexOf("PAGEONE", StringComparison.Ordinal);
            int two = both.IndexOf("PAGETWO", StringComparison.Ordinal);
            Assert.True(one >= 0 && two > one, $"copied: '{both}'");

            // The control: a band over the first page alone selects there alone.
            string first = ctrl.SelectBetween(new Point(0, 0), new Point(canvas.Width, canvas.Height / 2 - 20));
            Assert.Equal(1, ctrl.SelectedPageCount);
            Assert.Contains("PAGEONE", first, StringComparison.Ordinal);
            Assert.DoesNotContain("PAGETWO", first, StringComparison.Ordinal);
        });
    }

    /// <summary>The pages a band crosses, in page order, whatever order the layout lists them.</summary>
    [Fact]
    public void PagesUnderTheBand_AreThoseItCrosses_InPageOrder()
    {
        var layout = new[]
        {
            (1, new Rectangle(0, 300, 200, 280)),
            (0, new Rectangle(0, 10, 200, 280)),
            (2, new Rectangle(0, 590, 200, 280)),
        };
        Assert.Equal(new[] { 0, 1 }, PdfViewerControl.PagesUnder(layout, new RectangleF(10, 200, 50, 200)));
        Assert.Equal(new[] { 2 }, PdfViewerControl.PagesUnder(layout, new RectangleF(10, 600, 50, 20)));
        Assert.Empty(PdfViewerControl.PagesUnder(layout, new RectangleF(10, 292, 50, 4)));   // the gap between pages
    }
}
