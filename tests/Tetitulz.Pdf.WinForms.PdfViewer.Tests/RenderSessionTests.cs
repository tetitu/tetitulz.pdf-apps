// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Pages render through a session held over the shown document, not a one-shot call that parses the
/// whole document per page (80 ms a page at 800 pages against 12 through a session). The two paths
/// draw the same pixels, so only the session's own count can say which one ran.
/// </summary>
public sealed class RenderSessionTests
{
    [Fact]
    public void ThePageView_RendersEveryPageThroughOneSession()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.ManyPages(6));
            var source = viewer.DocumentSource!;

            for (int page = 0; page < 6; page++) Assert.NotNull(viewer.EnsurePage(page));
            viewer.Zoom = 2;
            Assert.NotNull(viewer.EnsurePage(3));

            Assert.Equal(1, source.SessionsOpened);
        });
    }

    [Fact]
    public void PrintingUsesASessionOfItsOwnIntent_AndTheScreenKeepsItsOwn()
    {
        var source = PdfDocumentSource.FromBytes(TestPdf.RedInchPages(null));
        using (var bmp = new Bitmap(200, 200))
        using (var g = Graphics.FromImage(bmp))
        {
            for (int page = 0; page < 3; page++)
                Assert.Null(PdfViewerControl.PrintPage(source, page, 72, g, new Rectangle(0, 0, 100, 100)));
        }
        Assert.Equal(1, source.SessionsOpened);                          // the print intent, once for the job

        using (source.RenderBitmap(0, 72)) { }
        Assert.Equal(2, source.SessionsOpened);                          // the screen's is another
    }

    [Fact]
    public void AReplacedDocument_ClosesItsSessions()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.OnePage());
            var first = viewer.DocumentSource!;
            Assert.NotNull(viewer.EnsurePage(0));
            Assert.Equal(1, first.OpenSessionCount);                     // potency: one was open

            viewer.LoadBytes(TestPdf.OnePage());

            Assert.Equal(0, first.OpenSessionCount);
            Assert.NotSame(first, viewer.DocumentSource);
        });
    }

    [Fact]
    public void ClosingTheDocument_ClosesItsSessions()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.OnePage());
            var source = viewer.DocumentSource!;
            Assert.NotNull(viewer.EnsurePage(0));

            viewer.CloseDocument();

            Assert.Equal(0, source.OpenSessionCount);
        });
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
