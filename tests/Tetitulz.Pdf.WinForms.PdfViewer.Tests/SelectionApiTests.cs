// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Linq;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Selection as a reader and a host reach it: a drag selects in reading order (a rectangle on
/// request), a double click a word, a triple click a line, Ctrl+A the page; the host reads the text
/// and each glyph's box, and hears every change.
/// </summary>
public sealed class SelectionApiTests
{
    [Fact]
    public void ADrag_SelectsInReadingOrder_AcrossTheLineBreak()
    {
        RunSta(() =>
        {
            using var viewer = Viewer(TestPdf.TwoLinesOfText());       // "First line here" / "Second line here"
            RectangleF here = WordRect(viewer, "here", 0);
            RectangleF second = WordRect(viewer, "Second", 0);
            Point from = new Point((int)here.Left + 1, (int)(here.Top + here.Height / 2));
            Point to = new Point((int)second.Right - 1, (int)(second.Top + second.Height / 2));

            Assert.Equal("here\nSecond", viewer.SelectBetween(from, to));
            Assert.Equal("here\nSecond", viewer.SelectBetween(to, from));      // backwards, the same text

            viewer.TextSelectionMode = PdfTextSelectionMode.Rectangle;            // the control: a column
            Assert.NotEqual("here\nSecond", viewer.SelectBetween(from, to));
        });
    }

    /// <summary>
    /// A copied line keeps its word spaces at any type size: they come from the page's own text, not
    /// from a gap between boxes that 12-point Helvetica's word space is narrower than.
    /// </summary>
    [Theory]
    [InlineData(PdfTextSelectionMode.ReadingOrder)]
    [InlineData(PdfTextSelectionMode.Rectangle)]
    public void ADraggedLine_KeepsItsWordSpaces(PdfTextSelectionMode mode)
    {
        RunSta(() =>
        {
            using var viewer = Viewer(TestPdf.TwoLinesOfText());
            viewer.TextSelectionMode = mode;
            RectangleF first = WordRect(viewer, "First", 0);
            RectangleF here = WordRect(viewer, "here", 0);
            int y = (int)(first.Top + first.Height / 2);

            Assert.Equal("First line here", viewer.SelectBetween(new Point((int)first.Left - 2, y), new Point((int)here.Right + 2, y)));
        });
    }

    [Fact]
    public void ADoubleClick_SelectsTheWord_AndATripleClickTheLine()
    {
        RunSta(() =>
        {
            using var viewer = Viewer(TestPdf.TwoLinesOfText());
            RectangleF line = WordRect(viewer, "line", 1);                         // the second line's
            Point at = new Point((int)(line.Left + line.Width / 2), (int)(line.Top + line.Height / 2));

            Assert.Equal("line", viewer.ClickSelect(at, line: false));
            Assert.Equal("Second line here", viewer.ClickSelect(at, line: true));
        });
    }

    [Fact]
    public void TheSelection_GivesTheHostEachPagesTextAndGlyphBoxes()
    {
        RunSta(() =>
        {
            using var viewer = Viewer(TestPdf.TwoLinesOfText());
            RectangleF line = WordRect(viewer, "line", 0);
            viewer.ClickSelect(new Point((int)(line.Left + 2), (int)(line.Top + line.Height / 2)), line: false);

            var part = Assert.Single(viewer.Selection);
            Assert.Equal(0, part.PageIndex);
            Assert.Equal("line", part.Text);
            Assert.Equal("line", viewer.SelectedText);
            Assert.Equal(4, part.GlyphBoxes.Count);
            // In user space: the first line's baseline is y 150, 12 pt type — the boxes hold it.
            foreach (var box in part.GlyphBoxes)
            {
                Assert.InRange(box.Y, 140f, 150f);
                Assert.InRange(box.Bottom, 155f, 165f);
                Assert.True(box.Width > 0);
            }
            Assert.True(part.GlyphBoxes[0].X < part.GlyphBoxes[3].X);              // in reading order
        });
    }

