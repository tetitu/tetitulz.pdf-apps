// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The glyphs a find match covers are found by binary search and the walk stops at the match's
/// end: every highlight walked every glyph of its page on every repaint, matches times glyphs a
/// scroll notch (Wave 22, SmallPlugins+Viewer-1:2). The range is the answer and the bound alike —
/// a full walk would return the page's whole glyph range.
/// </summary>
public sealed class FindHighlightRangeTests
{
    // "ab cd": glyphs a, b, c, d at offsets 0, 1, 3, 4 (the word gap at 2 is no glyph's).
    private static readonly int[] Starts = { 0, 1, 3, 4 };
    private static readonly int[] Ends = { 1, 2, 4, 5 };

    [Theory]
    [InlineData(0, 1, 0, 1)]   // "a"
    [InlineData(1, 3, 1, 3)]   // "b c" — the gap is no glyph's
    [InlineData(3, 2, 2, 4)]   // "cd"
    [InlineData(2, 1, 2, 2)]   // the gap alone: no glyph
    [InlineData(5, 1, 4, 4)]   // past the end
    public void TheRange_IsTheMatchsGlyphsAndNoOthers(int start, int length, int first, int end)
        => Assert.Equal((first, end), PdfViewerControl.GlyphsInRange(Starts, Ends, start, length));

    /// <summary>
    /// A glyph of several characters (a ligature) is touched by a match inside it; an empty glyph
    /// at the match's start is not, since nothing of it is inside.
    /// </summary>
    [Fact]
    public void ALigatureIsTouchedFromInside_AndAnEmptyGlyphIsNot()
    {
        int[] starts = { 0, 2, 2, 3 };   // "fi" ligature, an empty glyph, then x, y
        int[] ends = { 2, 2, 3, 4 };

        Assert.Equal((0, 1), PdfViewerControl.GlyphsInRange(starts, ends, 1, 1));
        Assert.Equal((2, 3), PdfViewerControl.GlyphsInRange(starts, ends, 2, 1));
    }

    /// <summary>On a page of 200 000 glyphs, a match near its end is found without walking the page.</summary>
    [Fact]
    public void AMatchOnAHugePage_IsItsOwnGlyphs()
    {
        const int n = 200_000;
        var starts = new int[n];
        var ends = new int[n];
        for (int i = 0; i < n; i++) { starts[i] = i; ends[i] = i + 1; }

        Assert.Equal((199_990, 199_993), PdfViewerControl.GlyphsInRange(starts, ends, 199_990, 3));
    }
}
