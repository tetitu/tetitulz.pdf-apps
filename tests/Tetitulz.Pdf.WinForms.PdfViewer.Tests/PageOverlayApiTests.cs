// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The host overlay API: page ↔ client mapping, the overlay paint hook, and page mouse events that
/// let a host take a gesture away from text selection.
/// </summary>
public sealed class PageOverlayApiTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void MapPointToPdf_InvertsMapRect_AtEveryRotation(int rotate)
    {
        // An offset crop box and a /UserUnit, so neither the origin nor the unit can be assumed.
        var geo = new PageGeometry(30f, 40f, 300f, 200f, rotate, 1.5f);
        float scale = PdfPixelMapper.ScaleForDpi(120);
        foreach (var (x, y) in new[] { (30f, 40f), (100f, 75f), (329f, 239f), (250f, 60f) })
        {
            RectangleF px = PdfPixelMapper.MapRect(geo, scale, x, y, 0f, 0f);
            PointF back = PdfPixelMapper.MapPointToPdf(geo, scale, px.X, px.Y);
            Assert.InRange(back.X, x - 0.01f, x + 0.01f);
            Assert.InRange(back.Y, y - 0.01f, y + 0.01f);
        }
    }

    [Fact]
    public void PageToClient_AndClientToPage_AgreeOnALoadedDocument()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(TestPdf.OnePage());

            Assert.Equal(new RectangleF(0, 0, 200, 200), viewer.GetPageBox(0));
            Assert.Equal(0, viewer.GetPageRotation(0));

            var stamp = new RectangleF(50, 120, 40, 30);
            Assert.True(viewer.TryPageToClient(0, stamp, out RectangleF client));
            Assert.True(client.Width > 0 && client.Height > 0);

            var centre = new Point((int)(client.X + client.Width / 2), (int)(client.Y + client.Height / 2));
            Assert.True(viewer.TryClientToPage(centre, out int page, out PointF pdf));
            Assert.Equal(0, page);
            Assert.InRange(pdf.X, 70 - 2, 70 + 2);    // the stamp's centre, within a pixel's worth of points
            Assert.InRange(pdf.Y, 135 - 2, 135 + 2);

            // Higher on the screen is higher on the page: y is flipped.
            Assert.True(viewer.TryClientToPage(0, new Point(centre.X, centre.Y - 20), out PointF above));
            Assert.True(above.Y > pdf.Y);
            // The top-left corner is the toolbar, over no page; and a page that is not laid out says so.
            Assert.False(viewer.TryClientToPage(new Point(1, 1), out int nowhere, out _));
            Assert.Equal(-1, nowhere);
            Assert.False(viewer.TryPageToClient(5, stamp, out _));
        });
    }

    [DesktopFact]
    public void PaintOverlay_DrawsWhereThePageRectangleMaps()
    {
        RunSta(() =>
        {
            // Shown (off-screen): a form never shown leaves its children invisible, and DrawToBitmap
            // then skips the scrolling surface the pages and the overlay are painted on.
            using var form = new Form
            {
                ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual,
                Location = new Point(-5000, -5000), ShowInTaskbar = false,
            };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            form.Controls.Add(viewer);
            form.Show();
            viewer.LoadBytes(TestPdf.OnePage());
            Application.DoEvents();

            var stamp = new RectangleF(50, 120, 40, 30);
            int calls = 0;
            viewer.PaintOverlay += (s, e) =>
            {
                calls++;
                using var red = new SolidBrush(Color.Red);
                e.Graphics.FillRectangle(red, e.ToCanvas(stamp));
            };

            // The viewer alone, so the bitmap's pixels are its client coordinates (a form's would include its frame).
            using var bmp = new Bitmap(viewer.Width, viewer.Height);
            viewer.DrawToBitmap(bmp, new Rectangle(Point.Empty, viewer.Size));
            Assert.True(calls > 0, "the overlay was never asked to paint");

            Assert.True(viewer.TryPageToClient(0, stamp, out RectangleF client));
            Color at = bmp.GetPixel((int)(client.X + client.Width / 2), (int)(client.Y + client.Height / 2));
            Assert.True(at.R > 200 && at.G < 60 && at.B < 60, $"expected the overlay's red at the stamp, got {at}");

            // Control: a point on the page outside the stamp is the page's white, not red.
            Assert.True(viewer.TryPageToClient(0, new RectangleF(150, 30, 1, 1), out RectangleF elsewhere));
            Color white = bmp.GetPixel((int)elsewhere.X, (int)elsewhere.Y);
            Assert.True(white.R > 200 && white.G > 200 && white.B > 200, $"expected the page's white, got {white}");
        });
    }

    [Fact]
    public void AHandledPress_TakesTheWholeGesture_AndStartsNoSelection()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(TestPdf.TwoLinesOfText());

            var seen = new System.Collections.Generic.List<string>();
            int downPage = -2;
            viewer.PageMouseDown += (s, e) => { seen.Add("down"); downPage = e.PageIndex; e.Handled = true; e.Cursor = Cursors.SizeAll; };
            viewer.PageMouseMove += (s, e) => seen.Add("move");
            viewer.PageMouseUp += (s, e) => seen.Add("up");

            Assert.True(viewer.TryPageToClient(0, new RectangleF(10, 10, 180, 180), out RectangleF page));
            Point a = SurfacePoint(viewer, new Point((int)page.X + 5, (int)page.Y + 5));
            Point b = SurfacePoint(viewer, new Point((int)page.Right - 5, (int)page.Bottom - 5));
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
            viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0));
            viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0));

            Assert.Equal(new[] { "down", "move", "up" }, seen);
            Assert.Equal(0, downPage);
            Assert.Equal(0, viewer.SelectedPageCount);   // the drag selected nothing: the host had it
        });
    }

    [Fact]
    public void AnUnhandledPress_LeavesSelectionToTheViewer()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(TestPdf.TwoLinesOfText());
            int downs = 0;
            viewer.PageMouseDown += (s, e) => downs++;   // listens, does not take

            Assert.True(viewer.TryPageToClient(0, new RectangleF(0, 0, 200, 200), out RectangleF page));
            Point a = SurfacePoint(viewer, new Point((int)page.X + 1, (int)page.Y + 1));
            Point b = SurfacePoint(viewer, new Point((int)page.Right - 1, (int)page.Bottom - 1));
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
            viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0));
            viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0));

            Assert.Equal(1, downs);
            Assert.Equal(1, viewer.SelectedPageCount);   // the whole-page drag selected the text
        });
    }

    /// <summary>A viewer client point in the coordinates of its inner scrolling surface, which the handlers take.</summary>
    private static Point SurfacePoint(PdfViewerControl viewer, Point client)
    {
        Control surface = FindSurface(viewer);
        return surface.PointToClient(viewer.PointToScreen(client));
    }

    private static Control FindSurface(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c.GetType().Name == "Surface") return c;
            Control? inner = FindSurfaceOrNull(c);
            if (inner != null) return inner;
        }
        throw new System.InvalidOperationException("no surface");
    }

    private static Control? FindSurfaceOrNull(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c.GetType().Name == "Surface") return c;
            Control? inner = FindSurfaceOrNull(c);
            if (inner != null) return inner;
        }
        return null;
    }

    private static void RunSta(System.Action action) => StaThread.Run(action);
}

