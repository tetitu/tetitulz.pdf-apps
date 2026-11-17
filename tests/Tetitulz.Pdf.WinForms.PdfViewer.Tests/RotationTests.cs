// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The view turns a quarter at a time with the file unchanged: the page's bitmap and every mapping
/// of user space to the screen turn together, so the ink is where the mapping says the text is at
/// each turn, in the background render and the paint's own alike.
/// </summary>
public sealed class RotationTests
{
    /// <summary>"Hello World" in 24 pt Helvetica from (20, 150) on a 300 x 200 page.</summary>
    private static readonly RectangleF TextArea = new RectangleF(22, 152, 130, 14);

    /// <summary>A part of the same page with nothing drawn on it.</summary>
    private static readonly RectangleF BlankArea = new RectangleF(160, 20, 120, 60);

    [DesktopFact]
    public void AtEveryTurn_TheInkIsWhereTheMappingPutsTheText()
    {
        foreach (bool background in new[] { false, true })
            foreach (int rotation in new[] { 0, 90, 180, 270 })
                StaThread.Run(() =>
                {
                    using var form = ShownForm(out var viewer, background);
                    viewer.LoadBytes(TestPdf.OnePageWithText());
                    viewer.ViewRotation = rotation;
                    // The background render lands after the first paint asks for it.
                    using (Capture(viewer)) { }
                    Assert.True(viewer.DrainRenders());

                    using var shot = Capture(viewer);
                    Assert.True(viewer.TryPageToClient(0, TextArea, out RectangleF text));
                    Assert.True(viewer.TryPageToClient(0, BlankArea, out RectangleF blank));

                    string at = $"{rotation}°, background {background}";
                    Assert.True(DarkPixels(shot, text) > 40, $"no ink where the text maps at {at}");
                    Assert.True(DarkPixels(shot, blank) == 0, $"ink where nothing is drawn at {at}");
                });
    }

