// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The text selection as a host reaches it: what is selected, where on each page, a change
    /// event, and the commands — select a page's text, copy, clear.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        private PdfTextSelectionMode _textSelectionMode = PdfTextSelectionMode.ReadingOrder;

        /// <summary>Whether the drag under way selects a rectangle (Alt was held when it began).</summary>
        private bool _dragRectangle;

        /// <summary>Raised when the selection changes: a drag ends, a word or line is clicked, the page's text is selected, or the selection is cleared.</summary>
        public event EventHandler? SelectionChanged;

        /// <summary>
        /// What a drag selects: text in reading order (the default, as every reader selects), or the
        /// glyphs inside its rectangle. Alt held while dragging selects a rectangle either way.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is not a member of the enum.</exception>
        [Category("PDF"), DefaultValue(PdfTextSelectionMode.ReadingOrder), Description("What a drag across the page selects.")]
        public PdfTextSelectionMode TextSelectionMode
        {
            get => _textSelectionMode;
            set
            {
                if (value != PdfTextSelectionMode.ReadingOrder && value != PdfTextSelectionMode.Rectangle)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Not a selection mode.");
                _textSelectionMode = value;
            }
        }

        /// <summary>The selected text, as Ctrl+C copies it; empty when nothing is selected.</summary>
        [Browsable(false)]
        public string SelectedText => _selectedText;

        /// <summary>
        /// The selection, page by page in page order: each page's text and its glyphs' boxes in that
        /// page's user space — what a host needs to highlight, underline or redact what the user chose.
        /// </summary>
        [Browsable(false)]
        public IReadOnlyList<PdfViewerSelectedText> Selection
        {
            get
            {
                var parts = new List<PdfViewerSelectedText>(_selection.Count);
                foreach (var kv in _selection)
                {
                    if (kv.Value.Count == 0) continue;
                    var idx = GetTextIndex(kv.Key);
                    var boxes = new List<RectangleF>(kv.Value.Count);
                    var glyphs = new List<(Tetitulz.Pdf.Base.Operations.Extraction.PdfTextChar, RectangleF)>(kv.Value.Count);
                    TryGetBounds(kv.Key, out Rectangle bounds);
                    foreach (int i in kv.Value)
                    {
                        if (i >= idx.Chars.Length) continue;
                        var c = idx.Chars[i];
                        boxes.Add(new RectangleF(c.X, c.Y - PdfPixelMapper.GlyphDescentFraction * c.H,
                            c.W, PdfPixelMapper.GlyphBoxHeightFraction * c.H));
                        glyphs.Add((c.Source, MapCharToCanvas(kv.Key, bounds, c)));
                    }
                    string text = CopyUsesLogicalOrder ? ClipboardTextFor(true, glyphs) : JoinFromIndex(idx, kv.Value);
                    parts.Add(new PdfViewerSelectedText(kv.Key, text, boxes));
                }
                return parts;
            }
        }

        /// <summary>Clears the selection.</summary>
        public void ClearSelection() => ResetSelection(invalidate: true);

        /// <summary>
        /// Selects every glyph of page <paramref name="pageIndex"/>, as Ctrl+A does for the page being
        /// read. Nothing happens with no document, a page out of range, or
        /// <see cref="PdfViewerFeatures.TextSelection"/> off.
        /// </summary>
        public void SelectPageText(int pageIndex)
        {
            if (_doc == null || !Has(PdfViewerFeatures.TextSelection) || pageIndex < 0 || pageIndex >= _pageCount) return;
            var idx = GetTextIndex(pageIndex);
            _selection.Clear();
            if (idx.Chars.Length > 0)
            {
                var all = new List<int>(idx.Chars.Length);
                for (int i = 0; i < idx.Chars.Length; i++) all.Add(i);
                _selection[pageIndex] = all;
            }
            SelectionDone();
        }

        /// <summary>What a double or triple click takes: the word, or the line.</summary>
        private enum TextUnit { Word, Line }

        /// <summary>Selects the word or the line of the glyph under a canvas point.</summary>
        private void SelectAround(Point v, TextUnit unit)
        {
            _selection.Clear();
            int page = PageAtVirtual(v);
            var nearest = page >= 0 ? GlyphNearest(v) : null;
            if (nearest.HasValue && nearest.Value.Page == page)
            {
                var idx = GetTextIndex(page);
                string text = idx.Text;
                int hit = nearest.Value.Index, first = hit, last = hit;
                // Whether the page's text between two neighbouring glyphs ends the unit: a line break
                // ends a line, and a space a word.
                bool Boundary(int from, int to)
                {
                    for (int k = idx.Ends[from]; k < idx.Starts[to]; k++)
                        if (text[k] == '\n' || (unit == TextUnit.Word && char.IsWhiteSpace(text[k]))) return true;
                    return false;
                }
                bool IsSpace(int i) => unit == TextUnit.Word && idx.Chars[i].Text.Length > 0 && string.IsNullOrWhiteSpace(idx.Chars[i].Text);
                while (first > 0 && !Boundary(first - 1, first) && !IsSpace(first - 1)) first--;
                while (last < idx.Chars.Length - 1 && !Boundary(last, last + 1) && !IsSpace(last + 1)) last++;
                var run = new List<int>(last - first + 1);
                for (int i = first; i <= last; i++) run.Add(i);
                _selection[page] = run;
            }
            SelectionDone();
        }

        /// <summary>After a selection made at once (a click, a page): its text, the status line, the event, a repaint.</summary>
        private void SelectionDone()
        {
            BuildSelectedText();
            if (!string.IsNullOrEmpty(_selectedText))
                _statusLabel.Text = PdfViewerStrings.Format(_strings.SelectedCharactersFormat, _selectedText.Length);
            _surface.Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // ── The page's right-click menu ───────────────────────────────────────

        private System.Windows.Forms.ContextMenuStrip _pageMenu = null!;
        private System.Windows.Forms.ToolStripMenuItem _menuCopy = null!, _menuSelectAll = null!, _menuFitWidth = null!, _menuFitPage = null!,
            _menuRotateClockwise = null!, _menuRotateCounterclockwise = null!,
            _menuSpreadNone = null!, _menuSpreadFacing = null!, _menuSpreadCover = null!;

        private System.Windows.Forms.ToolStripMenuItem SpreadMenuItem(PdfPageSpread spread)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem();
            item.Click += (s, e) => RunCommand(PdfViewerCommand.SetPageSpread, () => PageSpread = spread, spread: spread);
            return item;
        }
        private bool _showContextMenu = true;

        /// <summary>The page's right-click menu: Copy, Select All, the two fits, the spreads, and the view's two turns. Default on.</summary>
        [Category("PDF"), DefaultValue(true), Description("Show a menu on right-clicking the page.")]
        public bool ShowContextMenu
        {
            get => _showContextMenu;
            set { _showContextMenu = value; _surface.ContextMenuStrip = value ? _pageMenu : null; }
        }

        /// <summary>The page menu — what a test opens.</summary>
        internal System.Windows.Forms.ContextMenuStrip PageMenu => _pageMenu;

        private void BuildPageMenu()
        {
            _menuCopy = new System.Windows.Forms.ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl+C" };
            _menuCopy.Click += (s, e) => RunCommand(PdfViewerCommand.Copy, () => CopySelection());
            _menuSelectAll = new System.Windows.Forms.ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl+A" };
            _menuSelectAll.Click += (s, e) => RunCommand(PdfViewerCommand.SelectAll, () => SelectPageText(_currentPage), pageIndex: _currentPage);
            _menuFitWidth = new System.Windows.Forms.ToolStripMenuItem();
            _menuFitWidth.Click += (s, e) => RunCommand(PdfViewerCommand.FitWidth, FitWidth);
            _menuFitPage = new System.Windows.Forms.ToolStripMenuItem();
            _menuFitPage.Click += (s, e) => RunCommand(PdfViewerCommand.FitPage, FitPage);
            _menuRotateClockwise = new System.Windows.Forms.ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl+Shift++" };
            _menuRotateClockwise.Click += (s, e) => RunCommand(PdfViewerCommand.RotateClockwise, RotateClockwise);
            _menuRotateCounterclockwise = new System.Windows.Forms.ToolStripMenuItem { ShortcutKeyDisplayString = "Ctrl+Shift+-" };
            _menuRotateCounterclockwise.Click += (s, e) => RunCommand(PdfViewerCommand.RotateCounterclockwise, RotateCounterclockwise);
            _menuSpreadNone = SpreadMenuItem(PdfPageSpread.None);
            _menuSpreadFacing = SpreadMenuItem(PdfPageSpread.Facing);
            _menuSpreadCover = SpreadMenuItem(PdfPageSpread.FacingWithCover);
            _pageMenu = new System.Windows.Forms.ContextMenuStrip();
            _pageMenu.Items.AddRange(new System.Windows.Forms.ToolStripItem[]
            {
                _menuCopy, _menuSelectAll, new System.Windows.Forms.ToolStripSeparator(), _menuFitWidth, _menuFitPage,
                new System.Windows.Forms.ToolStripSeparator(), _menuSpreadNone, _menuSpreadFacing, _menuSpreadCover,
                new System.Windows.Forms.ToolStripSeparator(), _menuRotateClockwise, _menuRotateCounterclockwise,
            });
            // Each command as it stands when the menu opens.
            _pageMenu.Opening += (s, e) =>
            {
                if (_doc == null) { e.Cancel = true; return; }
                UpdatePageMenu();
            };
            _surface.ContextMenuStrip = _pageMenu;
        }

        /// <summary>The menu's items as they stand: Copy with a selection, Select All with selection on.</summary>
        internal void UpdatePageMenu()
        {
            bool selecting = Has(PdfViewerFeatures.TextSelection);
            _menuCopy.Available = _menuSelectAll.Available = selecting;
            _menuCopy.Enabled = selecting && _selectedText.Length > 0;
            _menuFitWidth.Checked = _zoomMode == PdfZoomMode.FitWidth;
            _menuFitPage.Checked = _zoomMode == PdfZoomMode.FitPage;
            _menuSpreadNone.Checked = _pageSpread == PdfPageSpread.None;
            _menuSpreadFacing.Checked = _pageSpread == PdfPageSpread.Facing;
            _menuSpreadCover.Checked = _pageSpread == PdfPageSpread.FacingWithCover;
        }

        private void ApplyPageMenuStrings()
        {
            _menuCopy.Text = _strings.CopyMenu;
            _menuSelectAll.Text = _strings.SelectAllMenu;
            _menuFitWidth.Text = _strings.FitWidth;
            _menuFitPage.Text = _strings.FitPage;
            _menuSpreadNone.Text = _strings.SpreadNoneMenu;
            _menuSpreadFacing.Text = _strings.SpreadFacingMenu;
            _menuSpreadCover.Text = _strings.SpreadCoverMenu;
            _menuRotateClockwise.Text = _strings.RotateClockwiseMenu;
            _menuRotateCounterclockwise.Text = _strings.RotateCounterclockwiseMenu;
            _pageMenu.RightToLeft = RightToLeft;
        }

        /// <summary>Selects as a double (<paramref name="line"/> false) or triple click at a canvas point would — for a test.</summary>
        internal string ClickSelect(Point virtualPoint, bool line)
        {
            SelectAround(virtualPoint, line ? TextUnit.Line : TextUnit.Word);
            return _selectedText;
        }
    }
}