/// <summary><see cref="PdfViewerControl.ShowOpenButton"/> hides the built-in Open button and shows it again.</summary>
public sealed class ShowOpenButtonTests
{
    [Fact]
    public void TheOpenButton_CanBeHidden_AndShownAgain()
    {
        System.Exception? captured = null;
        var t = new System.Threading.Thread(() =>
        {
            try
            {
                using var viewer = new PdfViewerControl();
                ToolStripItem open = FindOpen(viewer);
                Assert.True(viewer.ShowOpenButton);
                Assert.True(open.Available);
                viewer.ShowOpenButton = false;
                Assert.False(viewer.ShowOpenButton);
                Assert.False(open.Available);
                viewer.ShowOpenButton = true;
                Assert.True(open.Available);
            }
            catch (System.Exception e) { captured = e; }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join();
        if (captured != null) throw new System.Exception(captured.Message, captured);
    }

    /// <summary>The toolbar item that opens the file dialog: the first button of the built-in toolbar.</summary>
    private static ToolStripItem FindOpen(Control root)
    {
        // A StatusStrip is a ToolStrip too; the toolbar is the one that is not.
        foreach (Control c in root.Controls)
            if (c is ToolStrip strip && !(c is StatusStrip) && strip.Items.Count > 0) return strip.Items[0];
        foreach (Control c in root.Controls)
        {
            try { return FindOpen(c); } catch (System.InvalidOperationException) { }
        }
        throw new System.InvalidOperationException("no toolbar");
    }
}
