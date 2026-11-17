// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// What a screen reader is told and what the keyboard reaches: every glyph-only button and every box
/// is named from <see cref="PdfViewerControl.Strings"/>, the page area names the page being read, and
/// a thumbnail opens its page from the keyboard. The zoom box offers the two fits by name.
/// </summary>
public sealed class AccessibilityTests
{
    [Fact]
    public void TheGlyphButtons_AreNamed_InTheCurrentStrings()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            Assert.Equal("Previous page (PageUp)", viewer.PreviousPageButton.AccessibleName);
            Assert.Equal("Next page (PageDown)", viewer.NextPageButton.AccessibleName);
            Assert.Equal("Zoom in (Ctrl++)", viewer.ZoomInButton.AccessibleName);
            Assert.Equal("Zoom", viewer.ZoomCombo.AccessibleName);
            Assert.Equal(new[] { "Previous match (Shift+F3)", "Next match (F3)", "Close the find bar (Esc)" },
                viewer.FindBarButtons.Select(b => b.AccessibleName).ToArray());
            Assert.Equal("Text to find", viewer.FindTextBox.AccessibleName);

            viewer.Strings = PdfViewerStrings.Hebrew();

            Assert.Equal("העמוד הקודם (PageUp)", viewer.PreviousPageButton.AccessibleName);
            Assert.Equal("הגדלה", viewer.ZoomCombo.AccessibleName);
            Assert.Equal("ההתאמה הבאה (F3)", viewer.FindBarButtons[1].AccessibleName);
        });
    }

    [Fact]
    public void EveryToolbarItemThatIsAGlyph_HasAName()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            foreach (ToolStripItem item in viewer.Toolbar.Items)
            {
                if (item is ToolStripSeparator || item is ToolStripLabel) continue;
                bool glyphOnly = !string.IsNullOrEmpty(item.Text) && !item.Text.Any(char.IsLetter);
                if (glyphOnly || item is ToolStripTextBox || item is ToolStripComboBox)
                    Assert.False(string.IsNullOrEmpty(item.AccessibleName), $"'{item.Text}' has no accessible name");
            }
        });
    }

    [Fact]
    public void ThePageArea_NamesThePageBeingRead()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            Assert.Equal("Ready", viewer.PageSurface.AccessibleName);
            Assert.Equal(AccessibleRole.Document, viewer.PageSurface.AccessibleRole);

            viewer.LoadBytes(TestPdf.ManyPages(3));
            Assert.Equal("Page 1 of 3", viewer.PageSurface.AccessibleName);
            viewer.GoToPage(2);
            Assert.Equal("Page 3 of 3", viewer.PageSurface.AccessibleName);

            viewer.LoadBytes(TestPdf.LabelledPages());
            viewer.GoToPage(1);
            Assert.Equal("Page ii of 4", viewer.PageSurface.AccessibleName);   // as the page box shows it
        });
    }

    [Fact]
    public void AThumbnail_IsNamed_AndEnterOpensItsPage()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.LabelledPages());
            Control second = viewer.ThumbnailItems[1];
            Assert.Equal("Page ii", second.AccessibleName);
            Assert.True(second.TabStop);

            viewer.PressThumbnailKey(3, Keys.Enter);

            Assert.Equal(3, viewer.CurrentPage);
            Assert.True(viewer.CanGoBack);                               // a jump, as a click is
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheZoomBox_OffersTheFitsByName(bool hebrew)
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Size = new System.Drawing.Size(900, 700) };
            if (hebrew) viewer.Strings = PdfViewerStrings.Hebrew();
            viewer.LoadBytes(TestPdf.OnePage());
            var items = viewer.ZoomCombo.Items.Cast<object>().Select(o => o.ToString()).ToList();
            Assert.Contains(viewer.Strings.FitWidth, items);
            Assert.Contains(viewer.Strings.FitPage, items);

            viewer.ZoomCombo.SelectedItem = viewer.Strings.FitPage;
            Assert.Equal(PdfZoomMode.FitPage, viewer.ZoomMode);
            viewer.ZoomCombo.SelectedItem = viewer.Strings.FitWidth;
            Assert.Equal(PdfZoomMode.FitWidth, viewer.ZoomMode);
            viewer.ZoomCombo.SelectedItem = "200%";
            Assert.Equal(PdfZoomMode.Custom, viewer.ZoomMode);
            Assert.Equal(2.0, viewer.Zoom, 6);
        });
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