    [DesktopFact]
    public void AQuarterTurn_SwapsThePagesWidthAndHeight()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer, background: false);
            viewer.LoadBytes(TestPdf.OnePageWithText());            // 300 x 200 pt
            Assert.True(viewer.TryPageToClient(0, new RectangleF(0, 0, 300, 200), out RectangleF upright));
            Assert.True(upright.Width > upright.Height);             // potency: landscape upright

            viewer.RotateClockwise();

            Assert.Equal(90, viewer.ViewRotation);
            Assert.True(viewer.TryPageToClient(0, new RectangleF(0, 0, 300, 200), out RectangleF turned));
            Assert.Equal(upright.Width, turned.Height, 0);
            Assert.Equal(upright.Height, turned.Width, 0);
        });
    }

    [DesktopFact]
    public void ATurn_KeepsThePageBeingRead()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer, background: false);
            // Pages that change height when turned, so the pixel offset alone would land elsewhere.
            viewer.LoadBytes(TestPdf.WidePages(40));
            viewer.Zoom = 2;
            viewer.GoToPage(25);
            Assert.Equal(25, viewer.CurrentPage);                    // potency: the jump landed

            viewer.RotateClockwise();
            Application.DoEvents();
            Assert.Equal(25, viewer.CurrentPage);

            viewer.RotateCounterclockwise();
            viewer.RotateCounterclockwise();
            Application.DoEvents();
            Assert.Equal(270, viewer.ViewRotation);
            Assert.Equal(25, viewer.CurrentPage);
        });
    }

    [DesktopFact]
    public void AFit_IsFittedAgainToTheTurnedPage()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer, background: false);
            viewer.LoadBytes(TestPdf.OnePageWithText());             // 300 wide, 200 tall
            viewer.FitWidth();
            double upright = viewer.Zoom;

            viewer.RotateClockwise();                                // now 200 wide, and taller than the view

            Assert.Equal(PdfZoomMode.FitWidth, viewer.ZoomMode);
            // 1.5 times, less the vertical scroll bar the taller page brings.
            Assert.InRange(viewer.Zoom, upright * 1.35, upright * 1.5 * 1.01);
        });
    }

    [DesktopFact]
    public void AThumbnail_ShowsThePageAsTheFileHasIt_WhenTheViewTurns()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer, background: true);
            viewer.ShowSidebar = true;
            viewer.LoadBytes(TestPdf.OnePageWithText());             // 300 x 200: landscape
            viewer.RotateClockwise();
            Application.DoEvents();
            using (Capture(viewer)) { }                              // the strip's paint asks for its thumbnail
            Assert.True(viewer.DrainRenders());
            using (Capture(viewer)) { }
            Assert.True(viewer.DrainRenders());

            // Held, not rendered to answer: the worker's thumbnail, the one the view's turn could reach.
            var thumb = viewer.ThumbnailHeldAt(0);
            Assert.NotNull(thumb);
            Assert.True(thumb!.Width > thumb.Height, $"the thumbnail turned with the view ({thumb.Width} x {thumb.Height})");
        });
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-540, 180)]
    public void ViewRotation_TakesAnyMultipleOfNinety_Reduced(int asked, int expected)
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.ViewRotation = asked;
            Assert.Equal(expected, viewer.ViewRotation);
        });
    }

    [Fact]
    public void ViewRotation_RefusesAnAngleThatIsNoQuarterTurn_AndKeepsTheTurn()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { ViewRotation = 90 };
            Assert.Throws<ArgumentOutOfRangeException>(() => viewer.ViewRotation = 45);
            Assert.Equal(90, viewer.ViewRotation);
        });
    }

    [Fact]
    public void ADocumentLoads_Upright()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { RenderPagesInBackground = false };
            viewer.LoadBytes(TestPdf.OnePage());
            viewer.RotateClockwise();
            Assert.Equal(90, viewer.ViewRotation);                   // potency: the turn took

            viewer.LoadBytes(TestPdf.OnePageWithText());

            Assert.Equal(0, viewer.ViewRotation);
        });
    }

    [Theory]
    [InlineData(Keys.Control | Keys.Shift | Keys.Oemplus, "RotateClockwise")]
    [InlineData(Keys.Control | Keys.Shift | Keys.Add, "RotateClockwise")]
    [InlineData(Keys.Control | Keys.Shift | Keys.OemMinus, "RotateCounterclockwise")]
    [InlineData(Keys.Control | Keys.Shift | Keys.Subtract, "RotateCounterclockwise")]
    [InlineData(Keys.Control | Keys.Oemplus, "ZoomIn")]
    public void TheRotateKeys_AreAcrobats_AndLeaveTheZoomKeysAlone(Keys key, string expected)
    {
        Assert.Equal(expected, PdfViewerControl.ClassifyCmdKey(key, false, false, documentLoaded: true).ToString());
        if (expected.StartsWith("Rotate", StringComparison.Ordinal))
            Assert.Equal("None", PdfViewerControl.ClassifyCmdKey(key, false, false, documentLoaded: false).ToString());
    }

    [Fact]
    public void TheRotateKey_GoesThroughCommandExecuting_WhichMayHandleIt()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { RenderPagesInBackground = false };
            viewer.LoadBytes(TestPdf.OnePage());
            PdfViewerCommand? seen = null;
            bool cancel = true;
            viewer.CommandExecuting += (s, e) => { seen = e.Command; e.Handled = cancel; };

            viewer.ExecuteCmdKey(PdfViewerControl.CmdKeyAction.RotateClockwise);
            Assert.Equal(PdfViewerCommand.RotateClockwise, seen);
            Assert.Equal(0, viewer.ViewRotation);

            cancel = false;
            viewer.ExecuteCmdKey(PdfViewerControl.CmdKeyAction.RotateCounterclockwise);
            Assert.Equal(PdfViewerCommand.RotateCounterclockwise, seen);
            Assert.Equal(270, viewer.ViewRotation);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePageMenu_OffersBothTurns_InEitherLanguage(bool hebrew)
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl();
            if (hebrew) viewer.Strings = PdfViewerStrings.Hebrew();
            var strings = hebrew ? PdfViewerStrings.Hebrew() : new PdfViewerStrings();
            Assert.NotEqual(new PdfViewerStrings().RotateClockwiseMenu, PdfViewerStrings.Hebrew().RotateClockwiseMenu);

            bool clockwise = false, counter = false;
            foreach (ToolStripItem item in viewer.PageMenu.Items)
            {
                clockwise |= item.Text == strings.RotateClockwiseMenu;
                counter |= item.Text == strings.RotateCounterclockwiseMenu;
            }
            Assert.True(clockwise && counter);
        });
    }

    private static int DarkPixels(Bitmap shot, RectangleF area)
    {
        var r = Rectangle.Intersect(Rectangle.Round(area), new Rectangle(Point.Empty, shot.Size));
        int n = 0;
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
                if (shot.GetPixel(x, y).GetBrightness() < 0.5f) n++;
        return n;
    }

    private static Bitmap Capture(PdfViewerControl viewer)
    {
        var bmp = new Bitmap(viewer.Width, viewer.Height);
        viewer.DrawToBitmap(bmp, new Rectangle(Point.Empty, viewer.Size));
        return bmp;
    }

    private static Form ShownForm(out PdfViewerControl viewer, bool background)
    {
        var form = new Form
        {
            ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000), ShowInTaskbar = false,
        };
        viewer = new PdfViewerControl
        {
            Dock = DockStyle.Fill, LayoutMode = PdfViewLayoutMode.Continuous, DeviceDpiOverride = 96,
            RenderPagesInBackground = background,
        };
        form.Controls.Add(viewer);
        form.Show();
        Application.DoEvents();
        return form;
    }
}
