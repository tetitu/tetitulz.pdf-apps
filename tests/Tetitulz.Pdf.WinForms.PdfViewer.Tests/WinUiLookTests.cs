// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The WinUI 3 look: WinUI's own colour values in both themes and the system's under high contrast,
/// text that reads on what it is drawn on, the theme following Windows' app mode unless the host
/// chose one, the toolbar's icons (named for a screen reader, turned with the direction, words when
/// Windows has no icon font), the pane's tab row, the ProgressRing, the page's shadow, the floating
/// controls, and no colour written anywhere but the palette.
/// </summary>
public sealed class WinUiLookTests
{
    // ── The palette ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The tokens are WinUI's values (microsoft-ui-xaml, Common_themeresources_any.xaml), a
    /// translucent one blended over its layer: LayerFillColorDefault #80FFFFFF over #F3F3F3 is
    /// #F9F9F9; #4C3A3A3A over #202020 is #282828; TextFillColorPrimary #E4000000 over #F3F3F3 is
    /// #1A1A1A.
    /// </summary>
    [Fact]
    public void ThePalette_HoldsWinUisValues_InBothThemes()
    {
        AccentShades blue = AccentShades.From(ViewerPalette.DefaultAccent);
        var light = ViewerPalette.For(false, false, blue);
        var dark = ViewerPalette.For(false, true, blue);

        Assert.Equal(0xF3F3F3, Rgb(light.Base));
        Assert.Equal(0xF9F9F9, Rgb(light.PageArea));
        Assert.Equal(0xF9F9F9, Rgb(light.Card));
        Assert.Equal(0x1A1A1A, Rgb(light.TextPrimary));
        Assert.Equal(0x0F7B0F, Rgb(light.Success));
        Assert.Equal(0xC42B1C, Rgb(light.Critical));
        Assert.Equal(Rgb(blue.Dark1), Rgb(light.Accent));                 // AccentFillColorDefault: Dark1 in light
        Assert.False(light.Dark);

        Assert.Equal(0x202020, Rgb(dark.Base));
        Assert.Equal(0x282828, Rgb(dark.PageArea));
        Assert.Equal(0x2C2C2C, Rgb(dark.Card));
        Assert.Equal(0xFFFFFF, Rgb(dark.TextPrimary));
        Assert.Equal(0x6CCB5F, Rgb(dark.Success));
        Assert.Equal(0xFF99A4, Rgb(dark.Critical));
        Assert.Equal(Rgb(blue.Light2), Rgb(dark.Accent));                 // and Light2 in dark
        Assert.True(dark.Dark);
    }

    /// <summary>
    /// Text meets WCAG's 4.5:1 on what it is drawn on, and an icon, a stroke that must read as a
    /// control and the focus line 3:1 — in both themes, for Windows' default accent and for accents a
    /// host may choose (one light enough that white on it would not read).
    /// </summary>
    [Theory]
    [InlineData(false, 0x0078D4)]
    [InlineData(true, 0x0078D4)]
    [InlineData(false, 0xFFD700)]
    [InlineData(true, 0x000080)]
    [InlineData(false, 0x107C10)]
    public void EveryText_ReadsOnItsBackground(bool dark, int accent)
    {
        var p = ViewerPalette.For(false, dark, AccentShades.From(Color.FromArgb(accent)));
        AssertReads(p.TextPrimary, p.Base, 4.5, "TextPrimary on Base");
        AssertReads(p.TextSecondary, p.Base, 4.5, "TextSecondary on Base");
        AssertReads(p.CardText, p.Card, 4.5, "CardText on Card");
        AssertReads(p.CardTextSecondary, p.Card, 4.5, "CardTextSecondary on Card");
        AssertReads(p.TextPrimary, p.SubtleHover, 4.5, "TextPrimary on a hovered row");
        AssertReads(p.TextPrimary, p.ControlFill, 4.5, "TextPrimary in a text box");
        AssertReads(p.TextOnAccent, p.Accent, 4.5, "TextOnAccent on Accent");
        AssertReads(p.TextPrimary, p.SuccessBackground, 4.5, "banner text, valid");
        AssertReads(p.TextPrimary, p.CautionBackground, 4.5, "banner text, unknown");
        AssertReads(p.TextPrimary, p.CriticalBackground, 4.5, "banner text, invalid");
        AssertReads(p.ControlStrongStroke, p.Base, 3, "ControlStrongStroke on Base");
        AssertReads(p.FocusOuter, p.Base, 3, "FocusOuter on Base");
        AssertReads(p.VerdictText(SignatureVerdict.Valid), p.SuccessBackground, 3, "the valid banner's icon");
        AssertReads(p.VerdictText(SignatureVerdict.Invalid), p.CriticalBackground, 3, "the invalid banner's icon");
        AssertReads(p.VerdictText(SignatureVerdict.Valid), p.Base, 3, "a valid signature's row");
        AssertReads(p.VerdictText(SignatureVerdict.Invalid), p.Base, 3, "an invalid signature's row");
    }

