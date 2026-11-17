// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>Smoke test for the control's public load path (runs on an STA thread).</summary>
public sealed class PdfViewerControlTests
{
    [Fact]
    public void LoadBytes_SetsDocumentState()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            bool loadedFired = false;
            ctrl.DocumentLoaded += (s, e) => loadedFired = e.PageCount == 1;

            ctrl.LoadBytes(TestPdf.OnePage());

            Assert.True(ctrl.HasDocument);
            Assert.Equal(1, ctrl.PageCount);
            Assert.Equal(0, ctrl.CurrentPage);
            Assert.True(loadedFired);

            ctrl.CloseDocument();
            Assert.False(ctrl.HasDocument);
            Assert.Equal(0, ctrl.PageCount);
        });
    }

    [Fact]
    public void LayoutMode_DefaultsToContinuous_AndToggles()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            Assert.Equal(PdfViewLayoutMode.Continuous, ctrl.LayoutMode);
            ctrl.LoadBytes(TestPdf.OnePage());
            ctrl.LayoutMode = PdfViewLayoutMode.SinglePage;
            Assert.Equal(PdfViewLayoutMode.SinglePage, ctrl.LayoutMode);
        });
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
