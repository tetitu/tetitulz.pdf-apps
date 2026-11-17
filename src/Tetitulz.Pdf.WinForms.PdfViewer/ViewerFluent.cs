// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The WinUI 3 type ramp, icon font, corner radii and focus visual, for a WinForms control.
    /// </summary>
    /// <remarks>
    /// <para><b>Fonts are the ones Windows installs; the control ships none.</b> Text is Segoe UI
    /// Variable (the <c>Text</c> optical size, which GDI lists as its own family per weight) where
    /// Windows 11 has it, else Segoe UI. Icons are Segoe Fluent Icons (Windows 11), else Segoe MDL2
    /// Assets (Windows 10), which share the code points used here; with neither the chrome falls
    /// back to its words and arrow characters.</para>
    /// <para>Sizes are WinUI's, in pixels at 96 dpi (Caption 12, Body 14, an icon 16), scaled by the
    /// monitor's dpi when the fonts are made.</para>
    /// </remarks>
    internal static class ViewerFluent
    {
        public const string TextFamilyVariable = "Segoe UI Variable Text";
        public const string TextFamilyVariableSemibold = "Segoe UI Variable Text Semibold";
        public const string TextFamilyClassic = "Segoe UI";
        public const string TextFamilyClassicSemibold = "Segoe UI Semibold";
        public const string IconFamilyFluent = "Segoe Fluent Icons";
        public const string IconFamilyMdl2 = "Segoe MDL2 Assets";

        public const float CaptionPx = 12f, BodyPx = 14f, IconPx = 16f, SmallIconPx = 12f;
        /// <summary>ControlCornerRadius and OverlayCornerRadius.</summary>
        public const int ControlRadius = 4, OverlayRadius = 8;

        private static readonly Lazy<string[]> Installed = new Lazy<string[]>(() =>
        {
            try
            {
                using (var fonts = new InstalledFontCollection())
                    return fonts.Families.Select(f => f.Name).ToArray();
            }
            catch (Exception) { return Array.Empty<string>(); }   // no font service: the fallbacks
        });

        public static bool IsInstalled(string family)
            => Array.Exists(Installed.Value, n => string.Equals(n, family, StringComparison.OrdinalIgnoreCase));

        /// <summary>The icon font's family, or null when Windows has neither icon font.</summary>
        public static string? IconFamily
            => IsInstalled(IconFamilyFluent) ? IconFamilyFluent : IsInstalled(IconFamilyMdl2) ? IconFamilyMdl2 : null;

        public static string TextFamily => IsInstalled(TextFamilyVariable) ? TextFamilyVariable : TextFamilyClassic;

        public static string TextFamilySemibold
            => IsInstalled(TextFamilyVariableSemibold) ? TextFamilyVariableSemibold : TextFamilyClassicSemibold;

        /// <summary>A font of <paramref name="px"/> pixels at 96 dpi, scaled; a family GDI+ refuses falls back to the default font's.</summary>
        public static Font Make(string family, float px, double scale, FontStyle style = FontStyle.Regular)
        {
            float size = (float)Math.Max(1, px * scale);
            try { return new Font(family, size, style, GraphicsUnit.Pixel); }
            catch (ArgumentException) { return new Font(Control.DefaultFont.FontFamily, size, style, GraphicsUnit.Pixel); }
        }

        /// <summary>A rectangle with rounded corners, as a path; a radius of 0 is a plain rectangle.</summary>
        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(2 * radius, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = RoundedRect(r, radius))
            using (var brush = new SolidBrush(color))
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(brush, path);
                g.SmoothingMode = old;
            }
        }

        public static void StrokeRounded(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
        {
            // The stroke's centre half a width inside the box, so a 1 px line lands on whole pixels.
            var inner = RectangleF.Inflate(r, -width / 2f, -width / 2f);
            using (var path = RoundedRect(inner, Math.Max(0, radius - width / 2f)))
            using (var pen = new Pen(color, width))
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPath(pen, path);
                g.SmoothingMode = old;
            }
        }

        /// <summary>
        /// WinUI's focus visual: a 2 px outer stroke and a 1 px inner one, round the element's box.
        /// </summary>
        public static void DrawFocus(Graphics g, ViewerPalette palette, Rectangle r, float radius, double scale)
        {
            float outer = (float)Math.Max(1, Math.Round(2 * scale));
            float inner = (float)Math.Max(1, Math.Round(scale));
            StrokeRounded(g, palette.FocusOuter, r, radius + outer, outer);
            StrokeRounded(g, palette.FocusInner, RectangleF.Inflate(r, -outer, -outer), radius, inner);
        }

        /// <summary>A glyph of the icon font, centred in <paramref name="box"/>.</summary>
        public static void DrawIcon(Graphics g, string glyph, Font iconFont, Rectangle box, Color color)
            => TextRenderer.DrawText(g, glyph, iconFont, box, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    /// <summary>
    /// The code points of the icons the chrome draws, each named as Microsoft's Segoe Fluent Icons
    /// list names it (learn.microsoft.com, "Segoe Fluent Icons font"); Segoe MDL2 Assets carries the
    /// same code points.
    /// </summary>
    internal static class ViewerIcons
    {
        public const string OpenFile = "";
        public const string Save = "";
        public const string Back = "";
        public const string Forward = "";
        public const string ChevronLeft = "";
        public const string ChevronRight = "";
        public const string ChevronUp = "";
        public const string ChevronDown = "";
        public const string ChevronRightSmall = "";
        public const string ChevronLeftSmall = "";
        public const string ZoomIn = "";
        public const string ZoomOut = "";
        public const string FitPage = "";
        /// <summary>ResizeMouseWide: a wide box, standing for a page fitted to the width.</summary>
        public const string FitWidth = "";
        /// <summary>ScrollUpDown: pages scrolled one after another.</summary>
        public const string Continuous = "";
        public const string Page = "";
        public const string TwoPage = "";
        public const string OpenPane = "";
        public const string OpenPaneMirrored = "";
        public const string Search = "";
        public const string Print = "";
        public const string Cancel = "";
        public const string Rotate = "";
        public const string Copy = "";
        public const string ViewAll = "";
        public const string Bookmarks = "";
        public const string Comment = "";
        public const string SignatureCapture = "";
        public const string CheckMark = "";
        public const string Error = "";
        public const string CompletedSolid = "";
        public const string Warning = "";
        public const string StatusErrorFull = "";
    }
}
