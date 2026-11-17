// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Reading conveniences every desktop reader has: the keyboard scrolls the page, the wheel turns
    /// the page in single-page layout, Back and Forward return across jumps, and the page box shows a
    /// page's own label.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        /// <summary>One arrow key's scroll, in device pixels.</summary>
        private const int ScrollLinePx = 40;

        /// <summary>How many places Back remembers.</summary>
        internal const int MaxHistory = 100;

        /// <summary>A place in the document: a page, and the user-space y at the top of the view (null for its top).</summary>
        private readonly struct ReadingSpot
        {
            public ReadingSpot(int page, float? top) { Page = page; Top = top; }
            public readonly int Page;
            public readonly float? Top;
        }

        private readonly List<ReadingSpot> _back = new List<ReadingSpot>();
        private readonly List<ReadingSpot> _forward = new List<ReadingSpot>();

        private ToolStripButton _btnBack = null!, _btnForward = null!;
        private bool _showHistoryButtons = true;

        /// <summary>Whether <see cref="GoBack"/> has a place to return to.</summary>
        [Browsable(false)]
        public bool CanGoBack => _doc != null && _back.Count > 0;

        /// <summary>Whether <see cref="GoForward"/> has a place to go to.</summary>
        [Browsable(false)]
        public bool CanGoForward => _doc != null && _forward.Count > 0;

        /// <summary>The Back and Forward buttons; Alt+Left and Alt+Right work either way.</summary>
        [Category("PDF"), DefaultValue(true), Description("Show or hide the Back and Forward buttons.")]
        public bool ShowHistoryButtons
        {
            get => _showHistoryButtons;
            set { _showHistoryButtons = value; ApplyChromeVisibility(); }
        }

        /// <summary>
        /// Returns to where the reader was before the last jump — a link, a bookmark, a thumbnail, the
        /// page box, Home or End. False when there is nowhere to return to.
        /// </summary>
        public bool GoBack() => Travel(_back, _forward);

        /// <summary>Goes forward again after <see cref="GoBack"/>. False when there is nowhere to go.</summary>
        public bool GoForward() => Travel(_forward, _back);

        private bool Travel(List<ReadingSpot> from, List<ReadingSpot> to)
        {
            if (_doc == null || from.Count == 0) return false;
            ReadingSpot target = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            if (CurrentSpot() is ReadingSpot here) Push(to, here);
            GoToPage(Clamp(target.Page, 0, _pageCount - 1), target.Top);
            UpdateHistoryButtons();
            return true;
        }

        /// <summary>
        /// A jump the reader made: where they were joins the history, Forward is forgotten, and the view
        /// goes to <paramref name="pageIndex"/> at <paramref name="top"/>.
        /// </summary>
        internal void JumpTo(int pageIndex, float? top)
        {
            if (_doc == null) return;
            if (CurrentSpot() is ReadingSpot here)
            {
                Push(_back, here);
                _forward.Clear();
            }
            GoToPage(pageIndex, top);
            UpdateHistoryButtons();
        }

        private static void Push(List<ReadingSpot> stack, ReadingSpot spot)
        {
            stack.Add(spot);
            if (stack.Count > MaxHistory) stack.RemoveAt(0);
        }

        /// <summary>Where the reader is now: the page at the top of the view, and the point of it there.</summary>
        private ReadingSpot? CurrentSpot()
        {
            if (!TryReadingPosition(out int page, out float y)) return null;
            if (_layoutMode == PdfViewLayoutMode.SinglePage) page = _currentPage;
            return new ReadingSpot(page, float.IsInfinity(y) || float.IsNaN(y) ? (float?)null : y);
        }

        private void ClearHistory()
        {
            _back.Clear();
            _forward.Clear();
            UpdateHistoryButtons();
        }

        private void UpdateHistoryButtons()
        {
            _btnBack.Enabled = CanGoBack;
            _btnForward.Enabled = CanGoForward;
        }

        /// <summary>The page the history would return to — what a test reads.</summary>
        internal int? BackTarget => _back.Count > 0 ? _back[_back.Count - 1].Page : (int?)null;

        // ── Scrolling by keyboard and wheel ───────────────────────────────────

        /// <summary>Space's step: the view's height, less a line so the reader keeps their place.</summary>
        private int ScreenStepPx() => Math.Max(ScrollLinePx, _surface.ClientSize.Height - ScrollLinePx);

        /// <summary>The scroll the last keyboard scroll asked for, before WinForms applied it.</summary>
        internal Point LastScrollRequest { get; private set; }

        /// <summary>
        /// Scrolls the view by (<paramref name="dx"/>, <paramref name="dy"/>) pixels; in single-page
        /// layout a scroll past either end of the page turns it.
        /// </summary>
        internal void ScrollView(int dx, int dy)
        {
            if (_doc == null) return;
            if (_layoutMode == PdfViewLayoutMode.SinglePage && dy != 0 && TurnAtPageEnd(dy > 0)) return;
            int maxX = Math.Max(0, _surface.AutoScrollMinSize.Width - _surface.ClientSize.Width);
            int maxY = Math.Max(0, _surface.AutoScrollMinSize.Height - _surface.ClientSize.Height);
            var target = new Point(Clamp(-_surface.AutoScrollPosition.X + dx, 0, maxX),
                                   Clamp(-_surface.AutoScrollPosition.Y + dy, 0, maxY));
            LastScrollRequest = target;
            _surface.AutoScrollPosition = target;
            _surface.Invalidate();
            UpdateCurrentPageFromScroll();
            UpdateCurrentBookmark();
        }

        /// <summary>
        /// In single-page layout, at the page's end in the direction of travel, turns to the next or
        /// previous page — the previous one shown from its foot. True when it turned.
        /// </summary>
        private bool TurnAtPageEnd(bool down)
        {
            int y = -_surface.AutoScrollPosition.Y;
            int maxY = Math.Max(0, _surface.AutoScrollMinSize.Height - _surface.ClientSize.Height);
            if (down && y >= maxY && CanStep(+1))
            {
                RunCommand(PdfViewerCommand.NextPage, NextPage);
                return true;
            }
            if (!down && y <= 0 && CanStep(-1))
            {
                RunCommand(PdfViewerCommand.PreviousPage, () =>
                {
                    PreviousPage();
                    int bottom = Math.Max(0, _surface.AutoScrollMinSize.Height - _surface.ClientSize.Height);
                    LastScrollRequest = new Point(0, bottom);
                    _surface.AutoScrollPosition = LastScrollRequest;
                });
                return true;
            }
            return false;
        }

        // ── The document's own opening view ───────────────────────────────────

        /// <summary>Whether the chrome is mirrored because the last document asked (<c>/Direction /R2L</c>).</summary>
        private bool _directionFromDocument;

        /// <summary>
        /// Opens the shown document as it asks (<see cref="PdfViewerFeatures.DocumentView"/>): its
        /// layout, its sidebar tab, its direction, then its zoom and its opening page and top.
        /// </summary>
        private void ApplyDocumentView()
        {
            if (_doc == null || !Has(PdfViewerFeatures.DocumentView)) return;
            var view = _doc.InitialView;
            if (view == null)
            {
                if (!string.IsNullOrEmpty(_doc.InitialViewError))
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.DocumentViewUnavailableFormat, _doc.InitialViewError!);
                return;
            }

            bool r2l = string.Equals(view.ViewerPreferences?.Direction, "R2L", StringComparison.Ordinal);
            if (r2l && RightToLeft != RightToLeft.Yes)
            {
                _directionFromDocument = true;
                RightToLeft = RightToLeft.Yes;
            }
            else if (!r2l && _directionFromDocument)
            {
                _directionFromDocument = false;
                RightToLeft = RightToLeft.Inherit;               // the host's, again
            }

            switch (view.PageLayout)
            {
                case "SinglePage":     ShowLayout(PdfViewLayoutMode.SinglePage, PdfPageSpread.None); break;
                case "OneColumn":      ShowLayout(PdfViewLayoutMode.Continuous, PdfPageSpread.None); break;
                case "TwoColumnLeft":  ShowLayout(PdfViewLayoutMode.Continuous, PdfPageSpread.Facing); break;
                case "TwoColumnRight": ShowLayout(PdfViewLayoutMode.Continuous, PdfPageSpread.FacingWithCover); break;
                case "TwoPageLeft":    ShowLayout(PdfViewLayoutMode.SinglePage, PdfPageSpread.Facing); break;
                case "TwoPageRight":   ShowLayout(PdfViewLayoutMode.SinglePage, PdfPageSpread.FacingWithCover); break;
            }

            TabPage? tab = view.PageMode == "UseOutlines" ? _tabBookmarks : view.PageMode == "UseThumbs" ? _tabThumbnails : null;
            if (tab != null && _sidebarTabs.TabPages.Contains(tab)) _sidebarTabs.SelectedTab = tab;

            if (view.OpenAtZoom is double zoom && zoom > 0 && !double.IsNaN(zoom) && !double.IsInfinity(zoom)) Zoom = zoom;

            if (view.OpenAtPageIndex is int page && page >= 0 && page < _pageCount)
            {
                double? top = view.OpenAtTop;
                bool topUsable = top.HasValue && !double.IsNaN(top.Value) && !double.IsInfinity(top.Value) && Math.Abs(top.Value) <= float.MaxValue;
                GoToPage(page, topUsable ? (float)top!.Value : (float?)null);
            }
        }

        // ── Page labels ───────────────────────────────────────────────────────

        /// <summary>
        /// What the page box shows for a page: its <c>/PageLabels</c> label (§12.4.2) when the document
        /// labels it, else its number.
        /// </summary>
        internal string PageBoxText(int pageIndex)
        {
            var labels = _doc?.PageLabels;
            if (labels != null && pageIndex >= 0 && pageIndex < labels.Count && !string.IsNullOrEmpty(labels[pageIndex]))
                return labels[pageIndex];
            return (pageIndex + 1).ToString(CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// The page a typed entry names: a page label first (as written, then ignoring case), then a
        /// page number; -1 for neither.
        /// </summary>
        internal int PageFromEntry(string? entry)
        {
            string text = (entry ?? "").Trim();
            if (text.Length == 0 || _doc == null) return -1;
            var labels = _doc.PageLabels;
            for (int pass = 0; pass < 2; pass++)
            {
                var comparison = pass == 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                for (int i = 0; i < labels.Count; i++)
                    if (string.Equals(labels[i], text, comparison)) return i;
            }
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= _pageCount)
                return n - 1;
            return -1;
        }

        /// <summary>Whether the page's label is something other than its number.</summary>
        private bool HasOwnLabel(int pageIndex)
            => PageBoxText(pageIndex) != (pageIndex + 1).ToString(CultureInfo.CurrentCulture);
    }
}
