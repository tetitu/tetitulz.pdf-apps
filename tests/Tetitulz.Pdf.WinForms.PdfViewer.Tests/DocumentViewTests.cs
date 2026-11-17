// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// <see cref="PdfViewerFeatures.DocumentView"/>: a document that asks to open at a page, a zoom, a
/// layout, a sidebar tab and a direction gets them — only when the host turned the feature on.
/// </summary>
public sealed class DocumentViewTests
{
    [Fact]
    public void WithTheFeatureOn_TheDocumentOpensAsItAsks()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Size = new System.Drawing.Size(900, 700) };
            viewer.Features |= PdfViewerFeatures.DocumentView;

            viewer.LoadBytes(TestPdf.AsksForItsOwnView());

            Assert.Equal(2, viewer.CurrentPage);
            Assert.Equal(PdfViewLayoutMode.SinglePage, viewer.LayoutMode);
            Assert.Equal(2.0, viewer.Zoom, 6);
            Assert.Same(viewer.BookmarkTree.Parent, viewer.SidebarTabs.SelectedTab);
            Assert.Equal(RightToLeft.Yes, viewer.RightToLeft);
        });
    }

    [Fact]
    public void ByDefault_TheHostsViewIsKept()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Size = new System.Drawing.Size(900, 700) };

            viewer.LoadBytes(TestPdf.AsksForItsOwnView());

            Assert.Equal(0, viewer.CurrentPage);
            Assert.Equal(PdfViewLayoutMode.Continuous, viewer.LayoutMode);
            Assert.Equal(1.0, viewer.Zoom, 6);
            Assert.Equal(RightToLeft.No, viewer.RightToLeft);
        });
    }

    [Fact]
    public void TheDocumentsDirection_GoesWithTheDocument()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Size = new System.Drawing.Size(900, 700) };
            viewer.Features |= PdfViewerFeatures.DocumentView;
            viewer.LoadBytes(TestPdf.AsksForItsOwnView());
            Assert.Equal(RightToLeft.Yes, viewer.RightToLeft);

            viewer.LoadBytes(TestPdf.ManyPages(2));                       // asks for nothing

            Assert.Equal(RightToLeft.No, viewer.RightToLeft);
        });
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
