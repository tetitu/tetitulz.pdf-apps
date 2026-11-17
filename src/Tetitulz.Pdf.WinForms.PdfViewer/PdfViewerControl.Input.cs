// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations;
using Tetitulz.Pdf.Base.Operations.Extraction;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    public sealed partial class PdfViewerControl
    {
        // ── Selection tunables ────────────────────────────────────────────────

        /// <summary>Manhattan distance (device pixels) a press may travel and still count as a click.</summary>
        private const int ClickSlopPx = 3;

        /// <summary>Below this drag height (device pixels) the gesture is treated as a flat, single-line drag.</summary>
        private const int FlatDragHeightPx = 4;

        /// <summary>Line height, in PDF points, assumed for a flat drag so it selects the whole line.</summary>
        private const float FlatDragLineHeightPts = 14f;

        /// <summary>Vertical gap, as a fraction of glyph height, that starts a new line in the copied text.</summary>
        private const float NewLineGapFraction = 0.5f;

        /// <summary>Horizontal gap, as a fraction of glyph height, that inserts a space in the copied text.</summary>
        private const float WordGapFraction = 0.35f;

        // ── Mouse ─────────────────────────────────────────────────────────────

        internal void Surface_MouseDown(object? sender, MouseEventArgs e)
        {
            // A host that takes the press (a stamp it is about to drag) owns the gesture to its release.
            if (RaisePageMouse(PageMouseDown, e))
            {
                _hostGesture = true;
                _surface.Focus();
                return;
            }
            if (e.Button != MouseButtons.Left || _doc == null) return;
            // A field takes the press before a link or a selection could: its editor is the answer.
            if (BeginSigning(ToVirtual(e.Location))) return;
            if (OpenSignature(ToVirtual(e.Location))) return;
            if (BeginFieldInteraction(ToVirtual(e.Location))) return;
            _surface.Focus();

            Point v = ToVirtual(e.Location);
            _pendingLink = HitTestLink(v, forClick: true);
            _dragging = true;
            _draggedFar = false;
            _dragStartV = _dragEndV = v;
            _dragRectangle = (ModifierKeys & Keys.Alt) != 0;    // Alt+drag: a column, as Acrobat selects one
            int clicks = CountClicks(e);
            ResetSelection(invalidate: false);
            if (clicks >= 2 && _pendingLink == null && Has(PdfViewerFeatures.TextSelection))
            {
                SelectAround(v, clicks >= 3 ? TextUnit.Line : TextUnit.Word);
                _dragging = false;                               // the gesture is the clicks, not a drag
            }
            _surface.Invalidate();
        }

        private DateTime _lastClickAt;
        private Point _lastClickAt_Point;
        private int _clickRun;

        /// <summary>
        /// How many presses in quick succession this one ends — WinForms reports a double click and no
        /// more, and a triple click selects a line.
        /// </summary>
        private int CountClicks(MouseEventArgs e)
        {
            var now = DateTime.UtcNow;
            bool near = Math.Abs(e.X - _lastClickAt_Point.X) <= SystemInformation.DoubleClickSize.Width
                        && Math.Abs(e.Y - _lastClickAt_Point.Y) <= SystemInformation.DoubleClickSize.Height;
            _clickRun = near && (now - _lastClickAt).TotalMilliseconds <= SystemInformation.DoubleClickTime ? _clickRun + 1 : 1;
            _lastClickAt = now;
            _lastClickAt_Point = e.Location;
            return Math.Max(_clickRun, e.Clicks);
        }

        internal void Surface_MouseMove(object? sender, MouseEventArgs e)
        {
            if (RaisePageMouse(PageMouseMove, e) || _hostGesture) return;
            Point v = ToVirtual(e.Location);

            if (_dragging)
            {
                _dragEndV = v;
                if (!_draggedFar && Manhattan(_dragStartV, v) > ClickSlopPx)
                {
                    _draggedFar = true;
                    _pendingLink = null;     // it's a drag-select, not a click
                }
                if (_draggedFar && Has(PdfViewerFeatures.TextSelection)) UpdateSelection();
                _surface.Invalidate();
                return;
            }

            if (HitTestField(v) != null || HitTestUnsignedSignature(v) != null) { _surface.Cursor = Cursors.Hand; return; }

            // Hover: link → hand cursor + highlight.
            PdfLink? link = HitTestLink(v);
            if (!ReferenceEquals(link, _hoverLink))
            {
                _hoverLink = link;
                _surface.Invalidate();
                _linkTip.SetToolTip(_surface, link != null && ShowLinkToolTips ? DescribeLink(link) : null);
            }
            // A comment under no link describes itself; over a link the link's tip is the answer.
            UpdateCommentHover(link == null ? v : new Point(int.MinValue, int.MinValue));
            UpdateSignatureHover(link == null ? v : new Point(int.MinValue, int.MinValue));
            _surface.Cursor = link != null || _hoverSignature != null ? Cursors.Hand
                : Has(PdfViewerFeatures.TextSelection) ? Cursors.IBeam : Cursors.Default;
        }

        internal void Surface_MouseUp(object? sender, MouseEventArgs e)
        {
            if (_hostGesture)
            {
                _hostGesture = false;
                RaisePageMouse(PageMouseUp, e);
                return;
            }
            if (RaisePageMouse(PageMouseUp, e)) return;
            if (!_dragging) return;
            _dragging = false;
            _dragEndV = ToVirtual(e.Location);

            if (!_draggedFar && _pendingLink != null)
            {
                var link = _pendingLink;
                _pendingLink = null;
                FollowLink(link);
                return;
            }
            _pendingLink = null;
            // A click on a comment chooses it — its row in the list, its outline on the page.
            if (!_draggedFar && HitTestComment(_dragEndV) is PdfAnnotationInfo clicked) SelectComment(clicked);
            if (!Has(PdfViewerFeatures.TextSelection)) { _surface.Invalidate(); return; }

            // A click selects nothing; only a drag does.
            if (_draggedFar) UpdateSelection();
            BuildSelectedText();
            if (_selection.Count > 0) SelectionChanged?.Invoke(this, EventArgs.Empty);
            if (!string.IsNullOrEmpty(_selectedText))
                _statusLabel.Text = PdfViewerStrings.Format(
                    _strings.SelectedCharactersFormat, _selectedText.Length);
            else if (_draggedFar && _selection.Count == 0)
            {
                string? err = _doc?.FirstExtractionErrorIn(_strings);
                if (!string.IsNullOrEmpty(err)) _statusLabel.Text = err;
            }
            _surface.Invalidate();
        }

        private bool SurfaceMouseWheel(MouseEventArgs e) => SurfaceMouseWheel(e, ModifierKeys);

        /// <summary>
        /// Ctrl+wheel zooms one step anchored at the cursor; anything else scrolls. The modifiers are a
        /// parameter so a test can press Ctrl without a keyboard.
        /// </summary>
        internal bool SurfaceMouseWheel(MouseEventArgs e, Keys modifiers)
        {
            // In single-page layout the wheel turns the page past either end of it, as every reader does.
            if ((modifiers & Keys.Control) == 0)
                return _doc != null && _layoutMode == PdfViewLayoutMode.SinglePage && e.Delta != 0 && TurnAtPageEnd(e.Delta < 0);
            bool zoomIn = e.Delta > 0;
            RunCommand(zoomIn ? PdfViewerCommand.ZoomIn : PdfViewerCommand.ZoomOut,
                () => ZoomStepAt(zoomIn, e.Location));   // anchored at the cursor, not the viewport centre
            return true;   // consumed: do not scroll
        }

        // ── Links ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The link under a canvas point. A link whose action the viewer does not perform
        /// (<see cref="PdfLinkKind.Unknown"/>: JavaScript, SubmitForm, …) is passed over by a hover,
        /// which shows nothing for it, and found by a click (<paramref name="forClick"/>), so
        /// <see cref="LinkClicked"/> hands the host the action to judge while the viewer runs nothing.
        /// </summary>
        private PdfLink? HitTestLink(Point v, bool forClick = false)
        {
            // The one door to a link under the pointer, so Features.Links off ends hover and click alike.
            if (_doc == null || !Has(PdfViewerFeatures.Links)) return null;
            int page = PageAtVirtual(v);
            if (page < 0) return null;
            if (!TryGetBounds(page, out Rectangle bounds)) return null;

            foreach (var link in _doc.LinksFor(page))
            {
                if (link.Kind == PdfLinkKind.Unknown && !forClick) continue;
                var r = MapLinkToCanvas(link, bounds);
                if (r.Contains(v)) return link;
            }
            return null;
        }

        private void FollowLink(PdfLink link)
        {
            var args = new PdfViewerLinkClickedEventArgs(link.Kind, link.Uri, link.TargetPageIndex)
            {
                TargetTop = link.TargetTop,
                NamedAction = link.NamedAction,
                File = link.File,
            };
            LinkClicked?.Invoke(this, args);
            if (args.Handled) return;

            switch (link.Kind)
            {
                case PdfLinkKind.Uri when !string.IsNullOrEmpty(link.Uri):
                    OpenUriSafely(link.Uri!);
                    break;
                case PdfLinkKind.GoTo when link.TargetPageIndex >= 0:
                    JumpTo(link.TargetPageIndex, link.TargetTop);
                    break;
                case PdfLinkKind.Named:
                    RunNamedAction(link.NamedAction);
                    break;
                case PdfLinkKind.RemoteGoTo:
                case PdfLinkKind.Launch:
                    // Never opened by the viewer: a file a document names is the document's choice,
                    // not the user's. A host that wants it handles LinkClicked.
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.ExternalFileLinkFormat, link.File ?? "");
                    break;
                // Unknown — a JavaScript or SubmitForm action — runs nothing: the event above was the
                // host's chance to judge it.
            }
        }

        /// <summary>
        /// The four named actions every reader performs (§12.6.4.11, Table 211); any other name is a
        /// viewer's own and is reported, not guessed at.
        /// </summary>
        private void RunNamedAction(string? name)
        {
            switch (name)
            {
                case "NextPage":  NextPage(); break;
                case "PrevPage":  PreviousPage(); break;
                case "FirstPage": GoToPage(0); break;
                case "LastPage":  GoToPage(_pageCount - 1); break;
                default:
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.UnsupportedLinkActionFormat, name ?? "");
                    break;
            }
        }

        /// <summary>What a link's tooltip says: the address, the page, the action or the file.</summary>
        internal string? DescribeLink(PdfLink link) => link.Kind switch
        {
            PdfLinkKind.Uri => link.Uri,
            PdfLinkKind.GoTo when link.TargetPageIndex >= 0
                => PdfViewerStrings.Format(_strings.LinkToPageFormat, link.TargetPageIndex + 1),
            PdfLinkKind.Named => link.NamedAction,
            PdfLinkKind.RemoteGoTo or PdfLinkKind.Launch => link.File,
            _ => null,
        };

        private void OpenUriSafely(string uri)
        {
            // Only follow web / mail schemes by default; never launch arbitrary handlers.
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp &&
                 parsed.Scheme != Uri.UriSchemeHttps &&
                 parsed.Scheme != Uri.UriSchemeMailto))
            {
                _statusLabel.Text = PdfViewerStrings.Format(_strings.BlockedLinkFormat, uri);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _statusLabel.Text = PdfViewerStrings.Format(_strings.LinkFailedFormat, ex.Message);
            }
        }

        // ── Selection ─────────────────────────────────────────────────────────

        /// <summary>Drops the selection; raises <see cref="SelectionChanged"/> when there was one.</summary>
        private void ResetSelection(bool invalidate = true)
        {
            bool had = _selection.Count > 0 || _selectedText.Length > 0;
            _selectedText = "";
            _selection.Clear();
            if (invalidate) _surface.Invalidate();
            if (had) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private RectangleF DragRectVirtual() => SelectionBand(_dragStartV, _dragEndV, RenderScale);

        /// <summary>
        /// The selection rectangle in canvas coordinates for a drag from <paramref name="start"/> to
        /// <paramref name="end"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>A flat drag's band is CENTRED on the pointer, not hung below it.</b> Glyph
        /// rectangles are anchored to the baseline and rise above it, so a band that started at the
        /// pointer and extended downward contained the centres of the line <i>below</i> the one
        /// being swept and none of the line under the cursor: a horizontal drag along a line
        /// highlighted the next line down, and Ctrl+C copied that line's text.</para>
        /// <para>Static and pure so the geometry can be asserted against real glyph metrics rather
        /// than through a synthesised mouse gesture.</para>
        /// </remarks>
        internal static RectangleF SelectionBand(Point start, Point end, float renderScale)
        {
            float x = Math.Min(start.X, end.X);
            float y = Math.Min(start.Y, end.Y);
            float w = Math.Abs(end.X - start.X);
            float h = Math.Abs(end.Y - start.Y);
            // A flat drag selects the whole line under the cursor rather than nothing.
            if (h < FlatDragHeightPx)
            {
                h = renderScale * FlatDragLineHeightPts;
                y -= h / 2f;
            }
            return new RectangleF(x, y, Math.Max(w, 1), h);
        }

        private void UpdateSelection()
        {
            _selection.Clear();
            var glyphs = _dragRectangle || _textSelectionMode == PdfTextSelectionMode.Rectangle
                ? GlyphsInBand() : GlyphsInReadingOrder(_dragStartV, _dragEndV);
            foreach (var (page, index) in glyphs)
            {
                if (!_selection.TryGetValue(page, out var list)) _selection[page] = list = new List<int>();
                list.Add(index);
            }
        }

        /// <summary>
        /// The glyphs whose centres the drag band holds, on every laid-out page the band crosses, in
        /// page order and each page's reading order — a drag from one page into the next selects on
        /// both, as it did on the first alone.
        /// </summary>
        private List<(int Page, int Index)> GlyphsInBand()
        {
            var selected = new List<(int, int)>();
            var sel = DragRectVirtual();
            var layout = new List<(int, Rectangle)>(_layout.Length);
            foreach (var l in _layout) layout.Add((l.PageIndex, l.Bounds));
            foreach (int page in PagesUnder(layout, sel))
            {
                if (!TryGetBounds(page, out Rectangle bounds)) continue;
                var idx = GetTextIndex(page);
                for (int i = 0; i < idx.Chars.Length; i++)
                {
                    var r = MapCharToCanvas(page, bounds, idx.Chars[i]);
                    if (sel.Contains(r.X + r.Width / 2, r.Y + r.Height / 2)) selected.Add((page, i));
                }
            }
            return selected;
        }

        /// <summary>
        /// The glyphs from the one nearest <paramref name="from"/> to the one nearest
        /// <paramref name="to"/>, in reading order across the pages between — what every reader
        /// selects: a sentence that wraps is one selection, not a rectangle of two half-lines.
        /// </summary>
        /// <remarks>
        /// The per-page index is in reading order already (structured extraction's lines, words and
        /// glyphs), so the run is a range of it on each page: from the first glyph to the page's end,
        /// every glyph of a page between, and from the page's start to the last glyph.
        /// </remarks>
        private List<(int Page, int Index)> GlyphsInReadingOrder(Point from, Point to)
        {
            var selected = new List<(int, int)>();
            var a = GlyphNearest(from);
            var b = GlyphNearest(to);
            if (a == null || b == null) return selected;
            var (first, last) = (a.Value.Page, a.Value.Index).CompareTo((b.Value.Page, b.Value.Index)) <= 0 ? (a.Value, b.Value) : (b.Value, a.Value);
            for (int page = first.Page; page <= last.Page; page++)
            {
                var idx = GetTextIndex(page);
                int start = page == first.Page ? first.Index : 0;
                int end = page == last.Page ? last.Index : idx.Chars.Length - 1;
                for (int i = start; i <= end && i < idx.Chars.Length; i++) selected.Add((page, i));
            }
            return selected;
        }

        /// <summary>
        /// The glyph a canvas point means: on the page under it, or the nearest laid-out page; there,
        /// the glyph that holds it, else the nearest, a line's height counting four times its width so
        /// a point beside a line means that line. Null when no page has text.
        /// </summary>
        private (int Page, int Index)? GlyphNearest(Point v)
        {
            int page = PageAtVirtual(v);
            if (page < 0)
            {
                long best = long.MaxValue;
                foreach (var l in _layout)
                {
                    long d = Math.Abs((long)v.Y - Math.Max(l.Bounds.Top, Math.Min(v.Y, l.Bounds.Bottom)));
                    if (d < best) { best = d; page = l.PageIndex; }
                }
            }
            if (page < 0 || !TryGetBounds(page, out Rectangle bounds)) return null;
            var idx = GetTextIndex(page);
            int found = -1;
            double nearest = double.MaxValue;
            for (int i = 0; i < idx.Chars.Length; i++)
            {
                var r = MapCharToCanvas(page, bounds, idx.Chars[i]);
                double dx = v.X < r.Left ? r.Left - v.X : v.X > r.Right ? v.X - r.Right : 0;
                double dy = v.Y < r.Top ? r.Top - v.Y : v.Y > r.Bottom ? v.Y - r.Bottom : 0;
                double d = dy * 4 + dx;
                if (d < nearest) { nearest = d; found = i; }
                if (d == 0) break;
            }
            return found < 0 ? null : (page, found);
        }

        /// <summary>The selected glyphs, page by page in reading order, with each one's box on the canvas now.</summary>
        private List<(int Page, Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar Glyph, RectangleF Box)> SelectedGlyphs()
        {
            var selected = new List<(int, Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar, RectangleF)>();
            foreach (var kv in _selection)
            {
                if (!TryGetBounds(kv.Key, out Rectangle bounds)) bounds = Rectangle.Empty;
                var idx = GetTextIndex(kv.Key);
                foreach (int i in kv.Value)
                    if (i < idx.Chars.Length) selected.Add((kv.Key, idx.Chars[i].Source, MapCharToCanvas(kv.Key, bounds, idx.Chars[i])));
            }
            return selected;
        }

        /// <summary>
        /// The pages whose canvas bounds the selection band crosses, in page order. Static and pure,
        /// for the reason <see cref="SelectionBand"/> is.
        /// </summary>
        internal static List<int> PagesUnder(IReadOnlyList<(int PageIndex, Rectangle Bounds)> layout, RectangleF band)
        {
            var pages = new List<int>();
            foreach (var (page, bounds) in layout)
                if (band.IntersectsWith(bounds)) pages.Add(page);
            pages.Sort();
            return pages;
        }

        /// <summary>
        /// Selects as a drag from <paramref name="startVirtual"/> to <paramref name="endVirtual"/>
        /// (canvas coordinates) would, and returns the text Ctrl+C would copy — for tests, which have
        /// no mouse.
        /// </summary>
        internal string SelectBetween(Point startVirtual, Point endVirtual)
        {
            _dragStartV = startVirtual;
            _dragEndV = endVirtual;
            UpdateSelection();
            BuildSelectedText();
            return _selectedText;
        }

        /// <summary>How many pages the current selection highlights glyphs on.</summary>
        internal int SelectedPageCount
        {
            get
            {
                int pages = 0;
                foreach (var glyphs in _selection.Values) if (glyphs.Count > 0) pages++;
                return pages;
            }
        }

        /// <summary>What goes between two glyphs the selection kept, in the copied text.</summary>
        private enum GlyphSeparator
        {
            /// <summary>Nothing — the two glyphs are within one word.</summary>
            None,
            /// <summary>A space — there is clear ground between them.</summary>
            Space,
            /// <summary>A line break — the second glyph sits on a later line.</summary>
            NewLine,
        }

        /// <summary>
        /// Whether a space, a line break, or nothing separates two adjacent selected glyphs, given
        /// their canvas rectangles in the order they are read.
        /// </summary>
        /// <remarks>
        /// <b>The horizontal test must not assume which way the line runs.</b>
        /// <c>current.Left - previous.Right</c> is the gap only while reading order advances
        /// rightwards; in Hebrew, Arabic and every other right-to-left script the next glyph is
        /// further <i>left</i>, so that difference is negative for every adjacent pair and no space
        /// would be emitted. Taking whichever side has clear ground answers the question the name
        /// asks — is there a gap between these two boxes — without knowing the direction.
        /// </remarks>
        private static GlyphSeparator SeparatorBetween(RectangleF previous, RectangleF current)
        {
            if (current.Y - previous.Y > current.Height * NewLineGapFraction)
                return GlyphSeparator.NewLine;

            float gap = Math.Max(current.Left - previous.Right, previous.Left - current.Right);
            return gap > current.Height * WordGapFraction ? GlyphSeparator.Space : GlyphSeparator.None;
        }

        /// <summary>
        /// Assembles the clipboard text for a run of selected glyphs, given each one's text and its
        /// canvas rectangle, <b>in reading order</b>.
        /// </summary>
        /// <remarks>
        /// <para>Reading order is what the caller supplies and is not re-derived here:
        /// <c>PdfStructuredTextExtractor</c> sorts a right-to-left line rightmost-first, so the
        /// glyphs already arrive logically ordered and running bidi over them again would reverse
        /// what is already correct. All this decides is what goes <i>between</i> them.</para>
        /// <para>Static and pure so the result can be asserted against real glyph metrics rather
        /// than through a synthesised mouse gesture, the way <see cref="SelectionBand"/> is.</para>
        /// </remarks>
        internal static string JoinSelectedGlyphs(IReadOnlyList<(string Text, RectangleF Box)> glyphs)
        {
            if (glyphs == null || glyphs.Count == 0) return "";

            var sb = new StringBuilder();
            RectangleF? last = null;

            foreach (var (text, box) in glyphs)
            {
                if (last.HasValue)
                {
                    switch (SeparatorBetween(last.Value, box))
                    {
                        case GlyphSeparator.NewLine: sb.Append('\n'); break;
                        case GlyphSeparator.Space: sb.Append(' '); break;
                    }
                }

                sb.Append(text);
                last = box;
            }
            return sb.ToString().Trim();
        }

        private void BuildSelectedText()
        {
            _selectedText = "";
            if (CopyUsesLogicalOrder)
            {
                var selected = new List<(Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar Glyph, RectangleF Box)>();
                foreach (var (_, glyph, box) in SelectedGlyphs()) selected.Add((glyph, box));
                _selectedText = ClipboardTextFor(true, selected);
                return;
            }
            var pages = new List<string>(_selection.Count);
            foreach (var kv in _selection)
            {
                string text = JoinFromIndex(GetTextIndex(kv.Key), kv.Value);
                if (text.Length > 0) pages.Add(text);
            }
            _selectedText = string.Join("\n", pages);
        }

        /// <summary>
        /// A page's selected glyphs as text, separated as the page's own text separates them — the
        /// extractor's word gaps and line breaks — so a copied line keeps its spaces whatever its type
        /// size. A gap where glyphs were passed over (a column) is a space.
        /// </summary>
        /// <param name="index">The page's text index.</param>
        /// <param name="glyphs">Indices into it, ascending.</param>
        private static string JoinFromIndex(PageTextIndex index, List<int> glyphs)
        {
            var sb = new StringBuilder();
            int previous = -1;
            foreach (int i in glyphs)
            {
                if (i >= index.Chars.Length) continue;
                if (previous >= 0)
                {
                    bool newLine = false, space = i > previous + 1;
                    for (int k = index.Ends[previous]; k < index.Starts[i] && k < index.Text.Length; k++)
                    {
                        if (index.Text[k] == '\n') newLine = true;
                        else if (char.IsWhiteSpace(index.Text[k])) space = true;
                    }
                    if (newLine) sb.Append('\n');
                    else if (space) sb.Append(' ');
                }
                sb.Append(index.Chars[i].Text);
                previous = i;
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Picks the ordering rule for a selection — the whole of what
        /// <see cref="CopyUsesLogicalOrder"/> decides.
        /// </summary>
        /// <remarks>
        /// <b>Static and pure, for the reason <see cref="JoinSelectedGlyphs"/> gives:</b> so the
        /// choice can be asserted against real glyph metrics rather than through a synthesised mouse
        /// gesture: without it nothing reaches the decision without a drag, and a mutation that
        /// ignored the property would go unseen.
        /// </remarks>
        internal static string ClipboardTextFor(
            bool useLogicalOrder,
            IReadOnlyList<(Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar Glyph, RectangleF Box)> glyphs)
        {
            if (glyphs == null || glyphs.Count == 0) return "";

            return useLogicalOrder
                ? JoinSelectedGlyphsInLogicalOrder(glyphs)
                : JoinSelectedGlyphs(Texts(glyphs));
        }

        /// <summary>The glyphs as (text, box) pairs, for the geometric path's signature.</summary>
        private static List<(string, RectangleF)> Texts(
            IReadOnlyList<(Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar Glyph, RectangleF Box)> glyphs)
        {
            var pairs = new List<(string, RectangleF)>(glyphs.Count);
            foreach (var (glyph, box) in glyphs) pairs.Add((glyph?.Char ?? "", box));
            return pairs;
        }

        /// <summary>
        /// Assembles the clipboard text line by line, putting each line into reading order through
        /// the same code structured extraction uses.
        /// </summary>
        /// <remarks>
        /// <para><b>This is not "run bidi over the copied string", which is the trap.</b> Storage
        /// order already <i>is</i> reading order for a line that is wholly one direction, so
        /// reordering that reverses what was right. <c>PdfOperations.ToReadingOrder</c> sorts the
        /// glyphs back into <b>visual</b> order first — the input the Unicode bidi algorithm is
        /// defined on — which is what keeps an embedded Latin or numeric run inside Hebrew the
        /// right way round instead of reversing it.</para>
        ///
        /// <para><b>Per line, because visual order is a single X axis.</b> Handing it a whole
        /// multi-line selection would interleave the lines into nonsense. A line ends where the
        /// extractor's own glyph geometry says — the next glyph's baseline more than
        /// <see cref="NewLineGapFraction"/> of its height from the last, either way — in the page's
        /// user space, not in the canvas boxes, which the view's turn moves: at a quarter turn
        /// consecutive glyphs of one line step down the canvas by a box height, so every glyph became
        /// a line, and at a half turn the next line lay above, so no line ever ended and the whole
        /// selection was sorted as one.</para>
        /// </remarks>
        internal static string JoinSelectedGlyphsInLogicalOrder(
            IReadOnlyList<(Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar Glyph, RectangleF Box)> glyphs)
        {
            if (glyphs == null || glyphs.Count == 0) return "";

            var lines = new List<string>();
            var current = new List<Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar>();
            Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar? last = null;

            // One call site, so "which rule orders a line" cannot end up answered twice.
            void FlushLine()
            {
                lines.Add(Tetitulz.Pdf.Base.PdfOperations.ToReadingOrder(current, applyBidi: true));
                current = new List<Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar>();
            }

            foreach (var (glyph, _) in glyphs)
            {
                if (glyph == null) continue;
                // A baseline that moved by more than the fraction of a glyph's height, either way, is
                // another line: a selection runs down the page, and a caller's list may run either way.
                if (last != null && Math.Abs(last.Y - glyph.Y) > glyph.Height * NewLineGapFraction)
                    FlushLine();

                current.Add(glyph);
                last = glyph;
            }

            FlushLine();
            return string.Join("\n", lines).Trim();
        }

        /// <summary>
        /// Puts the selected text on the clipboard, as Ctrl+C does. False when nothing is selected,
        /// <see cref="PdfViewerFeatures.TextSelection"/> is off, or the clipboard refused it (the
        /// status line says why).
        /// </summary>
        public bool CopySelection()
        {
            if (string.IsNullOrEmpty(_selectedText) || !Has(PdfViewerFeatures.TextSelection)) return false;
            if (!Permits(Tetitulz.Pdf.Base.Encryption.PdfPermissions.Copy)) return false;
            try
            {
                Clipboard.SetText(_selectedText);
                _statusLabel.Text = PdfViewerStrings.Format(
                    _strings.CopiedCharactersFormat, _selectedText.Length);
                return true;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = PdfViewerStrings.Format(_strings.CopyFailedFormat, ex.Message);
                return false;
            }
        }

        // ── Keyboard ──────────────────────────────────────────────────────────

        /// <summary>What a command key means, given the control's state. See
        /// <see cref="ClassifyCmdKey"/> for why this is separated from acting on it.</summary>
        internal enum CmdKeyAction
        {
            None, FindNext, FindPrevious, HideFindBar, OpenFile, ShowFindBar, Copy, Print,
            ActualSize, ZoomIn, ZoomOut, PreviousPage, NextPage, Save,
            LineUp, LineDown, LineLeft, LineRight, ScreenUp, ScreenDown, FirstPage, LastPage, Back, Forward,
            SelectAll, RotateClockwise, RotateCounterclockwise,
        }

        /// <summary>
        /// The keyboard <b>policy</b>: which shortcut a key means, given whether the find bar is
        /// open, whether a text field has the focus, and whether a document is loaded. Pure — it
        /// reads no state and changes none.
        /// </summary>
        /// <remarks>
        /// <para><b>Separated from <see cref="ProcessCmdKey"/> so it can be tested at all.</b> The
        /// interesting rule here is a *focus* rule, and the only way to exercise it through
        /// <c>ProcessCmdKey</c> is to give a child control real keyboard focus — which needs a
        /// parented, shown <see cref="Form"/>, and a shown form on an STA thread with no message pump
        /// blocks unpredictably and can <b>wedge the test host</b>. Tests over this method are
        /// deterministic and need no window.</para>
        /// <para><b>The rule this encodes.</b> <c>ProcessCmdKey</c> runs for the whole control
        /// BEFORE the focused child sees the key, and this control owns three editable fields — the
        /// find box, the page-number box and the zoom combo. Intercepting Ctrl+C there would copy the
        /// page's drag-selection over what the user believes they are copying, and Ctrl+O would
        /// discard a half-typed search term. So every document-level shortcut is suppressed while a
        /// text field has the focus.</para>
        /// <para><b>F3 / Shift+F3 are the exception</b>, tested first: "type a term, press F3" is
        /// what the find bar is for, and the find box necessarily has the focus while the term is
        /// being typed. Neither key edits text, so letting the two through costs the text fields
        /// nothing. Escape is *not* in that exception — from inside the find box it is
        /// <c>FindBox_KeyDown</c> that closes the bar.</para>
        /// </remarks>
        /// <param name="keyData">The key and its modifiers.</param>
        /// <param name="findBarVisible">Whether the find bar is open.</param>
        /// <param name="textEntryFocused">Whether one of the control's text fields has the focus.</param>
        /// <param name="documentLoaded">Whether a document is shown.</param>
        /// <param name="pageFocused">
        /// Whether the page surface has the focus. The reading keys (arrows, Space, Home, End) are the
        /// page's only then: in the sidebar's tree they move through the tree.
        /// </param>
        /// <param name="singlePage">Whether the layout shows one page at a time, where Left and Right turn it.</param>
        internal static CmdKeyAction ClassifyCmdKey(
            Keys keyData, bool findBarVisible, bool textEntryFocused, bool documentLoaded,
            bool pageFocused = false, bool singlePage = false)
        {
            if (findBarVisible)
            {
                if (keyData == Keys.F3) return CmdKeyAction.FindNext;
                if (keyData == (Keys.Shift | Keys.F3)) return CmdKeyAction.FindPrevious;
            }

            if (textEntryFocused) return CmdKeyAction.None;

            switch (keyData)
            {
                case Keys.Control | Keys.O: return CmdKeyAction.OpenFile;
                case Keys.Control | Keys.F: return CmdKeyAction.ShowFindBar;
                case Keys.Control | Keys.C: return CmdKeyAction.Copy;
                case Keys.Control | Keys.A: return documentLoaded ? CmdKeyAction.SelectAll : CmdKeyAction.None;
                case Keys.Control | Keys.P: return CmdKeyAction.Print;
                case Keys.Control | Keys.S: return CmdKeyAction.Save;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: return CmdKeyAction.ActualSize;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add: return CmdKeyAction.ZoomIn;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: return CmdKeyAction.ZoomOut;
                // Acrobat's keys for turning the view.
                case Keys.Control | Keys.Shift | Keys.Oemplus:
                case Keys.Control | Keys.Shift | Keys.Add:
                    return documentLoaded ? CmdKeyAction.RotateClockwise : CmdKeyAction.None;
                case Keys.Control | Keys.Shift | Keys.OemMinus:
                case Keys.Control | Keys.Shift | Keys.Subtract:
                    return documentLoaded ? CmdKeyAction.RotateCounterclockwise : CmdKeyAction.None;
            }

            if (findBarVisible && keyData == Keys.Escape) return CmdKeyAction.HideFindBar;

            if (documentLoaded)
            {
                if (keyData == Keys.PageUp) return CmdKeyAction.PreviousPage;
                if (keyData == Keys.PageDown) return CmdKeyAction.NextPage;
                // Back and forward are the browser's keys, whatever has the focus.
                if (keyData == (Keys.Alt | Keys.Left)) return CmdKeyAction.Back;
                if (keyData == (Keys.Alt | Keys.Right)) return CmdKeyAction.Forward;
            }

            if (documentLoaded && pageFocused)
            {
                switch (keyData)
                {
                    case Keys.Up: return CmdKeyAction.LineUp;
                    case Keys.Down: return CmdKeyAction.LineDown;
                    case Keys.Left: return singlePage ? CmdKeyAction.PreviousPage : CmdKeyAction.LineLeft;
                    case Keys.Right: return singlePage ? CmdKeyAction.NextPage : CmdKeyAction.LineRight;
                    case Keys.Space: return CmdKeyAction.ScreenDown;
                    case Keys.Shift | Keys.Space: return CmdKeyAction.ScreenUp;
                    case Keys.Home:
                    case Keys.Control | Keys.Home: return CmdKeyAction.FirstPage;
                    case Keys.End:
                    case Keys.Control | Keys.End: return CmdKeyAction.LastPage;
                }
            }

            return CmdKeyAction.None;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Policy in ClassifyCmdKey, mechanism in ExecuteCmdKey; this only joins them.
            if (ExecuteCmdKey(ClassifyCmdKey(keyData, _findBar.Visible, IsTextEntryFocused(), _doc != null,
                    _surface.Focused, _layoutMode == PdfViewLayoutMode.SinglePage)))
                return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Acts on a classified key, a chrome command through <see cref="RunCommand"/>. False, so the
        /// key passes on to the host's form, for no action and for a feature that is switched off.
        /// </summary>
        internal bool ExecuteCmdKey(CmdKeyAction action)
        {
            switch (action)
            {
                case CmdKeyAction.FindNext:     FindStep(+1);  return true;
                case CmdKeyAction.FindPrevious: FindStep(-1);  return true;
                case CmdKeyAction.HideFindBar:  HideFindBar(); return true;
                case CmdKeyAction.OpenFile:
                    if (!Has(PdfViewerFeatures.OpenFile)) return false;
                    RunCommand(PdfViewerCommand.Open, OpenWithDialog); return true;
                case CmdKeyAction.ShowFindBar:
                    if (!Has(PdfViewerFeatures.Search)) return false;
                    RunCommand(PdfViewerCommand.Find, () => Find()); return true;
                case CmdKeyAction.Copy:
                    if (!Has(PdfViewerFeatures.TextSelection)) return false;
                    RunCommand(PdfViewerCommand.Copy, () => CopySelection()); return true;
                case CmdKeyAction.SelectAll:
                    if (!Has(PdfViewerFeatures.TextSelection)) return false;
                    RunCommand(PdfViewerCommand.SelectAll, () => SelectPageText(_currentPage), pageIndex: _currentPage); return true;
                case CmdKeyAction.Print:
                    if (!Has(PdfViewerFeatures.Print)) return false;
                    RunCommand(PdfViewerCommand.Print, PrintPreview); return true;
                case CmdKeyAction.Save:
                    if (!CanSave) return false;
                    RunCommand(PdfViewerCommand.Save, SaveWithDialog); return true;
                case CmdKeyAction.ActualSize:   RunCommand(PdfViewerCommand.ActualSize, ActualSize);     return true;
                case CmdKeyAction.ZoomIn:       RunCommand(PdfViewerCommand.ZoomIn, ZoomIn);             return true;
                case CmdKeyAction.ZoomOut:      RunCommand(PdfViewerCommand.ZoomOut, ZoomOut);           return true;
                case CmdKeyAction.PreviousPage: RunCommand(PdfViewerCommand.PreviousPage, PreviousPage); return true;
                case CmdKeyAction.NextPage:     RunCommand(PdfViewerCommand.NextPage, NextPage);         return true;
                case CmdKeyAction.LineUp:       ScrollView(0, -ScrollLinePx);                            return true;
                case CmdKeyAction.LineDown:     ScrollView(0, ScrollLinePx);                             return true;
                case CmdKeyAction.LineLeft:     ScrollView(-ScrollLinePx, 0);                            return true;
                case CmdKeyAction.LineRight:    ScrollView(ScrollLinePx, 0);                             return true;
                case CmdKeyAction.ScreenUp:     ScrollView(0, -ScreenStepPx());                          return true;
                case CmdKeyAction.ScreenDown:   ScrollView(0, ScreenStepPx());                           return true;
                case CmdKeyAction.FirstPage:
                    RunCommand(PdfViewerCommand.FirstPage, () => JumpTo(0, null), pageIndex: 0); return true;
                case CmdKeyAction.LastPage:
                    RunCommand(PdfViewerCommand.LastPage, () => JumpTo(_pageCount - 1, null), pageIndex: _pageCount - 1); return true;
                case CmdKeyAction.Back:         RunCommand(PdfViewerCommand.Back, () => GoBack());       return true;
                case CmdKeyAction.Forward:      RunCommand(PdfViewerCommand.Forward, () => GoForward()); return true;
                case CmdKeyAction.RotateClockwise:
                    RunCommand(PdfViewerCommand.RotateClockwise, RotateClockwise); return true;
                case CmdKeyAction.RotateCounterclockwise:
                    RunCommand(PdfViewerCommand.RotateCounterclockwise, RotateCounterclockwise); return true;
            }
            return false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private bool TryGetBounds(int pageIndex, out Rectangle bounds)
        {
            for (int i = 0; i < _layout.Length; i++)
            {
                if (_layout[i].PageIndex == pageIndex)
                {
                    bounds = _layout[i].Bounds;
                    return true;
                }
            }
            bounds = Rectangle.Empty;
            return false;
        }

        private static int Manhattan(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        /// <summary>
        /// Whether an editable field of this control currently has the keyboard focus, in which
        /// case the document-level shortcuts must stay out of its way.
        /// </summary>
        /// <remarks>
        /// <para>Resolved by walking <see cref="Control.Focused"/> from the find box outward rather than
        /// by reading <see cref="ContainerControl.ActiveControl"/>: the toolbar's fields are hosted
        /// by <see cref="ToolStripControlHost"/>, so the control that actually has the focus is not
        /// this control's <c>ActiveControl</c>.</para>
        /// <para>A form field's live editor counts while it is open: it is given the focus when it
        /// opens and is closed by <c>Leave</c> the moment the focus goes elsewhere, so an open editor is
        /// the field being typed into. Without it Ctrl+C copied the page's selection over what the user
        /// was copying, Ctrl+A selected the page, PageUp turned it, and Escape with the find bar open
        /// closed the bar in place of cancelling the edit.</para>
        /// </remarks>
        internal bool IsTextEntryFocused()
        {
            if (_findBar != null && _findBar.Visible && _findBox != null && _findBox.Focused) return true;
            if (_pageBox != null && _pageBox.Focused) return true;
            if (_zoomCombo != null && _zoomCombo.Focused) return true;
            if (_fieldEditor != null) return true;
            return false;
        }
    }
}
