// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations.Extraction;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Links beyond "open a URL, go to a page": a destination's position, the named page actions, a link
/// to another file (reported, never opened) and the hover tooltip.
/// </summary>
public sealed class LinkNavigationTests
{
    // ── GoToPage(index, top) ──────────────────────────────────────────────────

    /// <summary>
    /// A top lower on the page scrolls further down by exactly the distance at the zoom — 100 pt
    /// at 100% is 100 × 96 / 72 px — and the page's own top scrolls to where the page starts.
    /// </summary>
    [Fact]
    public void GoToPageWithATop_ScrollsThatHeightToTheTop()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(900, 700) };
            ctrl.LoadBytes(TestPdf.ManyPages(3));               // 100 × 100 pt pages

            ctrl.GoToPage(1, 100f);
            int pageTop = ctrl.LastDestinationScroll;
            ctrl.GoToPage(1, 25f);
            int lower = ctrl.LastDestinationScroll;

            Assert.Equal(1, ctrl.CurrentPage);
            Assert.InRange(lower - pageTop, 99, 101);            // 75 pt × 4/3

            ctrl.GoToPage(1, 9999f);                             // above the page: its top edge
            Assert.Equal(pageTop, ctrl.LastDestinationScroll);
        });
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void GoToPageWithATop_RefusesANonFiniteTop(float top)
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => ctrl.GoToPage(1, top));
            Assert.Equal(0, ctrl.CurrentPage);
        });
    }

    // ── Following a link ──────────────────────────────────────────────────────

    [Fact]
    public void AnXyzLink_GoesToItsPage_AtItsTop()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.TwoPagesWithLink("/Dest [4 0 R /XYZ 0 120 null]"));
            using (form)
            {
                PdfViewerLinkClickedEventArgs? seen = null;
                viewer.LinkClicked += (s, e) => seen = e;

                ClickTheLink(viewer);

                Assert.NotNull(seen);
                Assert.Equal(PdfLinkKind.GoTo, seen!.Kind);
                Assert.Equal(1, seen.TargetPageIndex);
                Assert.Equal(120f, seen.TargetTop);
                Assert.Equal(1, viewer.CurrentPage);

                // The same scroll GoToPage(1, 120) asks for.
                int followed = viewer.LastDestinationScroll;
                viewer.GoToPage(0);
                viewer.GoToPage(1, 120f);
                Assert.Equal(viewer.LastDestinationScroll, followed);
                Assert.True(followed > 0);
            }
        });
    }

    /// <summary>
    /// Each action starts on the page it must move away from: FirstPage from the second page, the
    /// others from the first — otherwise doing nothing passes. The control is not shown, so the
    /// scroll stays put and the link on page 0 stays under the pointer whatever the current page.
    /// </summary>
    [Theory]
    [InlineData("NextPage", 0, 1)]
    [InlineData("LastPage", 0, 1)]
    [InlineData("FirstPage", 1, 0)]
    public void ANamedLink_RunsItsPageAction(string name, int startPage, int expectedPage)
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.TwoPagesWithLink($"/A << /S /Named /N /{name} >>"));
            using (form)
            {
                viewer.GoToPage(startPage);
                Assert.Equal(startPage, viewer.CurrentPage);
                ClickTheLink(viewer);
                Assert.Equal(expectedPage, viewer.CurrentPage);
            }
        });
    }

    /// <summary>A name the viewer does not perform is said in the status line, and nothing moves.</summary>
    [Fact]
    public void AnUnknownNamedAction_IsReported()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.TwoPagesWithLink("/A << /S /Named /N /GoBack >>"));
            using (form)
            {
                ClickTheLink(viewer);
                Assert.Equal(0, viewer.CurrentPage);
                Assert.Contains("GoBack", viewer.StatusLabel.Text, StringComparison.Ordinal);
            }
        });
    }

    /// <summary>
    /// A link to a program is never opened by the viewer — it is reported, and the host gets the file
    /// to decide with. The status line naming the file is the evidence nothing else happened silently.
    /// </summary>
    [Fact]
    public void ALaunchLink_IsReported_AndHandedToTheHost_NeverOpened()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.TwoPagesWithLink("/A << /S /Launch /F (calc.exe) >>"));
            using (form)
            {
                string? file = null;
                viewer.LinkClicked += (s, e) => file = e.File;

                ClickTheLink(viewer);

                Assert.Equal("calc.exe", file);
                Assert.Contains("calc.exe", viewer.StatusLabel.Text, StringComparison.Ordinal);
                Assert.Equal(0, viewer.CurrentPage);
            }
        });
    }

    [Fact]
    public void AHandledLink_RunsNoBuiltInAction()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.TwoPagesWithLink("/A << /S /Named /N /NextPage >>"));
            using (form)
            {
                viewer.LinkClicked += (s, e) => e.Handled = true;
                ClickTheLink(viewer);
                Assert.Equal(0, viewer.CurrentPage);
            }
        });
    }

    /// <summary>
    /// A link whose action the viewer does not perform — JavaScript here — is handed to the host on a
    /// click as <see cref="PdfLinkKind.Unknown"/>, and the viewer runs nothing for it; a hover shows no
    /// hand for it, since there is nothing the viewer would do. Before this, such a link raised nothing.
    /// </summary>
    [Fact]
    public void ClickingAJavaScriptLink_RaisesLinkClickedAsUnknown_AndRunsNothing()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.TwoPagesWithLink("/A << /S /JavaScript /JS (app.alert\\(1\\)) >>"));
            using (form)
            {
                var seen = new List<PdfViewerLinkClickedEventArgs>();
                viewer.LinkClicked += (s, e) => seen.Add(e);

                Hover(viewer, onLink: true);
                Assert.NotEqual(Cursors.Hand, viewer.PageSurface.Cursor);

                ClickTheLink(viewer);

                var e = Assert.Single(seen);
                Assert.Equal(PdfLinkKind.Unknown, e.Kind);
                Assert.Null(e.Uri);
                Assert.Equal(0, viewer.CurrentPage);
            }
        });
    }

    // ── The tooltip ───────────────────────────────────────────────────────────

    [Fact]
    public void HoveringALink_ShowsItsTarget_UnlessTooltipsAreOff()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.TwoPagesWithLink("/A << /S /URI /URI (https://example.com/a) >>"));
            using (form)
            {
                Hover(viewer, onLink: true);
                Assert.Equal("https://example.com/a", viewer.LinkToolTipText);

                Hover(viewer, onLink: false);
                Assert.Equal("", viewer.LinkToolTipText);

                viewer.ShowLinkToolTips = false;
                Hover(viewer, onLink: true);
                Assert.Equal("", viewer.LinkToolTipText);
            }
        });
    }

    [Fact]
    public void ALinkToAPage_IsDescribedByItsOneBasedNumber()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.TwoPagesWithLink("/Dest [4 0 R /Fit]"));
            using (form)
            {
                Hover(viewer, onLink: true);
                Assert.Equal("Go to page 2", viewer.LinkToolTipText);

                viewer.Strings = PdfViewerStrings.Hebrew();
                Hover(viewer, onLink: false);
                Hover(viewer, onLink: true);
                Assert.Equal("מעבר לעמוד 2", viewer.LinkToolTipText);
            }
        });
    }

    // ----------------------------------------------------------------

    private static (Form, PdfViewerControl) Shown(byte[] pdf)
    {
        var form = new Form { ClientSize = new Size(800, 700) };
        var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
        form.Controls.Add(viewer);
        form.CreateControl();
        viewer.LoadBytes(pdf);
        return (form, viewer);
    }

    /// <summary>The surface point over the middle of the link (rect [10 20 110 60] on page 0), or off it.</summary>
    private static Point LinkPoint(PdfViewerControl viewer, bool onLink)
    {
        var at = onLink ? new RectangleF(60, 40, 1, 1) : new RectangleF(150, 150, 1, 1);
        Assert.True(viewer.TryPageToClient(0, at, out RectangleF client));
        return viewer.PageSurface.PointToClient(viewer.PointToScreen(new Point((int)client.X, (int)client.Y)));
    }

    private static void ClickTheLink(PdfViewerControl viewer)
    {
        Point p = LinkPoint(viewer, onLink: true);
        viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
    }

    private static void Hover(PdfViewerControl viewer, bool onLink)
    {
        Point p = LinkPoint(viewer, onLink);
        viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0));
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
