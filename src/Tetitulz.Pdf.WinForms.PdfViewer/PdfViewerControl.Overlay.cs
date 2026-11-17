// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// A host's own drawing and pointer handling on top of the pages: stamps, signature placeholders,
    /// markers. Positions are in PDF user space (points, origin bottom-left, the page's own
    /// coordinates), so what the host places is what it later writes into the file.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        /// <summary>
        /// Raised for each visible page after the page and the viewer's own highlights are drawn, so a
        /// host can draw on top of it. <see cref="PdfViewerOverlayPaintEventArgs.ToCanvas(RectangleF)"/>
        /// maps a page rectangle into the graphics' coordinates.
        /// </summary>
        public event EventHandler<PdfViewerOverlayPaintEventArgs>? PaintOverlay;

        /// <summary>
        /// A mouse button pressed over the page area. Set <see cref="PdfViewerPageMouseEventArgs.Handled"/>
        /// to take the gesture: the viewer then starts no text selection and follows no link, and the
        /// moves and the release that follow are raised to the host the same way.
        /// </summary>
        public event EventHandler<PdfViewerPageMouseEventArgs>? PageMouseDown;

        /// <summary>The mouse moved over the page area. A handled move suppresses the viewer's hover and selection.</summary>
        public event EventHandler<PdfViewerPageMouseEventArgs>? PageMouseMove;

        /// <summary>A mouse button released over the page area.</summary>
        public event EventHandler<PdfViewerPageMouseEventArgs>? PageMouseUp;

        /// <summary>Whether a host took the current press, so its moves and release go to the host alone.</summary>
        private bool _hostGesture;

        /// <summary>
        /// Drops are accepted on the pages as well as on the control: the scrolling surface that covers
        /// the control's middle is what the user drops onto, so the setting travels to it and its drag
        /// events are raised as the control's own (<see cref="Control.DragEnter"/>, <see cref="Control.DragOver"/>,
        /// <see cref="Control.DragDrop"/>, <see cref="Control.DragLeave"/>).
        /// </summary>
        public override bool AllowDrop
        {
            get => base.AllowDrop;
            set
            {
                base.AllowDrop = value;
                if (_surface != null) _surface.AllowDrop = value;
            }
        }

        /// <summary>Forwards the surface's drag events as the control's; called once the surface exists.</summary>
        private void WireSurfaceDragDrop()
        {
            _surface.AllowDrop = base.AllowDrop;
            _surface.DragEnter += (s, e) => OnDragEnter(e);
            _surface.DragOver += (s, e) => OnDragOver(e);
            _surface.DragDrop += (s, e) => OnDragDrop(e);
            _surface.DragLeave += (s, e) => OnDragLeave(e);
        }

        /// <summary>The page box the page is drawn from (the crop box), in user space.</summary>
        /// <exception cref="InvalidOperationException">No document is loaded.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is not a page.</exception>
        public RectangleF GetPageBox(int pageIndex)
        {
            var geo = GeometryOrThrow(pageIndex);
            return new RectangleF(geo.CropX0, geo.CropY0, geo.WidthPts, geo.HeightPts);
        }

        /// <summary>
        /// The clockwise turn page <paramref name="pageIndex"/> is displayed with, 0, 90, 180 or 270:
        /// its <c>/Rotate</c> and <see cref="ViewRotation"/> together, the turn <see cref="TryPageToClient(int, RectangleF, out RectangleF)"/>
        /// and the overlay's <see cref="PdfViewerOverlayPaintEventArgs.ToCanvas"/> map by.
        /// </summary>
        public int GetPageRotation(int pageIndex) => GeometryOrThrow(pageIndex).Rotate;

        /// <summary>
        /// The page under <paramref name="clientPoint"/> (this control's client coordinates) and the
        /// point in that page's user space. False when the point is over no page.
        /// </summary>
        public bool TryClientToPage(Point clientPoint, out int pageIndex, out PointF pagePoint)
        {
            pagePoint = PointF.Empty;
            pageIndex = -1;
            if (_doc == null) return false;
            Point v = ToVirtual(ToSurfaceClient(clientPoint));
            int page = PageAtVirtual(v);
            if (page < 0) return false;
            pageIndex = page;
            return TryClientToPage(page, clientPoint, out pagePoint);
        }

        /// <summary>
        /// <paramref name="clientPoint"/> in page <paramref name="pageIndex"/>'s user space, whether or
        /// not the point is over that page — what a drag that leaves the page needs. False when the page
        /// is not laid out.
        /// </summary>
        public bool TryClientToPage(int pageIndex, Point clientPoint, out PointF pagePoint)
        {
            pagePoint = PointF.Empty;
            if (_doc == null || !TryGetBounds(pageIndex, out Rectangle bounds)) return false;
            Point v = ToVirtual(ToSurfaceClient(clientPoint));
            pagePoint = PdfPixelMapper.MapPointToPdf(ViewGeometry(pageIndex), RenderScale, v.X - bounds.X, v.Y - bounds.Y);
            return true;
        }

        /// <summary>
        /// A user-space rectangle of page <paramref name="pageIndex"/> in this control's client
        /// coordinates (the axis-aligned box, for a rotated page). False when the page is not laid out.
        /// </summary>
        public bool TryPageToClient(int pageIndex, RectangleF pageRect, out RectangleF clientRect)
        {
            clientRect = RectangleF.Empty;
            if (_doc == null || !TryGetBounds(pageIndex, out Rectangle bounds)) return false;
            RectangleF canvas = MapPdfRectToCanvas(pageIndex, bounds, pageRect.X, pageRect.Y, pageRect.Width, pageRect.Height);
            Point origin = FromSurfaceClient(new Point(_surface.AutoScrollPosition.X, _surface.AutoScrollPosition.Y));
            clientRect = new RectangleF(canvas.X + origin.X, canvas.Y + origin.Y, canvas.Width, canvas.Height);
            return true;
        }

        /// <summary>Repaints the pages, so an overlay that changed is drawn again.</summary>
        public void InvalidatePages() => _surface.Invalidate();

        // ── Hooks the paint and input code call ───────────────────────────────────

        private void RaisePaintOverlay(Graphics g, int pageIndex, Rectangle pageBounds)
        {
            var handler = PaintOverlay;
            if (handler == null || _doc == null) return;
            handler(this, new PdfViewerOverlayPaintEventArgs(g, pageIndex, pageBounds, ViewGeometry(pageIndex).Rotate,
                r => MapPdfRectToCanvas(pageIndex, pageBounds, r.X, r.Y, r.Width, r.Height)));
        }

        /// <summary>Raises a page mouse event; true when the host handled it.</summary>
        private bool RaisePageMouse(EventHandler<PdfViewerPageMouseEventArgs>? handler, MouseEventArgs e)
        {
            if (handler == null || _doc == null) return false;
            Point client = FromSurfaceClient(e.Location);
            int page = -1;
            PointF pagePoint = PointF.Empty;
            Point v = ToVirtual(e.Location);
            int under = PageAtVirtual(v);
            if (under >= 0 && TryGetBounds(under, out Rectangle bounds))
            {
                page = under;
                pagePoint = PdfPixelMapper.MapPointToPdf(ViewGeometry(under), RenderScale, v.X - bounds.X, v.Y - bounds.Y);
            }
            var args = new PdfViewerPageMouseEventArgs(page, pagePoint, client, e.Button, ModifierKeys);
            handler(this, args);
            if (args.Cursor != null) _surface.Cursor = args.Cursor;
            return args.Handled;
        }

        private PageGeometry GeometryOrThrow(int pageIndex)
        {
            if (_doc == null) throw new InvalidOperationException("No document is loaded.");
            if (pageIndex < 0 || pageIndex >= _pageCount) throw new ArgumentOutOfRangeException(nameof(pageIndex));
            return ViewGeometry(pageIndex);
        }

        private Point ToSurfaceClient(Point controlClient) => _surface.PointToClient(PointToScreen(controlClient));

        private Point FromSurfaceClient(Point surfaceClient) => PointToClient(_surface.PointToScreen(surfaceClient));
    }

    /// <summary>One visible page, for a host drawing on top of it.</summary>
    public sealed class PdfViewerOverlayPaintEventArgs : EventArgs
    {
        private readonly Func<RectangleF, RectangleF> _toCanvas;

        internal PdfViewerOverlayPaintEventArgs(Graphics graphics, int pageIndex, Rectangle pageBounds, int rotation,
            Func<RectangleF, RectangleF> toCanvas)
        {
            Graphics = graphics;
            PageIndex = pageIndex;
            PageBounds = pageBounds;
            Rotation = rotation;
            _toCanvas = toCanvas;
        }

        /// <summary>The graphics the page was drawn with; draw in its coordinates.</summary>
        public Graphics Graphics { get; }

        /// <summary>The zero-based page.</summary>
        public int PageIndex { get; }

        /// <summary>The page's rectangle in <see cref="Graphics"/>' coordinates.</summary>
        public Rectangle PageBounds { get; }

        /// <summary>
        /// The clockwise turn the page is displayed with (0, 90, 180 or 270): its <c>/Rotate</c> and the
        /// viewer's <c>ViewRotation</c> together — the turn <see cref="ToCanvas"/> maps by.
        /// </summary>
        public int Rotation { get; }

        /// <summary>A user-space rectangle of this page in <see cref="Graphics"/>' coordinates.</summary>
        public RectangleF ToCanvas(RectangleF pageRect) => _toCanvas(pageRect);
    }

    /// <summary>A mouse event over the viewer's page area.</summary>
    public sealed class PdfViewerPageMouseEventArgs : EventArgs
    {
        internal PdfViewerPageMouseEventArgs(int pageIndex, PointF pagePoint, Point location, MouseButtons button, Keys modifiers)
        {
            PageIndex = pageIndex;
            PagePoint = pagePoint;
            Location = location;
            Button = button;
            Modifiers = modifiers;
        }

        /// <summary>The page under the pointer, or -1 between pages.</summary>
        public int PageIndex { get; }

        /// <summary>The pointer in <see cref="PageIndex"/>'s user space (meaningless when it is -1).</summary>
        public PointF PagePoint { get; }

        /// <summary>The pointer in the viewer control's client coordinates.</summary>
        public Point Location { get; }

        /// <summary>The button pressed or released (none for a plain move).</summary>
        public MouseButtons Button { get; }

        /// <summary>The modifier keys held.</summary>
        public Keys Modifiers { get; }

        /// <summary>Set to take the event from the viewer: no selection, no link, no hover.</summary>
        public bool Handled { get; set; }

        /// <summary>A cursor for the page area to show, or null to leave it.</summary>
        public Cursor? Cursor { get; set; }
    }
}
