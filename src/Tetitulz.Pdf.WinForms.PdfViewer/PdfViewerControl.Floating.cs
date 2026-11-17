// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Floating page controls: a card at the bottom centre of the pages, as browsers' PDF viewers have
    /// — previous, the page, next, zoom out, the zoom, zoom in, fit the page — shown while the mouse
    /// moves over the pages and hidden two seconds after it stops. Off by default; for a host that hides
    /// the toolbar.
    /// </summary>
    /// <remarks>
    /// The card appears and disappears rather than fading: a WinForms child window cannot be translucent
    /// without a platform call, which the library makes none of.
    /// </remarks>
    public sealed partial class PdfViewerControl
    {
        private bool _showFloatingControls;
        private Panel? _floating;
        private CardButton? _floatPrev, _floatNext, _floatZoomOut, _floatZoomIn, _floatFit;
        private Label? _floatPage, _floatZoom;
        private Timer? _floatTimer;

        /// <summary>How long the card stays after the mouse stops, in milliseconds.</summary>
        private const int FloatingLingerMs = 2000;

        /// <summary>
        /// Whether the pages show floating controls at their bottom centre while the mouse moves over
        /// them. Default off. Each button runs its command as the toolbar's does, through
        /// <see cref="CommandExecuting"/>.
        /// </summary>
        [Category("PDF"), DefaultValue(false),
         Description("Show page and zoom controls floating over the pages while the mouse moves.")]
        public bool ShowFloatingControls
        {
            get => _showFloatingControls;
            set
            {
                if (_showFloatingControls == value) return;
                _showFloatingControls = value;
                if (value) EnsureFloatingControls();
                else HideFloatingControls();
            }
        }

        /// <summary>The floating card, or null before it is first wanted — what a test reads.</summary>
        internal Control? FloatingControls => _floating;

        /// <summary>The floating card's buttons: previous, next, zoom out, zoom in, fit — what a test presses.</summary>
        internal Button[] FloatingButtons => _floating == null
            ? Array.Empty<Button>()
            : new Button[] { _floatPrev!, _floatNext!, _floatZoomOut!, _floatZoomIn!, _floatFit! };

        /// <summary>Floating button <paramref name="index"/> clicked — what a test does without a mouse.</summary>
        internal void ClickFloatingButton(int index) => ((CardButton)FloatingButtons[index]).Press();

        /// <summary>The floating card's page text — what a test reads.</summary>
        internal string FloatingPageText => _floatPage?.Text ?? "";

        private void EnsureFloatingControls()
        {
            if (_floating != null) return;
            _floating = new FloatingCard(this) { Visible = false };
            _floatPrev = FloatingButton(() => RunCommand(PdfViewerCommand.PreviousPage, PreviousPage));
            _floatNext = FloatingButton(() => RunCommand(PdfViewerCommand.NextPage, NextPage));
            _floatZoomOut = FloatingButton(() => RunCommand(PdfViewerCommand.ZoomOut, ZoomOut));
            _floatZoomIn = FloatingButton(() => RunCommand(PdfViewerCommand.ZoomIn, ZoomIn));
            _floatFit = FloatingButton(() => RunCommand(PdfViewerCommand.FitPage, FitPage));
            _floatPage = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
            _floatZoom = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter };
            _floating.Controls.AddRange(new Control[] { _floatPrev, _floatPage, _floatNext, _floatZoomOut, _floatZoom, _floatZoomIn, _floatFit });
            _split.Panel2.Controls.Add(_floating);
            _floating.MouseLeave += (s, e) => NudgeFloatingControls();
            _floatTimer = new Timer { Interval = FloatingLingerMs };
            _floatTimer.Tick += (s, e) =>
            {
                // The mouse resting on the card keeps it.
                if (_floating != null && _floating.ClientRectangle.Contains(_floating.PointToClient(Cursor.Position))) return;
                HideFloatingControls();
            };
            UpdateFloatingControls();
        }

        private CardButton FloatingButton(Action click)
        {
            var b = new CardButton(this);
            b.Click += (s, e) => { click(); NudgeFloatingControls(); };
            return b;
        }

        /// <summary>Shows the card (when wanted and a document is open) and restarts its two seconds.</summary>
        internal void NudgeFloatingControls()
        {
            if (!_showFloatingControls || _doc == null || _floating == null || _floatTimer == null) return;
            if (!_floating.Visible)
            {
                LayoutFloatingControls();
                _floating.Visible = true;
                _floating.BringToFront();
                _findBar.BringToFront();
            }
            _floatTimer.Stop();
            _floatTimer.Start();
        }

        private void HideFloatingControls()
        {
            _floatTimer?.Stop();
            if (_floating != null) _floating.Visible = false;
        }

        /// <summary>The card's texts, glyphs, names, fonts and colours as the view and the theme stand.</summary>
        private void UpdateFloatingControls()
        {
            if (_floating == null) return;
            bool rtl = RightToLeft == RightToLeft.Yes;
            bool icons = _fontIconSmall != null;
            _floatPrev!.Text = icons ? (rtl ? ViewerIcons.ChevronRight : ViewerIcons.ChevronLeft) : (rtl ? GlyphRight : GlyphLeft);
            _floatNext!.Text = icons ? (rtl ? ViewerIcons.ChevronLeft : ViewerIcons.ChevronRight) : (rtl ? GlyphLeft : GlyphRight);
            _floatZoomOut!.Text = icons ? ViewerIcons.ZoomOut : "−";
            _floatZoomIn!.Text = icons ? ViewerIcons.ZoomIn : "+";
            _floatFit!.Text = icons ? ViewerIcons.FitPage : "⤢";
            _floatPrev.AccessibleName = _strings.PreviousPageTip;
            _floatNext.AccessibleName = _strings.NextPageTip;
            _floatZoomOut.AccessibleName = _strings.ZoomOutTip;
            _floatZoomIn.AccessibleName = _strings.ZoomInTip;
            _floatFit.AccessibleName = _strings.FitPageTip;
            foreach (Button b in FloatingButtons)
            {
                b.Font = _fontIconSmall ?? _fontBody;
                b.BackColor = _palette.Card;
                b.Enabled = _doc != null;
            }
            _floatPrev.Enabled = _doc != null && CanStep(-1);
            _floatNext.Enabled = _doc != null && CanStep(+1);
            _floatPage!.Text = _pageCount > 0 ? PageBoxText(_currentPage) + " / " + _pageCount.ToString(CultureInfo.CurrentCulture) : "";
            _floatZoom!.Text = Math.Round(_zoom * 100).ToString(CultureInfo.InvariantCulture) + "%";
            foreach (Label l in new[] { _floatPage, _floatZoom })
            {
                l.Font = _fontBody;
                l.ForeColor = _palette.CardText;
                l.BackColor = _palette.Card;
            }
            _floating.BackColor = _palette.Card;
            if (_floating.Visible) LayoutFloatingControls();
            _floating.Invalidate();
        }

        /// <summary>The card's items from its leading edge, the card centred at the bottom of the pages.</summary>
        private void LayoutFloatingControls()
        {
            if (_floating == null) return;
            bool rtl = _floating.RightToLeft == RightToLeft.Yes;
            int nav = Px(32), pad = Px(6), h = Px(44);
            int pageW = Math.Max(Px(64), TextRenderer.MeasureText(_floatPage!.Text, _fontBody).Width + Px(12));
            int zoomW = Px(56);
            int x = pad;
            int w = pad + nav + pageW + nav + Px(13) + nav + zoomW + nav + nav + pad;
            _floating.Size = new Size(w, h);
            Lay(_floatPrev!, nav);
            Lay(_floatPage, pageW);
            Lay(_floatNext!, nav);
            _floatingDividerX = rtl ? w - x - Px(6) - 1 : x + Px(6);
            x += Px(13);
            Lay(_floatZoomOut!, nav);
            Lay(_floatZoom!, zoomW);
            Lay(_floatZoomIn!, nav);
            Lay(_floatFit!, nav);

            Rectangle s = _surface.Bounds;
            _floating.Location = new Point(s.Left + Math.Max(0, (_surface.ClientSize.Width - w) / 2),
                s.Top + Math.Max(0, _surface.ClientSize.Height - h - Px(24)));
            ((FloatingCard)_floating).Round(Px(ViewerFluent.OverlayRadius));

            void Lay(Control c, int width)
            {
                c.Bounds = new Rectangle(rtl ? w - x - width : x, (h - nav) / 2, width, nav);
                x += width;
            }
        }

        private int _floatingDividerX;

        /// <summary>The floating card: the flyout's fill, a hairline stroke, a divider between page and zoom.</summary>
        private sealed class FloatingCard : Panel
        {
            private readonly PdfViewerControl _owner;

            public FloatingCard(PdfViewerControl owner)
            {
                _owner = owner;
                DoubleBuffered = true;
            }

            public void Round(int radius)
            {
                Region? old = Region;
                using (var path = ViewerFluent.RoundedRect(new RectangleF(0, 0, Width, Height), radius))
                    Region = new Region(path);
                old?.Dispose();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                ViewerPalette p = _owner._palette;
                using (var back = new SolidBrush(p.Card)) e.Graphics.FillRectangle(back, ClientRectangle);
                ViewerFluent.StrokeRounded(e.Graphics, p.CardStrokeOnPageArea, new Rectangle(0, 0, Width, Height),
                    _owner.Px(ViewerFluent.OverlayRadius));
                int h = _owner.Px(20);
                using (var divider = new SolidBrush(p.CardStroke))
                    e.Graphics.FillRectangle(divider, _owner._floatingDividerX, (Height - h) / 2, 1, h);
            }
        }

        private void DisposeFloatingControls()
        {
            _floatTimer?.Dispose();
            _floatTimer = null;
        }
    }
}
