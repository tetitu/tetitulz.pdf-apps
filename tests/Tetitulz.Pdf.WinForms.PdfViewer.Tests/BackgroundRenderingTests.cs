// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Pages render on a background thread (<see cref="PdfViewerControl.RenderPagesInBackground"/>): a
/// paint asks and draws what is ready, a zoom shows the previous bitmap until the new one arrives,
/// and a result the view has moved past is dropped.
/// </summary>
public sealed class BackgroundRenderingTests
{
    // ── The worker ────────────────────────────────────────────────────────────

    [Fact]
    public void TheWorker_RendersOffTheCallingThread_WhatRetainKept()
    {
        byte[] doc = TestPdf.ManyPages(3);
        var results = new List<PageRenderResult>();
        using var done = new ManualResetEventSlim();
        using var worker = new PageRenderWorker(r => { lock (results) results.Add(r); done.Set(); }, startPaused: true);

        for (int page = 0; page < 3; page++) worker.Enqueue(Request(doc, page, priority: page));
        worker.Enqueue(Request(doc, 1, priority: 0));                    // the same work: re-ranked, not queued twice
        Assert.Equal(3, worker.PendingCount);

        worker.Retain(r => r.Page == 1);
        Assert.Equal(1, worker.PendingCount);
        worker.Resume();

        Assert.True(done.Wait(10_000), "the worker never delivered");
        Assert.True(SpinWait.SpinUntil(() => worker.IsIdle, 10_000));
        var result = Assert.Single(results);
        Assert.Equal(1, result.Request.Page);
        Assert.NotNull(result.Bitmap);
        Assert.Equal(134, result.Bitmap!.Width);                         // 100 pt at 96 dpi, rounded up
        Assert.NotEqual(Environment.CurrentManagedThreadId, worker.LastRenderThreadId);
        result.Bitmap.Dispose();
    }

    [Fact]
    public void TheWorker_RefusesAPageOverItsBudget_BeforeRenderingIt()
    {
        PageRenderResult? result = null;
        using var done = new ManualResetEventSlim();
        using var worker = new PageRenderWorker(r => { result = r; done.Set(); });

        var request = Request(TestPdf.ManyPages(1), 0, priority: 0);
        request.MaxBytes = 1000;                                          // 134 x 134 x 4 is far more
        worker.Enqueue(request);

        Assert.True(done.Wait(10_000));
        Assert.Null(result!.Bitmap);
        Assert.Equal(134L * 134 * 4, result.RefusedBytes);
        Assert.Null(result.Error);
    }

    [Fact]
    public void TheWorker_ScalesAThumbnailIntoItsBox()
    {
        PageRenderResult? result = null;
        using var done = new ManualResetEventSlim();
        using var worker = new PageRenderWorker(r => { result = r; done.Set(); });

        var request = Request(TestPdf.MixedPageSizes(), 1, priority: 0); // 400 x 100 pt
        request.Kind = PageRenderKind.Thumbnail;
        request.Box = new Size(140, 140);
        worker.Enqueue(request);

        Assert.True(done.Wait(10_000));
        Size size = result!.Bitmap!.Size;
        Assert.InRange(size.Width, 139, 140);                            // the box's width, its proportions kept
        Assert.InRange(size.Height, 34, 36);
        result.Bitmap.Dispose();
    }

    // ── The control ───────────────────────────────────────────────────────────

