// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The pages as WinUI cards on the content layer: a soft shadow round each, a ProgressRing while a
    /// page renders, an error glyph and caption when it cannot.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        // ── The shadow ──────────────────────────────────────────────────────────────────────

        /// <summary>The pens a page's shadow is drawn with, one a ring, outermost last; remade with the palette or the dpi.</summary>
        private Pen[]? _shadowPens;
        private Color _shadowPensColour;
        private double _shadowPensScale;

        /// <summary>How far a page's shadow reaches at 96 dpi, and how far down it falls.</summary>
        private const int ShadowReach = 6, ShadowDrop = 1;

        /// <summary>
        /// A shadow round a page: rings of a hairline each, fading outward and dropped a pixel, so a
        /// paint draws a few thin lines a page — no bitmap, nothing blended under the page itself.
        /// </summary>
        private void DrawPageShadow(Graphics g, Rectangle page)
        {
            Color shadow = _palette.Shadow;
            if (shadow.A == 0) return;
            if (_shadowPens == null || _shadowPensColour != shadow || Math.Abs(_shadowPensScale - _chromeScale) > 1e-9)
            {
                DisposePageShadow();
                int reach = Math.Max(1, Px(ShadowReach));
                _shadowPens = new Pen[reach];
                for (int i = 0; i < reach; i++)
                    _shadowPens[i] = new Pen(_palette.ShadowRing(i, reach));
                _shadowPensColour = shadow;
                _shadowPensScale = _chromeScale;
            }
            int drop = Px(ShadowDrop);
            // Whole pixels: the surface paints with a half-pixel offset (for its images), under which a
            // one-pixel line lands half a pixel out and the ring nearest the page leaves a light seam.
            PixelOffsetMode offset = g.PixelOffsetMode;
            g.PixelOffsetMode = PixelOffsetMode.None;
            for (int i = 0; i < _shadowPens.Length; i++)
            {
                int d = i + 1;
                g.DrawRectangle(_shadowPens[i], page.X - d, page.Y - d + drop, page.Width + 2 * d - 1, page.Height + 2 * d - 1);
            }
            g.PixelOffsetMode = offset;
        }

        private void DisposePageShadow()
        {
            if (_shadowPens == null) return;
            foreach (Pen pen in _shadowPens) pen.Dispose();
            _shadowPens = null;
        }

        // ── A page that cannot be drawn ─────────────────────────────────────────────────────

        private void DrawFailedPage(Graphics g, Rectangle page)
        {
            int icon = Px(32);
            Size text = TextRenderer.MeasureText(_strings.PageCannotRender, _fontCaption);
            int top = page.Y + (page.Height - icon - Px(8) - text.Height) / 2;
            if (_fontIcon != null)
                ViewerFluent.DrawIcon(g, ViewerIcons.Error, _fontIcon, new Rectangle(page.X, top, page.Width, icon), _palette.Critical);
            TextRenderer.DrawText(g, _strings.PageCannotRender, _fontCaption,
                new Rectangle(page.X, top + icon + Px(8), page.Width, text.Height), PaperTextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
        }

        /// <summary>WinUI's light theme's secondary text, which is what reads on white paper in any theme.</summary>
        private static readonly Color PaperTextSecondary = ViewerPalette.For(false).TextSecondary;

        // ── The ProgressRing ────────────────────────────────────────────────────────────────

        /// <summary>The pages a paint found still rendering, in canvas coordinates — where the ring turns.</summary>
        private readonly List<Rectangle> _ringPages = new List<Rectangle>();
        private Timer? _ringTimer;

        /// <summary>The ring's size and stroke at 96 dpi: WinUI's ProgressRing default.</summary>
        private const int RingSize = 32, RingStroke = 4;

        /// <summary>Whether the ring is turning now — what a test reads.</summary>
        internal bool ProgressRingRunning => _ringTimer != null && _ringTimer.Enabled;

        /// <summary>The pages the last paint found still rendering — what a test reads.</summary>
        internal int ProgressRingPageCount => _ringPages.Count;

        /// <summary>For a test: whether Windows' animation effects are on, in place of the system's.</summary>
        internal bool? AnimationsOverride;

        private bool AnimationsOn => AnimationsOverride ?? SystemInformation.UIEffectsEnabled;

        /// <summary>
        /// An indeterminate ring in the accent at the page's centre: an arc whose start turns once a
        /// second and whose length breathes, as WinUI's does. Still (a fixed arc) when animations are off.
        /// </summary>
        private void DrawProgressRing(Graphics g, Rectangle page)
        {
            int size = Px(RingSize);
            float stroke = Math.Max(1, Px(RingStroke));
            var box = new RectangleF(page.X + (page.Width - size) / 2f + stroke / 2, page.Y + (page.Height - size) / 2f + stroke / 2,
                size - stroke, size - stroke);
            _ringPages.Add(Rectangle.Inflate(Rectangle.Round(box), Core.Util.SaturatingCast.ToInt(stroke) + 1, Core.Util.SaturatingCast.ToInt(stroke) + 1));

            float start = 0, sweep = 270;
            if (AnimationsOn)
            {
                double t = (Environment.TickCount & int.MaxValue) / 1000.0;
                start = (float)(t * 360 % 360);
                sweep = (float)(150 + 110 * Math.Sin(t * Math.PI));
            }
            using (var pen = new Pen(_palette.HighContrast ? _palette.Accent : PaperAccent, stroke)
                   { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawArc(pen, box, start, sweep);
                g.SmoothingMode = old;
            }
        }

        /// <summary>The accent as drawn on white paper: the light theme's (Dark1), legible in any theme.</summary>
        private Color PaperAccent => ViewerPalette.For(false, false, AccentNow).Accent;

        /// <summary>Starts the ring's timer when a paint found a page rendering, stops it when none is.</summary>
        private void UpdateProgressRing()
        {
            bool want = _ringPages.Count > 0 && AnimationsOn && !IsDisposed;
            if (!want) { StopProgressRing(); return; }
            if (_ringTimer == null)
            {
                _ringTimer = new Timer { Interval = 33 };
                _ringTimer.Tick += (s, e) => TurnProgressRing();
            }
            if (!_ringTimer.Enabled) _ringTimer.Start();
        }

        /// <summary>One frame: the rings repainted where the last paint drew them.</summary>
        private void TurnProgressRing()
        {
            if (_ringPages.Count == 0 || IsDisposed) { StopProgressRing(); return; }
            Point scroll = _surface.AutoScrollPosition;
            foreach (Rectangle r in _ringPages)
                _surface.Invalidate(new Rectangle(r.X + scroll.X, r.Y + scroll.Y, r.Width, r.Height));
        }

        private void StopProgressRing()
        {
            _ringTimer?.Stop();
        }
    }
}