    [Fact]
    public void UnderHighContrast_EveryChromeToken_IsTheSystemsColour()
    {
        var hc = ViewerPalette.For(true, true, AccentShades.From(Color.Red));   // the theme and accent are overridden
        Assert.True(hc.HighContrast);
        Assert.False(hc.Dark);
        Assert.Equal(SystemColors.Window.ToArgb(), hc.Base.ToArgb());
        Assert.Equal(SystemColors.WindowText.ToArgb(), hc.TextPrimary.ToArgb());
        Assert.Equal(SystemColors.Highlight.ToArgb(), hc.Accent.ToArgb());
        Assert.Equal(SystemColors.HighlightText.ToArgb(), hc.TextOnAccent.ToArgb());
        Assert.Equal(SystemColors.WindowText.ToArgb(), hc.Divider.ToArgb());

        var standard = ViewerPalette.For(false, true, AccentShades.From(Color.Red));   // the control
        Assert.NotEqual(SystemColors.Window.ToArgb(), standard.Base.ToArgb());
    }

    /// <summary>Windows' accent palette is eight RGBA entries: Light3, Light2, Light1, the accent, Dark1, Dark2, Dark3, unused.</summary>
    [Fact]
    public void WindowsAccentPalette_IsReadInItsOrder()
    {
        byte[] bytes =
        {
            0xBA, 0xE6, 0xF2, 0, 0x97, 0xC5, 0xE1, 0, 0x58, 0x8C, 0xC4, 0, 0x41, 0x76, 0xB7, 0,
            0x33, 0x5D, 0x9C, 0, 0x21, 0x3D, 0x79, 0, 0x0C, 0x18, 0x51, 0, 0x88, 0x17, 0x98, 0,
        };
        Assert.True(AccentShades.TryParseRegistryPalette(bytes, out AccentShades s));
        Assert.Equal(0xBAE6F2, Rgb(s.Light3));
        Assert.Equal(0x97C5E1, Rgb(s.Light2));
        Assert.Equal(0x588CC4, Rgb(s.Light1));
        Assert.Equal(0x4176B7, Rgb(s.Accent));
        Assert.Equal(0x335D9C, Rgb(s.Dark1));
        Assert.Equal(0x213D79, Rgb(s.Dark2));
        Assert.Equal(0x0C1851, Rgb(s.Dark3));

        Assert.False(AccentShades.TryParseRegistryPalette(new byte[27], out _));     // too short: refused
        Assert.False(AccentShades.TryParseRegistryPalette(null, out _));
    }

    // ── The theme on the control ──────────────────────────────────────────────────────────

