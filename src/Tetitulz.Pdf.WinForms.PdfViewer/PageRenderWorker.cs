// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using Tetitulz.Pdf.Base.Rendering;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>What a render request makes: a page at the view's zoom, or a page's thumbnail.</summary>
    internal enum PageRenderKind
    {
        Page,
        Thumbnail,
    }

    /// <summary>One page to render, with everything the worker needs to render it on its own thread.</summary>
    internal sealed class PageRenderRequest
    {
        public PageRenderKind Kind;
        public int Page;
        public int Dpi;
        /// <summary>A thumbnail's box: the rendered page is scaled into it.</summary>
        public Size Box;
        /// <summary>The view's render generation when asked; a result from an older one is dropped.</summary>
        public int Generation;
        /// <summary>Smaller first.</summary>
        public int Priority;
        /// <summary>The shown bytes; a request over other bytes than the worker's session reopens it.</summary>
        public byte[] Bytes = Array.Empty<byte>();
        public PdfRenderOptions? Options;
        /// <summary>The most the page's bitmap may take; a larger one is refused before it is rendered.</summary>
        public long MaxBytes;
        /// <summary>The clockwise turn the bitmap is given after it renders (0, 90, 180, 270); a thumbnail asks for none.</summary>
        public int Rotation;

        internal bool SameWork(PageRenderRequest other)
            => Kind == other.Kind && Page == other.Page && Dpi == other.Dpi && Generation == other.Generation
               && Box == other.Box && Rotation == other.Rotation && ReferenceEquals(Bytes, other.Bytes);
    }

    /// <summary>What came of a request: a bitmap, a refusal by size, or the renderer's error.</summary>
    internal sealed class PageRenderResult
    {
        public PageRenderResult(PageRenderRequest request) => Request = request;
        public PageRenderRequest Request { get; }
        public Bitmap? Bitmap;
        public long RefusedBytes;
        public string? Error;
    }

    /// <summary>
    /// Renders pages on one background thread, so painting never waits for the renderer.
    /// </summary>
    /// <remarks>
    /// <para><b>One thread, one session.</b> <see cref="PdfRenderSession"/> is not thread-safe, and
    /// it is held because a one-shot render parses the whole document (see
    /// <c>PdfDocumentSource.Session</c>); the session is opened, used and disposed on the worker's own
    /// thread, over the bytes the newest request names.</para>
    /// <para><b>Asked, ranked, dropped.</b> The view asks for the pages it shows, most central first,
    /// and their neighbours after; <see cref="Retain"/> drops what has scrolled away or belongs to an
    /// older zoom, and cancels a render already under way, which the renderer polls inside the
    /// content stream. A result goes to the callback on the worker's thread; the control marshals it.</para>
    /// </remarks>
    internal sealed class PageRenderWorker : IDisposable
    {
        private readonly object _gate = new object();
        private readonly List<PageRenderRequest> _pending = new List<PageRenderRequest>();
        private readonly Action<PageRenderResult> _deliver;
        private Thread? _thread;
        private bool _stopping;
        private bool _paused;
        private PageRenderRequest? _running;
        private CancellationTokenSource? _runningCancel;

        /// <param name="deliver">Called on the worker's thread with each result not cancelled.</param>
        /// <param name="startPaused">For a test: requests queue and nothing renders until <see cref="Resume"/>.</param>
        public PageRenderWorker(Action<PageRenderResult> deliver, bool startPaused = false)
        {
            _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
            _paused = startPaused;
        }

        /// <summary>Renders finished — a bitmap made, or refused, or failed — what a test reads.</summary>
        internal int Completed { get; private set; }

        /// <summary>The managed thread the last render ran on.</summary>
        internal int LastRenderThreadId { get; private set; }

        /// <summary>Requests waiting, not counting the one under way.</summary>
        internal int PendingCount { get { lock (_gate) return _pending.Count; } }

        /// <summary>Nothing waiting and nothing under way.</summary>
        internal bool IsIdle { get { lock (_gate) return _pending.Count == 0 && _running == null; } }

        /// <summary>Queues <paramref name="request"/>, or re-ranks the same work already waiting or under way.</summary>
        public void Enqueue(PageRenderRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (_gate)
            {
                if (_stopping) return;
                if (_running != null && _running.SameWork(request)) return;
                foreach (var waiting in _pending)
                {
                    if (!waiting.SameWork(request)) continue;
                    waiting.Priority = Math.Min(waiting.Priority, request.Priority);
                    return;
                }
                _pending.Add(request);
                if (_thread == null)
                {
                    _thread = new Thread(Loop) { IsBackground = true, Name = "PdfViewer page renderer" };
                    _thread.Start();
                }
                Monitor.Pulse(_gate);
            }
        }

        /// <summary>
        /// Keeps only the requests <paramref name="keep"/> accepts, the one under way included: a render
        /// it refuses is cancelled.
        /// </summary>
        public void Retain(Func<PageRenderRequest, bool> keep)
        {
            lock (_gate)
            {
                _pending.RemoveAll(r => !keep(r));
                if (_running != null && !keep(_running)) _runningCancel?.Cancel();
            }
        }

        /// <summary>Drops every request and cancels the render under way.</summary>
        public void CancelAll() => Retain(_ => false);

        /// <summary>Lets a worker made paused start rendering.</summary>
        internal void Resume()
        {
            lock (_gate)
            {
                _paused = false;
                Monitor.Pulse(_gate);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _stopping = true;
                _pending.Clear();
                _runningCancel?.Cancel();
                Monitor.Pulse(_gate);
            }
        }

        private void Loop()
        {
            PdfRenderSession? session = null;
            byte[]? sessionBytes = null;
            (RenderIntent, AnnotationRendering) sessionKey = default;
            try
            {
                while (true)
                {
                    PageRenderRequest request;
                    CancellationToken token;
                    lock (_gate)
                    {
                        while (!_stopping && (_paused || _pending.Count == 0)) Monitor.Wait(_gate);
                        if (_stopping) return;
                        int best = 0;
                        for (int i = 1; i < _pending.Count; i++)
                            if (_pending[i].Priority < _pending[best].Priority) best = i;
                        request = _pending[best];
                        _pending.RemoveAt(best);
                        _running = request;
                        _runningCancel = new CancellationTokenSource();
                        token = _runningCancel.Token;
                    }

                    PageRenderResult? result = new PageRenderResult(request);
                    try
                    {
                        var options = request.Options ?? PdfRenderOptions.Default;
                        var key = (options.Intent, options.Annotations);
                        if (session == null || !ReferenceEquals(sessionBytes, request.Bytes) || !key.Equals(sessionKey))
                        {
                            session?.Dispose();
                            session = null;
                            session = PdfRenderSession.Open(new MemoryStream(request.Bytes, false), options);
                            sessionBytes = request.Bytes;
                            sessionKey = key;
                        }
                        Render(session, request, token, result);
                    }
                    catch (OperationCanceledException)
                    {
                        result = null;                          // asked away: nothing to say
                    }
                    catch (Exception ex)
                    {
                        result.Error = ex.Message;
                    }

                    lock (_gate)
                    {
                        _running = null;
                        _runningCancel?.Dispose();
                        _runningCancel = null;
                        if (result != null)
                        {
                            Completed++;
                            LastRenderThreadId = Thread.CurrentThread.ManagedThreadId;
                        }
                    }
                    if (result != null) _deliver(result);
                }
            }
            finally
            {
                session?.Dispose();
            }
        }

        private static void Render(PdfRenderSession session, PageRenderRequest request, CancellationToken token,
            PageRenderResult result)
        {
            // The size first, from the same session, so a page over the budget is refused before a
            // pixel is allocated for it.
            var size = session.GetRenderSize(request.Page, request.Dpi);
            long bytes = (long)size.WidthPixels * size.HeightPixels * 4L;
            if (bytes > request.MaxBytes)
            {
                result.RefusedBytes = bytes;
                return;
            }
            token.ThrowIfCancellationRequested();
            Bitmap page = PdfViewerControl.TurnBitmap(
                PdfDocumentSource.ToGdiBitmap(session.RenderPageToPixels(request.Page, request.Dpi, token)), request.Rotation);
            if (request.Kind == PageRenderKind.Page)
            {
                result.Bitmap = page;
                return;
            }
            using (page) result.Bitmap = PdfViewerControl.ScaleIntoBox(page, request.Box);
        }
    }
}
