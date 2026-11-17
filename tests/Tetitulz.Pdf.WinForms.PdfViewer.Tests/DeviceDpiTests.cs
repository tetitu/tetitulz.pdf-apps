// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Pages render at the monitor's resolution: on a 200% display (192 dpi) 100% zoom renders at
/// 192 dpi and lays the page out at twice the pixels, the zoom box still reading 100%; a fit fits
/// the same view at half the zoom; switched off, the 96-dpi rendering comes back.
/// </summary>
/// <remarks>
/// The monitor's dpi is put in through <c>DeviceDpiOverride</c>, so the answers do not depend on
/// the display the tests run on.
/// </remarks>
public sealed class DeviceDpiTests
{
    [Fact]
    public void AtTwiceTheReferenceDpi_APageRendersAndLaysOutAtTwiceThePixels()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { DeviceDpiOverride = 96 };
            viewer.LoadBytes(TestPdf.ManyPages(1));                      // a 100 x 100 pt page
            viewer.RefreshDeviceScale();
            Assert.Equal(96, viewer.CurrentRenderDpi);                   // potency: the baseline
            int before = ((ScrollableControl)viewer.PageSurface).AutoScrollMinSize.Height;

            viewer.DeviceDpiOverride = 192;
            viewer.RefreshDeviceScale();

            Assert.Equal(2.0, viewer.DeviceScale, 6);
            Assert.Equal(192, viewer.CurrentRenderDpi);
            Assert.Equal(1.0, viewer.Zoom, 6);                           // the zoom box still reads 100%
            int after = ((ScrollableControl)viewer.PageSurface).AutoScrollMinSize.Height;
            // The page grows by its own height (134 px at 96 dpi); the margins do not scale.
            Assert.InRange(after - before, 132, 136);
        });
    }

    [Fact]
    public void SwitchedOff_PagesRenderAtTheReferenceDpiOnEveryMonitor()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { DeviceDpiOverride = 192 };
            viewer.LoadBytes(TestPdf.ManyPages(1));
            viewer.RefreshDeviceScale();
            Assert.Equal(192, viewer.CurrentRenderDpi);                  // potency: scaling is on

            viewer.ScaleToDeviceDpi = false;

            Assert.Equal(1.0, viewer.DeviceScale, 6);
            Assert.Equal(96, viewer.CurrentRenderDpi);

            viewer.ScaleToDeviceDpi = true;
            Assert.Equal(192, viewer.CurrentRenderDpi);
        });
    }

    [Fact]
    public void ScaleToDeviceDpi_IsOnByDefault()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl();
            Assert.True(viewer.ScaleToDeviceDpi);
        });
    }

    [DesktopFact]
    public void AFit_OnATwiceScaledMonitor_IsHalfTheZoomForTheSameView()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer, dpi: 96);
            viewer.LoadBytes(TestPdf.ManyPages(3));
            viewer.FitWidth();
            double at96 = viewer.Zoom;
            Assert.True(at96 > 2, $"the fit did not enlarge the page (zoom {at96})");  // potency

            viewer.DeviceDpiOverride = 192;
            viewer.RefreshDeviceScale();

            Assert.Equal(PdfZoomMode.FitWidth, viewer.ZoomMode);
            Assert.InRange(viewer.Zoom, at96 / 2 * 0.98, at96 / 2 * 1.02);
            Assert.InRange(viewer.CurrentRenderDpi, Math.Round(96 * at96) - 2, Math.Round(96 * at96) + 2);

            // Back to 96: the page shrinks, no scroll bar comes or goes, so no resize refits it —
            // the scale change itself must.
            viewer.DeviceDpiOverride = 96;
            viewer.RefreshDeviceScale();

            Assert.InRange(viewer.Zoom, at96 * 0.99, at96 * 1.01);
        });
    }

    [DesktopFact]
    public void AMonitorChange_KeepsThePageBeingRead()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer, dpi: 96);
            viewer.LoadBytes(TestPdf.ManyPages(40));
            viewer.Zoom = 3;
            viewer.GoToPage(25);
            Assert.Equal(25, viewer.CurrentPage);                        // potency: the jump landed

            viewer.DeviceDpiOverride = 192;
            viewer.RefreshDeviceScale();
            Application.DoEvents();

            Assert.Equal(192 * 3, viewer.CurrentRenderDpi);
            Assert.Equal(25, viewer.CurrentPage);

            viewer.DeviceDpiOverride = 120;
            viewer.RefreshDeviceScale();
            Application.DoEvents();

            Assert.Equal(25, viewer.CurrentPage);
        });
    }

    private static Form ShownForm(out PdfViewerControl viewer, int dpi)
    {
        var form = new Form
        {
            ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual,
            Location = new Point(-2000, -2000), ShowInTaskbar = false,
        };
        viewer = new PdfViewerControl
        {
            Dock = DockStyle.Fill, LayoutMode = PdfViewLayoutMode.Continuous, DeviceDpiOverride = dpi,
        };
        form.Controls.Add(viewer);
        form.Show();
        Application.DoEvents();
        return form;
    }
}
