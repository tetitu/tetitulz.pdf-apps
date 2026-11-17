// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Two pages to a row (<see cref="PdfViewerControl.PageSpread"/>): the pages of a row meet at the
/// spine, the earlier on the left (the right, right to left), a cover alone on the right; Next and
/// Previous step a row; a fit fits the row; the document's two-page layouts open as they ask.
/// </summary>
public sealed class SpreadTests
{
    [DesktopFact]
    public void Facing_PairsOneAndTwo_AtTheSpine_AndLeavesALastOddPageOnTheLeft()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(5));
            RectangleF single0 = Bounds(viewer, 0), single1 = Bounds(viewer, 1);
            Assert.True(single1.Top > single0.Bottom);               // potency: one column first

            viewer.PageSpread = PdfPageSpread.Facing;

            RectangleF p0 = Bounds(viewer, 0), p1 = Bounds(viewer, 1), p2 = Bounds(viewer, 2), p4 = Bounds(viewer, 4);
            Assert.Equal(p0.Top, p1.Top);
            Assert.True(p0.Right < p1.Left, "the first page is not left of the second");
            Assert.InRange(p1.Left - p0.Right, 1, 20);               // the spine's gap
            Assert.True(p2.Top > p0.Bottom);                         // the next row below
            Assert.Equal(p2.Left, p0.Left);
            Assert.Equal(p0.Right, p4.Right);                        // a last page alone stays left of the spine
        });
    }

    [DesktopFact]
    public void FacingWithCover_PutsTheFirstPageAloneOnTheRight_ThenPairsTwoAndThree()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(5));

            viewer.PageSpread = PdfPageSpread.FacingWithCover;

            RectangleF p0 = Bounds(viewer, 0), p1 = Bounds(viewer, 1), p2 = Bounds(viewer, 2);
            Assert.Equal(p1.Top, p2.Top);
            Assert.True(p1.Right < p2.Left);
            Assert.True(p1.Top > p0.Bottom);
            Assert.Equal(p2.Left, p0.Left);                          // the cover stands right of the spine
        });
    }

    [DesktopFact]
    public void RightToLeft_PutsTheEarlierPageOnTheRight()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            // No sidebar to move to the other side: the flip itself must lay the pages out again.
            viewer.ShowSidebar = false;
            viewer.LoadBytes(TestPdf.ManyPages(4));
            viewer.PageSpread = PdfPageSpread.Facing;
            Assert.True(Bounds(viewer, 0).Right < Bounds(viewer, 1).Left);   // potency: left to right first

            viewer.RightToLeft = RightToLeft.Yes;

            Assert.True(Bounds(viewer, 1).Right < Bounds(viewer, 0).Left, "the first page is not on the right");
        });
    }

    [DesktopFact]
    public void OneSpreadAtATime_NextAndPreviousStepARow()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(5));
            viewer.LayoutMode = PdfViewLayoutMode.SinglePage;
            viewer.PageSpread = PdfPageSpread.Facing;
            Assert.True(Shown(viewer, 0) && Shown(viewer, 1) && !Shown(viewer, 2));

            viewer.NextPage();
            Assert.Equal(2, viewer.CurrentPage);
            Assert.True(Shown(viewer, 2) && Shown(viewer, 3) && !Shown(viewer, 1));

            viewer.NextPage();
            Assert.Equal(4, viewer.CurrentPage);
            viewer.NextPage();                                       // no row after the last
            Assert.Equal(4, viewer.CurrentPage);
            // Nor does scrolling on past the last row's end ask for one.
            int asked = 0;
            EventHandler<PdfViewerCommandEventArgs> count = (s, e) => { if (e.Command == PdfViewerCommand.NextPage) asked++; };
            viewer.CommandExecuting += count;
            viewer.ScrollView(0, 5000);
            viewer.ScrollView(0, 40);
            viewer.CommandExecuting -= count;
            Assert.Equal(0, asked);

            viewer.PreviousPage();
            Assert.Equal(2, viewer.CurrentPage);

            viewer.PageSpread = PdfPageSpread.FacingWithCover;       // rows: 0 | 1 2 | 3 4
            viewer.GoToPage(0);
            viewer.NextPage();
            Assert.Equal(1, viewer.CurrentPage);
            viewer.NextPage();
            Assert.Equal(3, viewer.CurrentPage);
            Assert.True(Shown(viewer, 3) && Shown(viewer, 4));
        });
    }

    [DesktopFact]
    public void TheRightPageOfARow_StaysTheCurrentPage_AsTheViewScrolls()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(40));
            viewer.Zoom = 3;
            viewer.PageSpread = PdfPageSpread.Facing;

            viewer.GoToPage(25);                                     // the right page of row 12
            Assert.Equal(25, viewer.CurrentPage);
            viewer.ScrollView(0, 40);
            Assert.Equal(25, viewer.CurrentPage);

            viewer.ScrollView(0, 3000);                              // rows later
            Assert.True(viewer.CurrentPage > 25 && viewer.CurrentPage % 2 == 0,
                $"a row further on makes its first page current (page {viewer.CurrentPage})");
        });
    }

    [DesktopFact]
    public void AFitWidth_FitsTheRow()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(4));
            viewer.FitWidth();
            double one = viewer.Zoom;

            viewer.PageSpread = PdfPageSpread.Facing;

            Assert.Equal(PdfZoomMode.FitWidth, viewer.ZoomMode);
            // About half: two pages and the spine's gap in the same width (a scroll bar more or less).
            Assert.InRange(viewer.Zoom, one * 0.4, one * 0.55);
            // The row fills the view's width, margin to margin, both pages in it.
            RectangleF left = Bounds(viewer, 0), right = Bounds(viewer, 1);
            var surface = viewer.PageSurface;
            float viewLeft = viewer.PointToClient(surface.PointToScreen(Point.Empty)).X;
            float viewWidth = surface.ClientSize.Width;
            Assert.True(left.Left >= viewLeft && right.Right <= viewLeft + viewWidth, "the row is wider than the view");
            Assert.True(right.Right - left.Left >= viewWidth - 2 * 14 - 4, $"the row does not fill the view ({right.Right - left.Left} of {viewWidth})");
        });
    }

    [Theory]
    [InlineData("TwoColumnLeft", PdfViewLayoutMode.Continuous, PdfPageSpread.Facing)]
    [InlineData("TwoColumnRight", PdfViewLayoutMode.Continuous, PdfPageSpread.FacingWithCover)]
    [InlineData("TwoPageLeft", PdfViewLayoutMode.SinglePage, PdfPageSpread.Facing)]
    [InlineData("TwoPageRight", PdfViewLayoutMode.SinglePage, PdfPageSpread.FacingWithCover)]
    [InlineData("OneColumn", PdfViewLayoutMode.Continuous, PdfPageSpread.None)]
    [InlineData("SinglePage", PdfViewLayoutMode.SinglePage, PdfPageSpread.None)]
    public void TheDocumentsPageLayout_OpensAsItAsks(string layout, PdfViewLayoutMode mode, PdfPageSpread spread)
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { RenderPagesInBackground = false };
            viewer.Features |= PdfViewerFeatures.DocumentView;
            viewer.LayoutMode = mode == PdfViewLayoutMode.Continuous ? PdfViewLayoutMode.SinglePage : PdfViewLayoutMode.Continuous;
            viewer.PageSpread = spread == PdfPageSpread.None ? PdfPageSpread.Facing : PdfPageSpread.None;

            viewer.LoadBytes(TestPdf.WithPageLayout(layout, 4));

            Assert.Equal(mode, viewer.LayoutMode);
            Assert.Equal(spread, viewer.PageSpread);
        });
    }

    [Fact]
    public void TheMenusSpread_GoesThroughCommandExecuting_WithTheSpreadChosen()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { RenderPagesInBackground = false };
            viewer.LoadBytes(TestPdf.ManyPages(4));
            PdfViewerCommandEventArgs? seen = null;
            bool handle = true;
            viewer.CommandExecuting += (s, e) => { seen = e; e.Handled = handle; };
            var strings = new PdfViewerStrings();

            Click(viewer, strings.SpreadCoverMenu);
            Assert.Equal(PdfViewerCommand.SetPageSpread, seen?.Command);
            Assert.Equal(PdfPageSpread.FacingWithCover, seen?.PageSpread);
            Assert.Equal(PdfPageSpread.None, viewer.PageSpread);       // handled by the host

            handle = false;
            Click(viewer, strings.SpreadFacingMenu);
            Assert.Equal(PdfPageSpread.Facing, viewer.PageSpread);
        });
    }

    [Fact]
    public void PageSpread_RefusesAnUndefinedValue()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { PageSpread = PdfPageSpread.Facing };
            Assert.Throws<ArgumentOutOfRangeException>(() => viewer.PageSpread = (PdfPageSpread)7);
            Assert.Equal(PdfPageSpread.Facing, viewer.PageSpread);
        });
    }

    private static void Click(PdfViewerControl viewer, string text)
    {
        foreach (ToolStripItem item in viewer.PageMenu.Items)
            if (item.Text == text) { item.PerformClick(); return; }
        throw new InvalidOperationException("No menu item " + text);
    }

    private static RectangleF Bounds(PdfViewerControl viewer, int page)
    {
        Assert.True(viewer.TryPageToClient(page, new RectangleF(0, 0, 100, 100), out RectangleF r), $"page {page} is not laid out");
        return RectangleF.FromLTRB((float)Math.Round(r.Left), (float)Math.Round(r.Top), (float)Math.Round(r.Right), (float)Math.Round(r.Bottom));
    }

    private static bool Shown(PdfViewerControl viewer, int page) => viewer.TryPageToClient(page, new RectangleF(0, 0, 1, 1), out _);

    private static Form ShownForm(out PdfViewerControl viewer)
    {
        var form = new Form
        {
            ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000), ShowInTaskbar = false,
        };
        viewer = new PdfViewerControl
        {
            Dock = DockStyle.Fill, LayoutMode = PdfViewLayoutMode.Continuous, DeviceDpiOverride = 96,
            RenderPagesInBackground = false,
        };
        form.Controls.Add(viewer);
        form.Show();
        Application.DoEvents();
        return form;
    }
}