    [DesktopFact]
    public void APaint_AsksTheWorker_AndThePageArrivesRenderedOffTheUiThread()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.OnePage());
            Paint(viewer);                                               // a paint, no message pumped since

            Assert.False(viewer.HasRenderedPage(0));                     // the paint did not render it
            Assert.True(viewer.DrainRenders());
            Assert.True(viewer.HasRenderedPage(0));
            Assert.NotNull(viewer.RenderWorker);
            Assert.NotEqual(Environment.CurrentManagedThreadId, viewer.RenderWorker!.LastRenderThreadId);
        });
    }

    [DesktopFact]
    public void AZoom_ShowsThePreviousBitmap_UntilThePageRendersAnew()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.OnePage());
            Paint(viewer);
            Assert.True(viewer.DrainRenders());
            Assert.True(viewer.HasRenderedPage(0));

            viewer.Zoom = 2;

            Assert.Equal(1, viewer.PreviewCount);                        // what the paint draws meanwhile
            Assert.False(viewer.HasRenderedPage(0));
            Paint(viewer);                                               // draws the preview, asks for the page
            Assert.Equal(1, viewer.PreviewCount);
            Assert.True(viewer.DrainRenders());
            Assert.True(viewer.HasRenderedPage(0));
            Assert.Equal(0, viewer.PreviewCount);
        });
    }

    [DesktopFact]
    public void SwitchedOff_APaintRendersThePageItself()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.RenderPagesInBackground = false;
            viewer.LoadBytes(TestPdf.OnePage());

            Paint(viewer);

            Assert.True(viewer.HasRenderedPage(0));
            Assert.Null(viewer.RenderWorker);
        });
    }

    [Fact]
    public void AResultFromBeforeTheContentChanged_IsDropped()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.ManyPages(2));
            var source = viewer.DocumentSource!;
            int dpi = PdfViewerControlDpi(viewer);
            viewer.ShowComments = false;                                 // what the pages show changed
            Assert.NotNull(viewer.EnsurePage(1));                        // the cache is at the view's zoom; page 0 is not in it

            var stale = new Bitmap(10, 10);
            var request = new PageRenderRequest { Kind = PageRenderKind.Page, Page = 0, Dpi = dpi, Generation = -1, Bytes = source.ShownBytes };
            viewer.OnRendered(new PageRenderResult(request) { Bitmap = stale });

            Assert.Throws<ArgumentException>(() => stale.Width);         // disposed, never cached
            Assert.False(viewer.HasRenderedPage(0));
            Assert.True(viewer.HasRenderedPage(1));                      // potency: the zoom is the one asked at
        });
    }

    [Fact]
    public void AResultForAnotherDocument_IsDropped()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.OnePage());
            byte[] earlier = viewer.DocumentSource!.ShownBytes;
            viewer.LoadBytes(TestPdf.OnePage());

            var stale = new Bitmap(10, 10);
            var request = new PageRenderRequest { Kind = PageRenderKind.Page, Page = 0, Dpi = PdfViewerControlDpi(viewer), Bytes = earlier };
            viewer.OnRendered(new PageRenderResult(request) { Bitmap = stale });

            Assert.Throws<ArgumentException>(() => stale.Width);
        });
    }

    // ── The cache under the view ─────────────────────────────────────────────

    /// <summary>A 100 pt page's bitmap at 96 dpi: 134 x 134 px.</summary>
    private const long PageBytes = 134L * 134 * 4;

    /// <summary>
    /// Where the cache cannot hold the pages in view (a page at fit width on a 4K display is some
    /// 50 MB), each that arrives must not drop another: the paint would ask for that one again, and
    /// the page being read blink, rendered over and over.
    /// </summary>
    [DesktopFact]
    public void APageInView_IsNeverDroppedForAnother_WhenTheViewOutgrowsTheCache()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.ShowSidebar = false;                                  // no thumbnails among the worker's renders
            viewer.ScaleToDeviceDpi = false;
            viewer.CachedBitmapBytesOverride = PageBytes;                // room for one page
            viewer.LoadBytes(TestPdf.ManyPages(12));
            var inView = viewer.PagesInView();
            Assert.True(inView.Count >= 2, $"the view shows {inView.Count} page(s); the test needs it to outgrow the cache");

            for (int round = 0; round < 3; round++)
            {
                Paint(viewer);
                Assert.True(viewer.DrainRenders());
            }

            foreach (int page in inView) Assert.True(viewer.HasRenderedPage(page), $"page {page} is in view and has no bitmap");
            Assert.Equal(inView.Count, viewer.RenderWorker!.Completed);   // each once, and no neighbour the cache cannot hold
        });
    }

    [DesktopFact]
    public void TheNextPage_IsAskedFor_WhenTheCacheHoldsOneNeighbourBesideTheView()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.ShowSidebar = false;
            viewer.ScaleToDeviceDpi = false;
            viewer.LoadBytes(TestPdf.ManyPages(20));
            viewer.GoToPage(8);
            var inView = viewer.PagesInView();
            int before = inView[0] - 1, after = inView[inView.Count - 1] + 1;
            Assert.True(before >= 0 && after < 20, $"pages {before} and {after} beside the view");
            viewer.CachedBitmapBytesOverride = (inView.Count + 1) * PageBytes;

            for (int round = 0; round < 3; round++)
            {
                Paint(viewer);
                Assert.True(viewer.DrainRenders());
            }

            foreach (int page in inView) Assert.True(viewer.HasRenderedPage(page), $"page {page} is in view and has no bitmap");
            Assert.True(viewer.HasRenderedPage(after));                  // reading on
            Assert.False(viewer.HasRenderedPage(before));                // no room left: never asked for
            Assert.Equal(inView.Count + 1, viewer.RenderWorker!.Completed);
        });
    }

    [DesktopFact]
    public void BothNeighbours_AreAskedFor_WhenTheCacheHoldsThem()
    {
        RunSta(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.ShowSidebar = false;
            viewer.ScaleToDeviceDpi = false;
            viewer.LoadBytes(TestPdf.ManyPages(20));
            viewer.GoToPage(8);
            var inView = viewer.PagesInView();
            int before = inView[0] - 1, after = inView[inView.Count - 1] + 1;

            for (int round = 0; round < 3; round++)
            {
                Paint(viewer);
                Assert.True(viewer.DrainRenders());
            }

            Assert.True(viewer.HasRenderedPage(before));
            Assert.True(viewer.HasRenderedPage(after));
            Assert.Equal(inView.Count + 2, viewer.RenderWorker!.Completed);
        });
    }

    /// <summary>The dpi the view renders at, read as a page's bitmap measures it (100% is 96 dpi).</summary>
    private static int PdfViewerControlDpi(PdfViewerControl viewer)
        => (int)Math.Round(96 * viewer.Zoom);

    private static PageRenderRequest Request(byte[] doc, int page, int priority) => new PageRenderRequest
    {
        Kind = PageRenderKind.Page, Page = page, Dpi = 96, Priority = priority, Bytes = doc,
        MaxBytes = PdfViewerControl.MaxSingleBitmapBytes,
    };

    /// <summary>
    /// One paint of the whole control: an off-screen window is never sent WM_PAINT, since nothing of
    /// it is on a screen, but DrawToBitmap paints it all.
    /// </summary>
    private static void Paint(PdfViewerControl viewer)
    {
        using var bmp = new Bitmap(viewer.Width, viewer.Height);
        viewer.DrawToBitmap(bmp, new Rectangle(Point.Empty, viewer.Size));
    }

    private static Form ShownForm(out PdfViewerControl viewer)
    {
        var form = new Form
        {
            ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual,
            Location = new Point(-2000, -2000), ShowInTaskbar = false,
        };
        viewer = new PdfViewerControl { Dock = DockStyle.Fill };
        form.Controls.Add(viewer);
        form.Show();
        Application.DoEvents();
        return form;
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
