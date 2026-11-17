// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// A zoom keeps the reader where they are. A fit (Fit Width, Fit Page, a refit when the view is
/// resized) and a zoom step change every page's height in continuous layout; the page being read and
/// the point of it at the top of the view must survive that, whatever the scroll offset in pixels.
/// </summary>
/// <remarks>
/// Shown windows: a control that has never been shown drops <c>AutoScrollPosition</c>, so where the
/// view lands can only be read off a real one.
/// </remarks>
public sealed class ReadingPositionTests
{
    [DesktopFact]
    public void FitWidth_InContinuousLayout_KeepsThePageBeingRead()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(40));                     // 100 x 100 pt pages
            viewer.GoToPage(25);
            Assert.Equal(25, viewer.CurrentPage);                        // potency: the jump landed
            int changes = 0;
            viewer.PageChanged += (s, e) => changes++;

            viewer.FitWidth();

            Assert.Equal(PdfZoomMode.FitWidth, viewer.ZoomMode);
            Assert.True(viewer.Zoom > 2, $"the fit did not enlarge the page (zoom {viewer.Zoom})");
            Assert.Equal(25, viewer.CurrentPage);
            Assert.Equal(25, PageAtTopOfView(viewer));
            Assert.Equal(0, changes);
        });
    }

    [DesktopFact]
    public void FitPage_InContinuousLayout_KeepsThePageBeingRead()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(40));
            viewer.Zoom = 6;                                             // a page taller than the view
            viewer.GoToPage(30);

            viewer.FitPage();

            Assert.True(viewer.Zoom < 6, $"the fit did not shrink the page (zoom {viewer.Zoom})");
            Assert.Equal(30, viewer.CurrentPage);
            Assert.Equal(30, PageAtTopOfView(viewer));
        });
    }

    [DesktopFact]
    public void ARefitOnResize_KeepsThePageBeingRead()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(40));
            viewer.FitWidth();
            viewer.GoToPage(20);
            double before = viewer.Zoom;

            form.ClientSize = new Size(1100, 700);                       // wider: the fit grows
            Application.DoEvents();

            Assert.True(viewer.Zoom > before, $"the resize did not refit ({before} -> {viewer.Zoom})");
            Assert.Equal(20, viewer.CurrentPage);
            Assert.Equal(20, PageAtTopOfView(viewer));
        });
    }

    [DesktopFact]
    public void AZoomStep_KeepsThePageBeingRead()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(40));
            viewer.Zoom = 3;                                             // one page fills the view, so its centre is page 12's
            viewer.GoToPage(12);

            viewer.ZoomIn();
            viewer.ZoomIn();
            viewer.ZoomOut();

            Assert.Equal(12, viewer.CurrentPage);
        });
    }

    /// <summary>The page under a point just below the top of the view, read off the shown control.</summary>
    private static int PageAtTopOfView(PdfViewerControl viewer)
    {
        var surface = viewer.PageSurface;
        Point probe = viewer.PointToClient(surface.PointToScreen(new Point(surface.ClientSize.Width / 2, 20)));
        return viewer.TryClientToPage(probe, out int page, out _) ? page : -1;
    }

    private static Form ShownForm(out PdfViewerControl viewer)
    {
        var form = new Form
        {
            ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual,
            Location = new Point(-2000, -2000), ShowInTaskbar = false,
        };
        viewer = new PdfViewerControl { Dock = DockStyle.Fill, LayoutMode = PdfViewLayoutMode.Continuous };
        form.Controls.Add(viewer);
        form.Show();                                                     // a control never shown drops its scroll
        Application.DoEvents();
        return form;
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