    [Fact]
    public void SelectionChanged_IsRaisedForEachChange_AndNotForNone()
    {
        RunSta(() =>
        {
            using var viewer = Viewer(TestPdf.TwoLinesOfText());
            int changes = 0;
            viewer.SelectionChanged += (s, e) => changes++;

            viewer.ClearSelection();
            Assert.Equal(0, changes);                                    // nothing was selected

            viewer.SelectPageText(0);
            Assert.Equal(1, changes);
            Assert.Equal("First line here\nSecond line here", viewer.SelectedText);

            viewer.ClearSelection();
            Assert.Equal(2, changes);
            Assert.Equal("", viewer.SelectedText);
            Assert.Empty(viewer.Selection);
        });
    }

    [Fact]
    public void CtrlA_SelectsThePageBeingRead_ThroughCommandExecuting()
    {
        RunSta(() =>
        {
            using var viewer = Viewer(TestPdf.TwoPagesOfText());
            viewer.GoToPage(1);
            PdfViewerCommand? seen = null;
            viewer.CommandExecuting += (s, e) => seen = e.Command;

            Assert.True(viewer.ExecuteCmdKey(PdfViewerControl.ClassifyCmdKey(
                System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.A, false, false, true)));

            Assert.Equal(PdfViewerCommand.SelectAll, seen);
            Assert.Equal("PAGETWO", viewer.SelectedText);
            Assert.Equal(1, Assert.Single(viewer.Selection).PageIndex);
        });
    }

    [Fact]
    public void WithSelectionOff_NothingSelects()
    {
        RunSta(() =>
        {
            using var viewer = Viewer(TestPdf.TwoLinesOfText());
            viewer.Features &= ~PdfViewerFeatures.TextSelection;

            viewer.SelectPageText(0);

            Assert.Equal("", viewer.SelectedText);
            Assert.False(viewer.CopySelection());
        });
    }

    [Fact]
    public void TextSelectionMode_RefusesAValueOutsideTheEnum()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            Assert.Throws<ArgumentOutOfRangeException>(() => viewer.TextSelectionMode = (PdfTextSelectionMode)7);
            Assert.Equal(PdfTextSelectionMode.ReadingOrder, viewer.TextSelectionMode);
            viewer.TextSelectionMode = PdfTextSelectionMode.Rectangle;   // the control
            Assert.Equal(PdfTextSelectionMode.Rectangle, viewer.TextSelectionMode);
        });
    }

    /// <summary>A word's canvas rectangle, the <paramref name="occurrence"/>th on the page, found as the find box finds it.</summary>
    private static RectangleF WordRect(PdfViewerControl viewer, string word, int occurrence)
    {
        viewer.Find(word);
        Assert.True(viewer.DrainSearch());
        Assert.True(viewer.FindMatchCount > occurrence, $"'{word}' found {viewer.FindMatchCount} times");
        return viewer.FindMatchRect(occurrence)!.Value;
    }

    /// <summary>
    /// Under <see cref="PdfViewerControl.CopyUsesLogicalOrder"/> a page selected whole copies the same two
    /// lines however the view is turned: the lines are told apart by the glyphs' page geometry, not
    /// by canvas boxes the turn moves — at 90° every glyph was a line, at 180° the lines interleaved.
    /// </summary>
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void SelectingAPage_UnderLogicalOrder_CopiesTheSameLines_WhateverTheViewsTurn(int turn)
    {
        RunSta(() =>
        {
            using var viewer = Viewer(TestPdf.TwoLinesOfText());
            viewer.CopyUsesLogicalOrder = true;
            viewer.SelectPageText(0);
            string upright = viewer.SelectedText;
            // The premise: two lines, each read in order (the word gaps are the reading-order rule's own).
            Assert.Equal(2, upright.Split('\n').Length);
            Assert.StartsWith("First", upright, StringComparison.Ordinal);
            Assert.Contains("\nSecond", upright, StringComparison.Ordinal);

            viewer.ViewRotation = turn;
            viewer.SelectPageText(0);
            Assert.Equal(upright, viewer.SelectedText);
        });
    }

    private static PdfViewerControl Viewer(byte[] pdf)
    {
        var viewer = new PdfViewerControl { Size = new Size(900, 700) };
        viewer.LoadBytes(pdf);
        return viewer;
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