    [Fact]
    public void TheSystemTheme_FollowsWindowsAppMode_AndAThemeTheHostChoseWins()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { SystemDarkOverride = true, HighContrastOverride = false };
            viewer.ReapplyLook();
            Assert.True(viewer.AppliedPalette.Dark);
            Assert.Equal(0x202020, Rgb(viewer.Toolbar.BackColor));
            Assert.Equal(0x202020, Rgb(viewer.StatusBar.BackColor));
            Assert.Equal(0x282828, Rgb(viewer.PageSurface.BackColor));
            Assert.Equal(0x202020, Rgb(viewer.BookmarkTree.BackColor));

            viewer.Theme = PdfViewerTheme.Light;                               // the host's choice wins
            Assert.False(viewer.AppliedPalette.Dark);
            Assert.Equal(0xF3F3F3, Rgb(viewer.Toolbar.BackColor));
            Assert.Equal(0xF9F9F9, Rgb(viewer.PageSurface.BackColor));

            viewer.Theme = PdfViewerTheme.System;                              // back to Windows' (dark)
            Assert.True(viewer.AppliedPalette.Dark);
            viewer.SystemDarkOverride = false;                                 // the user switches Windows to light
            viewer.ReapplyLook();
            Assert.Equal(0xF3F3F3, Rgb(viewer.Toolbar.BackColor));

