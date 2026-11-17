// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Drawing;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Ctrl+C over a right-to-left line has to copy words, not one run of letters.
///
/// <para>The word-gap test was <c>current.Left - previous.Right</c>, which is the distance between
/// two boxes only while reading order advances rightwards. Hebrew and Arabic advance leftwards, so
/// that difference was negative for every adjacent pair on the line and the space was never
/// emitted — <c>שלום עולם</c> came back as <c>שלוםעולם</c>. Asserting that the selection is
/// non-empty, or that it contains the letters, passes with the defect present; only the word count
/// and the exact string can see it.</para>
/// </summary>
public sealed class RtlSelectionTextTests
{
    // Glyph boxes as the control builds them: canvas coordinates, y downward, one line.
    private const float GlyphW = 8f;
    private const float GlyphH = 12f;
    private const float LineY = 100f;

    /// <summary>A glyph box whose LEFT edge is at <paramref name="left"/>.</summary>
    private static RectangleF At(float left, float top = LineY)
        => new RectangleF(left, top, GlyphW, GlyphH);

    /// <summary>
    /// Lays a word out right-to-left starting at <paramref name="rightEdge"/>, in reading order —
    /// the order <c>PdfStructuredTextExtractor</c> hands a right-to-left line over in.
    /// </summary>
    private static void AppendRtl(List<(string, RectangleF)> into, string word, float rightEdge)
    {
        float left = rightEdge - GlyphW;
        foreach (char c in word)
        {
            into.Add((c.ToString(), At(left)));
            left -= GlyphW;
        }
    }

    /// <summary>...and its left-to-right counterpart, for the control.</summary>
    private static void AppendLtr(List<(string, RectangleF)> into, string word, float leftEdge)
    {
        float left = leftEdge;
        foreach (char c in word)
        {
            into.Add((c.ToString(), At(left)));
            left += GlyphW;
        }
    }

    /// <summary>Two Hebrew words with a word-sized gap between them copy as two words.</summary>
    [Fact]
    public void HebrewLine_CopiesWithTheWordSpaceBetweenTheWords()
    {
        var glyphs = new List<(string, RectangleF)>();
        AppendRtl(glyphs, "שלום", rightEdge: 200f);          // rightmost word, read first
        AppendRtl(glyphs, "עולם", rightEdge: 200f - 4 * GlyphW - GlyphW * 2);

        Assert.Equal("שלום עולם", PdfViewerControl.JoinSelectedGlyphs(glyphs));
    }

    /// <summary>
    /// The same claim stated as a count, so a failure says which half broke: the words are there
    /// but unseparated, or the order is wrong.
    /// </summary>
    [Fact]
    public void HebrewLine_CopiesTwoWordsInReadingOrder()
    {
        var glyphs = new List<(string, RectangleF)>();
        AppendRtl(glyphs, "שלום", rightEdge: 200f);
        AppendRtl(glyphs, "עולם", rightEdge: 200f - 4 * GlyphW - GlyphW * 2);

        string[] words = PdfViewerControl.JoinSelectedGlyphs(glyphs).Split(' ');

        Assert.Equal(2, words.Length);
        Assert.Equal("שלום", words[0]);   // the rightmost word is the first one read
        Assert.Equal("עולם", words[1]);
    }

    /// <summary>
    /// A right-to-left word with no gap inside it stays one word — without this the fix could be
    /// "emit a space between every pair", which separates words and shreds them equally.
    /// </summary>
    [Fact]
    public void HebrewWord_WithNoGap_CopiesAsOneWord()
    {
        var glyphs = new List<(string, RectangleF)>();
        AppendRtl(glyphs, "שלום", rightEdge: 200f);

        Assert.Equal("שלום", PdfViewerControl.JoinSelectedGlyphs(glyphs));
    }

    /// <summary>The control: left-to-right text is unchanged by the direction-agnostic rule.</summary>
    [Fact]
    public void LatinLine_StillCopiesWithItsWordSpace()
    {
        var glyphs = new List<(string, RectangleF)>();
        AppendLtr(glyphs, "hello", leftEdge: 20f);
        AppendLtr(glyphs, "world", leftEdge: 20f + 5 * GlyphW + GlyphW * 2);

        Assert.Equal("hello world", PdfViewerControl.JoinSelectedGlyphs(glyphs));
    }

    [Fact]
    public void LatinWord_WithNoGap_CopiesAsOneWord()
    {
        var glyphs = new List<(string, RectangleF)>();
        AppendLtr(glyphs, "hello", leftEdge: 20f);

        Assert.Equal("hello", PdfViewerControl.JoinSelectedGlyphs(glyphs));
    }

    /// <summary>
    /// A second line still breaks, and the line break wins over the word gap: the first glyph of
    /// the next right-to-left line is far to the <i>right</i> of the last of this one, which the
    /// horizontal rule would otherwise read as a word gap.
    /// </summary>
    [Fact]
    public void HebrewSecondLine_StartsANewLineRatherThanAWord()
    {
        var glyphs = new List<(string, RectangleF)>();
        AppendRtl(glyphs, "שלום", rightEdge: 200f);
        glyphs.Add(("ע", At(200f - GlyphW, LineY + GlyphH)));

        string[] lines = PdfViewerControl.JoinSelectedGlyphs(glyphs).Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal("שלום", lines[0]);
        Assert.Equal("ע", lines[1]);
    }

    [Fact]
    public void NoSelectedGlyphs_CopiesNothing()
    {
        Assert.Equal("", PdfViewerControl.JoinSelectedGlyphs(new List<(string, RectangleF)>()));
        Assert.Equal("", PdfViewerControl.JoinSelectedGlyphs(null!));
    }
}
