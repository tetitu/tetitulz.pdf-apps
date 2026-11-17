// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// <see cref="PdfViewerControl.ZoomMode"/>: a fit stays a fit. Fit Width and Fit Page used to fit
/// once and leave a fixed factor behind, so resizing the window or turning to a page of another
/// size left the page over- or under-filling the view.
/// </summary>
public sealed class ZoomModeTests
{
    private const int PageMargin = 14;   // PdfViewerControl.PageMargin

    [Fact]
    public void FitWidth_RefitsWhenTheViewIsResized()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(900, 700) };
            ctrl.LoadBytes(TestPdf.OnePage());                 // 200 pt wide
            var surface = ctrl.PageSurface;

            ctrl.FitWidth();
            Assert.Equal(PdfZoomMode.FitWidth, ctrl.ZoomMode);
            Assert.Equal(ZoomMath.FitWidth(surface.ClientSize.Width - 2 * PageMargin, 200), ctrl.Zoom, 6);
            double wide = ctrl.Zoom;

            ctrl.Size = new Size(600, 700);

            Assert.True(surface.ClientSize.Width < 600, "the surface did not follow the control's size");
            Assert.Equal(ZoomMath.FitWidth(surface.ClientSize.Width - 2 * PageMargin, 200), ctrl.Zoom, 6);
            Assert.True(ctrl.Zoom < wide, $"the zoom stayed at {wide} after the view narrowed");
        });
    }

    /// <summary>The control: a fixed zoom is not refitted by the same resize.</summary>
    [Fact]
    public void CustomZoom_IsKeptWhenTheViewIsResized()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(900, 700) };
            ctrl.LoadBytes(TestPdf.OnePage());
            ctrl.Zoom = 1.5;

            ctrl.Size = new Size(600, 700);

            Assert.Equal(PdfZoomMode.Custom, ctrl.ZoomMode);
            Assert.Equal(1.5, ctrl.Zoom, 6);
        });
    }

    [Fact]
    public void FitPage_InSinglePageLayout_RefitsToEachPagesSize()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(900, 700), LayoutMode = PdfViewLayoutMode.SinglePage };
            ctrl.LoadBytes(TestPdf.MixedPageSizes());
            var surface = ctrl.PageSurface;
            ctrl.FitPage();

            double Expected(double w, double h) => ZoomMath.FitPage(
                surface.ClientSize.Width - 2 * PageMargin, surface.ClientSize.Height - 2 * PageMargin, w, h);

            Assert.Equal(Expected(200, 200), ctrl.Zoom, 6);

            ctrl.GoToPage(1);
            Assert.Equal(Expected(400, 100), ctrl.Zoom, 6);
            Assert.NotEqual(Expected(200, 200), ctrl.Zoom, 6);

            ctrl.GoToPage(0);
            Assert.Equal(Expected(200, 200), ctrl.Zoom, 6);
        });
    }

    /// <summary>
    /// In continuous layout a fit is not refitted as the current page changes — the zoom would
    /// change under the reader while scrolling.
    /// </summary>
    [Fact]
    public void FitWidth_InContinuousLayout_IsNotRefittedByAPageChange()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(900, 700) };
            ctrl.LoadBytes(TestPdf.MixedPageSizes());
            ctrl.FitWidth();
            double page0 = ctrl.Zoom;
            Assert.Equal(ZoomMath.FitWidth(ctrl.PageSurface.ClientSize.Width - 2 * PageMargin, 200), page0, 6);

            ctrl.GoToPage(1);

            Assert.Equal(page0, ctrl.Zoom, 6);
            Assert.Equal(PdfZoomMode.FitWidth, ctrl.ZoomMode);
        });
    }

    [Fact]
    public void AFitAppliesToTheNextDocumentLoaded()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(900, 700) };
            ctrl.LoadBytes(TestPdf.OnePageWithBoxes("[0 0 400 400]", "[0 0 400 400]"));
            ctrl.FitWidth();
            double wide = ctrl.Zoom;

            ctrl.LoadBytes(TestPdf.OnePage());                 // 200 pt wide: twice the zoom

            Assert.Equal(PdfZoomMode.FitWidth, ctrl.ZoomMode);
            Assert.Equal(ZoomMath.FitWidth(ctrl.PageSurface.ClientSize.Width - 2 * PageMargin, 200), ctrl.Zoom, 6);
            Assert.Equal(2 * wide, ctrl.Zoom, 3);
        });
    }

    [Fact]
    public void AnExplicitZoom_EndsTheFit()
    {
        RunSta(() =>
        {
            void Check(Action<PdfViewerControl> zoom)
            {
                using var ctrl = new PdfViewerControl { Size = new Size(900, 700) };
                ctrl.LoadBytes(TestPdf.OnePage());
                ctrl.FitWidth();
                Assert.Equal(PdfZoomMode.FitWidth, ctrl.ZoomMode);   // potency: the fit was on

                zoom(ctrl);
                double chosen = ctrl.Zoom;
                ctrl.Size = new Size(600, 700);

                Assert.Equal(PdfZoomMode.Custom, ctrl.ZoomMode);
                Assert.Equal(chosen, ctrl.Zoom, 6);
            }

            Check(c => c.ZoomIn());
            Check(c => c.ZoomOut());
            Check(c => c.ActualSize());
            Check(c => c.Zoom = 2.0);
            Check(c => Assert.True(c.SurfaceMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 100, 100, 120), Keys.Control)));
        });
    }

    [Fact]
    public void SettingZoomModeToCustom_KeepsTheFittedFactor()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(900, 700) };
            ctrl.LoadBytes(TestPdf.OnePage());
            ctrl.ZoomMode = PdfZoomMode.FitPage;
            double fitted = ctrl.Zoom;
            Assert.NotEqual(1.0, fitted, 6);                   // potency: the fit moved the zoom

            ctrl.ZoomMode = PdfZoomMode.Custom;
            ctrl.Size = new Size(600, 500);

            Assert.Equal(fitted, ctrl.Zoom, 6);
        });
    }

    [Fact]
    public void ZoomMode_RefusesAnUndefinedValue_AndAcceptsTheDefinedOnes()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            Assert.Throws<ArgumentOutOfRangeException>(() => ctrl.ZoomMode = (PdfZoomMode)3);
            Assert.Equal(PdfZoomMode.Custom, ctrl.ZoomMode);

            ctrl.ZoomMode = PdfZoomMode.FitPage;               // no document: recorded, nothing to fit
            Assert.Equal(PdfZoomMode.FitPage, ctrl.ZoomMode);
            Assert.Equal(1.0, ctrl.Zoom);
        });
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
