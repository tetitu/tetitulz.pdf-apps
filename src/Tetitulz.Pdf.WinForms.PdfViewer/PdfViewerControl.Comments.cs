// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations.Extraction;
using Tetitulz.Pdf.Base.Rendering;
using Tetitulz.Pdf.Core.Constants;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The document's comments (<see cref="PdfViewerFeatures.Comments"/>, off by default): a sidebar
    /// tab listing them by page, a click going to one and outlining it, a comment's author, date and
    /// text on hover — and <see cref="ShowComments"/>, which leaves them off the page.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        /// <summary>
        /// The subtypes that are not comments — what Acrobat's comment list leaves out: navigation,
        /// form fields, a comment's own popup, production marks, and media.
        /// </summary>
        private static readonly HashSet<string> NotComments = new HashSet<string>(StringComparer.Ordinal)
        {
            PdfAnnotationSubtypes.Link, PdfAnnotationSubtypes.Widget, PdfAnnotationSubtypes.Popup,
            PdfAnnotationSubtypes.PrinterMark, PdfAnnotationSubtypes.TrapNet, PdfAnnotationSubtypes.Watermark,
            PdfAnnotationSubtypes.Screen, PdfAnnotationSubtypes.Movie, PdfAnnotationSubtypes.Sound,
            PdfAnnotationSubtypes.ThreeD, PdfAnnotationSubtypes.RichMedia, PdfAnnotationSubtypes.Projection,
        };

        /// <summary>The longest stretch of a comment's text a list row shows.</summary>
        private const int CommentRowChars = 60;

        private static readonly PdfRenderOptions WithoutComments = new PdfRenderOptions { Annotations = AnnotationRendering.FormFieldsOnly };

        private readonly ToolTip _commentTip = new ToolTip();
        private PdfAnnotationInfo? _hoverComment;
        private bool _showComments = true;

        /// <summary>
        /// Whether comments and markup are drawn on the pages and thumbnails; form fields are drawn
        /// either way. Default on.
        /// </summary>
        [Category("PDF"), DefaultValue(true), Description("Draw comments and markup on the pages.")]
        public bool ShowComments
        {
            get => _showComments;
            set
            {
                if (_showComments == value) return;
                _showComments = value;
                ClearRenderCache();
                if (_doc != null) PopulateSidebar();          // the thumbnails were drawn the other way
                _surface.Invalidate();
            }
        }

        /// <summary>What the page view and the thumbnails render under: null for every annotation.</summary>
        internal PdfRenderOptions? ScreenRenderOptions => _showComments ? null : WithoutComments;

        /// <summary>The comment last chosen in the list or on the page, outlined on its page; null when none.</summary>
        internal PdfAnnotationInfo? SelectedComment { get; private set; }

        /// <summary>The tree of comments — what a test reads.</summary>
        internal TreeView CommentTree => _commentTree;

        /// <summary>The hover text the page shows now, or empty — what a test reads.</summary>
        internal string CommentToolTipText => _commentTip.GetToolTip(_surface) ?? "";

        /// <summary>A comment: not one of <see cref="NotComments"/>, and not flagged Hidden (§12.5.3).</summary>
        internal static bool IsComment(PdfAnnotationInfo a)
            => !NotComments.Contains(a.Subtype ?? "") && (a.Flags & PdfAnnotationFlags.Hidden) == 0;

        /// <summary>The document's comments, in page order and, on a page, in the order the page lists them.</summary>
        private IEnumerable<PdfAnnotationInfo> Comments()
            => _doc == null ? Enumerable.Empty<PdfAnnotationInfo>() : _doc.Annotations.Where(IsComment);

        /// <summary>The deepest a reply is nested under the comment it answers; a reply below that joins the row above.</summary>
        private const int MaxReplyDepth = 8;

        /// <summary>
        /// Fills the Comments tab: one group per page that has comments, each comment a row, a reply
        /// (<c>/IRT</c>) under the comment it answers.
        /// </summary>
        private void PopulateComments()
        {
            _commentTree.BeginUpdate();
            _commentTree.Nodes.Clear();
            SelectedComment = null;
            if (_doc != null && Has(PdfViewerFeatures.Comments))
            {
                foreach (var page in Comments().GroupBy(c => c.PageIndex))
                {
                    var group = new TreeNode(PdfViewerStrings.Format(_strings.CommentsPageFormat, page.Key + 1));
                    // A reply goes under the comment it answers, found by name among the page's
                    // comments listed before it (a reply follows its comment); one answering no such
                    // comment is a row of its own, and a chain of replies stops nesting at MaxReplyDepth.
                    var rowsByName = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
                    foreach (var c in page)
                    {
                        var row = new TreeNode(CommentRow(c)) { Tag = c, ToolTipText = DescribeComment(c) };
                        TreeNode? parent = c.InReplyToName != null && rowsByName.TryGetValue(c.InReplyToName, out TreeNode? answered) ? answered : null;
                        while (parent != null && parent.Level > MaxReplyDepth) parent = parent.Parent;
                        (parent?.Nodes ?? group.Nodes).Add(row);
                        if (!string.IsNullOrEmpty(c.Name) && !rowsByName.ContainsKey(c.Name!)) rowsByName[c.Name!] = row;
                    }
                    _commentTree.Nodes.Add(group);
                    group.ExpandAll();
                }
                if (!string.IsNullOrEmpty(_doc.AnnotationListError))
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.CommentsIncompleteFormat, _doc.AnnotationListError!);
            }
            _commentTree.EndUpdate();
        }

        /// <summary>A list row: the author, or the kind when there is none, and the start of the text.</summary>
        internal static string CommentRow(PdfAnnotationInfo c)
        {
            string who = string.IsNullOrEmpty(c.Author) ? c.Subtype : c.Author!;
            string text = (c.Contents ?? "").Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length > CommentRowChars) text = text.Substring(0, CommentRowChars) + "…";
            return text.Length == 0 ? who : who + ": " + text;
        }

        /// <summary>The hover text: the kind, the author and the date on one line, the text under them.</summary>
        internal static string DescribeComment(PdfAnnotationInfo c)
        {
            var head = new List<string> { c.Subtype };
            if (!string.IsNullOrEmpty(c.Author)) head.Add(c.Author!);
            string? date = FormatPdfDate(c.ModifiedDate);
            if (date != null) head.Add(date);
            string line = string.Join(" · ", head);
            return string.IsNullOrEmpty(c.Contents) ? line : line + Environment.NewLine + c.Contents;
        }

        /// <summary>A <c>/M</c> date (§7.9.4) in the reader's own culture and zone; as written when it does not parse; null when absent.</summary>
        private static string? FormatPdfDate(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            DateTimeOffset? when = PdfDate.TryParse(raw!);
            return when.HasValue ? when.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : raw;
        }

        internal void CommentTree_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (!(e.Node?.Tag is PdfAnnotationInfo c)) return;
            RunCommand(PdfViewerCommand.GoToPage, () =>
            {
                SelectComment(c);
                JumpTo(c.PageIndex, Top(c));
            }, pageIndex: c.PageIndex);
        }

        /// <summary>A comment's top edge in user space, where going to it scrolls; null when it is not finite.</summary>
        private static float? Top(PdfAnnotationInfo c)
        {
            double top = c.Y + c.Height;
            return double.IsNaN(top) || double.IsInfinity(top) || Math.Abs(top) > float.MaxValue ? (float?)null : (float)top;
        }

        /// <summary>Outlines <paramref name="c"/> on its page and selects its row.</summary>
        private void SelectComment(PdfAnnotationInfo? c)
        {
            SelectedComment = c;
            TreeNode? row = c == null ? null : RowOf(_commentTree.Nodes, c);
            if (row != null && _commentTree.SelectedNode != row) _commentTree.SelectedNode = row;
            _surface.Invalidate();
        }

        /// <summary>The row carrying <paramref name="c"/>, at any depth (a reply's is under its comment's); null when none.</summary>
        private static TreeNode? RowOf(TreeNodeCollection nodes, PdfAnnotationInfo c)
        {
            foreach (TreeNode n in nodes)
            {
                if (ReferenceEquals(n.Tag, c)) return n;
                TreeNode? below = RowOf(n.Nodes, c);          // bounded by MaxReplyDepth
                if (below != null) return below;
            }
            return null;
        }

        /// <summary>The comment under a virtual-space point — the last one the page lists there, as it is drawn on top.</summary>
        private PdfAnnotationInfo? HitTestComment(Point v)
        {
            if (_doc == null || !Has(PdfViewerFeatures.Comments)) return null;
            int page = PageAtVirtual(v);
            if (page < 0 || !TryGetBounds(page, out Rectangle bounds)) return null;
            PdfAnnotationInfo? hit = null;
            foreach (var c in Comments())
                if (c.PageIndex == page && CommentCanvasRect(c, bounds).Contains(v)) hit = c;
            return hit;
        }

        private RectangleF CommentCanvasRect(PdfAnnotationInfo c, Rectangle pageBounds)
            => MapPdfRectToCanvas(c.PageIndex, pageBounds, (float)c.X, (float)c.Y, (float)c.Width, (float)c.Height);

        /// <summary>Hover over a comment: its description as the tooltip, changed only when the comment changes.</summary>
        private void UpdateCommentHover(Point v)
        {
            PdfAnnotationInfo? c = HitTestComment(v);
            if (ReferenceEquals(c, _hoverComment)) return;
            _hoverComment = c;
            _commentTip.SetToolTip(_surface, c == null ? null : DescribeComment(c));
        }

        private static readonly Color CommentOutlineColor = ViewerPalette.CommentOutline;

        /// <summary>The chosen comment's outline, drawn over the page.</summary>
        private void DrawSelectedComment(Graphics g, int pageIndex, Rectangle bounds)
        {
            var c = SelectedComment;
            if (c == null || c.PageIndex != pageIndex || !Has(PdfViewerFeatures.Comments)) return;
            RectangleF r = CommentCanvasRect(c, bounds);
            using var pen = new Pen(CommentOutlineColor, 2f);
            g.DrawRectangle(pen, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
        }
    }
}
