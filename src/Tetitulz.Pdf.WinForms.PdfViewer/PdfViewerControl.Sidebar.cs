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
using Tetitulz.Pdf.Base.Operations.Structure;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    public sealed partial class PdfViewerControl
    {
        private void PopulateSidebar()
        {
            // Thumbnails (one lazily-rendered item per page, up to MaxThumbnails).
            //
            // Only the thumbnail BITMAP was ever lazy; the control was not. Each ThumbItem is a
            // native window handle once realised, so an unbounded strip lets the file's page count
            // spend the process's USER handle quota — see MaxThumbnails.
            _thumbFlow.SuspendLayout();
            ClearThumbnails();
            _thumbsTruncated = false;
            if (_doc != null && Has(PdfViewerFeatures.Thumbnails))
            {
                int shown = Math.Min(_pageCount, MaxThumbnails);
                _thumbsTruncated = shown < _pageCount;
                for (int p = 0; p < shown; p++)
                    _thumbFlow.Controls.Add(new ThumbItem(this, p));
            }
            CentreThumbnails();
            _thumbFlow.ResumeLayout();

            // Bookmarks.
            _bookmarkTree.BeginUpdate();
            ClearBookmarkTree();
            if (_doc != null && Has(PdfViewerFeatures.Bookmarks))
                AddBookmarkNodes(_bookmarkTree.Nodes, _doc.Bookmarks, _bookmarkTree.Font, _strings.UntitledBookmark, _bookmarkFonts);
            _bookmarkTree.EndUpdate();

            HighlightCurrentThumb();
            UpdateCurrentBookmark();
            PopulateComments();
            PopulateSignatures();
        }

        /// <summary>Thumbnail items currently in the strip — the cap's own evidence.</summary>
        internal int ThumbnailItemCount => _thumbFlow.Controls.Count;

        /// <summary>Thumbnail item <paramref name="index"/> rendered into a box of <paramref name="target"/> — what a test reads.</summary>
        internal Bitmap? ThumbnailAt(int index, Size target) => ((ThumbItem)_thumbFlow.Controls[index]).ThumbnailFor(target);

        /// <summary>Thumbnail item <paramref name="index"/>'s bitmap as it stands, null before one arrived — nothing is rendered to answer.</summary>
        internal Bitmap? ThumbnailHeldAt(int index) => ((ThumbItem)_thumbFlow.Controls[index]).Held;

        /// <summary>A key pressed on thumbnail <paramref name="index"/> — what a test does without a keyboard.</summary>
        internal void PressThumbnailKey(int index, Keys key) => ((ThumbItem)_thumbFlow.Controls[index]).Press(key);

        /// <summary>The strip's items, so a test can hold one and check it was disposed.</summary>
        internal Control.ControlCollection ThumbnailItems => _thumbFlow.Controls;

        /// <summary>
        /// Removes every thumbnail item <b>and disposes it</b>.
        /// </summary>
        /// <remarks>
        /// <see cref="Control.ControlCollection.Clear"/> removes without disposing, and a
        /// <c>ThumbItem</c> owns a realised window handle plus a cached <see cref="Bitmap"/> that
        /// only its <c>Dispose</c> override releases — so clearing without disposing would leak both,
        /// per page, per load, whenever documents are opened in sequence. Handle and GDI quota
        /// exhaustion shows up as blank thumbnails and then as failed control creation.
        /// </remarks>
        private void ClearThumbnails()
        {
            while (_thumbFlow.Controls.Count > 0)
            {
                Control c = _thumbFlow.Controls[0];
                _thumbFlow.Controls.RemoveAt(0);
                c.Dispose();
            }
        }

        /// <summary>
        /// One node per bookmark, its tag the bookmark: the title in its style (<c>/F</c>) and colour
        /// (<c>/C</c>), expanded as the author left it (<c>/Count</c>).
        /// </summary>
        private static void AddBookmarkNodes(TreeNodeCollection nodes, IReadOnlyList<PdfBookmark> marks, Font treeFont,
            string untitled, Dictionary<FontStyle, Font> fonts)
        {
            foreach (var bm in marks)
            {
                var node = new TreeNode(string.IsNullOrEmpty(bm.Title) ? untitled : bm.Title) { Tag = bm };
                var style = (bm.IsBold ? FontStyle.Bold : FontStyle.Regular) | (bm.IsItalic ? FontStyle.Italic : FontStyle.Regular);
                if (style != FontStyle.Regular)
                {
                    if (!fonts.TryGetValue(style, out Font? font)) fonts[style] = font = new Font(treeFont, style);
                    node.NodeFont = font;
                }
                if (bm.Color != null)
                    node.ForeColor = Color.FromArgb(Channel(bm.Color[0]), Channel(bm.Color[1]), Channel(bm.Color[2]));
                nodes.Add(node);
                if (bm.Children != null && bm.Children.Count > 0)
                {
                    AddBookmarkNodes(node.Nodes, bm.Children, treeFont, untitled, fonts);
                    if (bm.IsOpen) node.Expand();
                }
            }
        }

        /// <summary>
        /// The styled fonts the bookmark tree's nodes share, one per style: a <see cref="TreeNode"/>
        /// does not dispose its <see cref="TreeNode.NodeFont"/>, so the tree's owner does, when the
        /// nodes go.
        /// </summary>
        private readonly Dictionary<FontStyle, Font> _bookmarkFonts = new Dictionary<FontStyle, Font>();

        /// <summary>How many styled fonts the bookmark tree holds — what a test reads.</summary>
        internal int BookmarkFontCount => _bookmarkFonts.Count;

        /// <summary>Empties the bookmark tree and disposes the fonts its nodes used.</summary>
        private void ClearBookmarkTree()
        {
            _bookmarkTree.Nodes.Clear();
            foreach (var font in _bookmarkFonts.Values) font.Dispose();
            _bookmarkFonts.Clear();
        }

        /// <summary>A 0–1 colour component as a byte (the reader has clamped it).</summary>
        private static int Channel(float v) => SaturatingCast.ToInt(Math.Round(v * 255));

        internal void BookmarkTree_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node?.Tag is PdfBookmark { PageNumber: int pageIndex } bm && pageIndex >= 0)
                RunCommand(PdfViewerCommand.GoToPage, () => JumpTo(pageIndex, bm.Top), pageIndex: pageIndex);
        }

        private bool _highlightCurrentBookmark = true;

        /// <summary>
        /// Whether the bookmark of the section being read is selected in the sidebar as the view
        /// scrolls — or, under a closed parent, the nearest one shown, so reading never unfolds the
        /// outline. Default on.
        /// </summary>
        [Category("PDF"), DefaultValue(true), Description("Select the bookmark of the section being read.")]
        public bool HighlightCurrentBookmark
        {
            get => _highlightCurrentBookmark;
            set { _highlightCurrentBookmark = value; UpdateCurrentBookmark(); }
        }

        /// <summary>The node the last highlight selected, or null — what a test reads.</summary>
        internal TreeNode? CurrentBookmarkNode { get; private set; }

        /// <summary>
        /// Selects the bookmark of the section the view is at: the last one, in reading order, whose
        /// destination lies at or before the reading position — the point at the top of the view.
        /// </summary>
        private void UpdateCurrentBookmark()
        {
            TreeNode? found = null;
            if (_highlightCurrentBookmark && _bookmarkTree.Nodes.Count > 0 && TryReadingPosition(out int page, out float y))
            {
                PdfBookmark? best = null;
                foreach (TreeNode node in BookmarkNodesInOrder(_bookmarkTree.Nodes))
                {
                    if (!(node.Tag is PdfBookmark { PageNumber: int p } bm) || !StartsAtOrBefore(p, bm.Top, page, y))
                        continue;
                    // The later of two positions; between equal ones, the later in the outline.
                    if (best == null || StartsAtOrBefore(best.PageNumber!.Value, best.Top, p, bm.Top ?? float.PositiveInfinity))
                    {
                        best = bm;
                        found = node;
                    }
                }
            }

            // Under a closed parent, the nearest node the reader can see.
            for (TreeNode? n = found?.Parent; n != null; n = n.Parent)
                if (!n.IsExpanded) found = n;

            CurrentBookmarkNode = found;
            if (_bookmarkTree.SelectedNode != found) _bookmarkTree.SelectedNode = found;
        }

        /// <summary>
        /// Whether a destination (page, top — null for the page's top) is at or before a position:
        /// pages in order, and on one page the higher point first, as user space's y grows upwards.
        /// </summary>
        private static bool StartsAtOrBefore(int page, float? top, int atPage, float atY)
            => page < atPage || (page == atPage && (top ?? float.PositiveInfinity) >= atY);

        private static IEnumerable<TreeNode> BookmarkNodesInOrder(TreeNodeCollection nodes)
        {
            var stack = new Stack<TreeNode>();
            for (int i = nodes.Count - 1; i >= 0; i--) stack.Push(nodes[i]);
            while (stack.Count > 0)
            {
                TreeNode n = stack.Pop();
                yield return n;
                for (int i = n.Nodes.Count - 1; i >= 0; i--) stack.Push(n.Nodes[i]);
            }
        }

        /// <summary>
        /// The reading position: the page at the top of the view and the user-space y there — a
        /// little below the edge, so a destination scrolled exactly to the top counts as read. Above a
        /// page, in the gap, it is that page's top.
        /// </summary>
        private bool TryReadingPosition(out int page, out float y)
        {
            page = -1;
            y = 0;
            if (_doc == null || _layout.Length == 0) return false;
            var vis = VisibleVirtualRect;
            int probe = vis.Y + ReadingLineOffset;
            foreach (var info in _layout)
            {
                var b = info.Bounds;
                if (probe > b.Bottom) continue;
                page = info.PageIndex;
                if (probe < b.Top) { y = float.PositiveInfinity; return true; }
                y = PdfPixelMapper.MapPointToPdf(ViewGeometry(page), RenderScale, b.Width / 2, probe - b.Y).Y;
                return true;
            }
            page = _layout[_layout.Length - 1].PageIndex;     // past the last page: its end
            y = float.NegativeInfinity;
            return true;
        }

        /// <summary>How far below the view's top edge the reading position is, in pixels.</summary>
        private const int ReadingLineOffset = 8;

        private void HighlightCurrentThumb()
        {
            ThumbItem? current = null;
            foreach (Control c in _thumbFlow.Controls)
            {
                if (c is ThumbItem t)
                {
                    bool isCur = t.PageIndex == _currentPage;
                    t.SetCurrent(isCur);
                    if (isCur) current = t;
                }
            }
            if (current != null && _thumbFlow.Visible)
            {
                try { _thumbFlow.ScrollControlIntoView(current); } catch { /* ignore */ }
            }
        }

        /// <summary>One lazily-rendered page thumbnail in the sidebar strip.</summary>
        private sealed class ThumbItem : Panel
        {
            // At 96 dpi; scaled by the monitor's dpi when the item is made.
            private const int PadAt96 = 8;
            private const int LabelHAt96 = 26;
            /// <summary>Width of the thumbnail image box at 96 dpi; the height follows the page aspect ratio.</summary>
            private const int ContentWidthAt96 = 140;
            private readonly int Pad, LabelH, ContentWidth;
            private bool _hot;
            /// <summary>Floor on the derived height, so an extremely wide page still gets a visible box.</summary>
            private const int MinContentHeight = 20;
            private readonly PdfViewerControl _owner;
            private Bitmap? _thumb;
            private bool _failed;
            private bool _current;

            public int PageIndex { get; }

            public ThumbItem(PdfViewerControl owner, int pageIndex)
            {
                _owner = owner;
                PageIndex = pageIndex;
                Pad = owner.Px(PadAt96);
                LabelH = owner.Px(LabelHAt96);
                ContentWidth = owner.Px(ContentWidthAt96);
                Margin = new Padding(owner.Px(4), owner.Px(2), owner.Px(4), owner.Px(2));
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                BackColor = owner._palette.Base;

                var geo = owner._doc!.Geometry(pageIndex);
                float aspect = geo.DisplayHeightPts / Math.Max(1f, geo.DisplayWidthPts);
                int contentH = Math.Max(MinContentHeight, SaturatingCast.ToInt(Math.Round(ContentWidth * aspect)));
                Size = new Size(ContentWidth + 2 * Pad, contentH + 2 * Pad + LabelH);

                Click += (s, e) => { Focus(); Activate(); };

                // Reachable without a mouse: Tab reaches the strip, the arrows move along it, Enter opens.
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
                AccessibleRole = AccessibleRole.ListItem;
                AccessibleName = PdfViewerStrings.Format(owner._strings.ThumbnailFormat, owner.PageBoxText(pageIndex));
            }

            /// <summary>Goes to this item's page, as a click or Enter does.</summary>
            internal void Activate()
                => _owner.RunCommand(PdfViewerCommand.GoToPage, () => _owner.JumpTo(PageIndex, null), pageIndex: PageIndex);

            protected override bool IsInputKey(Keys keyData)
                => keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Enter or Keys.Space
                   || base.IsInputKey(keyData);

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                switch (e.KeyCode)
                {
                    case Keys.Enter:
                    case Keys.Space:
                        Activate();
                        e.Handled = true;
                        break;
                    case Keys.Up:
                    case Keys.Left:
                        e.Handled = FocusSibling(-1);
                        break;
                    case Keys.Down:
                    case Keys.Right:
                        e.Handled = FocusSibling(+1);
                        break;
                }
            }

            /// <summary>A key pressed on the item, as the keyboard would — for a test, which has none.</summary>
            internal void Press(Keys key) => OnKeyDown(new KeyEventArgs(key));

            /// <summary>Moves the focus to the item <paramref name="step"/> places along the strip.</summary>
            internal bool FocusSibling(int step)
            {
                if (Parent == null) return false;
                int at = Parent.Controls.GetChildIndex(this) + step;
                if (at < 0 || at >= Parent.Controls.Count) return false;
                Control next = Parent.Controls[at];
                next.Focus();
                if (Parent is ScrollableControl strip) strip.ScrollControlIntoView(next);
                return true;
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; Invalidate(); }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            public void SetCurrent(bool value)
            {
                if (_current == value) return;
                _current = value;
                Invalidate();
            }

            /// <summary>
            /// The item as a WinUI <c>GridView</c> item: a rounded hover fill, the page as a card with a
            /// hairline stroke, the current page outlined in the accent with its number on an accent pill,
            /// and WinUI's two-line focus visual.
            /// </summary>
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                ViewerPalette p = _owner._palette;
                if (BackColor != p.Base) BackColor = p.Base;
                using (var back = new SolidBrush(p.Base)) g.FillRectangle(back, ClientRectangle);
                int radius = _owner.Px(ViewerFluent.ControlRadius);
                if (_hot) ViewerFluent.FillRounded(g, p.SubtleHover, new Rectangle(0, 0, Width, Height), radius);
                var content = new Rectangle(Pad, Pad, Width - 2 * Pad, Height - 2 * Pad - LabelH);

                // In the background when the owner can, so scrolling the strip never waits for a render.
                if (_thumb == null && !_failed && _owner._doc != null && _requestedGeneration != _owner._renderGeneration)
                {
                    if (_owner.RequestThumbnail(PageIndex, content.Size, out int asked)) _requestedGeneration = asked;
                    else EnsureThumb(content.Size);
                }

                using (var paper = new SolidBrush(ViewerPalette.Paper)) g.FillRectangle(paper, content);
                if (_thumb != null)
                {
                    int w = Math.Min(content.Width, _thumb.Width);
                    int h = Math.Min(content.Height, _thumb.Height);
                    var dest = new Rectangle(content.X + (content.Width - w) / 2,
                                             content.Y + (content.Height - h) / 2, w, h);
                    g.DrawImage(_thumb, dest);
                }
                else if (_failed)
                {
                    TextRenderer.DrawText(g, "—", _owner._fontCaption, content, p.TextTertiary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                if (_current)
                {
                    // The accent outline sits just outside the page, so it never covers what the page shows.
                    int w = Math.Max(2, _owner.Px(2));
                    ViewerFluent.StrokeRounded(g, p.CurrentThumbnail, Rectangle.Inflate(content, w, w), radius, w);
                }
                else
                {
                    using (var pen = new Pen(p.ControlStroke)) g.DrawRectangle(pen, content.X - 1, content.Y - 1, content.Width + 1, content.Height + 1);
                }

                if (Focused && ShowFocusCues)
                    ViewerFluent.DrawFocus(g, p, Rectangle.Inflate(new Rectangle(0, 0, Width, Height), -_owner.Px(2), -_owner.Px(2)),
                        radius, _owner._chromeScale);

                var label = new Rectangle(0, Height - LabelH, Width, LabelH);
                string number = _owner.PageBoxText(PageIndex);
                if (_current)
                {
                    Size size = TextRenderer.MeasureText(number, _owner._fontStrong);
                    int pillW = Math.Max(_owner.Px(28), size.Width + _owner.Px(12)), pillH = _owner.Px(20);
                    var pill = new Rectangle((Width - pillW) / 2, label.Y + (label.Height - pillH) / 2, pillW, pillH);
                    ViewerFluent.FillRounded(g, p.Accent, pill, pillH / 2f);
                    TextRenderer.DrawText(g, number, _owner._fontStrong, pill, p.TextOnAccent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                else
                {
                    TextRenderer.DrawText(g, number, _owner._fontCaption, label, p.TextSecondary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }

            /// <summary>The thumbnail held now, null before one arrived.</summary>
            internal Bitmap? Held => _thumb;

            /// <summary>The thumbnail rendered into a box of <paramref name="target"/>, as painting renders it — what a test reads.</summary>
            internal Bitmap? ThumbnailFor(Size target)
            {
                EnsureThumb(target);
                return _thumb;
            }

            private void EnsureThumb(Size target)
            {
                if (_thumb != null || _failed || _owner._doc == null) return;
                try
                {
                    // At the dpi that fills the box, through the page view's guard: a fixed dpi
                    // would render a vast page at the renderer's 400 MB ceiling for a 140-pixel image.
                    int w = Math.Max(1, target.Width);
                    int h = Math.Max(1, target.Height);
                    var geo = _owner._doc.Geometry(PageIndex);
                    int dpi = ZoomMath.DpiToFill(w, h, geo.DisplayWidthPts, geo.DisplayHeightPts);
                    using var raw = _owner._doc.RenderWithin(PageIndex, dpi, MaxSingleBitmapBytes, _owner.ScreenRenderOptions, out _);
                    if (raw == null) { _failed = true; return; }
                    _thumb = ScaleIntoBox(raw, new Size(w, h));   // once, so repeated paints are cheap
                }
                catch (Exception ex)
                {
                    // Said, not swallowed: an empty box reads as a blank page otherwise.
                    _failed = true;
                    _owner._statusLabel.Text = PdfViewerStrings.Format(_owner._strings.PageFailedFormat, PageIndex + 1, ex.Message);
                }
            }

            /// <summary>The owner's render generation this item last asked the worker under; -1 for never.</summary>
            private int _requestedGeneration = -1;

            /// <summary>A background render's thumbnail, or null when the page could not be rendered.</summary>
            internal void TakeThumbnail(Bitmap? thumbnail)
            {
                if (_thumb != null || IsDisposed) { thumbnail?.Dispose(); return; }
                _thumb = thumbnail;
                _failed = thumbnail == null;
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _thumb?.Dispose(); _thumb = null; }
                base.Dispose(disposing);
            }
        }
    }
}
