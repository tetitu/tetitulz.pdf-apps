// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The WinUI 3 look: the theme (light, dark, or Windows' own), the accent, the type ramp and the
    /// icon font, written onto every part of the chrome from one place.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        private PdfViewerTheme _theme = PdfViewerTheme.System;
        private Color _accentColor = Color.Empty;
        private PdfViewerToolbarStyle _toolbarStyle = PdfViewerToolbarStyle.Icons;

        /// <summary>The monitor's dpi over 96, which every chrome measurement is scaled by.</summary>
        private double _chromeScale = 1.0;

        private ViewerToolStripRenderer _renderer = null!;
        /// <summary>Keeps the zoom box's painter alive as long as the box.</summary>
        private FlatComboPainter? _zoomComboPainter;

        // The type ramp, made at the monitor's dpi and remade when it changes; owned and disposed here.
        private Font _fontBody = null!, _fontStrong = null!, _fontCaption = null!;
        private Font? _fontIcon, _fontIconSmall;
        /// <summary>The transparent image a toolbar button holds so the strip lays out room for its icon.</summary>
        private Bitmap? _iconPlaceholder;

        /// <summary>
        /// The colours the chrome is drawn in: WinUI's light or dark theme, or (the default) whichever
        /// Windows' app mode says, followed as the user changes it. Windows' high-contrast setting
        /// overrides every theme.
        /// </summary>
        [Category("Appearance"), DefaultValue(PdfViewerTheme.System),
         Description("Light, dark, or as Windows' app mode says.")]
        public PdfViewerTheme Theme
        {
            get => _theme;
            set
            {
                if (value != PdfViewerTheme.System && value != PdfViewerTheme.Light && value != PdfViewerTheme.Dark)
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown viewer theme.");
                if (_theme == value) return;
                _theme = value;
                ApplyTheme();
            }
        }

        /// <summary>
        /// The accent the chrome uses — checked buttons, the current page, the selection, focus lines.
        /// <see cref="Color.Empty"/> (the default) takes Windows' accent; any other colour is used opaque,
        /// with the lighter and darker shades WinUI needs mixed from it.
        /// </summary>
        [Category("Appearance"), Description("The accent colour; empty for Windows' own.")]
        public Color AccentColor
        {
            get => _accentColor;
            set
            {
                if (_accentColor == value) return;
                _accentColor = value;
                ApplyTheme();
            }
        }

        private bool ShouldSerializeAccentColor() => !_accentColor.IsEmpty;

        private void ResetAccentColor() => AccentColor = Color.Empty;

        /// <summary>
        /// Whether the toolbar's buttons show an icon alone (the default, the name in the tooltip) or an
        /// icon with its name. Without an icon font on the system the buttons show their names whatever
        /// this says.
        /// </summary>
        [Category("Appearance"), DefaultValue(PdfViewerToolbarStyle.Icons),
         Description("Icons alone, or icons with their names.")]
        public PdfViewerToolbarStyle ToolbarStyle
        {
            get => _toolbarStyle;
            set
            {
                if (value != PdfViewerToolbarStyle.Icons && value != PdfViewerToolbarStyle.IconsAndText)
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown toolbar style.");
                if (_toolbarStyle == value) return;
                _toolbarStyle = value;
                ApplyToolbarStyle();
            }
        }

        // ── What the system says ────────────────────────────────────────────────────────────

        /// <summary>For a test: Windows' app mode (true for dark), in place of the registry's.</summary>
        internal bool? SystemDarkOverride;

        /// <summary>For a test: Windows' accent shades, in place of the registry's.</summary>
        internal AccentShades? SystemAccentOverride;

        /// <summary>For a test: the icon font's family in place of the system's; empty for none installed.</summary>
        internal string? IconFamilyOverride;

        private static readonly object SystemLock = new object();
        private static bool? s_systemDark;
        private static AccentShades? s_systemAccent;

        /// <summary>Windows' app mode, read once and again after the user changes a setting.</summary>
        private static bool SystemAppsDark()
        {
            lock (SystemLock)
            {
                if (!s_systemDark.HasValue)
                {
                    bool dark = false;
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                            dark = key?.GetValue("AppsUseLightTheme") is int light && light == 0;
                    }
                    catch (Exception) { /* no registry access: light, as Windows' default */ }
                    s_systemDark = dark;
                }
                return s_systemDark.Value;
            }
        }

        /// <summary>Windows' accent and its shades, read once and again after the user changes a setting.</summary>
        private static AccentShades SystemAccent()
        {
            lock (SystemLock)
            {
                if (!s_systemAccent.HasValue)
                {
                    AccentShades shades = AccentShades.From(ViewerPalette.DefaultAccent);
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                            if (AccentShades.TryParseRegistryPalette(key?.GetValue("AccentPalette") as byte[], out AccentShades read))
                                shades = read;
                    }
                    catch (Exception) { /* no registry access: Windows' default blue */ }
                    s_systemAccent = shades;
                }
                return s_systemAccent.Value;
            }
        }

        private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color
                && e.Category != UserPreferenceCategory.VisualStyle && e.Category != UserPreferenceCategory.Accessibility)
                return;
            lock (SystemLock) { s_systemDark = null; s_systemAccent = null; }
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(new Action(ApplyTheme));
            else ApplyTheme();
        }

        private bool _listeningToSystem;

        private void ListenToSystem(bool on)
        {
            if (on == _listeningToSystem) return;
            _listeningToSystem = on;
            if (on) SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            else SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }

        /// <summary>Whether the dark theme is in force, before high contrast is considered.</summary>
        internal bool IsDarkTheme
        {
            get
            {
                if (_theme == PdfViewerTheme.Dark) return true;
                if (_theme == PdfViewerTheme.Light) return false;
#if !NETFRAMEWORK
                // A host that chose a colour mode for its application has its native controls drawn in
                // it; the viewer follows, so its own chrome and Windows' scroll bars agree.
                if (!SystemDarkOverride.HasValue && Application.ColorMode != SystemColorMode.Classic)
                    return Application.IsDarkModeEnabled;
#endif
                return SystemDarkOverride ?? SystemAppsDark();
            }
        }

        private AccentShades AccentNow => _accentColor.IsEmpty ? SystemAccentOverride ?? SystemAccent() : AccentShades.From(_accentColor);

        /// <summary>For a test: the high-contrast setting the palette reads, in place of the system's.</summary>
        internal bool? HighContrastOverride;

        /// <summary>The colours to draw with now — the system's under high contrast.</summary>
        internal ViewerPalette Palette
            => ViewerPalette.For(HighContrastOverride ?? SystemInformation.HighContrast, IsDarkTheme, AccentNow);

        /// <summary>The palette the chrome was last coloured with.</summary>
        private ViewerPalette _palette = ViewerPalette.For(false);

        /// <summary>The colours the chrome is drawn in now — what a test reads.</summary>
        internal ViewerPalette AppliedPalette => _palette;

        /// <summary>Whether two palettes colour the chrome alike.</summary>
        private static bool SamePalette(ViewerPalette a, ViewerPalette b)
            => a.HighContrast == b.HighContrast && a.Dark == b.Dark && a.Accent == b.Accent && a.Base == b.Base
               && a.Selection == b.Selection;

        /// <summary>
        /// The palette read afresh and, when it differs from the one the chrome holds, written onto the
        /// chrome — the high-contrast switch has no event the control hears, so a paint asks.
        /// </summary>
        private void RefreshPaletteIfChanged()
        {
            ViewerPalette now = Palette;
            if (!SamePalette(now, _palette)) ApplyTheme(now);
            else _palette = now;
        }

        // ── Writing it onto the chrome ──────────────────────────────────────────────────────

        /// <summary>Colours every part of the chrome from the palette in force.</summary>
        private void ApplyTheme() => ApplyTheme(Palette);

        /// <summary>The fonts, icons and colours made again from what the system (or a test's override) says now.</summary>
        internal void ReapplyLook()
        {
            ApplyTypography();
            ApplyTheme();
        }

        private void ApplyTheme(ViewerPalette p)
        {
            _palette = p;
            if (_renderer == null) return;               // still being built
            _renderer.Palette = p;

            BackColor = p.Base;
            ForeColor = p.TextPrimary;
            _toolbar.BackColor = _statusStrip.BackColor = p.Base;
            _toolbar.ForeColor = _statusStrip.ForeColor = p.TextPrimary;
            _pageBox.BackColor = p.ControlFill;
            _pageBox.ForeColor = p.TextPrimary;
            _zoomCombo.BackColor = p.ControlFill;
            _zoomCombo.ForeColor = p.TextPrimary;
            _zoomCombo.ComboBox.Invalidate();

            _split.BackColor = p.Base;
            _split.Panel1.BackColor = p.Base;
            _split.Panel2.BackColor = p.PageArea;
            _sidebarHeader.BackColor = p.Base;
            _sidebarTabs.BackColor = p.Base;
            foreach (TabPage tab in new[] { _tabThumbnails, _tabBookmarks, _tabComments, _tabSignatures })
                tab.BackColor = p.Base;
            _thumbFlow.BackColor = p.Base;
            foreach (TreeView tree in new[] { _bookmarkTree, _commentTree, _signatureTree })
            {
                tree.BackColor = p.Base;
                tree.ForeColor = p.TextPrimary;
                tree.LineColor = p.Divider;
            }
            RecolourSignatureNodes();
            _surface.BackColor = p.PageArea;

            ApplyFindCardColours(p);
            UpdateFloatingControls();

            foreach (Control c in _thumbFlow.Controls) c.Invalidate();
            _toolbar.Invalidate();
            _statusStrip.Invalidate();
            _pageMenu.Invalidate();
            _sidebarHeader.Invalidate();
            _signatureBanner.Invalidate();
            _split.Invalidate();
            _surface.Invalidate();
            foreach (TreeView tree in new[] { _bookmarkTree, _commentTree, _signatureTree }) tree.Invalidate();
        }

        /// <summary>The monitor's dpi read afresh; on a change the fonts and every measurement are remade.</summary>
        private void RefreshChromeScale()
        {
            double scale = MonitorDpi() / ZoomMath.BaseDpi;
            if (!(scale > 0)) scale = 1.0;
            if (_fontBody != null && Math.Abs(scale - _chromeScale) < 1e-9) return;
            _chromeScale = scale;
            ApplyTypography();
        }

        /// <summary>A chrome measurement given at 96 dpi, in pixels at the monitor's.</summary>
        private int Px(double v) => Core.Util.SaturatingCast.ToInt(Math.Round(v * _chromeScale));

        /// <summary>The chrome's scale over 96 dpi — what a test reads.</summary>
        internal double ChromeScale => _chromeScale;

        /// <summary>
        /// The type ramp and icon font made at the monitor's dpi and handed to the chrome. The body text
        /// is the host's font when the host set one (the control's ambient font is not the default),
        /// else WinUI's.
        /// </summary>
        private void ApplyTypography()
        {
            Font? oldBody = _fontBody, oldStrong = _fontStrong, oldCaption = _fontCaption,
                  oldIcon = _fontIcon, oldIconSmall = _fontIconSmall;
            Bitmap? oldPlaceholder = _iconPlaceholder;

            bool hostFont = !Font.Equals(DefaultFont);
            double s = _chromeScale;
            _fontBody = hostFont ? new Font(Font, Font.Style) : ViewerFluent.Make(ViewerFluent.TextFamily, ViewerFluent.BodyPx, s);
            _fontStrong = ViewerFluent.Make(hostFont ? Font.FontFamily.Name : ViewerFluent.TextFamilySemibold,
                ViewerFluent.BodyPx, s, hostFont ? FontStyle.Bold : FontStyle.Regular);
            _fontCaption = ViewerFluent.Make(hostFont ? Font.FontFamily.Name : ViewerFluent.TextFamily, ViewerFluent.CaptionPx, s);
            string? icons = IconFamilyOverride == null ? ViewerFluent.IconFamily
                          : IconFamilyOverride.Length == 0 ? null : IconFamilyOverride;
            _fontIcon = icons == null ? null : ViewerFluent.Make(icons, ViewerFluent.IconPx, s);
            _fontIconSmall = icons == null ? null : ViewerFluent.Make(icons, ViewerFluent.SmallIconPx, s);
            int side = Px(ViewerFluent.IconPx);
            _iconPlaceholder = new Bitmap(side, side);

            if (_renderer != null)
            {
                _renderer.IconFont = _fontIcon;
                _renderer.Scale = s;
                ApplyChromeFonts();
                ApplyToolbarStyle();
                LayoutSidebarHeader();
                LayoutFindBar();
                UpdateFloatingControls();
            }

            // Not disposed yet: a control keeps its own reference to a font it was given (a tree, a hosted
            // text box) past the assignment that replaced it, and a disposed font there throws on its next
            // measure. They are few — fonts are remade only when the dpi or the host's font changes — and
            // go with the control.
            foreach (IDisposable? old in new IDisposable?[] { oldBody, oldStrong, oldCaption, oldIcon, oldIconSmall, oldPlaceholder })
                if (old != null) _retiredLook.Add(old);
        }

        /// <summary>Fonts and images replaced by a newer set, disposed with the control.</summary>
        private readonly List<IDisposable> _retiredLook = new List<IDisposable>();

        private void ApplyChromeFonts()
        {
            _toolbar.Font = _fontBody;
            _statusStrip.Font = _fontCaption;
            _pageMenu.Font = _fontBody;
            _pageBox.Font = _fontBody;
            _zoomCombo.Font = _fontBody;
            foreach (TreeView tree in new[] { _bookmarkTree, _commentTree, _signatureTree })
            {
                tree.Font = _fontBody;
                tree.ItemHeight = Math.Max(_fontBody.Height + Px(4), Px(32));
                tree.Indent = Px(16);
            }
            _signatureBanner.Font = _fontBody;
            _signatureBanner.Height = Px(48);
            _findBox.Font = _fontBody;
            _findResult.Font = _fontCaption;
            foreach (Button b in new[] { _btnFindPrev, _btnFindNext, _btnFindClose })
                b.Font = _fontIconSmall ?? _fontBody;
            ApplyFindButtonGlyphs();
            foreach (Control c in _thumbFlow.Controls) c.Invalidate();

            _toolbar.Padding = new Padding(Px(4), Px(4), Px(4), Px(4));
            _statusStrip.Padding = new Padding(Px(8), Px(1), Px(8), 0);
            _statusStrip.MinimumSize = new Size(0, Px(26));
            _pageBox.Width = Px(40);
            _pageBox.Margin = new Padding(Px(10), Px(2), Px(6), Px(2));
            _zoomCombo.Width = Px(84);
            _zoomCombo.Margin = new Padding(Px(4), Px(2), Px(4), Px(2));
            _split.SplitterWidth = Math.Max(1, Px(4));
        }

        // ── The toolbar's icons ─────────────────────────────────────────────────────────────

        /// <summary>The icon an item draws, as the direction and the layout stand; null for none.</summary>
        internal string? IconOf(ToolStripItem item)
        {
            bool rtl = RightToLeft == RightToLeft.Yes;
            if (item == _btnOpen) return ViewerIcons.OpenFile;
            if (item == _btnSave) return ViewerIcons.Save;
            if (item == _btnBack) return rtl ? ViewerIcons.Forward : ViewerIcons.Back;
            if (item == _btnForward) return rtl ? ViewerIcons.Back : ViewerIcons.Forward;
            if (item == _btnPrev) return rtl ? ViewerIcons.ChevronRight : ViewerIcons.ChevronLeft;
            if (item == _btnNext) return rtl ? ViewerIcons.ChevronLeft : ViewerIcons.ChevronRight;
            if (item == _btnZoomOut) return ViewerIcons.ZoomOut;
            if (item == _btnZoomIn) return ViewerIcons.ZoomIn;
            if (item == _btnFitWidth) return ViewerIcons.FitWidth;
            if (item == _btnFitPage) return ViewerIcons.FitPage;
            if (item == _btnLayout) return _layoutMode == PdfViewLayoutMode.Continuous ? ViewerIcons.Continuous : ViewerIcons.Page;
            if (item == _btnSidebar) return rtl ? ViewerIcons.OpenPaneMirrored : ViewerIcons.OpenPane;
            if (item == _btnFind) return ViewerIcons.Search;
            if (item == _btnPrint) return ViewerIcons.Print;
            return null;
        }

        /// <summary>The buttons that are a glyph alone even with names shown: their names are long, their meaning plain.</summary>
        private bool IsGlyphOnlyButton(ToolStripItem item)
            => item == _btnBack || item == _btnForward || item == _btnPrev || item == _btnNext
               || item == _btnZoomOut || item == _btnZoomIn;

        /// <summary>Whether the toolbar draws icons — false when Windows has no icon font.</summary>
        internal bool ToolbarShowsIcons => _fontIcon != null;

        /// <summary>
        /// Each button as <see cref="ToolbarStyle"/> says: an icon alone in a 40 × 36 square, as a WinUI
        /// <c>AppBarButton</c>, or an icon with its name; with no icon font, its name or arrow as text.
        /// </summary>
        private void ApplyToolbarStyle()
        {
            if (_renderer == null) return;
            _toolbar.SuspendLayout();
            foreach (ToolStripItem item in _toolbar.Items)
            {
                if (!(item is ToolStripButton button)) continue;
                if (_fontIcon == null || IconOf(item) == null)
                {
                    button.Image = null;
                    button.DisplayStyle = ToolStripItemDisplayStyle.Text;
                    button.AutoSize = true;
                    button.Padding = new Padding(Px(6), 0, Px(6), 0);
                    continue;
                }
                button.Image = _iconPlaceholder;
                button.ImageScaling = ToolStripItemImageScaling.None;
                bool iconOnly = _toolbarStyle == PdfViewerToolbarStyle.Icons || IsGlyphOnlyButton(item);
                if (iconOnly)
                {
                    button.DisplayStyle = ToolStripItemDisplayStyle.Image;
                    button.AutoSize = false;
                    button.Size = new Size(Px(40), Px(36));
                    button.Padding = Padding.Empty;
                }
                else
                {
                    button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                    button.TextImageRelation = TextImageRelation.ImageBeforeText;
                    button.AutoSize = true;
                    button.Padding = new Padding(Px(8), Px(8), Px(10), Px(8));
                }
                button.Margin = new Padding(Px(1), 0, Px(1), 0);
            }
            _toolbar.ResumeLayout();
            _toolbar.Invalidate();
        }

        /// <summary>
        /// Paints over a flat combo box's own frame and drop-down button after Windows paints them, in
        /// the theme's colours: WinForms draws both in fixed system colours (a white frame, a light
        /// button) whatever the box's <c>BackColor</c>, which a dark bar shows. A managed
        /// <see cref="NativeWindow"/> on the box's own messages, no platform call.
        /// </summary>
        private sealed class FlatComboPainter : NativeWindow
        {
            private const int WmPaint = 0x000F;
            private readonly PdfViewerControl _owner;
            private readonly ComboBox _box;

            public FlatComboPainter(PdfViewerControl owner, ComboBox box)
            {
                _owner = owner;
                _box = box;
                box.HandleCreated += (s, e) => AssignHandle(box.Handle);
                box.HandleDestroyed += (s, e) => ReleaseHandle();
                if (box.IsHandleCreated) AssignHandle(box.Handle);
            }

            /// <summary>A copy of the box drawn into a device context (<c>DrawToBitmap</c>, a print), painted over the same way.</summary>
            private const int WmPrint = 0x0317;

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if ((m.Msg != WmPaint && m.Msg != WmPrint) || _box.IsDisposed) return;
                try
                {
                    using (Graphics g = m.Msg == WmPrint ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(_box.Handle)) Paint(g);
                }
                catch (Exception) { /* a box going away mid-paint: nothing to draw on */ }
            }

            /// <summary>The frame filled with the field's colour, the button area with it too and a chevron on it.</summary>
            internal void Paint(Graphics g)
            {
                ViewerPalette p = _owner._palette;
                Rectangle r = _box.ClientRectangle;
                if (r.Width <= 4 || r.Height <= 4) return;
                Color fill = _box.ContainsFocus ? p.ControlFillActive : p.ControlFill;
                using (var pen = new Pen(fill, 2)) g.DrawRectangle(pen, 1, 1, r.Width - 2, r.Height - 2);
                int button = _owner.Px(20);
                bool rtl = _box.RightToLeft == RightToLeft.Yes;
                var box = new Rectangle(rtl ? 1 : r.Width - button - 1, 1, button, r.Height - 2);
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, box);
                Font? chevron = _owner._fontIconSmall;
                if (chevron != null) ViewerFluent.DrawIcon(g, ViewerIcons.ChevronDown, chevron, box, p.TextSecondary);
                else TextRenderer.DrawText(g, "▾", _box.Font, box, p.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        private void DisposeThemeResources()
        {
            ListenToSystem(false);
            _fontBody?.Dispose();
            _fontStrong?.Dispose();
            _fontCaption?.Dispose();
            _fontIcon?.Dispose();
            _fontIconSmall?.Dispose();
            _iconPlaceholder?.Dispose();
            foreach (IDisposable old in _retiredLook) old.Dispose();
            _retiredLook.Clear();
        }
    }
}
