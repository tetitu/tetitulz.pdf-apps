// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Two pages side by side (<see cref="PageSpread"/>), in either layout: continuous, the
    /// document's <c>TwoColumnLeft</c> and <c>TwoColumnRight</c>; one spread at a time, its
    /// <c>TwoPageLeft</c> and <c>TwoPageRight</c>. The pages of a row meet at a spine down the
    /// middle of the canvas, the earlier on the left — on the right when the control is right to left.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        private PdfPageSpread _pageSpread = PdfPageSpread.None;

        /// <summary>
        /// Whether pages are shown one to a row (the default) or two side by side, the first page on
        /// its own as a book's cover under <see cref="PdfPageSpread.FacingWithCover"/>. Works in
        /// both layouts: continuous shows every row, single page one row at a time, and Next and
        /// Previous step a row. The reader's page is kept; a fit is fitted to the row.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not a defined spread.</exception>
        [Category("PDF"), DefaultValue(PdfPageSpread.None),
         Description("One page to a row, or two side by side (optionally with the cover alone).")]
        public PdfPageSpread PageSpread
        {
            get => _pageSpread;
            set
            {
                if (value != PdfPageSpread.None && value != PdfPageSpread.Facing && value != PdfPageSpread.FacingWithCover)
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown page spread.");
                if (_pageSpread == value) return;
                _pageSpread = value;
                RelayOutKeepingPage();
            }
        }

        /// <summary>A layout and a spread together, laid out once — the document's own <c>/PageLayout</c>.</summary>
        private void ShowLayout(PdfViewLayoutMode mode, PdfPageSpread spread)
        {
            bool spreadChanged = _pageSpread != spread;
            _pageSpread = spread;
            if (_layoutMode != mode) LayoutMode = mode;
            else if (spreadChanged) RelayOutKeepingPage();
        }

        /// <summary>Lays the pages out again after a change of spread or of direction, the current page kept in view.</summary>
        private void RelayOutKeepingPage()
        {
            if (_doc == null) return;
            int page = _currentPage;
            ApplyZoomMode();
            RebuildLayout();
            GoToPage(page, forceScroll: true);
            UpdateCommandStates();
            _surface.Invalidate();
        }

        /// <summary>The row page <paramref name="pageIndex"/> is in: 0-based, one page a row without a spread.</summary>
        internal int RowOf(int pageIndex)
        {
            switch (_pageSpread)
            {
                case PdfPageSpread.Facing: return pageIndex / 2;
                case PdfPageSpread.FacingWithCover: return (pageIndex + 1) / 2;
                default: return pageIndex;
            }
        }

        /// <summary>The first page of row <paramref name="row"/>.</summary>
        private int FirstPageOfRow(int row)
        {
            switch (_pageSpread)
            {
                case PdfPageSpread.Facing: return row * 2;
                case PdfPageSpread.FacingWithCover: return row == 0 ? 0 : row * 2 - 1;
                default: return row;
            }
        }

        /// <summary>The pages of row <paramref name="row"/>, in reading order: one or two.</summary>
        private int[] PagesOfRow(int row)
        {
            int first = FirstPageOfRow(row);
            bool pair = _pageSpread != PdfPageSpread.None
                        && !(_pageSpread == PdfPageSpread.FacingWithCover && row == 0)
                        && first + 1 < _pageCount;
            return pair ? new[] { first, first + 1 } : new[] { first };
        }

        private int RowCount => _pageCount == 0 ? 0 : RowOf(_pageCount - 1) + 1;

        /// <summary>The page Next (<paramref name="step"/> +1) or Previous (−1) goes to: the next row's first page.</summary>
        private int PageAfterStep(int step)
        {
            if (_pageSpread == PdfPageSpread.None) return _currentPage + step;
            int row = Clamp(RowOf(_currentPage) + step, 0, Math.Max(0, RowCount - 1));
            return FirstPageOfRow(row);
        }

        /// <summary>Whether Next (+1) or Previous (−1) has a row to go to.</summary>
        private bool CanStep(int step)
        {
            if (_doc == null || _pageCount == 0) return false;
            int row = RowOf(_currentPage) + step;
            return row >= 0 && row < RowCount;
        }

        /// <summary>Whether a lone page of a row sits on the right of the spine (a cover) or the left (a last page).</summary>
        private bool LoneOnTheRight(int row) => _pageSpread == PdfPageSpread.FacingWithCover && row == 0;

        private bool SpineMirrored => RightToLeft == RightToLeft.Yes;

        /// <summary>
        /// What a fit fits, in points, over rows <paramref name="firstRow"/> to <paramref name="lastRow"/>:
        /// the widest page (a slot, either side of the spine) and the tallest.
        /// </summary>
        private void SpreadExtentPts(int firstRow, int lastRow, out float slotWPts, out float rowHPts)
        {
            slotWPts = 1f; rowHPts = 1f;
            for (int row = firstRow; row <= lastRow; row++)
                foreach (int p in PagesOfRow(row))
                {
                    var geo = ViewGeometry(p);
                    slotWPts = Math.Max(slotWPts, geo.DisplayWidthPts);
                    rowHPts = Math.Max(rowHPts, geo.DisplayHeightPts);
                }
        }

        /// <summary>
        /// Lays out <paramref name="firstRow"/> to <paramref name="lastRow"/> two pages to a row
        /// against a spine at the middle of the canvas; every page is in the list in page order.
        /// </summary>
        private void BuildSpreadLayout(float scale, int viewportW, int viewportH, int firstRow, int lastRow)
        {
            int rows = lastRow - firstRow + 1;
            var sizes = new Dictionary<int, Size>();
            int slotW = 1;
            for (int row = firstRow; row <= lastRow; row++)
                foreach (int p in PagesOfRow(row))
                {
                    var geo = ViewGeometry(p);
                    var s = new Size(Math.Max(1, SaturatingCast.ToInt(Math.Round(geo.DisplayWidthPts * scale))),
                                     Math.Max(1, SaturatingCast.ToInt(Math.Round(geo.DisplayHeightPts * scale))));
                    sizes[p] = s;
                    slotW = Math.Max(slotW, s.Width);
                }

            int canvasW = Math.Max(2 * slotW + Gap + 2 * PageMargin, viewportW);
            int spine = canvasW / 2;
            var list = new List<PageLayoutInfo>();
            int y = PageMargin;
            for (int row = firstRow; row <= lastRow; row++)
            {
                int[] pages = PagesOfRow(row);
                int rowH = 0;
                foreach (int p in pages) rowH = Math.Max(rowH, sizes[p].Height);
                for (int k = 0; k < pages.Length; k++)
                {
                    int p = pages[k];
                    bool right = pages.Length == 2 ? k == 1 : LoneOnTheRight(row);
                    if (SpineMirrored) right = !right;
                    Size s = sizes[p];
                    int x = right ? spine + Gap / 2 : spine - Gap / 2 - s.Width;
                    list.Add(new PageLayoutInfo
                    {
                        PageIndex = p,
                        Bounds = new Rectangle(x, y + (rowH - s.Height) / 2, s.Width, s.Height),
                    });
                }
                y += rowH + Gap;
            }

            int contentH = y - Gap + PageMargin;
            int canvasH = Math.Max(contentH, viewportH);
            if (rows == 1 && canvasH > contentH)
            {
                // One spread at a time sits in the middle of the view, as one page does.
                int shift = (canvasH - contentH) / 2;
                for (int i = 0; i < list.Count; i++)
                {
                    var l = list[i];
                    l.Bounds = new Rectangle(l.Bounds.X, l.Bounds.Y + shift, l.Bounds.Width, l.Bounds.Height);
                    list[i] = l;
                }
            }
            _layout = list.ToArray();
            _surface.AutoScrollMinSize = new Size(canvasW, canvasH);
        }
    }
}
