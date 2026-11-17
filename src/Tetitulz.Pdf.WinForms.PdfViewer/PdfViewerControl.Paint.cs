// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations;
using Tetitulz.Pdf.Base.Operations.Extraction;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    public sealed partial class PdfViewerControl
    {

        /// <summary>
        /// The selection: each page's selected glyphs, as indices into its text index, ascending. Kept
        /// as glyphs, not as rectangles, so a zoom or a scroll draws it where the glyphs now are.
        /// </summary>
        private readonly SortedDictionary<int, List<int>> _selection = new SortedDictionary<int, List<int>>();

        // ── Surface paint ─────────────────────────────────────────────────────

        private void PaintSurface(PaintEventArgs e)
        {
            var g = e.Graphics;
            RefreshPaletteIfChanged();
            if (_surface.BackColor != _palette.PageArea) _surface.BackColor = _palette.PageArea;   // its next paint
            _ringPages.Clear();
            if (_doc == null || _layout.Length == 0)
            {
                UpdateProgressRing();
                return;
            }

            // Draw in virtual (canvas) coordinates.
            g.TranslateTransform(_surface.AutoScrollPosition.X, _surface.AutoScrollPosition.Y);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            Rectangle vis = VisibleVirtualRect;
            bool background = RendersInBackground;
            int first = -1, last = -1;
            for (int i = 0; i < _layout.Length; i++)
            {
                if (!_layout[i].Bounds.IntersectsWith(vis)) continue;
                if (first < 0) first = i;
                last = i;
                DrawPage(g, _layout[i], background ? PagePriority(_layout[i].Bounds, vis) : -1);
            }
            if (background && first >= 0) RequestNeighbours(first, last);
            UpdateProgressRing();
        }

        /// <summary>
        /// A visible page's rank in the worker's queue: the nearer its middle is to the view's, the
        /// sooner it renders.
        /// </summary>
        private static int PagePriority(Rectangle page, Rectangle view)
            => Math.Abs((page.Top + page.Bottom) / 2 - (view.Top + view.Bottom) / 2) / 16;

        /// <summary>Draws one laid-out page and everything the viewer and the host put over it.</summary>
        /// <param name="g">The surface's graphics, in canvas coordinates.</param>
        /// <param name="info">The page and where it is laid out.</param>
        /// <param name="priority">The page's rank for a background render; -1 renders it here and now.</param>
        private void DrawPage(Graphics g, PageLayoutInfo info, int priority = -1)
        {
            Rectangle b = info.Bounds;

            // The page as a card on the layer: a soft shadow round it, no outline.
            DrawPageShadow(g, b);
            using (var paper = new SolidBrush(ViewerPalette.Paper)) g.FillRectangle(paper, b);
            Bitmap? bmp = priority < 0 ? EnsurePage(info.PageIndex) : CachedOrRequested(info.PageIndex, priority);
            if (bmp != null)
                g.DrawImage(bmp, b);
            else if (_previews.TryGetValue(info.PageIndex, out Bitmap? preview))
                g.DrawImage(preview, b);                         // the previous zoom's, scaled, while this one renders
            else if (_failedPages.Contains(info.PageIndex))
                DrawFailedPage(g, b);
            else
                DrawProgressRing(g, b);

            // Find highlights on this page — from the per-page index built once by RunSearch, not
            // by re-scanning the whole match list for every visible page on every repaint.
            if (_findByPage.TryGetValue(info.PageIndex, out var onThisPage))
            {
                using var normal = new SolidBrush(_palette.FindMatch);
                using var current = new SolidBrush(_palette.FindCurrent);
                foreach (int m in onThisPage)
                {
                    var r = RectForRange(info.PageIndex, b, _findMatches[m].Start, _findMatches[m].Length);
                    if (r.HasValue)
                        g.FillRectangle(m == _findIndex ? current : normal, r.Value);
                }
            }

            DrawFormFieldHighlights(g, info.PageIndex, b);
            DrawUnsignedSignatureFields(g, info.PageIndex, b);
            DrawSelectedComment(g, info.PageIndex, b);

            // Text selection on this page.
            if (_selection.TryGetValue(info.PageIndex, out var selectedHere))
            {
                using var sb = new SolidBrush(_palette.Selection);
                var idx = GetTextIndex(info.PageIndex);
                foreach (int i in selectedHere)
                    if (i < idx.Chars.Length) g.FillRectangle(sb, MapCharToCanvas(info.PageIndex, b, idx.Chars[i]));
            }

            // Hovered link outline.
            if (_hoverLink != null && _hoverLink.PageIndex == info.PageIndex)
            {
                var r = MapLinkToCanvas(_hoverLink, b);
                using var fill = new SolidBrush(_palette.LinkHover);
                using var pen = new Pen(_palette.LinkHoverBorder);
                g.FillRectangle(fill, r);
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }

            // The host's own layer, last, so it sits above the highlights.
            RaisePaintOverlay(g, info.PageIndex, b);
        }

        // ── Render cache ──────────────────────────────────────────────────────

        internal Bitmap? EnsurePage(int pageIndex)
        {
            if (_doc == null) return null;

            int dpi = RenderDpi;
            if (dpi != _bmpCacheDpi)
            {
                // Another zoom, the same content: the bitmaps go, nothing asked of the worker does.
                RetireRenderCacheForZoom(dpi);
                ClearPreviews();
            }

            if (_failedPages.Contains(pageIndex)) return null;
            if (_bmpCache.TryGetValue(pageIndex, out var cached)) return cached;

            try
            {
                // Asks what the render will cost BEFORE allocating it. The renderer's own ceiling
                // is 100 MP — 400 MB of RGBA, twice over once GDI+ has its own copy — which a
                // page's file-controlled /UserUnit reaches on its own. Then straight to pixels —
                // no PNG encode/decode round trip. This runs on the UI thread inside OnPaint, and
                // encoding alone would be ~80 ms of it for a full-size page.
                var bmp = _doc.RenderWithinBudget(pageIndex, dpi, MaxSingleBitmapBytes, ScreenRenderOptions, out long refusedBytes);
                if (bmp == null)
                {
                    _failedPages.Add(pageIndex);
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.PageTooLargeFormat,
                        pageIndex + 1, PdfDocumentSource.Megabytes(refusedBytes));
                    return null;
                }
                _bmpCache[pageIndex] = TurnBitmap(bmp, _viewRotation);
                _bmpCacheBytes += BitmapBytes(bmp);
                EvictIfNeeded(pageIndex);
                // Re-read rather than returning the local: eviction may still have chosen this
                // page (it is the only entry when the cache is one bitmap over budget), and the
                // reference in hand would then be a DISPOSED Bitmap handed straight to
                // Graphics.DrawImage — an unhandled ArgumentException on the UI thread, thrown
                // from DrawPage where EnsurePage's own catch cannot see it.
                return _bmpCache.TryGetValue(pageIndex, out var kept) ? kept : null;
            }
            catch (Exception ex)
            {
                _failedPages.Add(pageIndex);
                _statusLabel.Text = PdfViewerStrings.Format(_strings.PageFailedFormat, pageIndex + 1, ex.Message);
                return null;
            }
        }

        private static long BitmapBytes(Bitmap bmp) => (long)bmp.Width * bmp.Height * 4L;

        /// <summary>For a test: the cache's byte bound in place of <see cref="MaxCachedBitmapBytes"/>; 0 keeps it.</summary>
        internal long CachedBitmapBytesOverride;

        private long CachedBitmapBytesBound => CachedBitmapBytesOverride > 0 ? CachedBitmapBytesOverride : MaxCachedBitmapBytes;

        /// <summary>
        /// Drops cached pages until the cache is inside both bounds, never dropping
        /// <paramref name="keepPage"/> or a page <see cref="PagesKept()"/> keeps while anything else can go.
        /// </summary>
        private void EvictIfNeeded(int keepPage)
        {
            HashSet<int>? kept = null;
            while (_bmpCache.Count > MaxCachedPages || _bmpCacheBytes > CachedBitmapBytesBound)
            {
                kept ??= PagesKept();
                int victim = ChooseEvictionVictim(_bmpCache.Keys, _currentPage, keepPage, kept);
                if (victim < 0) return;               // only the page just rendered and the pages kept are left
                if (!_bmpCache.TryGetValue(victim, out var bmp)) return;
                _bmpCache.Remove(victim);
                _bmpCacheBytes -= BitmapBytes(bmp);
                bmp.Dispose();
            }
        }

        /// <summary>
        /// The pages the cache keeps whatever its bounds say: those in view, and the neighbours beside
        /// them the bounds still hold — the pages <see cref="RequestNeighbours"/> asks the worker for.
        /// </summary>
        /// <remarks>
        /// <para><b>A page in view is never dropped for another.</b> The paint draws it, so a bitmap
        /// dropped to make room is asked for again at once, and its arrival drops another: where the
        /// pages in view and their neighbours outgrow the bounds (a page at fit width on a 4K display is
        /// some 50 MB), the page being read blinks, rendered over and over. The view holds a column or
        /// two of pages, each under <see cref="MaxSingleBitmapBytes"/>.</para>
        /// <para><b>A neighbour is a prefetch.</b> The page after the view, then the one before it, is
        /// kept only while it fits in both bounds beside the pages in view; one that does not is not
        /// asked for, since holding it would cost a page in view.</para>
        /// </remarks>
        private HashSet<int> PagesKept()
            => LayoutInView(out int first, out int last) ? PagesKept(first, last) : new HashSet<int>();

        /// <param name="first">The first laid-out page in view, as an index into the layout.</param>
        /// <param name="last">The last.</param>
        private HashSet<int> PagesKept(int first, int last)
        {
            var kept = new HashSet<int>();
            long bytes = 0;
            for (int i = first; i <= last; i++)
                if (kept.Add(_layout[i].PageIndex)) bytes += CacheBytesAt(i);
            KeepIfRoom(last + 1);                     // reading on first
            KeepIfRoom(first - 1);
            return kept;

            void KeepIfRoom(int i)
            {
                if (i < 0 || i >= _layout.Length || kept.Contains(_layout[i].PageIndex)) return;
                long more = CacheBytesAt(i);
                if (kept.Count >= MaxCachedPages || bytes + more > CachedBitmapBytesBound) return;
                kept.Add(_layout[i].PageIndex);
                bytes += more;
            }
        }

        /// <summary>
        /// What laid-out page <paramref name="i"/> takes in the cache: its bitmap's bytes, nothing when
        /// it cannot render, else its laid-out size's with a pixel more each way (the layout rounds a
        /// page's size to the nearest pixel, the renderer up).
        /// </summary>
        private long CacheBytesAt(int i)
        {
            int page = _layout[i].PageIndex;
            if (_bmpCache.TryGetValue(page, out var bmp)) return BitmapBytes(bmp);
            if (_failedPages.Contains(page)) return 0;
            Rectangle b = _layout[i].Bounds;
            return (b.Width + 1L) * (b.Height + 1L) * 4L;
        }

        /// <summary>The layout indices of the first and last pages in view; false when none is.</summary>
        private bool LayoutInView(out int first, out int last)
        {
            first = last = -1;
            Rectangle vis = VisibleVirtualRect;
            for (int i = 0; i < _layout.Length; i++)
            {
                if (!_layout[i].Bounds.IntersectsWith(vis)) continue;
                if (first < 0) first = i;
                last = i;
            }
            return first >= 0;
        }

        /// <summary>The pages laid out in view now, in layout order — what a test reads.</summary>
        internal List<int> PagesInView()
        {
            var pages = new List<int>();
            if (LayoutInView(out int first, out int last))
                for (int i = first; i <= last; i++) pages.Add(_layout[i].PageIndex);
            return pages;
        }

        /// <summary>
        /// The cached page farthest from <paramref name="currentPage"/>, excluding
        /// <paramref name="keepPage"/> and every page in <paramref name="kept"/>; <c>-1</c> when there
        /// is nothing else to drop.
        /// </summary>
        /// <remarks>
        /// <b>The exclusion is essential.</b> The victim is chosen by distance from the current page,
        /// and the page just rendered is frequently the farthest one: opening a document leaves
        /// <c>_currentPage</c> at 0 while <c>PaintSurface</c> walks the visible pages in ascending
        /// order, so without it a freshly inserted bitmap could win the distance contest and be
        /// disposed while the paint loop still holds it.
        /// </remarks>
        internal static int ChooseEvictionVictim(ICollection<int> cached, int currentPage, int keepPage,
            ISet<int>? kept = null)
        {
            int victim = -1, bestDist = -1;
            foreach (int key in cached)
            {
                if (key == keepPage || (kept != null && kept.Contains(key))) continue;
                int dist = Math.Abs(key - currentPage);
                if (dist > bestDist) { bestDist = dist; victim = key; }
            }
            return victim;
        }

        /// <summary>
        /// Forgets every rendered page, previews included, and every render asked for: what a page
        /// shows has changed (another document, a fill, a signature, comments on or off).
        /// </summary>
        private void ClearRenderCache()
        {
            foreach (var bmp in _bmpCache.Values) bmp.Dispose();
            _bmpCache.Clear();
            _bmpCacheBytes = 0;
            _failedPages.Clear();
            _bmpCacheDpi = -1;
            ClearPreviews();
            _renderGeneration++;
            _worker?.CancelAll();
        }

        // ── Coordinate mapping helpers ────────────────────────────────────────

        private RectangleF MapPdfRectToCanvas(int pageIndex, Rectangle pageBounds,
            float x, float y, float w, float h)
        {
            var geo = ViewGeometry(pageIndex);
            var px = PdfPixelMapper.MapRect(geo, RenderScale, x, y, w, h);
            return new RectangleF(pageBounds.X + px.X, pageBounds.Y + px.Y, px.Width, px.Height);
        }

        private RectangleF MapLinkToCanvas(PdfLink link, Rectangle pageBounds)
            => MapPdfRectToCanvas(link.PageIndex, pageBounds, link.X, link.Y, link.Width, link.Height);

        /// <summary>
        /// A glyph's <b>visual</b> box in canvas pixels — see
        /// <see cref="PdfPixelMapper.MapGlyph"/> for why it is not the extractor's box verbatim.
        /// </summary>
        private RectangleF MapCharToCanvas(int pageIndex, Rectangle pageBounds, CharBox c)
        {
            var geo = ViewGeometry(pageIndex);
            var px = PdfPixelMapper.MapGlyph(geo, RenderScale, c.X, c.Y, c.W, c.H);
            return new RectangleF(pageBounds.X + px.X, pageBounds.Y + px.Y, px.Width, px.Height);
        }

        /// <summary>Union of the canvas rectangles covering text range [start, start+len).</summary>
        private RectangleF? RectForRange(int pageIndex, Rectangle pageBounds, int start, int len)
        {
            var idx = GetTextIndex(pageIndex);
            float? left = null, top = null, right = null, bottom = null;
            var (first, end) = GlyphsInRange(idx.Starts, idx.Ends, start, len);
            for (int i = first; i < end; i++)
            {
                var c = idx.Chars[i];
                var r = MapCharToCanvas(pageIndex, pageBounds, c);
                left = left.HasValue ? Math.Min(left.Value, r.Left) : r.Left;
                top = top.HasValue ? Math.Min(top.Value, r.Top) : r.Top;
                right = right.HasValue ? Math.Max(right.Value, r.Right) : r.Right;
                bottom = bottom.HasValue ? Math.Max(bottom.Value, r.Bottom) : r.Bottom;
            }
            if (!left.HasValue || !top.HasValue || !right.HasValue || !bottom.HasValue) return null;
            return new RectangleF(left.Value, top.Value, right.Value - left.Value, bottom.Value - top.Value);
        }

        // ── Per-page positioned-text index (built lazily, cached) ─────────────

        /// <summary>Builds (and caches) a page's text index — the bound's own evidence.</summary>
        internal void TouchTextIndex(int pageIndex) => GetTextIndex(pageIndex);

        /// <summary>Pages currently held in the text-index cache.</summary>
        internal int TextIndexCacheCount => _textCache.Count;

        /// <summary>Pages whose text is kept for search.</summary>
        internal int CachedPageTextCount => _pageTexts.Count;

        private PageTextIndex GetTextIndex(int pageIndex)
        {
            if (_textCache.TryGetValue(pageIndex, out var cached)) return cached;

            TextIndexBuilds++;
            var index = new PageTextIndex();
            try
            {
                var layout = _doc?.LayoutFor(pageIndex);
                if (layout != null) index = BuildTextIndex(layout);
            }
            catch (Exception ex)
            {
                // Non-fatal, but recorded: an empty index makes the page unsearchable and
                // unselectable, which is indistinguishable from a page that genuinely has no text.
                _doc?.ReportPageTextError(pageIndex, ex.Message);
            }

            _textCache[pageIndex] = index;
            _pageTexts[pageIndex] = index.Text;
            EvictTextIfNeeded(pageIndex);
            return index;
        }

        /// <summary>
        /// A page's text and the glyph each character came from: its lines in reading order, a space
        /// between words, a line break between lines. The one spelling of a page's text — the
        /// background search builds it too, so its offsets are the highlights'.
        /// </summary>
        private static PageTextIndex BuildTextIndex(PdfPageTextLayout layout)
        {
            var index = new PageTextIndex();
            var sb = new System.Text.StringBuilder();
            var chars = new List<CharBox>();
            var starts = new List<int>();

            for (int li = 0; li < layout.Lines.Count; li++)
            {
                var line = layout.Lines[li];
                for (int wi = 0; wi < line.Words.Count; wi++)
                {
                    if (wi > 0) sb.Append(' ');     // synthetic word gap
                    var word = line.Words[wi];
                    for (int ci = 0; ci < word.Chars.Count; ci++)
                    {
                        var ch = word.Chars[ci];
                        int st = sb.Length;
                        sb.Append(ch.Char ?? "");
                        chars.Add(new CharBox(ch));
                        starts.Add(st);
                    }
                }
                if (li < layout.Lines.Count - 1) sb.Append('\n');
            }

            index.Text = sb.ToString();
            index.Chars = chars.ToArray();
            index.Starts = starts.ToArray();
            index.Ends = new int[index.Starts.Length];
            for (int i = 0; i < index.Ends.Length; i++)
                index.Ends[i] = index.Starts[i] + index.Chars[i].Text.Length;
            return index;
        }

        /// <summary>
        /// A page's extracted text, as its glyph index spells it — from the text cache, which keeps
        /// it after the index itself is evicted, so a search rebuilds no index it has built before.
        /// </summary>
        /// <remarks>
        /// Every keystroke in the find box reads every page's text. With the glyph index bounded
        /// and nothing else kept, each keystroke would re-extract every page past the bound; with
        /// the glyphs of every page kept, the bound would bound nothing. The text alone is a string
        /// a page.
        /// </remarks>
        private string PageText(int pageIndex)
            => _pageTexts.TryGetValue(pageIndex, out var text) ? text : GetTextIndex(pageIndex).Text;

        /// <summary>Indexes extracted since the document was opened — the evidence a search rebuilt none.</summary>
        internal int TextIndexBuilds { get; private set; }

        /// <summary>
        /// Keeps the per-glyph text index bounded, the way the bitmap cache is.
        /// </summary>
        /// <remarks>
        /// One <c>CharBox</c> object plus a string per glyph, for every page ever searched or selected
        /// on: a single keystroke in the find box walks every page, so without eviction a 5 000-page
        /// text-heavy document would hold on the order of 10^7 objects until it closed — an
        /// <see cref="OutOfMemoryException"/> on the net462 32-bit hosts this control serves.
        /// Eviction reuses the bitmap
        /// cache's victim rule so the two caches cannot drift on what "farthest from the reader"
        /// means.
        /// </remarks>
        private void EvictTextIfNeeded(int keepPage)
        {
            while (_textCache.Count > MaxCachedTextPages)
            {
                int victim = ChooseEvictionVictim(_textCache.Keys, _currentPage, keepPage);
                if (victim < 0) return;
                _textCache.Remove(victim);
            }
        }
    }
}
