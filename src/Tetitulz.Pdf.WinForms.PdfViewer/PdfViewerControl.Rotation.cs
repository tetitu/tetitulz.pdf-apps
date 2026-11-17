// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Drawing;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The view turned a quarter at a time, every page alike, with the file unchanged: the layout,
    /// the overlays' mapping and the page's bitmap turn together, and printing prints the pages as
    /// the file has them.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        /// <summary>The view's turn clockwise: 0, 90, 180 or 270.</summary>
        private int _viewRotation;

        /// <summary>
        /// How far the view is turned clockwise, in degrees: 0, 90, 180 or 270. Any multiple of 90 is
        /// taken and reduced (−90 is 270); the file is not changed. Every page turns, the reader's
        /// page is kept, and a fit is fitted again. A document loads upright, at 0.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not a multiple of 90.</exception>
        [Category("PDF"), DefaultValue(0), Description("The view's clockwise turn in degrees (0, 90, 180, 270).")]
        public int ViewRotation
        {
            get => _viewRotation;
            set
            {
                if (value % 90 != 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "The view turns by multiples of 90 degrees.");
                int r = ((value % 360) + 360) % 360;
                if (r == _viewRotation) return;
                ApplyViewRotation(r);
            }
        }

        /// <summary>Turns the view a quarter clockwise (Ctrl+Shift+Plus).</summary>
        public void RotateClockwise() => ViewRotation = _viewRotation + 90;

        /// <summary>Turns the view a quarter counter-clockwise (Ctrl+Shift+Minus).</summary>
        public void RotateCounterclockwise() => ViewRotation = _viewRotation - 90;

        /// <summary>A page as the view shows it: the document's geometry turned by <see cref="ViewRotation"/>.</summary>
        private PageGeometry ViewGeometry(int pageIndex) => _doc!.Geometry(pageIndex).Turned(_viewRotation);

        private void ApplyViewRotation(int rotation)
        {
            PageAnchor? reading = _doc != null && _layout.Length > 0
                ? CaptureAnchor(new PointF(_surface.ClientSize.Width / 2f, 0f))
                : null;
            _viewRotation = rotation;
            if (_doc == null) return;

            // Every bitmap is of the old turn, and so is every render under way: none is a preview.
            ClearRenderCache();
            RebuildLayout();
            if (reading.HasValue) RestoreAnchor(reading.Value);
            ApplyZoomMode();
            _surface.Invalidate();
            UpdateCurrentPageFromScroll();
        }

        /// <summary>
        /// <paramref name="page"/>, rendered as the file has it, turned in place by
        /// <paramref name="rotation"/> — a quarter turn of a bitmap is exact.
        /// </summary>
        internal static Bitmap TurnBitmap(Bitmap page, int rotation)
        {
            switch (rotation)
            {
                case 90: page.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 180: page.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 270: page.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
            return page;
        }
    }
}
