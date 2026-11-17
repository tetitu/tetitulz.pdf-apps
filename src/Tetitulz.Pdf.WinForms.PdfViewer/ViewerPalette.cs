// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Every colour the viewer draws with, as WinUI 3 names them: the chrome, the page area, text,
    /// fills, strokes, the accent, the status colours, and the overlays drawn over a page. Under
    /// Windows' high-contrast setting every token is a system colour, so a reader who needs contrast
    /// sees the chrome, the selection and the current page in the colours they chose.
    /// </summary>
    /// <remarks>
    /// <para><b>The values are WinUI's own</b>, copied from microsoft-ui-xaml at commit
    /// <c>8463f45162149de0ec3ad7df752596893fe3e13e</c>:
    /// <c>controls/dev/CommonStyles/Common_themeresources_any.xaml</c> (the Light and Default — dark —
    /// dictionaries) and <c>controls/dev/Materials/Acrylic/AcrylicBrush_themeresources.xaml</c> (the
    /// flyout's fallback colour, used here since a control cannot draw acrylic). Each token keeps
    /// WinUI's name in its comment.</para>
    /// <para><b>A translucent token is blended once, over the layer it sits on, and stored opaque.</b>
    /// A WinForms child does not composite its alpha over its parent, and GDI text drawn on a
    /// translucent fill smears.</para>
    /// </remarks>
    internal sealed class ViewerPalette
    {
        /// <summary>The accent WinUI falls back to when Windows names none (Windows' default blue).</summary>
        internal static readonly Color DefaultAccent = Color.FromArgb(0x00, 0x78, 0xD4);

        private ViewerPalette(bool highContrast, bool dark, AccentShades accent)
        {
            HighContrast = highContrast;
            Dark = !highContrast && dark;
            if (highContrast) { SetHighContrast(); return; }

            // The base layers (SolidBackgroundFillColorBase, LayerFillColorDefault over it).
            Base = dark ? Rgb(0x202020) : Rgb(0xF3F3F3);
            PageArea = Over(dark ? Argb(0x4C3A3A3A) : Argb(0x80FFFFFF), Base);
            // AcrylicBackgroundFillColorDefaultBrush's FallbackColor.
            Card = dark ? Rgb(0x2C2C2C) : Rgb(0xF9F9F9);

            // TextFillColorPrimary / Secondary / Tertiary / Disabled, over the base.
            Color text = dark ? Argb(0xFFFFFFFF) : Argb(0xE4000000);
            TextPrimary = Over(text, Base);
            TextSecondary = Over(dark ? Argb(0xC5FFFFFF) : Argb(0x9E000000), Base);
            TextTertiary = Over(dark ? Argb(0x87FFFFFF) : Argb(0x72000000), Base);
            TextDisabled = Over(dark ? Argb(0x5DFFFFFF) : Argb(0x5C000000), Base);
            CardText = Over(text, Card);
            CardTextSecondary = Over(dark ? Argb(0xC5FFFFFF) : Argb(0x9E000000), Card);

            // SubtleFillColorSecondary / Tertiary: hover and pressed on a button with no fill of its own.
            SubtleHover = Over(dark ? Argb(0x0FFFFFFF) : Argb(0x09000000), Base);
            SubtlePressed = Over(dark ? Argb(0x0AFFFFFF) : Argb(0x06000000), Base);
            CardHover = Over(dark ? Argb(0x0FFFFFFF) : Argb(0x09000000), Card);
            CardPressed = Over(dark ? Argb(0x0AFFFFFF) : Argb(0x06000000), Card);

            // ControlFillColorDefault, ControlFillColorInputActive (a text box with the focus).
            ControlFill = Over(dark ? Argb(0x0FFFFFFF) : Argb(0xB3FFFFFF), Base);
            ControlFillActive = Over(dark ? Argb(0xB31E1E1E) : Argb(0xFFFFFFFF), Base);
            CardControlFill = Over(dark ? Argb(0x0FFFFFFF) : Argb(0xB3FFFFFF), Card);
            CardControlFillActive = Over(dark ? Argb(0xB31E1E1E) : Argb(0xFFFFFFFF), Card);

            // ControlStrokeColorDefault / Secondary, ControlStrongStrokeColorDefault, DividerStrokeColorDefault,
            // CardStrokeColorDefault.
            ControlStroke = Over(dark ? Argb(0x12FFFFFF) : Argb(0x0F000000), Base);
            ControlStrokeSecondary = Over(dark ? Argb(0x18FFFFFF) : Argb(0x29000000), Base);
            ControlStrongStroke = Over(dark ? Argb(0x8BFFFFFF) : Argb(0x72000000), Base);
            Divider = Over(dark ? Argb(0x15FFFFFF) : Argb(0x0F000000), Base);
            CardStroke = Over(dark ? Argb(0x19000000) : Argb(0x0F000000), Card);
            CardStrokeOnPageArea = Over(dark ? Argb(0x19000000) : Argb(0x0F000000), PageArea);

            // AccentFillColorDefault (Light: SystemAccentColorDark1; Dark: SystemAccentColorLight2),
            // AccentTextFillColorPrimary (Dark2 / Light3), TextOnAccentFillColorPrimary.
            Accent = dark ? accent.Light2 : accent.Dark1;
            AccentHover = Over(Color.FromArgb(230, Accent), Base);           // AccentFillColorSecondary, 0.9
            AccentText = dark ? accent.Light3 : accent.Dark2;
            // WinUI's own choice (white on Dark1, black on Light2) whenever it reads; a host's accent
            // can be light enough in the light theme that it does not, and then black is the one that does.
            Color onAccent = dark ? Color.Black : Color.White, other = dark ? Color.White : Color.Black;
            TextOnAccent = ContrastRatio(onAccent, Accent) >= 4.5 || ContrastRatio(onAccent, Accent) >= ContrastRatio(other, Accent)
                ? onAccent : other;

            // FocusStrokeColorOuter / Inner.
            FocusOuter = Over(dark ? Argb(0xFFFFFFFF) : Argb(0xE4000000), Base);
            FocusInner = dark ? Over(Argb(0xB3000000), Base) : Over(Argb(0xB3FFFFFF), Base);

            // SystemFillColorSuccess / Caution / Critical and their backgrounds (the InfoBar's).
            Success = dark ? Rgb(0x6CCB5F) : Rgb(0x0F7B0F);
            Caution = dark ? Rgb(0xFCE100) : Rgb(0x9D5D00);
            Critical = dark ? Rgb(0xFF99A4) : Rgb(0xC42B1C);
            SuccessBackground = dark ? Rgb(0x393D1B) : Rgb(0xDFF6DD);
            CautionBackground = dark ? Rgb(0x433519) : Rgb(0xFFF4CE);
            CriticalBackground = dark ? Rgb(0x442726) : Rgb(0xFDE7E9);

            // The shadow a card or a page casts: WinUI's ThemeShadow is darker on a dark layer.
            Shadow = dark ? Color.FromArgb(110, 0, 0, 0) : Color.FromArgb(46, 0, 0, 0);

            // Overlays over a page, which is white in every theme. The selection is the accent's,
            // as a WinUI text control's (TextControlSelectionHighlightColor: SystemAccentColor).
            Selection = Color.FromArgb(90, accent.Accent);
            FindMatch = Color.FromArgb(80, 255, 235, 0);
            FindCurrent = Color.FromArgb(150, 255, 150, 0);
            LinkHover = Color.FromArgb(40, accent.Accent);
            LinkHoverBorder = Color.FromArgb(160, accent.Accent);
            FormField = Color.FromArgb(48, accent.Accent);
        }

        private void SetHighContrast()
        {
            Base = SystemColors.Window;
            PageArea = SystemColors.AppWorkspace;
            Card = SystemColors.Window;
            TextPrimary = TextSecondary = TextTertiary = CardText = CardTextSecondary = SystemColors.WindowText;
            TextDisabled = SystemColors.GrayText;
            SubtleHover = SubtlePressed = CardHover = CardPressed = SystemColors.Highlight;
            ControlFill = ControlFillActive = CardControlFill = CardControlFillActive = SystemColors.Window;
            ControlStroke = ControlStrokeSecondary = ControlStrongStroke = Divider = CardStroke = CardStrokeOnPageArea
                = SystemColors.WindowText;
            Accent = AccentHover = AccentText = SystemColors.Highlight;
            TextOnAccent = SystemColors.HighlightText;
            FocusOuter = SystemColors.WindowText;
            FocusInner = SystemColors.Window;
            Success = Caution = Critical = SystemColors.WindowText;
            SuccessBackground = CautionBackground = CriticalBackground = SystemColors.Window;
            Shadow = Color.Transparent;
            Selection = Color.FromArgb(110, SystemColors.Highlight);
            FindMatch = Color.FromArgb(70, SystemColors.Highlight);
            FindCurrent = Color.FromArgb(140, SystemColors.Highlight);
            LinkHover = Color.FromArgb(40, SystemColors.Highlight);
            LinkHoverBorder = SystemColors.Highlight;
            FormField = Color.FromArgb(48, SystemColors.Highlight);
        }

        /// <summary>Whether these are the system's high-contrast colours.</summary>
        public bool HighContrast { get; }

        /// <summary>Whether this is the dark theme (never under high contrast).</summary>
        public bool Dark { get; }

        /// <summary>The chrome: toolbar, sidebar, status line (SolidBackgroundFillColorBase).</summary>
        public Color Base { get; private set; }
        /// <summary>The background the pages sit on (LayerFillColorDefault over the base).</summary>
        public Color PageArea { get; private set; }
        /// <summary>A flyout: the find card, the floating controls, a menu.</summary>
        public Color Card { get; private set; }

        public Color TextPrimary { get; private set; }
        public Color TextSecondary { get; private set; }
        public Color TextTertiary { get; private set; }
        public Color TextDisabled { get; private set; }
        public Color CardText { get; private set; }
        public Color CardTextSecondary { get; private set; }

        public Color SubtleHover { get; private set; }
        public Color SubtlePressed { get; private set; }
        public Color CardHover { get; private set; }
        public Color CardPressed { get; private set; }

        public Color ControlFill { get; private set; }
        public Color ControlFillActive { get; private set; }
        public Color CardControlFill { get; private set; }
        public Color CardControlFillActive { get; private set; }

        public Color ControlStroke { get; private set; }
        /// <summary>The bottom edge of a text box, darker than its other three.</summary>
        public Color ControlStrokeSecondary { get; private set; }
        /// <summary>An icon or a chevron that must read as a control (ControlStrongStrokeColorDefault).</summary>
        public Color ControlStrongStroke { get; private set; }
        public Color Divider { get; private set; }
        public Color CardStroke { get; private set; }
        /// <summary>A card's stroke where the card sits over the page area.</summary>
        public Color CardStrokeOnPageArea { get; private set; }

        public Color Accent { get; private set; }
        public Color AccentHover { get; private set; }
        /// <summary>Accent-coloured text and icons on the base (AccentTextFillColorPrimary).</summary>
        public Color AccentText { get; private set; }
        public Color TextOnAccent { get; private set; }

        public Color FocusOuter { get; private set; }
        public Color FocusInner { get; private set; }

        public Color Success { get; private set; }
        public Color Caution { get; private set; }
        public Color Critical { get; private set; }
        public Color SuccessBackground { get; private set; }
        public Color CautionBackground { get; private set; }
        public Color CriticalBackground { get; private set; }

        public Color Shadow { get; private set; }

        public Color Selection { get; private set; }
        public Color FindMatch { get; private set; }
        /// <summary>The match the find bar is on.</summary>
        public Color FindCurrent { get; private set; }
        public Color LinkHover { get; private set; }
        public Color LinkHoverBorder { get; private set; }
        /// <summary>The tint over a fillable form field.</summary>
        public Color FormField { get; private set; }

        /// <summary>
        /// Ring <paramref name="ring"/> (0 nearest the page) of <paramref name="rings"/> in a page's
        /// shadow: the shadow's colour, fading with the square of the distance.
        /// </summary>
        public Color ShadowRing(int ring, int rings)
        {
            double t = 1 - (ring + 1) / (double)(rings + 1);
            return Color.FromArgb(Math.Max(1, SaturatingCast.ToInt(Math.Round(Shadow.A * t * t))), Shadow);
        }

        /// <summary>The current page's thumbnail outline.</summary>
        public Color CurrentThumbnail => Accent;

        // ── Overlays whose colour means the same in every theme ─────────────────────────────

        /// <summary>The outline round the comment chosen in the sidebar.</summary>
        public static readonly Color CommentOutline = Color.FromArgb(230, 120, 0);
        /// <summary>An unsigned signature field's tint and dashed border.</summary>
        public static readonly Color UnsignedField = Color.FromArgb(56, 255, 170, 0);
        public static readonly Color UnsignedFieldBorder = Color.FromArgb(200, 160, 100, 0);
        /// <summary>A page: white paper in every theme, as a PDF viewer draws one.</summary>
        public static readonly Color Paper = Color.White;
        /// <summary>The text of a page that failed to print, on paper.</summary>
        public static readonly Color PrintedText = Color.Black;

        /// <summary>
        /// The palette for a theme, read afresh: the system's colours change when the user changes
        /// theme, and a palette kept from before would keep the old ones.
        /// </summary>
        public static ViewerPalette For(bool highContrast, bool dark, AccentShades accent)
            => new ViewerPalette(highContrast, dark, accent);

        /// <summary>The light palette (or high contrast) with Windows' default accent.</summary>
        public static ViewerPalette For(bool highContrast)
            => new ViewerPalette(highContrast, false, AccentShades.From(DefaultAccent));

        /// <summary>The colour status a signature verdict reads in, as WinUI's InfoBar severities.</summary>
        public Color VerdictText(SignatureVerdict verdict)
            => verdict == SignatureVerdict.Valid ? Success : verdict == SignatureVerdict.Unknown ? Caution : Critical;

        /// <summary>The background a verdict's banner is drawn on.</summary>
        public Color VerdictBackground(SignatureVerdict verdict)
            => verdict == SignatureVerdict.Valid ? SuccessBackground
             : verdict == SignatureVerdict.Unknown ? CautionBackground : CriticalBackground;

        // ── Colour arithmetic ────────────────────────────────────────────────────────────────

        /// <summary><paramref name="top"/> composited over the opaque <paramref name="bottom"/>, opaque.</summary>
        internal static Color Over(Color top, Color bottom)
        {
            double a = top.A / 255.0;
            int Mix(int t, int b) => SaturatingCast.ToInt(Math.Round(t * a + b * (1 - a)));
            return Color.FromArgb(255, Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
        }

        /// <summary>WCAG 2's contrast ratio between two opaque colours, 1 to 21.</summary>
        internal static double ContrastRatio(Color a, Color b)
        {
            double la = RelativeLuminance(a), lb = RelativeLuminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            static double Lin(int v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }

        private static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        /// <summary>A colour from WinUI's <c>#AARRGGBB</c> written as a number.</summary>
        private static Color Argb(long argb) => Color.FromArgb(unchecked((int)argb));
    }

    /// <summary>
    /// An accent colour and the six shades WinUI draws with (SystemAccentColorLight1–3, Dark1–3).
    /// </summary>
    internal readonly struct AccentShades
    {
        public AccentShades(Color light3, Color light2, Color light1, Color accent, Color dark1, Color dark2, Color dark3)
        {
            Light3 = light3; Light2 = light2; Light1 = light1; Accent = accent; Dark1 = dark1; Dark2 = dark2; Dark3 = dark3;
        }

        public Color Light3 { get; }
        public Color Light2 { get; }
        public Color Light1 { get; }
        public Color Accent { get; }
        public Color Dark1 { get; }
        public Color Dark2 { get; }
        public Color Dark3 { get; }

        /// <summary>
        /// The shades of an accent Windows did not supply (a host's own), mixed toward white and black.
        /// </summary>
        /// <remarks>
        /// Windows works its shades out with an algorithm it does not publish; when it is the system's
        /// accent the viewer reads Windows' own (<see cref="TryParseRegistryPalette"/>). These mixes
        /// land near Windows' shades for its default blue, close enough for a host-chosen accent.
        /// </remarks>
        public static AccentShades From(Color accent)
        {
            accent = Color.FromArgb(255, accent);
            return new AccentShades(
                Mix(accent, Color.White, 0.60), Mix(accent, Color.White, 0.40), Mix(accent, Color.White, 0.20),
                accent,
                Mix(accent, Color.Black, 0.15), Mix(accent, Color.Black, 0.40), Mix(accent, Color.Black, 0.65));
        }

        /// <summary>
        /// Windows' own shades, from the 32 bytes of <c>Explorer\Accent\AccentPalette</c>: eight RGBA
        /// entries, Light3, Light2, Light1, the accent, Dark1, Dark2, Dark3, and one unused.
        /// </summary>
        public static bool TryParseRegistryPalette(byte[]? bytes, out AccentShades shades)
        {
            shades = default;
            if (bytes == null || bytes.Length < 28) return false;
            Color At(int i) => Color.FromArgb(255, bytes[4 * i], bytes[4 * i + 1], bytes[4 * i + 2]);
            shades = new AccentShades(At(0), At(1), At(2), At(3), At(4), At(5), At(6));
            return true;
        }

        private static Color Mix(Color a, Color b, double t)
        {
            int M(int x, int y) => SaturatingCast.ToInt(Math.Round(x + (y - x) * t));
            return Color.FromArgb(255, M(a.R, b.R), M(a.G, b.G), M(a.B, b.B));
        }
    }
}
