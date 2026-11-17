// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>The page's right-click menu, and the colours the viewer draws with under high contrast.</summary>
public sealed class PageMenuAndPaletteTests
{
    [Fact]
    public void ThePageMenu_OffersCopySelectAllTheFitsTheSpreadsAndTheTurns_InTheCurrentStrings()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Size = new Size(900, 700) };
            viewer.LoadBytes(TestPdf.TwoLinesOfText());
            Assert.Same(viewer.PageMenu, viewer.PageSurface.ContextMenuStrip);

            var texts = MenuTexts(viewer);
            Assert.Equal(new[] { "Copy", "Select All", "Fit Width", "Fit Page", "One Page", "Two Pages", "Two Pages, Cover Alone", "Rotate Clockwise", "Rotate Counterclockwise" }, texts);

            viewer.Strings = PdfViewerStrings.Hebrew();
            Assert.Equal(new[] { "העתקה", "בחירת הכול", "רוחב", "עמוד", "עמוד אחד", "שני עמודים", "שני עמודים, עמוד שער לבד", "סיבוב עם כיוון השעון", "סיבוב נגד כיוון השעון" }, MenuTexts(viewer));
        });
    }

    [Fact]
    public void ThePageMenu_Acts_ThroughCommandExecuting()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Size = new Size(900, 700) };
            viewer.LoadBytes(TestPdf.TwoLinesOfText());
            var seen = new System.Collections.Generic.List<PdfViewerCommand>();
            viewer.CommandExecuting += (s, e) => seen.Add(e.Command);

            viewer.UpdatePageMenu();
            Assert.False(Item(viewer, "Copy").Enabled);                  // nothing selected yet

            Item(viewer, "Select All").PerformClick();
            Assert.Equal("First line here\nSecond line here", viewer.SelectedText);
            viewer.UpdatePageMenu();
            Assert.True(Item(viewer, "Copy").Enabled);

            Item(viewer, "Fit Width").PerformClick();
            Assert.Equal(PdfZoomMode.FitWidth, viewer.ZoomMode);
            Assert.Equal(new[] { PdfViewerCommand.SelectAll, PdfViewerCommand.FitWidth }, seen);
        });
    }

    [Fact]
    public void ShowContextMenuOff_TakesTheMenuOffThePage()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.ShowContextMenu = false;
            Assert.Null(viewer.PageSurface.ContextMenuStrip);
            viewer.ShowContextMenu = true;                               // the control
            Assert.NotNull(viewer.PageSurface.ContextMenuStrip);
        });
    }

    [Fact]
    public void UnderHighContrast_TheViewerDrawsInTheSystemsColours()
    {
        var hc = ViewerPalette.For(true);
        Assert.Equal(SystemColors.Highlight.ToArgb() & 0xFFFFFF, hc.Selection.ToArgb() & 0xFFFFFF);
        Assert.Equal(SystemColors.Highlight.ToArgb(), hc.CurrentThumbnail.ToArgb());
        Assert.Equal(SystemColors.AppWorkspace.ToArgb(), hc.PageArea.ToArgb());

        // The control: the viewer's own — WinUI's LayerFillColorDefault (#80FFFFFF) over its base (#F3F3F3).
        var standard = ViewerPalette.For(false);
        Assert.Equal(Color.FromArgb(0xF9, 0xF9, 0xF9).ToArgb(), standard.PageArea.ToArgb());
        Assert.False(standard.HighContrast);
    }

    [DesktopFact]
    public void APaint_TakesThePageAreasColourFromThePalette()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(600, 500), StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, HighContrastOverride = true };
            form.Controls.Add(viewer);
            form.Show();
            viewer.LoadBytes(TestPdf.OnePage());
            using var bmp = new Bitmap(viewer.Width, viewer.Height);
            viewer.DrawToBitmap(bmp, new Rectangle(Point.Empty, viewer.Size));

            Assert.Equal(SystemColors.AppWorkspace.ToArgb(), viewer.PageSurface.BackColor.ToArgb());
        });
    }

    private static string[] MenuTexts(PdfViewerControl viewer)
        => viewer.PageMenu.Items.OfType<ToolStripMenuItem>().Select(i => i.Text ?? "").ToArray();

    private static ToolStripMenuItem Item(PdfViewerControl viewer, string text)
        => viewer.PageMenu.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == text);

    private static void RunSta(Action action) => StaThread.Run(action);
}