            viewer.Theme = PdfViewerTheme.Dark;
            Assert.Equal(0x202020, Rgb(viewer.Toolbar.BackColor));
        });
    }

    [Fact]
    public void AHostsAccent_ColoursTheAccentAndTheSelection_AndEmptyGoesBackToWindows()
    {
        RunSta(() =>
        {
            AccentShades windows = AccentShades.From(Color.FromArgb(0x10, 0x7C, 0x10));
            using var viewer = new PdfViewerControl
            {
                SystemDarkOverride = false, SystemAccentOverride = windows, HighContrastOverride = false,
            };
            viewer.ReapplyLook();
            Assert.Equal(Rgb(windows.Dark1), Rgb(viewer.AppliedPalette.Accent));

            viewer.AccentColor = Color.FromArgb(0xC0, 0x10, 0x60);
            AccentShades host = AccentShades.From(Color.FromArgb(0xC0, 0x10, 0x60));
            Assert.Equal(Rgb(host.Dark1), Rgb(viewer.AppliedPalette.Accent));
            Assert.Equal(0xC01060, Rgb(viewer.AppliedPalette.Selection));
            Assert.True(viewer.AppliedPalette.Selection.A is > 0 and < 255);   // over the page, not covering it

            viewer.AccentColor = Color.Empty;
            Assert.Equal(Rgb(windows.Dark1), Rgb(viewer.AppliedPalette.Accent));
        });
    }

    [Fact]
    public void ThemeAndToolbarStyle_RefuseAValueThatNamesNothing()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            Assert.Throws<ArgumentOutOfRangeException>(() => viewer.Theme = (PdfViewerTheme)7);
            Assert.Throws<ArgumentOutOfRangeException>(() => viewer.ToolbarStyle = (PdfViewerToolbarStyle)5);
            viewer.Theme = PdfViewerTheme.Dark;                                // the control: a real value is taken
            viewer.ToolbarStyle = PdfViewerToolbarStyle.IconsAndText;
            Assert.Equal(PdfViewerTheme.Dark, viewer.Theme);
            Assert.Equal(PdfViewerToolbarStyle.IconsAndText, viewer.ToolbarStyle);
        });
    }

    // ── The toolbar ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An icon-only button is named for what its icon stands for, in the current strings — the word
    /// the icon replaced. A font every Windows has stands in for the icon font, so the test does not
    /// depend on which icon font the machine carries.
    /// </summary>
    [Fact]
    public void IconOnlyButtons_AreNamedInTheCurrentStrings()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { IconFamilyOverride = "Segoe UI" };
            viewer.ReapplyLook();
            Assert.Equal(ToolStripItemDisplayStyle.Image, viewer.FindButton.DisplayStyle);
            Assert.Equal("Find", viewer.FindButton.AccessibleName);
            Assert.Equal("Print", viewer.PrintButton.AccessibleName);
            Assert.Equal(viewer.Strings.Open, viewer.OpenButton.AccessibleName);
            Assert.Equal(viewer.Strings.Sidebar, viewer.SidebarButton.AccessibleName);

            viewer.Strings = PdfViewerStrings.Hebrew();
            Assert.Equal(PdfViewerStrings.Hebrew().Find, viewer.FindButton.AccessibleName);
            Assert.Equal(PdfViewerStrings.Hebrew().Print, viewer.PrintButton.AccessibleName);
            Assert.NotEqual("Find", viewer.FindButton.AccessibleName);          // potency: the language changed
        });
    }

    [Fact]
    public void WithNoIconFont_TheButtonsShowTheirWordsAndArrows()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { IconFamilyOverride = "" };
            viewer.ReapplyLook();
            Assert.False(viewer.ToolbarShowsIcons);
            Assert.Equal(ToolStripItemDisplayStyle.Text, viewer.FindButton.DisplayStyle);
            Assert.Null(viewer.FindButton.Image);
            Assert.Equal(new[] { "▲", "▼", "✕" }, viewer.FindBarButtons.Select(b => b.Text).ToArray());

            viewer.IconFamilyOverride = "Segoe UI";                             // the control: a font, icons
            viewer.ReapplyLook();
            Assert.True(viewer.ToolbarShowsIcons);
            Assert.Equal(ToolStripItemDisplayStyle.Image, viewer.FindButton.DisplayStyle);
            Assert.Equal(new[] { ViewerIcons.ChevronUp, ViewerIcons.ChevronDown, ViewerIcons.Cancel },
                viewer.FindBarButtons.Select(b => b.Text).ToArray());
        });
    }

    [Fact]
    public void IconsAndText_ShowsTheNames_ButKeepsTheArrowsBare()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { IconFamilyOverride = "Segoe UI" };
            viewer.ReapplyLook();
            int iconOnly = viewer.FindButton.Width;
            viewer.ToolbarStyle = PdfViewerToolbarStyle.IconsAndText;
            Assert.Equal(ToolStripItemDisplayStyle.ImageAndText, viewer.FindButton.DisplayStyle);
            Assert.Equal(ToolStripItemDisplayStyle.Image, viewer.PreviousPageButton.DisplayStyle);
            Assert.Equal(ToolStripItemDisplayStyle.Image, viewer.ZoomInButton.DisplayStyle);

            viewer.ToolbarStyle = PdfViewerToolbarStyle.Icons;
            Assert.Equal(ToolStripItemDisplayStyle.Image, viewer.FindButton.DisplayStyle);
            Assert.Equal(iconOnly, viewer.FindButton.Width);
            Assert.Equal(40, iconOnly);                                         // an AppBarButton's width at 96 dpi
        });
    }

    [Fact]
    public void PointingIcons_TurnWithTheDirection()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            Assert.Equal(ViewerIcons.ChevronLeft, viewer.IconOf(viewer.PreviousPageButton));
            Assert.Equal(ViewerIcons.ChevronRight, viewer.IconOf(viewer.NextPageButton));
            Assert.Equal(ViewerIcons.OpenPane, viewer.IconOf(viewer.SidebarButton));
            Assert.Equal(ViewerIcons.Search, viewer.IconOf(viewer.FindButton));

            viewer.RightToLeft = RightToLeft.Yes;
            Assert.Equal(ViewerIcons.ChevronRight, viewer.IconOf(viewer.PreviousPageButton));
            Assert.Equal(ViewerIcons.ChevronLeft, viewer.IconOf(viewer.NextPageButton));
            Assert.Equal(ViewerIcons.OpenPaneMirrored, viewer.IconOf(viewer.SidebarButton));
            Assert.Equal(ViewerIcons.Search, viewer.IconOf(viewer.FindButton));    // a search does not point
        });
    }

    /// <summary>
    /// The icons are drawn: the Find and Print buttons each hold dark pixels in the light theme, not
    /// the same ones, and the gap beside them holds none.
    /// </summary>
    [DesktopFact]
    public void TheToolbarsIcons_AreDrawnIntoTheirButtons()
    {
        RunSta(() =>
        {
            using var form = OffscreenForm();
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Theme = PdfViewerTheme.Light, HighContrastOverride = false };
            form.Controls.Add(viewer);
            form.Show();
            viewer.LoadBytes(TestPdf.OnePage());                                // Find and Print enabled: drawn in the text colour
            Assert.True(viewer.ToolbarShowsIcons, "this machine has neither Segoe Fluent Icons nor Segoe MDL2 Assets");

            using var bmp = new Bitmap(viewer.Width, viewer.Height);
            viewer.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            Point bar = viewer.Toolbar.Location;
            Rectangle find = viewer.FindButton.Bounds, print = viewer.PrintButton.Bounds;
            find.Offset(bar);
            print.Offset(bar);
            HashSet<Point> findInk = Ink(bmp, find), printInk = Ink(bmp, print);
            Assert.True(findInk.Count >= 12, $"the find icon left {findInk.Count} dark pixels in {find}, bitmap {bmp.Size}, style {viewer.FindButton.DisplayStyle}");
            Assert.True(printInk.Count >= 12, $"the print icon left {printInk.Count} dark pixels");
            Assert.False(findInk.SetEquals(printInk), "two icons drew the same pixels");

            var gap = new Rectangle(print.Right + 4, find.Y, Math.Max(1, Math.Min(20, bmp.Width - print.Right - 6)), find.Height);
            Assert.Empty(Ink(bmp, gap));                                         // potency: the background is not ink
        });

        static HashSet<Point> Ink(Bitmap bmp, Rectangle r)
        {
            var set = new HashSet<Point>();
            for (int y = Math.Max(0, r.Top); y < Math.Min(bmp.Height, r.Bottom); y++)
                for (int x = Math.Max(0, r.Left); x < Math.Min(bmp.Width, r.Right); x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R + c.G + c.B < 3 * 110) set.Add(new Point(x - r.Left, y - r.Top));
                }
            return set;
        }
    }

    // ── The pane ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThePanesTabRow_HasAButtonPerTab_NamedForIt()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            Assert.Equal(new[] { "Pages", "Bookmarks" }, viewer.SidebarTabButtons.Select(b => b.AccessibleName).ToArray());

            viewer.Features |= PdfViewerFeatures.Comments;
            Assert.Equal(new[] { "Pages", "Bookmarks", "Comments" }, viewer.SidebarTabButtons.Select(b => b.AccessibleName).ToArray());

            viewer.Features &= ~(PdfViewerFeatures.Comments | PdfViewerFeatures.Thumbnails);
            Assert.Equal(new[] { "Bookmarks" }, viewer.SidebarTabButtons.Select(b => b.AccessibleName).ToArray());

            viewer.Strings = PdfViewerStrings.Hebrew();
            Assert.Equal(new[] { "סימניות" }, viewer.SidebarTabButtons.Select(b => b.AccessibleName).ToArray());
            Assert.All(viewer.SidebarTabButtons, b => Assert.Equal(AccessibleRole.PageTab, b.AccessibleRole));
        });
    }

    [Fact]
    public void ThePanesTabRow_ChoosesATab_ByClickAndByArrow_TheArrowsMirroredInRtl()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = PdfViewerFeatures.Default | PdfViewerFeatures.Comments };

            viewer.ClickSidebarTab(2);
            Assert.Equal(2, viewer.SidebarTabs.SelectedIndex);
            viewer.PressSidebarTabKey(2, Keys.Left);
            Assert.Equal(1, viewer.SidebarTabs.SelectedIndex);
            viewer.PressSidebarTabKey(1, Keys.Right);
            Assert.Equal(2, viewer.SidebarTabs.SelectedIndex);
            viewer.PressSidebarTabKey(2, Keys.Right);                           // the last stays the last
            Assert.Equal(2, viewer.SidebarTabs.SelectedIndex);

            viewer.RightToLeft = RightToLeft.Yes;                               // mirrored: Left is onward
            viewer.ClickSidebarTab(0);
            viewer.PressSidebarTabKey(0, Keys.Left);
            Assert.Equal(1, viewer.SidebarTabs.SelectedIndex);
            viewer.PressSidebarTabKey(1, Keys.Right);
            Assert.Equal(0, viewer.SidebarTabs.SelectedIndex);
        });
    }

    [Fact]
    public void ThePanesTabRow_LaysItsButtonsFromTheLeadingEdge()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Width = 900, Height = 600 };
            viewer.PerformLayout();
            var row = viewer.SidebarHeader;
            Control first = viewer.SidebarTabButtons[0], second = viewer.SidebarTabButtons[1];
            Assert.Equal(8, first.Left);
            Assert.True(first.Right < second.Left);

            viewer.RightToLeft = RightToLeft.Yes;
            viewer.PerformLayout();
            Assert.Equal(row.Width - 8, first.Right);
            Assert.True(second.Right < first.Left);
        });
    }

    // ── The page area ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A page still rendering shows a ring, which turns (its timer runs) only while Windows' animation
    /// effects are on; a page drawn where the view paints it needs none.
    /// </summary>
    [DesktopFact]
    public void APageStillRendering_ShowsARing_ThatTurnsOnlyWithAnimations()
    {
        RunSta(() =>
        {
            using var form = OffscreenForm();
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, AnimationsOverride = true };
            form.Controls.Add(viewer);
            form.Show();
            viewer.HoldBackgroundRenders();
            viewer.LoadBytes(TestPdf.OnePage());
            Paint(viewer);
            Assert.Equal(1, viewer.ProgressRingPageCount);
            Assert.True(viewer.ProgressRingRunning);

            viewer.AnimationsOverride = false;
            Paint(viewer);
            Assert.Equal(1, viewer.ProgressRingPageCount);                       // drawn, still
            Assert.False(viewer.ProgressRingRunning);

            viewer.AnimationsOverride = true;
            viewer.RenderPagesInBackground = false;                              // the control: rendered as painted
            Paint(viewer);
            Assert.Equal(0, viewer.ProgressRingPageCount);
            Assert.False(viewer.ProgressRingRunning);
        });
    }

    /// <summary>A page casts a shadow on the layer round it, and its own edge stays the page's.</summary>
    [DesktopFact]
    public void APage_CastsAShadow_AndKeepsItsOwnEdge()
    {
        RunSta(() =>
        {
            using var form = OffscreenForm();
            var viewer = new PdfViewerControl
            {
                Dock = DockStyle.Fill, Theme = PdfViewerTheme.Light, HighContrastOverride = false, RenderPagesInBackground = false,
            };
            form.Controls.Add(viewer);
            form.Show();
            viewer.LoadBytes(TestPdf.OnePage());
            Assert.True(viewer.TryPageToClient(0, new RectangleF(0, 0, 200, 200), out RectangleF page));
            using var bmp = new Bitmap(viewer.Width, viewer.Height);
            viewer.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));

            int midY = (int)((page.Top + page.Bottom) / 2);
            Color area = bmp.GetPixel((int)page.Left - 30, midY);
            Color shadow = bmp.GetPixel((int)Math.Round(page.Left) - 1, midY);   // the ring touching the page: no seam
            Color edge = bmp.GetPixel((int)Math.Round(page.Left) + 1, midY);
            Assert.Equal(0xF9F9F9, Rgb(area));                                   // the layer, clear of the shadow
            Assert.True(Luma(shadow) < Luma(area) - 4, $"no shadow beside the page {page}: {shadow} against {area}");
            Assert.Equal(0xFFFFFF, Rgb(edge));                                   // the page's own edge is paper
        });
    }

    // ── The floating controls ────────────────────────────────────────────────────────────

    [Fact]
    public void FloatingControls_AreOffByDefault_AndRunTheirCommandsThroughCommandExecuting()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Width = 800, Height = 600 };
            viewer.LoadBytes(TestPdf.ManyPages(3));
            Assert.Null(viewer.FloatingControls);
            viewer.NudgeFloatingControls();
            Assert.Null(viewer.FloatingControls);                                // off: nothing made

            var commands = new List<PdfViewerCommand>();
            bool handle = false;
            viewer.CommandExecuting += (s, e) => { commands.Add(e.Command); e.Handled = handle; };
            viewer.ShowFloatingControls = true;
            viewer.NudgeFloatingControls();
            Assert.True(viewer.FloatingControls!.Visible);
            Assert.Equal("1 / 3", viewer.FloatingPageText);
            Assert.Equal(new[] { "Previous page (PageUp)", "Next page (PageDown)" },
                viewer.FloatingButtons.Take(2).Select(b => b.AccessibleName).ToArray());

            viewer.ClickFloatingButton(1);                                       // next
            Assert.Equal(1, viewer.CurrentPage);
            Assert.Equal("2 / 3", viewer.FloatingPageText);
            Assert.Equal(PdfViewerCommand.NextPage, commands.Last());

            handle = true;                                                       // the host takes it over
            viewer.ClickFloatingButton(1);
            Assert.Equal(1, viewer.CurrentPage);
            Assert.Equal(2, commands.Count);

            viewer.ShowFloatingControls = false;
            Assert.False(viewer.FloatingControls.Visible);
        });
    }

    // ── One place for colour ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every colour the viewer writes comes from <c>ViewerPalette.cs</c>: a literal elsewhere is a
    /// colour no theme and no high-contrast setting reaches. The one exception is a bookmark's own
    /// colour, which the document chose.
    /// </summary>
    [Fact]
    public void NoColour_IsWrittenOutsideThePalette()
    {
        string dir = ViewerSourceDirectory();
        var literal = new Regex(@"\b(Color\.(FromArgb|[A-Z][a-z]+[A-Za-z]*)\b|SystemColors\.|Brushes\.|Pens\.)",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var found = new List<string>();
        int inPalette = 0;
        foreach (string file in Directory.GetFiles(dir, "*.cs"))
        {
            string name = Path.GetFileName(file);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.Length > 2000) continue;
                foreach (Match m in literal.Matches(line))
                {
                    if (name == "ViewerPalette.cs") { inPalette++; continue; }
                    if (m.Value == "Color.Empty") continue;                          // "no colour", not a colour
                    if (m.Value == "Color.FromArgb" && line.Contains("Color.FromArgb(Channel(", StringComparison.Ordinal)) continue;
                    found.Add($"{name}:{i + 1}: {line}");
                }
            }
        }
        Assert.True(inPalette > 40, $"the scan found only {inPalette} colours in ViewerPalette.cs — the wrong folder?");
        Assert.Empty(found);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private static void AssertReads(Color fore, Color back, double floor, string what)
    {
        double ratio = ViewerPalette.ContrastRatio(fore, back);
        Assert.True(ratio >= floor, $"{what}: {ratio:0.00}:1 is under {floor}:1 ({fore} on {back})");
    }

    private static int Rgb(Color c) => c.ToArgb() & 0xFFFFFF;

    private static int Luma(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000;

    private static void Paint(PdfViewerControl viewer)
    {
        using var bmp = new Bitmap(viewer.Width, viewer.Height);
        viewer.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
    }

    private static Form OffscreenForm()
        => new Form { ClientSize = new Size(800, 600), StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };

    private static string ViewerSourceDirectory()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "src", "Tetitulz.Pdf.WinForms.PdfViewer");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("The viewer's source folder is not above " + AppContext.BaseDirectory);
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
