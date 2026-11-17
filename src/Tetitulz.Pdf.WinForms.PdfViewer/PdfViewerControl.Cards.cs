// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The two cards drawn over or above the pages: the find card, a WinUI flyout in the trailing top
    /// corner, and the signatures' banner, a WinUI <c>InfoBar</c>.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        // ── The find card ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lays the card out from leading-edge offsets, mirroring them when the card is right-to-left,
        /// sizes it to what it holds, and places it over the pages.
        /// </summary>
        /// <remarks>
        /// A plain <see cref="Panel"/> does not mirror absolutely-positioned children — measured: a
        /// child at <c>Location.X = 6</c> is still at 6 after the panel is set to
        /// <see cref="RightToLeft.Yes"/> — so every X is worked out here. Called when the result
        /// label's width changes (it is <c>AutoSize</c>, and the buttons after it follow it), when the
        /// direction changes, and when the fonts are remade.
        /// </remarks>
        private void LayoutFindBar()
        {
            if (_findBar == null || _fontBody == null) return;
            bool rtl = _findBar.RightToLeft == RightToLeft.Yes;
            int pad = Px(FindPad), boxW = Px(FindBoxWidth), boxH = Px(FindBoxHeight), icon = Px(FindBoxIconWidth);
            int nav = Px(FindNavSize), cardH = Px(FindCardHeight);

            int resultLead = pad + boxW + Px(FindGap);
            int prevLead = resultLead + _findResult.Width + Px(FindGap);
            int nextLead = prevLead + nav + Px(FindNavGap);
            int closeLead = nextLead + nav + 2 * Px(FindDividerGap) + 1;
            int w = closeLead + nav + pad;
            if (_findBar.Size != new Size(w, cardH)) _findBar.Size = new Size(w, cardH);

            int boxTop = (cardH - boxH) / 2;
            int textH = _findBox.PreferredHeight;
            Place(_findBox, pad + icon, boxTop + (boxH - textH) / 2, boxW - icon - Px(8), textH);
            Place(_findResult, resultLead, (cardH - _findResult.Height) / 2, _findResult.Width, _findResult.Height);
            Place(_btnFindPrev, prevLead, (cardH - nav) / 2, nav, nav);
            Place(_btnFindNext, nextLead, (cardH - nav) / 2, nav, nav);
            Place(_btnFindClose, closeLead, (cardH - nav) / 2, nav, nav);

            _findBoxFrame = new Rectangle(rtl ? w - pad - boxW : pad, boxTop, boxW, boxH);
            _findDividerX = rtl ? w - (nextLead + nav + Px(FindDividerGap)) - 1 : nextLead + nav + Px(FindDividerGap);
            ((FindCard)_findBar).Round(Px(ViewerFluent.OverlayRadius));
            _findBar.Invalidate();
            PlaceFindCard();

            void Place(Control c, int lead, int top, int width, int height)
                => c.Bounds = new Rectangle(rtl ? w - lead - width : lead, top, width, height);
        }

        /// <summary>The search box's frame inside the card, and the divider before close.</summary>
        private Rectangle _findBoxFrame;
        private int _findDividerX;

        /// <summary>
        /// The card over the pages' trailing top corner — the right in left-to-right, the left when
        /// mirrored — clear of the scroll bar, which the page surface keeps on its right.
        /// </summary>
        private void PlaceFindCard()
        {
            if (_findBar == null || _surface == null) return;
            Rectangle s = _surface.Bounds;
            int margin = Px(FindCardMargin);
            int x = RightToLeft == RightToLeft.Yes
                ? s.Left + margin
                : s.Left + _surface.ClientSize.Width - _findBar.Width - margin;
            x = Math.Max(s.Left, x);
            var at = new Point(x, s.Top + margin);
            if (_findBar.Location != at) _findBar.Location = at;
        }

        /// <summary>A subtle button of the find card, its glyph from the icon font.</summary>
        private CardButton FindCardButton() => new CardButton(this);

        /// <summary>The card's glyphs: the icon font's chevrons and cross, else the arrows and cross as text.</summary>
        private void ApplyFindButtonGlyphs()
        {
            bool icons = _fontIconSmall != null;
            _btnFindPrev.Text = icons ? ViewerIcons.ChevronUp : "▲";
            _btnFindNext.Text = icons ? ViewerIcons.ChevronDown : "▼";
            _btnFindClose.Text = icons ? ViewerIcons.Cancel : "✕";
        }

        private void ApplyFindCardColours(ViewerPalette p)
        {
            _findBar.BackColor = p.Card;
            _findBox.BackColor = _findBox.Focused ? p.CardControlFillActive : p.CardControlFill;
            _findBox.ForeColor = p.CardText;
            _findResult.BackColor = p.Card;
            _findResult.ForeColor = p.CardTextSecondary;
            foreach (Control b in new Control[] { _btnFindPrev, _btnFindNext, _btnFindClose })
            {
                b.BackColor = p.Card;
                b.ForeColor = p.CardText;
                b.Invalidate();
            }
            _findBar.Invalidate();
        }

        /// <summary>The card: the flyout's fill, a hairline stroke, rounded corners, the search box's WinUI frame.</summary>
        private sealed class FindCard : Panel
        {
            private readonly PdfViewerControl _owner;

            public FindCard(PdfViewerControl owner)
            {
                _owner = owner;
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            /// <summary>Clips the card to rounded corners.</summary>
            public void Round(int radius)
            {
                Region? old = Region;
                using (var path = ViewerFluent.RoundedRect(new RectangleF(0, 0, Width, Height), radius))
                    Region = new Region(path);
                old?.Dispose();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                ViewerPalette p = _owner._palette;
                int radius = _owner.Px(ViewerFluent.OverlayRadius);
                using (var back = new SolidBrush(p.Card)) g.FillRectangle(back, ClientRectangle);
                ViewerFluent.StrokeRounded(g, p.CardStrokeOnPageArea, new Rectangle(0, 0, Width, Height), radius);

                // The search box, as a WinUI TextBox: fill, hairline, the bottom edge accent while focused.
                Rectangle box = _owner._findBoxFrame;
                bool focused = _owner._findBox.Focused;
                int r = _owner.Px(ViewerFluent.ControlRadius);
                Color fill = focused ? p.CardControlFillActive : p.CardControlFill;
                if (_owner._findBox.BackColor != fill) _owner._findBox.BackColor = fill;
                ViewerFluent.FillRounded(g, fill, box, r);
                ViewerFluent.StrokeRounded(g, p.ControlStroke, box, r);
                int line = focused ? Math.Max(2, _owner.Px(2)) : 1;
                using (var edge = new SolidBrush(focused ? p.Accent : p.ControlStrokeSecondary))
                    g.FillRectangle(edge, box.X + r / 2, box.Bottom - line, box.Width - r, line);

                if (_owner._fontIconSmall != null)
                {
                    int icon = _owner.Px(FindBoxIconWidth);
                    bool rtl = RightToLeft == RightToLeft.Yes;
                    var at = new Rectangle(rtl ? box.Right - icon : box.X, box.Y, icon, box.Height);
                    ViewerFluent.DrawIcon(g, ViewerIcons.Search, _owner._fontIconSmall, at, p.CardTextSecondary);
                }

                using (var divider = new SolidBrush(p.CardStroke))
                {
                    int h = _owner.Px(20);
                    g.FillRectangle(divider, _owner._findDividerX, (Height - h) / 2, 1, h);
                }
            }
        }

        /// <summary>
        /// A WinUI subtle button: no fill until hovered or pressed, then a rounded one, its glyph centred,
        /// WinUI's focus visual from the keyboard.
        /// </summary>
        private sealed class CardButton : Button
        {
            private readonly PdfViewerControl _owner;
            private bool _hot, _down;

            public CardButton(PdfViewerControl owner)
            {
                _owner = owner;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

            /// <summary>The button clicked, shown or not — what a test does without a mouse.</summary>
            internal void Press() => OnClick(EventArgs.Empty);

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                ViewerPalette p = _owner._palette;
                using (var back = new SolidBrush(p.Card)) g.FillRectangle(back, ClientRectangle);
                int r = _owner.Px(ViewerFluent.ControlRadius);
                var box = new Rectangle(0, 0, Width, Height);
                if (_down) ViewerFluent.FillRounded(g, p.CardPressed, box, r);
                else if (_hot) ViewerFluent.FillRounded(g, p.CardHover, box, r);
                Color fore = Enabled ? p.CardText : p.TextDisabled;
                ViewerFluent.DrawIcon(g, Text, Font, box, fore);
                if (Focused && ShowFocusCues)
                    ViewerFluent.DrawFocus(g, p, Rectangle.Inflate(box, -_owner.Px(2), -_owner.Px(2)), r, _owner._chromeScale);
            }
        }

        // ── The signatures' banner ──────────────────────────────────────────────────────────

        /// <summary>The verdict the banner stands for, which picks its colours and icon.</summary>
        private SignatureVerdict _bannerTone = SignatureVerdict.Valid;

        /// <summary>
        /// The banner as a WinUI <c>InfoBar</c>: a rounded bar in its severity's background colour, a
        /// hairline stroke, the severity's icon, the message.
        /// </summary>
        private sealed class InfoBar : Label
        {
            private readonly PdfViewerControl _owner;

            public InfoBar(PdfViewerControl owner)
            {
                _owner = owner;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                ViewerPalette p = _owner._palette;
                using (var back = new SolidBrush(p.PageArea)) g.FillRectangle(back, ClientRectangle);
                int m = _owner.Px(8);
                var bar = new Rectangle(m, m, Math.Max(0, Width - 2 * m), Math.Max(0, Height - m));
                int r = _owner.Px(ViewerFluent.ControlRadius);
                SignatureVerdict tone = _owner._bannerTone;
                ViewerFluent.FillRounded(g, p.VerdictBackground(tone), bar, r);
                ViewerFluent.StrokeRounded(g, p.CardStroke, bar, r);

                bool rtl = RightToLeft == RightToLeft.Yes;
                int iconW = _owner.Px(40);
                var iconBox = new Rectangle(rtl ? bar.Right - iconW : bar.X, bar.Y, iconW, bar.Height);
                if (_owner._fontIcon != null)
                {
                    string glyph = tone == SignatureVerdict.Valid ? ViewerIcons.CompletedSolid
                                 : tone == SignatureVerdict.Unknown ? ViewerIcons.Warning : ViewerIcons.StatusErrorFull;
                    ViewerFluent.DrawIcon(g, glyph, _owner._fontIcon, iconBox, p.VerdictText(tone));
                }
                var text = new Rectangle(rtl ? bar.X + _owner.Px(12) : iconBox.Right, bar.Y,
                    Math.Max(0, bar.Width - iconW - _owner.Px(12)), bar.Height);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                            | TextFormatFlags.NoPrefix | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
                TextRenderer.DrawText(g, Text, Font, text, p.TextPrimary, flags);
            }
        }
    }
}
