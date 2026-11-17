// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Pages and thumbnails rendered off the UI thread (<see cref="RenderPagesInBackground"/>): the
    /// paint draws what is ready, the previous zoom's bitmap scaled while a page renders at the new
    /// one, and a placeholder before either exists.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        private bool _renderPagesInBackground = true;
        private PageRenderWorker? _worker;

        /// <summary>
        /// Bumped whenever what a page shows changes (another document, a fill, a signature, comments
        /// shown or hidden): a background result from before is dropped.
        /// </summary>
        private int _renderGeneration;

        /// <summary>
        /// The previous zoom's bitmaps of the pages that were in view, drawn scaled until the page
        /// renders at the new zoom; bounded by the pages in view.
        /// </summary>
        private readonly Dictionary<int, Bitmap> _previews = new Dictionary<int, Bitmap>();

        /// <summary>Results posted to the UI thread and not yet taken — a test's drain waits for them.</summary>
        private int _pendingRenderPosts;

        /// <summary>
        /// Whether pages and thumbnails render on a background thread, so scrolling and zooming never
        /// wait for the renderer. Default on. Off, every page renders in the paint that first shows
        /// it — what a host capturing the control with <c>DrawToBitmap</c> straight after a load wants.
        /// </summary>
        [Category("PDF"), DefaultValue(true),
         Description("Render pages on a background thread; the previous zoom's page is shown meanwhile.")]
        public bool RenderPagesInBackground
        {
            get => _renderPagesInBackground;
            set
            {
                if (_renderPagesInBackground == value) return;
                _renderPagesInBackground = value;
                if (!value) _worker?.CancelAll();
                _surface.Invalidate();
            }
        }

        /// <summary>
        /// Whether this paint renders in the background: switched on, and a window to post the result
        /// to — a control with no handle (never shown) renders as it paints.
        /// </summary>
        private bool RendersInBackground
            => _renderPagesInBackground && _doc != null && IsHandleCreated && !IsDisposed && !Disposing;

        private PageRenderWorker Worker => _worker ??= new PageRenderWorker(DeliverRender);

        /// <summary>For a test: a worker that holds every render it is asked for, so a page stays "rendering".</summary>
        internal void HoldBackgroundRenders()
        {
            _worker?.Dispose();
            _worker = new PageRenderWorker(DeliverRender, startPaused: true);
        }

        /// <summary>The page's bitmap at the view's zoom, or null after asking the worker for it.</summary>
        private Bitmap? CachedOrRequested(int pageIndex, int priority)
        {
            int dpi = RenderDpi;
            if (dpi != _bmpCacheDpi) RetireRenderCacheForZoom(dpi);
            if (_failedPages.Contains(pageIndex)) return null;
            if (_bmpCache.TryGetValue(pageIndex, out var cached)) return cached;
            Worker.Enqueue(PageRequest(PageRenderKind.Page, pageIndex, dpi, priority, Size.Empty));
            return null;
        }

        private PageRenderRequest PageRequest(PageRenderKind kind, int pageIndex, int dpi, int priority, Size box)
            => new PageRenderRequest
            {
                Kind = kind, Page = pageIndex, Dpi = dpi, Box = box, Priority = priority,
                Generation = _renderGeneration, Bytes = _doc!.ShownBytes, Options = ScreenRenderOptions,
                MaxBytes = MaxSingleBitmapBytes,
                // A thumbnail shows the page as the file has it; only the view turns.
                Rotation = kind == PageRenderKind.Page ? _viewRotation : 0,
            };

        /// <summary>The worker's priority above which a request is a prefetch, not a page in view.</summary>
        private const int PrefetchPriority = 100_000;

        /// <summary>The worker's priority for thumbnails: after every page.</summary>
        private const int ThumbnailPriority = 200_000;

        /// <summary>
        /// After a paint: the pages beside the view that <see cref="PagesKept(int,int)"/> keeps are asked
        /// for too, and whatever it does not keep or belongs to another zoom is dropped from the queue (a
        /// render under way is cancelled).
        /// </summary>
        /// <param name="first">The first laid-out page in view, as an index into the layout.</param>
        /// <param name="last">The last.</param>
        private void RequestNeighbours(int first, int last)
        {
            int dpi = RenderDpi;
            var keep = PagesKept(first, last);
            for (int i = Math.Max(0, first - 1); i <= Math.Min(_layout.Length - 1, last + 1); i++)
            {
                int page = _layout[i].PageIndex;
                if ((i < first || i > last) && keep.Contains(page)
                    && !_bmpCache.ContainsKey(page) && !_failedPages.Contains(page))
                    Worker.Enqueue(PageRequest(PageRenderKind.Page, page, dpi, PrefetchPriority, Size.Empty));
            }
            int generation = _renderGeneration;
            Worker.Retain(r => r.Generation == generation
                               && (r.Kind == PageRenderKind.Thumbnail || (r.Dpi == dpi && keep.Contains(r.Page))));
        }

        /// <summary>
        /// The zoom changed: the cached bitmaps of the pages in view become previews, drawn scaled
        /// until each page renders at <paramref name="dpi"/>; every other bitmap goes.
        /// </summary>
        private void RetireRenderCacheForZoom(int dpi)
        {
            var inView = new HashSet<int>();
            Rectangle vis = VisibleVirtualRect;
            foreach (var l in _layout) if (l.Bounds.IntersectsWith(vis)) inView.Add(l.PageIndex);

            foreach (var kv in _bmpCache)
            {
                if (inView.Contains(kv.Key))
                {
                    if (_previews.TryGetValue(kv.Key, out var older)) older.Dispose();
                    _previews[kv.Key] = kv.Value;
                }
                else kv.Value.Dispose();
            }
            _bmpCache.Clear();
            _bmpCacheBytes = 0;
            var gone = new List<int>();
            foreach (var page in _previews.Keys) if (!inView.Contains(page)) gone.Add(page);
            foreach (var page in gone) { _previews[page].Dispose(); _previews.Remove(page); }
            _failedPages.Clear();
            _bmpCacheDpi = dpi;
        }

        private void ClearPreviews()
        {
            foreach (var bmp in _previews.Values) bmp.Dispose();
            _previews.Clear();
        }

        /// <summary>On the worker's thread: hands a result to the UI thread, or drops it when there is none to take it.</summary>
        private void DeliverRender(PageRenderResult result)
        {
            Interlocked.Increment(ref _pendingRenderPosts);
            try
            {
                if (IsDisposed || !IsHandleCreated) throw new ObjectDisposedException(nameof(PdfViewerControl));
                BeginInvoke(new Action(() =>
                {
                    Interlocked.Decrement(ref _pendingRenderPosts);
                    OnRendered(result);
                }));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                // The window went between the check and the post: nobody will draw this bitmap.
                Interlocked.Decrement(ref _pendingRenderPosts);
                result.Bitmap?.Dispose();
            }
        }

        /// <summary>
        /// On the UI thread: puts a finished render where it is drawn from, unless the document, its
        /// content or the zoom moved on since it was asked for.
        /// </summary>
        internal void OnRendered(PageRenderResult result)
        {
            var r = result.Request;
            bool current = !IsDisposed && _doc != null && r.Generation == _renderGeneration
                           && ReferenceEquals(r.Bytes, _doc.ShownBytes);
            if (r.Kind == PageRenderKind.Thumbnail)
            {
                ThumbItem? item = current ? ThumbItemFor(r.Page) : null;
                if (item == null) { result.Bitmap?.Dispose(); return; }
                item.TakeThumbnail(result.Bitmap);
                if (result.Error != null)
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.PageFailedFormat, r.Page + 1, result.Error);
                return;
            }

            if (!current || r.Dpi != RenderDpi || r.Dpi != _bmpCacheDpi)
            {
                result.Bitmap?.Dispose();
                return;
            }
            if (result.Bitmap == null)
            {
                _failedPages.Add(r.Page);
                _statusLabel.Text = result.RefusedBytes > 0
                    ? PdfViewerStrings.Format(_strings.PageTooLargeFormat, r.Page + 1, PdfDocumentSource.Megabytes(result.RefusedBytes))
                    : PdfViewerStrings.Format(_strings.PageFailedFormat, r.Page + 1, result.Error ?? "");
            }
            else if (_bmpCache.ContainsKey(r.Page))
            {
                result.Bitmap.Dispose();                       // rendered here meanwhile
            }
            else
            {
                _bmpCache[r.Page] = result.Bitmap;
                _bmpCacheBytes += BitmapBytes(result.Bitmap);
                EvictIfNeeded(r.Page);
                if (_previews.TryGetValue(r.Page, out var preview)) { preview.Dispose(); _previews.Remove(r.Page); }
            }
            _surface.Invalidate();
        }

        private ThumbItem? ThumbItemFor(int pageIndex)
        {
            foreach (Control c in _thumbFlow.Controls)
                if (c is ThumbItem t && t.PageIndex == pageIndex && !t.IsDisposed) return t;
            return null;
        }

        /// <summary>A thumbnail asked of the worker; false when this paint renders it itself.</summary>
        private bool RequestThumbnail(int pageIndex, Size box, out int generation)
        {
            generation = _renderGeneration;
            if (!RendersInBackground) return false;
            var geo = _doc!.Geometry(pageIndex);
            int dpi = ZoomMath.DpiToFill(box.Width, box.Height, geo.DisplayWidthPts, geo.DisplayHeightPts);
            Worker.Enqueue(PageRequest(PageRenderKind.Thumbnail, pageIndex, dpi,
                ThumbnailPriority + Math.Abs(pageIndex - _currentPage), box));
            return true;
        }

        /// <summary>
        /// <paramref name="raw"/> scaled down into <paramref name="box"/>, its proportions kept — a
        /// thumbnail, made once so a repaint only copies it.
        /// </summary>
        internal static Bitmap ScaleIntoBox(Bitmap raw, Size box)
        {
            float r = Math.Min((float)Math.Max(1, box.Width) / raw.Width, (float)Math.Max(1, box.Height) / raw.Height);
            int tw = Math.Max(1, SaturatingCast.ToInt(raw.Width * r));
            int th = Math.Max(1, SaturatingCast.ToInt(raw.Height * r));
            var scaled = new Bitmap(tw, th);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(raw, 0, 0, tw, th);
            }
            return scaled;
        }

        // ── Test hooks ────────────────────────────────────────────────────────

        /// <summary>The worker, once a background render was asked for.</summary>
        internal PageRenderWorker? RenderWorker => _worker;

        /// <summary>Pages drawn from the previous zoom's bitmap now.</summary>
        internal int PreviewCount => _previews.Count;

        /// <summary>Whether page <paramref name="pageIndex"/> has a bitmap at the view's zoom.</summary>
        internal bool HasRenderedPage(int pageIndex) => _bmpCacheDpi == RenderDpi && _bmpCache.ContainsKey(pageIndex);

        /// <summary>
        /// Pumps messages until the worker is idle and every result it posted has been taken, or the
        /// time runs out; true when idle.
        /// </summary>
        internal bool DrainRenders(int timeoutMs = 10_000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                Application.DoEvents();
                if ((_worker == null || _worker.IsIdle) && Volatile.Read(ref _pendingRenderPosts) == 0) return true;
                Thread.Sleep(2);
            }
            return false;
        }
    }
}
