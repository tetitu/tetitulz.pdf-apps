// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations;
using Tetitulz.Pdf.Base.Operations.Extraction;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// An embeddable WinForms PDF viewer control (desktop-viewer style): renders
    /// pages, zooms, searches text, follows clickable links, supports continuous and
    /// single-page layouts, text selection + copy, printing, a thumbnail strip and a
    /// bookmark tree. Consumable from .NET Framework 4.6.2 hosts and modern .NET.
    /// </summary>
    [ToolboxItem(true)]
    public sealed partial class PdfViewerControl : UserControl
    {
        // ── Tunables ──────────────────────────────────────────────────────────
        // The page dpi at 100% zoom lives in ZoomMath.BaseDpi, shared with the fit-zoom maths.
        private const int Gap = 16;                  // space between pages (continuous): room for their shadows
        private const int PageMargin = 14;           // canvas padding
        private const double MinZoom = 0.1;
        private const double MaxZoom = 8.0;
        private const double ZoomStep = 1.25;        // one Zoom In / Zoom Out click
        private const double ZoomEpsilon = 0.0001;   // a smaller change than this is a no-op
        private const int MaxCachedPages = 16;

        /// <summary>Ceiling on the total decoded bytes held in the page-bitmap cache.</summary>
        /// <remarks>
        /// <para><b>A count is not a budget.</b> <see cref="MaxCachedPages"/> caps how many
        /// bitmaps are held and nothing capped their size, while per-page pixel extent is the
        /// product of the zoom (up to <see cref="MaxZoom"/>) and the page's own box and
        /// <c>/UserUnit</c> — both file-controlled. A plain US-Letter page at 8x is
        /// 6528x8448 px = 220 MB, so a full cache would be 3.5 GB; a page declaring
        /// <c>/UserUnit 75000</c> reaches the renderer's own 100 MP ceiling, 400 MB per bitmap, on
        /// a ~500-byte file.</para>
        /// <para>Both bounds apply: eviction runs until the cache is under the count
        /// <b>and</b> under this many bytes, or only the pages in view are left — those it never
        /// drops (<c>PagesKept</c>).</para>
        /// </remarks>
        internal const long MaxCachedBitmapBytes = 128L * 1024 * 1024;

        /// <summary>
        /// Ceiling on a SINGLE page bitmap. A page over it is refused before it is rendered and
        /// reported in the status bar, rather than being allowed to throw
        /// <see cref="OutOfMemoryException"/> — which on the net462 x86 hosts this control exists to
        /// serve is what the first such page does.
        /// </summary>
        internal const long MaxSingleBitmapBytes = 64L * 1024 * 1024;

        /// <summary>
        /// Upper bound on the thumbnail strip.
        /// </summary>
        /// <remarks>
        /// Each item is a WinForms <see cref="Panel"/>, i.e. a native window handle once realised,
        /// and the page count is entirely file-controlled and cheap to inflate. Windows enforces a
        /// default per-process USER handle quota of 10 000, so a document declaring 20 000 pages —
        /// a few hundred KB with object streams — would fail control creation with a
        /// <c>Win32Exception</c> out of <c>LoadCore</c> <i>after</i> the document was assigned,
        /// leaving the control half-loaded. Below the quota the cost is still a
        /// multi-second <see cref="FlowLayoutPanel"/> layout pass over tens of thousands of
        /// children, on the UI thread, during load.
        /// </remarks>
        internal const int MaxThumbnails = 512;

        /// <summary>
        /// Pages whose per-glyph text index is kept.
        /// </summary>
        /// <remarks>
        /// The index is one heap object plus a string per glyph, and one keystroke in the find box
        /// walks every page — so an unbounded cache would turn a long text-heavy document into
        /// hundreds of megabytes of managed heap held until the document closed.
        /// </remarks>
        internal const int MaxCachedTextPages = 64;

        /// <summary>
        /// Ceiling on the find-match list, which is otherwise unbounded and file-controlled.
        /// </summary>
        /// <remarks>
        /// A single-character term against a dense document yields hundreds of matches per page,
        /// every one of them repainted from the whole list on every scroll notch. Past this the
        /// result label says "N+" rather than pretending to a total nobody can act on.
        /// </remarks>
        internal const int MaxFindMatches = 2000;

        // Belt-and-braces bounds on the dpi handed to the renderer. MinZoom/MaxZoom already keep
        // it inside this range; these stop a bad Zoom value from reaching the rasteriser.
        private const int MinRenderDpi = 10;
        private const int MaxRenderDpi = 1200;

        // ── Document / view state ─────────────────────────────────────────────
        private PdfDocumentSource? _doc;
        /// <summary>The file name a document was opened from; null for bytes and streams.</summary>
        private string? _documentTitle;
        private int _pageCount;
        private int _currentPage;
        private double _zoom = 1.0;
        private PdfZoomMode _zoomMode = PdfZoomMode.Custom;
        private PdfViewLayoutMode _layoutMode = PdfViewLayoutMode.Continuous;

        // ── Layout + render caches ────────────────────────────────────────────
        private PageLayoutInfo[] _layout = Array.Empty<PageLayoutInfo>();
        private readonly Dictionary<int, Bitmap> _bmpCache = new Dictionary<int, Bitmap>();
        private readonly HashSet<int> _failedPages = new HashSet<int>();
        private int _bmpCacheDpi = -1;
        /// <summary>Decoded bytes currently held in <see cref="_bmpCache"/>.</summary>
        private long _bmpCacheBytes;
        /// <summary>Set when <see cref="PopulateSidebar"/> stopped at <see cref="MaxThumbnails"/>.</summary>
        private bool _thumbsTruncated;
        private readonly Dictionary<int, PageTextIndex> _textCache = new Dictionary<int, PageTextIndex>();
        // Every indexed page's text, kept after its glyph index is evicted: search needs only this.
        private readonly Dictionary<int, string> _pageTexts = new Dictionary<int, string>();

        // ── Selection ─────────────────────────────────────────────────────────
        private bool _dragging;
        private bool _draggedFar;
        private Point _dragStartV, _dragEndV;
        private string _selectedText = "";

        /// <summary>Backing field for <see cref="Strings"/>; never null.</summary>
        private PdfViewerStrings _strings = PdfViewerStrings.English();

        /// <summary>The page counter beside the page box, from the current strings.</summary>
        /// <remarks>
        /// One writer, called both when the page count changes and when the strings do. Two
        /// call sites formatting the same label is how one of them keeps the old language.
        /// </remarks>
        private void UpdatePageCountLabel()
            => _pageCountLabel.Text = _pageCount <= 0 ? _strings.PageCountUnknown
                // A labelled page ("iii") is also given its number, so the reader knows where it is.
                : HasOwnLabel(_currentPage) ? PdfViewerStrings.Format(_strings.PageCountWithNumberFormat, _currentPage + 1, _pageCount)
                : PdfViewerStrings.Format(_strings.PageCountFormat, _pageCount);

        // ── Link interaction ──────────────────────────────────────────────────
        private PdfLink? _hoverLink;
        private PdfLink? _pendingLink;

        // ── Find ──────────────────────────────────────────────────────────────
        private readonly List<FindMatch> _findMatches = new List<FindMatch>();
        /// <summary>Indices into <see cref="_findMatches"/>, grouped by page and built once per search.</summary>
        /// <remarks>
        /// Walking the entire match list for every visible page on every repaint would cost a common
        /// term in a long document O(matches) per page per scroll notch.
        /// </remarks>
        private readonly Dictionary<int, List<int>> _findByPage = new Dictionary<int, List<int>>();
        private int _findIndex = -1;
        /// <summary>Set when the search stopped at <see cref="MaxFindMatches"/>.</summary>
        private bool _findTruncated;

        // ── Controls ──────────────────────────────────────────────────────────
        private ToolStrip _toolbar = null!;
        private ToolStripButton _btnOpen = null!, _btnSave = null!, _btnPrev = null!, _btnNext = null!;
        private ToolStripTextBox _pageBox = null!;
        private ToolStripLabel _pageCountLabel = null!;
        private ToolStripButton _btnZoomOut = null!, _btnZoomIn = null!;
        private ToolStripComboBox _zoomCombo = null!;
        private ToolStripButton _btnFitWidth = null!, _btnFitPage = null!;
        private ToolStripButton _btnLayout = null!, _btnSidebar = null!, _btnFind = null!, _btnPrint = null!;

        private SplitContainer _split = null!;
        private TabControl _sidebarTabs = null!;
        private TabPage _tabThumbnails = null!, _tabBookmarks = null!, _tabComments = null!, _tabSignatures = null!;
        private TreeView _commentTree = null!, _signatureTree = null!;
        private Label _signatureBanner = null!;
        private FlowLayoutPanel _thumbFlow = null!;
        private TreeView _bookmarkTree = null!;
        private Surface _surface = null!;

        private Panel _findBar = null!;
        private TextBox _findBox = null!;
        private Button _btnFindPrev = null!, _btnFindNext = null!, _btnFindClose = null!;
        private Label _findResult = null!;

        private StatusStrip _statusStrip = null!;
        private ToolStripStatusLabel _statusLabel = null!;

        // ── Public events ─────────────────────────────────────────────────────
        public event EventHandler<PdfViewerLoadedEventArgs>? DocumentLoaded;
        public event EventHandler<PdfViewerPageChangedEventArgs>? PageChanged;
        public event EventHandler<PdfViewerLinkClickedEventArgs>? LinkClicked;

        public PdfViewerControl()
        {
            ShowSignatureStatus = ShowSignatureStatusWindow;
            BuildUi();
            UpdateCommandStates();
        }

        // ── Nested types ──────────────────────────────────────────────────────

        private struct PageLayoutInfo
        {
            public int PageIndex;
            public Rectangle Bounds;     // virtual (canvas) coordinates
        }

        private sealed class CharBox
        {
            /// <summary>
            /// Built from the extractor's glyph rather than field by field.
            /// </summary>
            /// <remarks>
            /// A constructor rather than <c>= null!</c> on <see cref="Source"/>: every box comes
            /// from a glyph, so the invariant is real and this checks it instead of asserting it
            /// (CLAUDE.md, nullable reference types). It also stops the five copied fields drifting
            /// from the glyph they were copied out of.
            /// </remarks>
            internal CharBox(Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar source)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                Text = source.Char ?? "";
                X = source.X; Y = source.Y; W = source.Width; H = source.Height;
            }

            public string Text;
            public float X, Y, W, H;     // PDF user space, origin bottom-left

            /// <summary>
            /// The extractor's own glyph, kept so a selection can be put into reading order by the
            /// same code the structured extractor uses.
            /// </summary>
            /// <remarks>
            /// Copying builds its string from these boxes, not from <c>PdfTextLine.Text</c>, because
            /// the selection is a rectangle and only per-glyph geometry says what is inside it. That
            /// is exactly why the viewer could not reach <c>WordAssemblyOptions.ApplyBidi</c>, which
            /// rebuilds the line text and leaves the chars alone — so the glyph travels with the box.
            /// </remarks>
            public Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar Source;
        }

        private sealed class PageTextIndex
        {
            public string Text = "";
            public CharBox[] Chars = Array.Empty<CharBox>();
            public int[] Starts = Array.Empty<int>();   // char i → start offset in Text
            public int[] Ends = Array.Empty<int>();     // char i → end offset in Text
        }

        /// <summary>
        /// The glyphs a text range [<paramref name="start"/>, <paramref name="start"/> +
        /// <paramref name="length"/>) touches, as indices [First, End) into a page index's glyphs.
        /// </summary>
        /// <remarks>
        /// The glyphs are in text order and none overlaps the next, so both <paramref name="starts"/>
        /// and <paramref name="ends"/> ascend: the first glyph is found by binary search and the
        /// walk stops at the range's end. Walking every glyph of the page for each highlight on every
        /// repaint would be 4x10^8 steps a scroll notch for 2 000 matches on a 200 000-glyph page.
        /// </remarks>
        internal static (int First, int End) GlyphsInRange(int[] starts, int[] ends, int start, int length)
        {
            int lo = 0, hi = ends.Length;   // the first glyph ending after start
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (ends[mid] <= start) lo = mid + 1; else hi = mid;
            }
            int end = lo;
            while (end < starts.Length && starts[end] < start + length) end++;
            return (lo, end);
        }

        private struct FindMatch
        {
            public int Page;
            public int Start;
            public int Length;
        }

        /// <summary>
        /// The scrollable painting surface. It is both the AutoScroll container and the
        /// painter (paints in virtual coordinates translated by the scroll offset), which
        /// avoids the child-control wheel/focus pitfalls of a nested canvas.
        /// </summary>
        private sealed class Surface : Panel
        {
            private readonly PdfViewerControl _owner;
            public Surface(PdfViewerControl owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                AutoScroll = true;
                BackColor = owner._palette.PageArea;
                TabStop = true;
            }

            protected override void OnPaint(PaintEventArgs e) => _owner.PaintSurface(e);

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if (_owner.SurfaceMouseWheel(e)) return;   // Ctrl+wheel consumed by zoom
                Point before = AutoScrollPosition;
                base.OnMouseWheel(e);
                // The wheel scrolls without raising Scroll, so the current page, its thumbnail and
                // bookmark follow here.
                if (AutoScrollPosition != before) _owner.OnSurfaceScrolled();
            }

            /// <summary>For a test: a wheel notch as the window would deliver it.</summary>
            internal void Wheel(MouseEventArgs e) => OnMouseWheel(e);
        }

        /// <summary>For a test: the wheel turned over the page, scrolling it as the mouse does.</summary>
        internal void WheelSurface(int delta)
            => _surface.Wheel(new MouseEventArgs(MouseButtons.None, 0, 50, 50, delta));

        // ── Public properties ─────────────────────────────────────────────────

        [Browsable(false)] public int PageCount => _pageCount;
        [Browsable(false)] public int CurrentPage => _currentPage;
        [Browsable(false)] public bool HasDocument => _doc != null;

        /// <summary>
        /// Whether copied text is put into logical reading order rather than the order the
        /// glyphs sit on the page. Default <see langword="false"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>What it fixes:</b> a majority right-to-left line sorts every glyph by X
        /// descending, so an embedded Latin or numeric run inside Hebrew — a part code, a date,
        /// <c>"1,234 ₪"</c> — comes out reversed. With this set, each selected line goes through
        /// the same reading-order rule structured extraction applies, which keeps those runs the
        /// right way round.</para>
        ///
        /// <para><b>Off by default, matching <c>WordAssemblyOptions.ApplyBidi</c>.</b> Extraction
        /// made the same choice, and a viewer that silently started copying different text would
        /// be a behaviour change for every host that already ships one.</para>
        ///
        /// <para><b>It costs nothing for text that is wholly one direction</b>, which is why it is
        /// safe to switch on for a mixed-language corpus rather than per document.</para>
        /// </remarks>
        [DefaultValue(false)]
        public bool CopyUsesLogicalOrder { get; set; }


        [Category("PDF"), DefaultValue(PdfViewLayoutMode.Continuous),
         Description("Continuous vertical scroll or single page at a time.")]
        public PdfViewLayoutMode LayoutMode
        {
            get => _layoutMode;
            set
            {
                if (_layoutMode == value) return;
                _layoutMode = value;
                UpdateLayoutButton();
                RebuildLayout();
                GoToPage(_currentPage, forceScroll: true);
            }
        }

        /// <summary>
        /// Zoom factor (1.0 = 100%). A value outside [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]
        /// is clamped; a non-finite one is <b>rejected</b>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="value"/> is NaN or infinite.
        /// </exception>
        /// <remarks>
        /// NaN cannot be clamped: <c>Math.Min</c>/<c>Math.Max</c> propagate it rather than bounding
        /// it, and the no-op test <c>Math.Abs(NaN - _zoom) &lt; ZoomEpsilon</c> is false as well, so a
        /// stored NaN would put <see cref="RenderDpi"/> at 0, the clamp would lift it to
        /// <see cref="MinRenderDpi"/>, and every page would render at 10 dpi while the zoom combo read
        /// "NaN%" — a host that divided by a zero page dimension would get an unreadable smear and no
        /// sign its input was refused. ±∞ would clamp; it is refused too, so "finite" is one rule.
        /// </remarks>
        [Category("PDF"), DefaultValue(1.0), Description("Zoom factor (1.0 = 100%).")]
        public double Zoom
        {
            get => _zoom;
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Zoom must be a finite number.");
                SetZoom(value, anchor: null);
            }
        }

        /// <summary>
        /// Whether the zoom is a fixed factor or a fit kept in step with the view. A fit is refitted
        /// when the view is resized, a document is loaded, and — in single-page layout — a page of
        /// another size is shown; in continuous layout it fits the current page and is not refitted
        /// by scrolling, which would change the zoom under the reader. Setting a fit applies it at
        /// once; any explicit zoom (<see cref="Zoom"/>, <see cref="ZoomIn"/>, <see cref="ZoomOut"/>,
        /// <see cref="ActualSize"/>, the zoom box, Ctrl+wheel) returns to <see cref="PdfZoomMode.Custom"/>.
        /// </summary>
        [Category("PDF"), DefaultValue(PdfZoomMode.Custom),
         Description("A fixed zoom, or a fit (width / page) kept as the view resizes.")]
        public PdfZoomMode ZoomMode
        {
            get => _zoomMode;
            set
            {
                if (value != PdfZoomMode.Custom && value != PdfZoomMode.FitWidth && value != PdfZoomMode.FitPage)
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown zoom mode.");
                _zoomMode = value;
                ApplyZoomMode();
                UpdateZoomCombo();
            }
        }

        /// <summary>
        /// Current render DPI: the zoom factor at the monitor's resolution
        /// (<see cref="ScaleToDeviceDpi"/>).
        /// </summary>
        private int RenderDpi
        {
            get
            {
                int dpi = SaturatingCast.ToInt(Math.Round(ZoomMath.BaseDpi * _deviceScale * _zoom));
                return Clamp(dpi, MinRenderDpi, MaxRenderDpi);
            }
        }

        private float RenderScale => PdfPixelMapper.ScaleForDpi(RenderDpi);

        // ── Public API: loading ───────────────────────────────────────────────

        /// <summary>
        /// Loads a PDF from a file path. For encrypted PDFs, set <see cref="Password"/> first or
        /// handle <see cref="PasswordRequested"/>; otherwise a <see cref="PdfPasswordRequiredException"/>
        /// is thrown.
        /// </summary>
        /// <exception cref="Tetitulz.Pdf.Core.PdfException">The file cannot be read as a PDF, or holds no page.</exception>
        public void LoadFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
            LoadCore(() => PdfDocumentSource.FromFile(path, BuildPasswordProvider()), Path.GetFileName(path));
        }

        /// <summary>Loads a PDF from an in-memory byte array.</summary>
        /// <exception cref="Tetitulz.Pdf.Core.PdfException">The bytes cannot be read as a PDF, or hold no page.</exception>
        public void LoadBytes(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            LoadCore(() => PdfDocumentSource.FromBytes(data, BuildPasswordProvider()), null);
        }

        /// <summary>Loads a PDF from a stream. The stream is read fully and not retained.</summary>
        /// <exception cref="Tetitulz.Pdf.Core.PdfException">The stream cannot be read as a PDF, or holds no page.</exception>
        public void LoadStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            LoadCore(() => PdfDocumentSource.FromStream(stream, BuildPasswordProvider()), null);
        }

        /// <summary>Closes the current document and clears the view.</summary>
        public void CloseDocument()
        {
            ResetFormState();
            ReplaceDocument(null);
            ClearHistory();
            _documentTitle = null;
            _pageCount = 0;
            _currentPage = 0;
            ClearRenderCache();
            _textCache.Clear();
            _pageTexts.Clear();
            CancelSearch();
            _findMatches.Clear();
            _findByPage.Clear();
            _findTruncated = false;
            _findIndex = -1;
            ClearSelection();
            ClearThumbnails();
            ClearBookmarkTree();
            CurrentBookmarkNode = null;
            _commentTree.Nodes.Clear();
            SelectedComment = null;
            _hoverComment = null;
            _signatureTree.Nodes.Clear();
            _signatureResults = Array.Empty<Tetitulz.Pdf.Signing.Models.SignatureVerificationResult>();
            _signatureError = null;
            _hoverSignature = null;
            UpdateSignatureBanner();
            _layout = Array.Empty<PageLayoutInfo>();
            _surface.AutoScrollMinSize = Size.Empty;
            _surface.Invalidate();
            _statusLabel.Text = _strings.Ready;
            UpdateCommandStates();
            UpdatePageBox();
        }

        /// <summary>
        /// Puts <paramref name="next"/> on show and closes the render sessions of the document it
        /// replaces — the one place the shown document changes.
        /// </summary>
        private void ReplaceDocument(PdfDocumentSource? next)
        {
            PdfDocumentSource? previous = _doc;
            _doc = next;
            if (previous != null && !ReferenceEquals(previous, next)) previous.Dispose();
        }

        private void LoadCore(Func<PdfDocumentSource> open, string? title)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var doc = open();
                // The file's own content is refused, so the library's own type (CLAUDE.md): a host catching
                // PdfException for "this file cannot be shown" catches a document with no pages too. Refused
                // before anything of the shown document changes: a refusal leaves it on show with its typed
                // values and its unsaved flag, so a later save still writes them.
                if (doc.PageCount <= 0)
                {
                    doc.Dispose();
                    throw new Tetitulz.Pdf.Core.PdfException("The PDF contains no renderable pages.");
                }

                ResetFormState();
                ReplaceDocument(doc);
                ClearHistory();
                _documentTitle = title;
                _pageCount = doc.PageCount;
                _currentPage = 0;
                _viewRotation = 0;                             // a turn is a view of one document
                ClearRenderCache();
                _textCache.Clear();
                _pageTexts.Clear();
                CancelSearch();
                _findMatches.Clear();
                _findByPage.Clear();
                _findTruncated = false;
                _findIndex = -1;
                ClearSelection();

                ApplyZoomMode();
                RebuildLayout();
                _surface.AutoScrollPosition = new Point(0, 0);
                PopulateSidebar();
                UpdateCommandStates();
                UpdatePageBox();

                _statusLabel.Text = title != null
                    ? PdfViewerStrings.Format(_strings.OpenedWithTitleFormat, title, _pageCount)
                    : PdfViewerStrings.Format(_strings.OpenedFormat, _pageCount);
                if (_thumbsTruncated)
                    _statusLabel.Text += PdfViewerStrings.Format(
                        _strings.ThumbnailsLimitedFormat, MaxThumbnails);
                // PopulateSidebar has just forced the outline, so this is the one moment the viewer
                // knows an EMPTY bookmarks tab means "extraction threw" rather than "the document
                // has none". Reported here because LoadCore owns the status line.
                if (!string.IsNullOrEmpty(doc.BookmarkExtractionError))
                    _statusLabel.Text += " — " + PdfViewerStrings.Format(_strings.BookmarksUnavailableFormat, doc.BookmarkExtractionError!);
                ApplyDocumentView();

                DocumentLoaded?.Invoke(this, new PdfViewerLoadedEventArgs(_pageCount));
                PageChanged?.Invoke(this, new PdfViewerPageChangedEventArgs(_currentPage, _pageCount));
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        // ── Public API: navigation ────────────────────────────────────────────

        /// <summary>The next page — with two pages to a row (<see cref="PageSpread"/>), the next row's first.</summary>
        public void NextPage() => GoToPage(PageAfterStep(+1));

        /// <summary>The previous page — with two pages to a row, the previous row's first.</summary>
        public void PreviousPage() => GoToPage(PageAfterStep(-1));

        /// <summary>Navigates to a zero-based page index.</summary>
        public void GoToPage(int index) => GoToPage(index, forceScroll: true);

        /// <summary>
        /// Navigates to a zero-based page index with the page's user-space y <paramref name="top"/> at
        /// the top of the view — what a destination's <c>/XYZ</c> or <c>/FitH</c> top asks for. A null
        /// <paramref name="top"/> is the page's top; a top beyond the page is its edge.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="top"/> is NaN or infinite.</exception>
        public void GoToPage(int index, float? top)
        {
            if (top.HasValue) Tetitulz.Pdf.Core.Util.NumericArguments.RequireFinite(top.Value, nameof(top));
            GoToPage(index, forceScroll: true);
            if (top.HasValue && _doc != null) ScrollPageYToTop(Clamp(index, 0, _pageCount - 1), top.Value);
        }

        /// <summary>The vertical scroll the last positioned navigation asked for, before WinForms applied it.</summary>
        /// <remarks>See <see cref="LastAnchoredScroll"/>: a control never shown drops the request itself.</remarks>
        internal int LastDestinationScroll { get; private set; } = -1;

        /// <summary>Scrolls so the page's user-space y sits at the top of the view, mapped as every overlay is.</summary>
        private void ScrollPageYToTop(int pageIndex, float top)
        {
            if (!TryGetBounds(pageIndex, out Rectangle bounds)) return;
            var geo = ViewGeometry(pageIndex);
            RectangleF at = MapPdfRectToCanvas(pageIndex, bounds, geo.CropX0, top, 0, 0);
            int y = Clamp(SaturatingCast.ToInt(Math.Round(at.Y)), bounds.Top, bounds.Bottom);
            LastDestinationScroll = y;
            _surface.AutoScrollPosition = new Point(-_surface.AutoScrollPosition.X, Math.Max(0, y));
            _surface.Invalidate();
            UpdateCurrentBookmark();
        }

        private void GoToPage(int index, bool forceScroll)
        {
            if (_doc == null || _pageCount == 0) return;
            index = Clamp(index, 0, _pageCount - 1);

            if (_layoutMode == PdfViewLayoutMode.SinglePage)
            {
                bool changed = index != _currentPage;
                _currentPage = index;
                ApplyZoomMode();          // a fit follows the page shown, whatever its size
                RebuildLayout();
                _surface.AutoScrollPosition = new Point(0, 0);
                _surface.Invalidate();
                if (changed || forceScroll) RaisePageChanged();
            }
            else
            {
                // Guarded the same way the SinglePage branch above is: a navigation that does not
                // move — a bookmark targeting the page already displayed — raises no PageChanged.
                bool changed = index != _currentPage;
                _currentPage = index;
                if (forceScroll && index < _layout.Length)
                {
                    var b = _layout[index].Bounds;
                    _surface.AutoScrollPosition = new Point(0, Math.Max(0, b.Top - PageMargin));
                }
                _surface.Invalidate();
                if (changed) RaisePageChanged();
            }
            UpdatePageBox();
            HighlightCurrentThumb();
            UpdateCurrentBookmark();
        }

        private void RaisePageChanged()
        {
            UpdateCommandStates();
            PageChanged?.Invoke(this, new PdfViewerPageChangedEventArgs(_currentPage, _pageCount));
        }

        // ── Public API: zoom ──────────────────────────────────────────────────

        public void ZoomIn() => SetZoom(_zoom * ZoomStep, ViewportCentre());
        public void ZoomOut() => SetZoom(_zoom / ZoomStep, ViewportCentre());
        public void ActualSize() => SetZoom(1.0, ViewportCentre());

        /// <summary>
        /// Fits the current page's width to the viewport, and keeps it fitted
        /// (<see cref="ZoomMode"/> becomes <see cref="PdfZoomMode.FitWidth"/>).
        /// </summary>
        public void FitWidth() => ZoomMode = PdfZoomMode.FitWidth;

        /// <summary>
        /// Fits the whole current page within the viewport, and keeps it fitted
        /// (<see cref="ZoomMode"/> becomes <see cref="PdfZoomMode.FitPage"/>).
        /// </summary>
        public void FitPage() => ZoomMode = PdfZoomMode.FitPage;

        /// <summary>
        /// Applies <see cref="ZoomMode"/>'s fit to the current page; nothing under
        /// <see cref="PdfZoomMode.Custom"/>, with no document, or before the view has a size.
        /// </summary>
        private void ApplyZoomMode()
        {
            if (_zoomMode == PdfZoomMode.Custom || _doc == null) return;
            var geo = ViewGeometry(_currentPage);
            float widthPts = geo.DisplayWidthPts, heightPts = geo.DisplayHeightPts;
            int spineGap = 0;
            if (_pageSpread != PdfPageSpread.None)
            {
                // The row is fitted: two slots of its widest page, either side of the spine's gap.
                int row = RowOf(_currentPage);
                SpreadExtentPts(row, row, out float slotPts, out heightPts);
                widthPts = 2 * slotPts;
                spineGap = Gap;
            }
            // The fit is a zoom, and a zoom is reckoned at 96 dpi: the view's pixels are counted at
            // that resolution, or a 200% display would fit the page into twice the view.
            float availW = (float)((_surface.ClientSize.Width - 2 * PageMargin - spineGap) / _deviceScale);
            float availH = (float)((_surface.ClientSize.Height - 2 * PageMargin) / _deviceScale);
            if (_zoomMode == PdfZoomMode.FitWidth)
            {
                if (availW < 1 || widthPts < 1) return;
                SetZoom(ZoomMath.FitWidth(availW, widthPts), anchor: null, _zoomMode);
            }
            else
            {
                if (availW < 1 || availH < 1) return;
                SetZoom(ZoomMath.FitPage(availW, availH, widthPts, heightPts),
                    anchor: null, _zoomMode);
            }
        }

        /// <summary>The viewport's centre, the anchor for toolbar and keyboard zoom.</summary>
        private PointF ViewportCentre()
            => new PointF(_surface.ClientSize.Width / 2f, _surface.ClientSize.Height / 2f);

        /// <summary>
        /// Zooms by one step with the canvas point under <paramref name="viewportPoint"/> held there —
        /// the Ctrl+wheel path, where that point is the cursor.
        /// </summary>
        internal void ZoomStepAt(bool zoomIn, Point viewportPoint)
            => SetZoom(zoomIn ? _zoom * ZoomStep : _zoom / ZoomStep, viewportPoint);

        /// <summary>
        /// The scroll offset the last anchored zoom asked for, before WinForms applied (or, on a control
        /// with no window, ignored) it.
        /// </summary>
        /// <remarks>
        /// A control that has never been shown silently drops <c>AutoScrollPosition</c>, so a test can
        /// only see where an anchored zoom <i>meant</i> to scroll by asking for the request itself.
        /// </remarks>
        internal Point LastAnchoredScroll { get; private set; }

        /// <param name="value">The zoom factor asked for, before clamping.</param>
        /// <param name="anchor">
        /// A point in the viewport whose canvas position is kept there, or null for the fits, which
        /// set their own scroll.
        /// </param>
        /// <param name="mode">
        /// The zoom mode this zoom belongs to: <see cref="PdfZoomMode.Custom"/> for every explicit
        /// zoom, so a fit ends when the user picks a factor; the fit itself passes its own.
        /// </param>
        private void SetZoom(double value, PointF? anchor, PdfZoomMode mode = PdfZoomMode.Custom)
        {
            _zoomMode = mode;
            double clamped = Math.Min(MaxZoom, Math.Max(MinZoom, value));
            if (Math.Abs(clamped - _zoom) < ZoomEpsilon) { UpdateZoomCombo(); return; }

            // Whether to re-anchor afterwards is decided from the pre-rebuild state.
            bool recentre = anchor.HasValue && _doc != null && _layout.Length > 0;

            // The centre fraction has to be READ BEFORE the rebuild, because the rebuild is what
            // changes the canvas it is a fraction of. Hard-coding it to 0.5 afterwards would not be
            // "approximate anchoring": in Continuous layout the canvas is the whole document, so
            // every zoom step would move the reader to the middle page.
            double fx = 0.5, fy = 0.5;
            PointF at = anchor ?? default;
            if (recentre)
            {
                fx = ZoomMath.AnchorFraction(-_surface.AutoScrollPosition.X, at.X, _surface.AutoScrollMinSize.Width);
                fy = ZoomMath.AnchorFraction(-_surface.AutoScrollPosition.Y, at.Y, _surface.AutoScrollMinSize.Height);
            }
            // A fit has no anchor of its own: the reader's place is the page point at the top of the
            // view, which the pixel offset alone would not keep — every page changes height.
            PageAnchor? reading = !anchor.HasValue ? CaptureAnchor(new PointF(_surface.ClientSize.Width / 2f, 0f)) : null;

            _zoom = clamped;
            // In the background the pages in view keep their old bitmaps as previews until they render
            // anew; rendering as it paints, there is nothing to show meanwhile.
            if (RendersInBackground) RetireRenderCacheForZoom(RenderDpi);
            else ClearRenderCache();
            RebuildLayout();
            UpdateZoomCombo();

            if (reading.HasValue) RestoreAnchor(reading.Value);
            else if (recentre && _layout.Length > 0)
            {
                // The document point that was under the anchor is not preserved exactly — only the
                // anchor's fraction of the canvas is, because the page margins do not scale.
                int newX = ZoomMath.ScrollForAnchorFraction(fx, at.X,
                    _surface.ClientSize.Width, _surface.AutoScrollMinSize.Width);
                int newY = ZoomMath.ScrollForAnchorFraction(fy, at.Y,
                    _surface.ClientSize.Height, _surface.AutoScrollMinSize.Height);
                LastAnchoredScroll = new Point(newX, newY);
                _surface.AutoScrollPosition = new Point(Math.Max(0, newX), Math.Max(0, newY));
            }
            _surface.Invalidate();
            UpdateCurrentPageFromScroll();
            UpdateCurrentBookmark();
        }

        /// <summary>
        /// A point of the canvas held while the layout changes under it: the page it is on, where on
        /// that page as fractions of the page's size, and — in the margin or the gap above a page,
        /// which do not scale — how many pixels above the page's top it is.
        /// </summary>
        private readonly struct PageAnchor
        {
            public PageAnchor(int page, float fx, float fy, float gapPx, PointF viewportPoint)
            {
                Page = page; Fx = fx; Fy = fy; GapPx = gapPx; ViewportPoint = viewportPoint;
            }
            public readonly int Page;
            public readonly float Fx, Fy, GapPx;
            /// <summary>Where in the viewport the point is, and is put back.</summary>
            public readonly PointF ViewportPoint;
        }

        /// <summary>The page point under <paramref name="viewportPoint"/>; null with nothing laid out.</summary>
        private PageAnchor? CaptureAnchor(PointF viewportPoint)
        {
            if (_doc == null || _layout.Length == 0) return null;
            float vx = -_surface.AutoScrollPosition.X + viewportPoint.X;
            float vy = -_surface.AutoScrollPosition.Y + viewportPoint.Y;
            PageLayoutInfo info = _layout[_layout.Length - 1];
            foreach (var l in _layout)
                if (vy <= l.Bounds.Bottom) { info = l; break; }   // on this page, or in the gap above it
            Rectangle b = info.Bounds;
            float fx = (vx - b.Left) / Math.Max(1, b.Width);
            float fy = vy < b.Top ? 0f : Math.Min(1f, (vy - b.Top) / Math.Max(1, b.Height));
            float gap = vy < b.Top ? vy - b.Top : 0f;
            return new PageAnchor(info.PageIndex, fx, fy, gap, viewportPoint);
        }

        /// <summary>Scrolls so the anchored page point is back under its viewport point.</summary>
        private void RestoreAnchor(PageAnchor a)
        {
            if (!TryGetBounds(a.Page, out Rectangle b)) return;
            double x = b.Left + a.Fx * b.Width - a.ViewportPoint.X;
            double y = b.Top + a.Fy * b.Height + a.GapPx - a.ViewportPoint.Y;
            int maxX = Math.Max(0, _surface.AutoScrollMinSize.Width - _surface.ClientSize.Width);
            int maxY = Math.Max(0, _surface.AutoScrollMinSize.Height - _surface.ClientSize.Height);
            var target = new Point(Clamp(SaturatingCast.ToInt(Math.Round(x)), 0, maxX),
                                   Clamp(SaturatingCast.ToInt(Math.Round(y)), 0, maxY));
            LastAnchoredScroll = target;
            _surface.AutoScrollPosition = target;
        }

        // ── Public API: misc ──────────────────────────────────────────────────

        /// <summary>Opens the find bar (and runs the search if a term is supplied).</summary>
        public void Find(string? term = null)
        {
            if (!Has(PdfViewerFeatures.Search)) return;
            ShowFindBar();
            if (!string.IsNullOrEmpty(term)) { _findBox.Text = term; }
            _findBox.Focus();
            _findBox.SelectAll();
        }

        // ── Layout ────────────────────────────────────────────────────────────

        private void RebuildLayout()
        {
            // A live field editor sits at the old layout's coordinates: its value is kept, it is not.
            EndFieldEdit(commit: true);
            if (_doc == null || _pageCount == 0)
            {
                _layout = Array.Empty<PageLayoutInfo>();
                _surface.AutoScrollMinSize = Size.Empty;
                return;
            }

            float scale = RenderScale;
            int viewportW = Math.Max(1, _surface.ClientSize.Width);
            int viewportH = Math.Max(1, _surface.ClientSize.Height);

            if (_pageSpread != PdfPageSpread.None)
            {
                int row = RowOf(_currentPage);
                if (_layoutMode == PdfViewLayoutMode.Continuous) BuildSpreadLayout(scale, viewportW, viewportH, 0, RowCount - 1);
                else BuildSpreadLayout(scale, viewportW, viewportH, row, row);
            }
            else if (_layoutMode == PdfViewLayoutMode.Continuous)
            {
                var sizes = new Size[_pageCount];
                int maxW = 1;
                for (int i = 0; i < _pageCount; i++)
                {
                    var geo = ViewGeometry(i);
                    int w = Math.Max(1, SaturatingCast.ToInt(Math.Round(geo.DisplayWidthPts * scale)));
                    int h = Math.Max(1, SaturatingCast.ToInt(Math.Round(geo.DisplayHeightPts * scale)));
                    sizes[i] = new Size(w, h);
                    if (w > maxW) maxW = w;
                }

                int canvasW = Math.Max(maxW + 2 * PageMargin, viewportW);
                var list = new PageLayoutInfo[_pageCount];
                int y = PageMargin;
                for (int i = 0; i < _pageCount; i++)
                {
                    int x = (canvasW - sizes[i].Width) / 2;
                    list[i] = new PageLayoutInfo
                    {
                        PageIndex = i,
                        Bounds = new Rectangle(x, y, sizes[i].Width, sizes[i].Height)
                    };
                    y += sizes[i].Height + Gap;
                }
                int canvasH = Math.Max(y - Gap + PageMargin, viewportH);
                _layout = list;
                _surface.AutoScrollMinSize = new Size(canvasW, canvasH);
            }
            else
            {
                var geo = ViewGeometry(_currentPage);
                int w = Math.Max(1, SaturatingCast.ToInt(Math.Round(geo.DisplayWidthPts * scale)));
                int h = Math.Max(1, SaturatingCast.ToInt(Math.Round(geo.DisplayHeightPts * scale)));
                int canvasW = Math.Max(w + 2 * PageMargin, viewportW);
                int canvasH = Math.Max(h + 2 * PageMargin, viewportH);
                int x = (canvasW - w) / 2;
                int yy = (canvasH - h) / 2;
                _layout = new[]
                {
                    new PageLayoutInfo { PageIndex = _currentPage, Bounds = new Rectangle(x, yy, w, h) }
                };
                _surface.AutoScrollMinSize = new Size(canvasW, canvasH);
            }
        }

        private Rectangle VisibleVirtualRect =>
            new Rectangle(-_surface.AutoScrollPosition.X, -_surface.AutoScrollPosition.Y,
                          _surface.ClientSize.Width, _surface.ClientSize.Height);

        private Point ToVirtual(Point client) =>
            new Point(client.X - _surface.AutoScrollPosition.X, client.Y - _surface.AutoScrollPosition.Y);

        /// <summary>Finds the laid-out page whose bounds contain a virtual-space point.</summary>
        private int PageAtVirtual(Point v)
        {
            for (int i = 0; i < _layout.Length; i++)
                if (_layout[i].Bounds.Contains(v)) return _layout[i].PageIndex;
            return -1;
        }

        /// <summary>The surface scrolled, by its scroll bar or the wheel: the view, the current page and its bookmark follow.</summary>
        private void OnSurfaceScrolled()
        {
            _surface.Invalidate();
            UpdateCurrentPageFromScroll();
            UpdateCurrentBookmark();
        }

        private void UpdateCurrentPageFromScroll()
        {
            if (_doc == null || _layoutMode != PdfViewLayoutMode.Continuous || _layout.Length == 0) return;
            var vis = VisibleVirtualRect;
            int centerY = vis.Y + vis.Height / 2;
            int best = _currentPage;
            for (int i = 0; i < _layout.Length; i++)
            {
                var b = _layout[i].Bounds;
                if (centerY >= b.Top && centerY <= b.Bottom) { best = _layout[i].PageIndex; break; }
                if (centerY < b.Top) { best = _layout[i].PageIndex; break; }
            }
            // Two to a row: either page of the row in view is being read; the one already current stays.
            if (_pageSpread != PdfPageSpread.None && RowOf(best) == RowOf(_currentPage)) best = _currentPage;
            if (best != _currentPage)
            {
                _currentPage = best;
                UpdatePageBox();
                HighlightCurrentThumb();
                RaisePageChanged();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private void UpdateCommandStates()
        {
            bool has = _doc != null;
            _btnPrev.Enabled = has && CanStep(-1);
            _btnNext.Enabled = has && CanStep(+1);
            _btnZoomIn.Enabled = _btnZoomOut.Enabled = _zoomCombo.Enabled = has;
            _btnFitWidth.Enabled = _btnFitPage.Enabled = has;
            _btnFind.Enabled = has;
            _btnPrint.Enabled = has && Permits(Tetitulz.Pdf.Base.Encryption.PdfPermissions.Print);
            _pageBox.Enabled = has;
            UpdateSaveButton();
            UpdateFloatingControls();
        }

        private void UpdatePageBox()
        {
            _pageBox.Text = _pageCount > 0 ? PageBoxText(_currentPage) : "";
            UpdatePageCountLabel();
            UpdateSurfaceAccessibleName();
            UpdateFloatingControls();
        }

        /// <remarks>
        /// Releases the document as well as the bitmap cache. Dropping only the bitmaps left
        /// <c>_doc</c> — which owns the full decrypted PDF byte array — plus the per-glyph text
        /// index of every page ever searched, the match list and the last selection reachable from
        /// a <b>disposed</b> control, so a host that keeps the reference (a closed-tab list, a
        /// Controls collection that is never cleared) held all of it for the lifetime of the form.
        /// For a password-protected file that is the plaintext.
        /// <para>The buffer is deliberately not zeroed: it is a <c>byte[]</c> the library hands out
        /// and may already have been copied, so wiping this one reference would promise more than
        /// it delivers. A host with plaintext-lifetime requirements must own that itself.</para>
        /// </remarks>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReplaceDocument(null);
                _pageCount = 0;
                _layout = Array.Empty<PageLayoutInfo>();
                _textCache.Clear();
                _pageTexts.Clear();
                CancelSearch();
                _findMatches.Clear();
                _findIndex = -1;
                _selectedText = "";
                _selection.Clear();
                ClearRenderCache();
                _worker?.Dispose();
                _worker = null;
                ClearBookmarkTree();
                _linkTip.Dispose();
                _findTip.Dispose();
                _pageMenu.Dispose();
                _commentTip.Dispose();
                _signatureTip.Dispose();
                _paneTip.Dispose();
                _ringTimer?.Dispose();
                _ringTimer = null;
                DisposeFloatingControls();
                DisposeThemeResources();
                DisposePageShadow();
            }
            base.Dispose(disposing);
        }
    }
}
