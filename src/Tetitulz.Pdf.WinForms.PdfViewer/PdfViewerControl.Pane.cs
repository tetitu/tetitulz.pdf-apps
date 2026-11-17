// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The sidebar drawn as a WinUI <c>NavigationView</c> pane: a row of icon tabs with the accent
    /// selection pill under the chosen one, WinUI-style trees (no lines, rounded row highlights, a pill at
    /// the selected row's leading edge, chevron expanders), and a divider for a splitter.
    /// </summary>
    /// <remarks>
    /// The tab pages stay in a <see cref="TabControl"/>, whose own headers and 3-D border are covered by
    /// its pages (<see cref="PaneTabs"/>): <c>Features</c>, the tab order and the pages' names keep the
    /// meaning they had, and the row above draws what a reader clicks.
    /// </remarks>
    public sealed partial class PdfViewerControl
    {
        private Panel _sidebarHeader = null!;
        private readonly List<PaneTabButton> _paneButtons = new List<PaneTabButton>();
        private readonly ToolTip _paneTip = new ToolTip();

        /// <summary>The row of tab buttons above the sidebar — what a test reads.</summary>
        internal Control SidebarHeader => _sidebarHeader;

        /// <summary>The tab buttons, in the tabs' order — what a test reads.</summary>
        internal IReadOnlyList<Control> SidebarTabButtons => _paneButtons;

        /// <summary>Tab button <paramref name="index"/> clicked — what a test does without a mouse.</summary>
        internal void ClickSidebarTab(int index) => _paneButtons[index].Activate();

        /// <summary>A key pressed on tab button <paramref name="index"/> — what a test does without a keyboard.</summary>
        internal void PressSidebarTabKey(int index, Keys key) => _paneButtons[index].Press(key);

        /// <summary>A tab control whose pages cover the whole of it, so neither its headers nor its border show.</summary>
        /// <remarks>
        /// Multiline, so Windows never adds the scroll arrows a single row of tabs gets when they do not
        /// fit: those are a window of their own, and a page covering the headers does not cover it.
        /// </remarks>
        private sealed class PaneTabs : TabControl
        {
            public PaneTabs() => Multiline = true;

            public override Rectangle DisplayRectangle => new Rectangle(0, 0, Width, Height);
        }

        private string? PaneIconOf(TabPage tab)
            => tab == _tabThumbnails ? ViewerIcons.ViewAll
             : tab == _tabBookmarks ? ViewerIcons.Bookmarks
             : tab == _tabComments ? ViewerIcons.Comment
             : tab == _tabSignatures ? ViewerIcons.SignatureCapture
             : null;

        /// <summary>One button per tab the control holds, laid from the leading edge.</summary>
        private void LayoutSidebarHeader()
        {
            if (_sidebarHeader == null) return;
            var pages = _sidebarTabs.TabPages;
            bool same = _paneButtons.Count == pages.Count;
            for (int i = 0; same && i < pages.Count; i++) same = _paneButtons[i].Tab == pages[i];
            if (!same)
            {
                foreach (var b in _paneButtons) { _sidebarHeader.Controls.Remove(b); b.Dispose(); }
                _paneButtons.Clear();
                foreach (TabPage tab in pages)
                {
                    var button = new PaneTabButton(this, tab);
                    _paneButtons.Add(button);
                    _sidebarHeader.Controls.Add(button);
                }
            }

            _sidebarHeader.Height = Px(44);
            bool rtl = _sidebarHeader.RightToLeft == RightToLeft.Yes;
            int x = Px(8);
            foreach (var b in _paneButtons)
            {
                _paneTip.SetToolTip(b, b.Tab.Text);
                b.AccessibleName = b.Tab.Text;
                int w = _fontIcon != null ? Px(40) : TextRenderer.MeasureText(b.Tab.Text, _fontBody).Width + Px(20);
                int h = Px(36);
                b.Bounds = new Rectangle(rtl ? _sidebarHeader.Width - x - w : x, (_sidebarHeader.Height - h) / 2, w, h);
                x += w + Px(4);
                b.Invalidate();
            }
        }

        /// <summary>Chooses a tab from its button, as a click or Enter does.</summary>
        private void SelectPaneTab(TabPage tab)
        {
            if (_sidebarTabs.SelectedTab != tab) _sidebarTabs.SelectedTab = tab;
            foreach (var b in _paneButtons) b.Invalidate();
        }

        /// <summary>A tab of the pane's header: an icon (or the tab's name), the selection pill under the chosen one.</summary>
        private sealed class PaneTabButton : Control
        {
            private readonly PdfViewerControl _owner;
            private bool _hot, _pressed;

            public PaneTabButton(PdfViewerControl owner, TabPage tab)
            {
                _owner = owner;
                Tab = tab;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                         | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
                TabStop = true;
                AccessibleRole = AccessibleRole.PageTab;
                Cursor = Cursors.Hand;
            }

            public TabPage Tab { get; }

            public bool IsSelected => _owner._sidebarTabs.SelectedTab == Tab;

            /// <summary>Presses the button, as a click does — for a test.</summary>
            internal void Activate() => _owner.SelectPaneTab(Tab);

            protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hot = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnClick(EventArgs e) { Focus(); Activate(); base.OnClick(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override bool IsInputKey(Keys keyData)
                => keyData is Keys.Left or Keys.Right or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                int step = e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Left ? -1 : 0;
                if (step != 0)
                {
                    if (_owner._sidebarHeader.RightToLeft == RightToLeft.Yes) step = -step;
                    var list = _owner._paneButtons;
                    int at = list.IndexOf(this) + step;
                    if (at >= 0 && at < list.Count) { list[at].Focus(); list[at].Activate(); }
                    e.Handled = true;
                }
                else if (e.KeyCode is Keys.Enter or Keys.Space)
                {
                    Activate();
                    e.Handled = true;
                }
            }

            /// <summary>A key pressed on the button, as the keyboard would — for a test, which has none.</summary>
            internal void Press(Keys key) => OnKeyDown(new KeyEventArgs(key));

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                ViewerPalette p = _owner._palette;
                using (var back = new SolidBrush(p.Base)) g.FillRectangle(back, ClientRectangle);
                var r = new Rectangle(0, 0, Width, Height);
                int radius = _owner.Px(ViewerFluent.ControlRadius);
                if (_pressed) ViewerFluent.FillRounded(g, p.SubtlePressed, r, radius);
                else if (_hot || IsSelected) ViewerFluent.FillRounded(g, p.SubtleHover, r, radius);

                bool selected = IsSelected;
                Color fore = selected ? p.TextPrimary : p.TextSecondary;
                string? glyph = _owner._fontIcon != null ? _owner.PaneIconOf(Tab) : null;
                if (glyph != null) ViewerFluent.DrawIcon(g, glyph, _owner._fontIcon!, r, fore);
                else
                    TextRenderer.DrawText(g, Tab.Text, selected ? _owner._fontStrong : _owner._fontBody, r, fore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                        | TextFormatFlags.NoPrefix);

                if (selected)
                {
                    // NavigationView's indicator, laid along the bottom in a top row: 16 × 3, rounded.
                    int w = _owner.Px(16), h = Math.Max(2, _owner.Px(3));
                    ViewerFluent.FillRounded(g, p.Accent, new Rectangle((Width - w) / 2, Height - h - _owner.Px(1), w, h), h / 2f);
                }
                if (Focused && ShowFocusCues)
                    ViewerFluent.DrawFocus(g, p, Rectangle.Inflate(r, -_owner.Px(3), -_owner.Px(3)), radius, _owner._chromeScale);
            }
        }

        // ── Trees ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A tree with no horizontal scroll bar: its rows end in an ellipsis instead, as WinUI's do. The
        /// bar is a window style (<c>TVS_NOHSCROLL</c>), asked for at creation.
        /// </summary>
        private sealed class PaneTree : TreeView
        {
            private const int TvsNoHScroll = 0x8000;

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.Style |= TvsNoHScroll;
                    return cp;
                }
            }
        }

        /// <summary>The node under the mouse in each tree, for its hover fill.</summary>
        private readonly Dictionary<TreeView, TreeNode?> _hotNodes = new Dictionary<TreeView, TreeNode?>();

        /// <summary>Makes a tree draw as a WinUI TreeView does: no lines, rounded rows, chevron expanders.</summary>
        private void StyleTree(TreeView tree)
        {
            tree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
            tree.ShowLines = false;
            tree.ShowRootLines = false;
            tree.ShowPlusMinus = false;
            tree.FullRowSelect = true;
            tree.HotTracking = false;
            tree.BorderStyle = BorderStyle.None;
            tree.DrawNode += Tree_DrawNode;
            tree.MouseMove += (s, e) => SetHotNode(tree, tree.GetNodeAt(e.Location));
            tree.MouseLeave += (s, e) => SetHotNode(tree, null);
            tree.MouseDown += (s, e) =>
            {
                TreeNode? node = tree.GetNodeAt(e.Location);
                if (node != null && node.Nodes.Count > 0 && e.Button == MouseButtons.Left && ChevronBox(tree, node).Contains(e.Location))
                    node.Toggle();
            };
        }

        private void SetHotNode(TreeView tree, TreeNode? node)
        {
            _hotNodes.TryGetValue(tree, out TreeNode? old);
            if (old == node) return;
            _hotNodes[tree] = node;
            if (old != null && old.TreeView == tree) tree.Invalidate(RowOf(tree, old));
            if (node != null) tree.Invalidate(RowOf(tree, node));
        }

        private static Rectangle RowOf(TreeView tree, TreeNode node)
            => new Rectangle(0, node.Bounds.Y, tree.ClientSize.Width, tree.ItemHeight);

        /// <summary>Where a node's expander is drawn and clicked.</summary>
        private Rectangle ChevronBox(TreeView tree, TreeNode node)
        {
            int x = Px(12) + node.Level * tree.Indent;
            return new Rectangle(x, node.Bounds.Y, Px(20), tree.ItemHeight);
        }

        private void Tree_DrawNode(object? sender, DrawTreeNodeEventArgs e)
        {
            if (!(sender is TreeView tree) || e.Node == null || e.Bounds.Height <= 0) return;
            TreeNode node = e.Node;
            Graphics g = e.Graphics;
            ViewerPalette p = _palette;
            var row = new Rectangle(0, e.Bounds.Y, tree.ClientSize.Width, e.Bounds.Height);
            using (var back = new SolidBrush(p.Base)) g.FillRectangle(back, row);

            var inner = Rectangle.Inflate(row, -Px(4), -Px(2));
            int radius = Px(ViewerFluent.ControlRadius);
            bool selected = tree.SelectedNode == node;
            _hotNodes.TryGetValue(tree, out TreeNode? hot);
            if (selected && hot == node) ViewerFluent.FillRounded(g, p.SubtlePressed, inner, radius);
            else if (selected || hot == node) ViewerFluent.FillRounded(g, p.SubtleHover, inner, radius);
            if (selected)
            {
                int h = Px(16), w = Math.Max(2, Px(3));
                ViewerFluent.FillRounded(g, p.Accent, new Rectangle(inner.X, inner.Y + (inner.Height - h) / 2, w, h), w / 2f);
            }

            Rectangle chevron = ChevronBox(tree, node);
            bool rtl = tree.RightToLeft == RightToLeft.Yes && tree.RightToLeftLayout;
            if (node.Nodes.Count > 0)
            {
                string glyph = node.IsExpanded ? ViewerIcons.ChevronDown : rtl ? ViewerIcons.ChevronLeft : ViewerIcons.ChevronRight;
                if (_fontIconSmall != null) ViewerFluent.DrawIcon(g, glyph, _fontIconSmall, chevron, p.TextSecondary);
                else TextRenderer.DrawText(g, node.IsExpanded ? "▾" : rtl ? "◂" : "▸", tree.Font, chevron, p.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            var text = new Rectangle(chevron.Right + Px(4), row.Y, Math.Max(0, inner.Right - Px(8) - chevron.Right - Px(4)), row.Height);
            Color fore = node.ForeColor.IsEmpty || p.HighContrast ? p.TextPrimary : node.ForeColor;
            // No RightToLeft flag: a mirrored tree's device context already lays the row right to left,
            // and the flag on top of it moves an ellipsised title toward the chevron.
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
            if (tree.RightToLeft == RightToLeft.Yes && !tree.RightToLeftLayout) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, node.Text, node.NodeFont ?? tree.Font, text, fore, flags);

            if (selected && tree.Focused && ShowFocusCues)
                ViewerFluent.DrawFocus(g, p, Rectangle.Inflate(inner, -Px(1), -Px(1)), radius, _chromeScale);
        }

        /// <summary>Each signature's row in its verdict's colour, as the theme in force draws it.</summary>
        private void RecolourSignatureNodes()
        {
            foreach (TreeNode row in _signatureTree.Nodes)
                if (row.Tag is Tetitulz.Pdf.Signing.Models.SignatureVerificationResult r)
                    row.ForeColor = _palette.VerdictText(VerdictOf(r));
        }

        // ── The splitter ────────────────────────────────────────────────────────────────────

        /// <summary>The splitter drawn as WinUI's pane edge: the base colour, a hairline divider down its middle.</summary>
        private void Split_Paint(object? sender, PaintEventArgs e)
        {
            Rectangle s = _split.SplitterRectangle;
            using (var back = new SolidBrush(_palette.Base)) e.Graphics.FillRectangle(back, s);
            using (var line = new SolidBrush(_palette.Divider))
                e.Graphics.FillRectangle(line, s.X + s.Width / 2, s.Y, Math.Max(1, Px(1)), s.Height);
        }

        /// <summary>The thumbnails centred in the pane, whatever its width.</summary>
        private void CentreThumbnails()
        {
            if (_thumbFlow.Controls.Count == 0) return;
            Control first = _thumbFlow.Controls[0];
            int room = _thumbFlow.ClientSize.Width - first.Width - first.Margin.Horizontal;
            int pad = Math.Max(0, room / 2);
            if (_thumbFlow.Padding.Left != pad) _thumbFlow.Padding = new Padding(pad, Px(4), 0, Px(4));
        }
    }
}
