// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Draws the toolbar as a WinUI <c>CommandBar</c>, the status line as a slim bar, and every menu as
    /// a <c>MenuFlyout</c>: flat base colours, rounded subtle fills on hover and press, an accent fill on
    /// a checked toggle, icons from the system's icon font, and text boxes framed as WinUI's.
    /// </summary>
    /// <remarks>
    /// A managed <see cref="ToolStripRenderer"/>: nothing here asks Windows to theme a window, so the
    /// library keeps its rule of no platform calls.
    /// </remarks>
    internal sealed class ViewerToolStripRenderer : ToolStripRenderer
    {
        private readonly Func<ToolStripItem, string?> _iconOf;

        public ViewerToolStripRenderer(Func<ToolStripItem, string?> iconOf) => _iconOf = iconOf;

        public ViewerPalette Palette { get; set; } = ViewerPalette.For(false);

        /// <summary>The icon font, or null when Windows has none — then items draw as they would without this renderer's icons.</summary>
        public Font? IconFont { get; set; }

        /// <summary>The monitor's dpi over 96.</summary>
        public double Scale { get; set; } = 1.0;

        private int Px(double v) => Tetitulz.Pdf.Core.Util.SaturatingCast.ToInt(Math.Round(v * Scale));

        private static bool IsMenu(ToolStrip strip) => strip is ToolStripDropDown;

        // ── Backgrounds and borders ─────────────────────────────────────────────────────────

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            Color back = IsMenu(e.ToolStrip) ? Palette.Card : Palette.Base;
            using (var brush = new SolidBrush(back)) g.FillRectangle(brush, e.AffectedBounds);

            if (IsMenu(e.ToolStrip)) return;
            // A text box or combo box hosted on the bar, framed as a WinUI TextBox: a rounded control
            // fill, a hairline stroke, and the bottom edge darker — accent and two pixels thick while it
            // has the focus.
            foreach (ToolStripItem item in e.ToolStrip.Items)
            {
                if (!(item is ToolStripControlHost host) || !item.Available || !item.Visible) continue;
                Rectangle r = FieldFrame(host);
                bool focused = host.Control.ContainsFocus;
                ViewerFluent.FillRounded(g, focused ? Palette.ControlFillActive : Palette.ControlFill, r, Px(ViewerFluent.ControlRadius));
                ViewerFluent.StrokeRounded(g, Palette.ControlStroke, r, Px(ViewerFluent.ControlRadius));
                int line = focused ? Math.Max(2, Px(2)) : Math.Max(1, Px(1));
                using (var brush = new SolidBrush(focused ? Palette.Accent : Palette.ControlStrokeSecondary))
                    g.FillRectangle(brush, r.X + Px(ViewerFluent.ControlRadius) / 2, r.Bottom - line,
                        r.Width - Px(ViewerFluent.ControlRadius), line);
            }
        }

        /// <summary>The frame drawn round a hosted text box: its bounds, padded to a WinUI field's size.</summary>
        internal Rectangle FieldFrame(ToolStripControlHost host)
        {
            Rectangle b = host.Bounds;
            int h = Px(30);
            int padX = host.Control is ComboBox ? Px(2) : Px(8);
            return new Rectangle(b.X - padX, b.Y + (b.Height - h) / 2, b.Width + 2 * padX, h);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            Rectangle r = new Rectangle(Point.Empty, e.ToolStrip.Size);
            if (IsMenu(e.ToolStrip))
            {
                using (var pen = new Pen(Palette.CardStroke)) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
                return;
            }
            using (var brush = new SolidBrush(Palette.Divider))
            {
                if (e.ToolStrip is StatusStrip) g.FillRectangle(brush, 0, 0, r.Width, 1);
                else g.FillRectangle(brush, 0, r.Height - 1, r.Width, 1);
            }
        }

        protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e) { }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderLabelBackground(ToolStripItemRenderEventArgs e) { }

        // ── Buttons ─────────────────────────────────────────────────────────────────────────

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) => ButtonBackground(e);

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e) => ButtonBackground(e);

        protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e) => ButtonBackground(e);

        protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e) => ButtonBackground(e);

        private void ButtonBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripItem item = e.Item;
            bool isChecked = item is ToolStripButton b && b.Checked;
            Color? fill = null;
            if (isChecked)
                fill = item.Selected && item.Enabled ? Palette.AccentHover : Palette.Accent;
            else if (item.Pressed && item.Enabled)
                fill = Palette.SubtlePressed;
            else if (item.Selected && item.Enabled)
                fill = Palette.SubtleHover;
            if (fill == null) return;
            var r = new Rectangle(Point.Empty, item.Size);
            r.Inflate(-Px(1), -Px(2));
            ViewerFluent.FillRounded(e.Graphics, fill.Value, r, Px(ViewerFluent.ControlRadius));
        }

        /// <summary>Whether an item is drawn with an accent fill, so its icon and text go in the colour on accent.</summary>
        private static bool OnAccent(ToolStripItem item) => item is ToolStripButton b && b.Checked;

        private Color ForegroundOf(ToolStripItem item)
        {
            bool menu = item.Owner is ToolStripDropDown || item.IsOnDropDown;
            if (!item.Enabled) return Palette.TextDisabled;
            if (OnAccent(item)) return Palette.TextOnAccent;
            return menu ? Palette.CardText : Palette.TextPrimary;
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            string? glyph = IconFont == null ? null : _iconOf(e.Item);
            if (glyph == null) { base.OnRenderItemImage(e); return; }
            Rectangle box = e.ImageRectangle;
            int size = Px(ViewerFluent.IconPx);
            var centred = new Rectangle(box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2, size, size);
            ViewerFluent.DrawIcon(e.Graphics, glyph, IconFont!, centred, ForegroundOf(e.Item));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool secondary = e.Item is ToolStripStatusLabel || (e.Item is ToolStripLabel && !(e.Item.Owner is ToolStripDropDown));
            e.TextColor = !e.Item.Enabled ? Palette.TextDisabled
                        : secondary ? Palette.TextSecondary
                        : ForegroundOf(e.Item);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && !e.Item.Enabled ? Palette.TextDisabled : Palette.TextSecondary;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            Size s = e.Item.Size;
            using (var brush = new SolidBrush(IsMenu(e.Item.Owner!) ? Palette.CardStroke : Palette.Divider))
            {
                if (e.Vertical)
                {
                    int h = Math.Min(s.Height, Px(20));
                    g.FillRectangle(brush, s.Width / 2, (s.Height - h) / 2, Math.Max(1, Px(1)), h);
                }
                else
                {
                    g.FillRectangle(brush, 0, s.Height / 2, s.Width, Math.Max(1, Px(1)));
                }
            }
        }

        // ── Menus ───────────────────────────────────────────────────────────────────────────

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripItem item = e.Item;
            if (!item.Enabled || !(item.Selected || item.Pressed)) return;
            var r = new Rectangle(Point.Empty, item.Size);
            r.Inflate(-Px(4), -Px(1));
            ViewerFluent.FillRounded(e.Graphics, item.Pressed ? Palette.CardPressed : Palette.CardHover, r,
                Px(ViewerFluent.ControlRadius));
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (IconFont == null) { base.OnRenderItemCheck(e); return; }
            ViewerFluent.DrawIcon(e.Graphics, ViewerIcons.CheckMark, IconFont, e.ImageRectangle, ForegroundOf(e.Item));
        }
    }
}
