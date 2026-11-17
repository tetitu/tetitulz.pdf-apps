// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Tetitulz.Pdf.Base;
using Tetitulz.Pdf.Base.Operations.Extraction;
using Xunit;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Copying a mixed-direction selection out of the viewer.
/// </summary>
/// <remarks>
/// <para><b>The defect is narrow and the obvious fix is wrong, which is why it stayed open.</b> A
/// majority right-to-left line sorts every glyph by X descending, so the Hebrew comes out correct
/// and an embedded Latin or numeric run inside it comes out reversed. Running the bidi algorithm
/// over the copied string would reverse the Hebrew too — storage order already <i>is</i> reading
/// order. The fix sorts the glyphs back into <b>visual</b> order first, which is the input the
/// algorithm is defined on.</para>
///
/// <para><b>These assert the characters, not "it did something".</b> A reversed part code and a
/// correct one are the same length and the same letters.</para>
/// </remarks>
public class CopyLogicalOrderTests
{
    private const string Hebrew = "שלום";          // 4 letters, strong RTL
    private const string Latin = "AB12";           // an embedded left-to-right run

    /// <summary>
    /// Glyphs for one line, laid out right to left, with <paramref name="embedded"/> placed inside
    /// the Hebrew.
    /// </summary>
    /// <remarks>
    /// Built by hand rather than rendered, because the input under test is a glyph list with
    /// positions and the page it came from is irrelevant. X descends across the Hebrew; the
    /// embedded run occupies a contiguous block whose own characters read left to right.
    /// </remarks>
    private static List<(PdfTextChar Glyph, RectangleF Box)> MixedLine(string embedded)
    {
        var glyphs = new List<(PdfTextChar, RectangleF)>();
        const float width = 10f, height = 12f;
        float x = 200f;   // start at the right-hand end

        void Emit(string ch)
        {
            var g = new PdfTextChar(ch, x, 100f, width, height);
            glyphs.Add((g, new RectangleF(x, 100f, width, height)));
            x -= width;
        }

        // Two Hebrew letters, then the embedded run laid out left-to-right, then two more.
        Emit(Hebrew.Substring(0, 1));
        Emit(Hebrew.Substring(1, 1));

        float runLeft = x - (embedded.Length - 1) * width;
        for (int i = 0; i < embedded.Length; i++)
        {
            var g = new PdfTextChar(embedded[i].ToString(), runLeft + i * width, 100f, width, height);
            glyphs.Add((g, new RectangleF(g.X, 100f, width, height)));
        }
        x = runLeft - width;

        Emit(Hebrew.Substring(2, 1));
        Emit(Hebrew.Substring(3, 1));

        // THE LIST ORDER IS THE EXTRACTOR'S, NOT THE PAGE'S. PdfStructuredTextExtractor sorts a
        // majority right-to-left line by X DESCENDING, which is what gets the Hebrew right and the
        // embedded run wrong — the run arrives already reversed. A fixture that handed the glyphs
        // over in their laid-out order would not reproduce the defect at all: the geometric path
        // would return the run correctly and the test would pass for the wrong reason. The first
        // version of this file did exactly that, and the test asserting the two paths DIFFER is
        // what caught it.
        return glyphs.OrderByDescending(g => g.Item1.X).ToList();
    }

    // ================================================================

    /// <summary>
    /// <b>The embedded Latin run keeps its order</b>, and the Hebrew keeps its.
    /// </summary>
    /// <remarks>
    /// The whole point. Asserting only that the Hebrew survives would pass for the shipped
    /// behaviour, which already gets the Hebrew right.
    /// </remarks>
    [Fact]
    public void AnEmbeddedLatinRunIsNotReversed()
    {
        string copied = PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(MixedLine(Latin));

        Assert.Contains(Latin, copied);
        Assert.DoesNotContain(new string(Latin.Reverse().ToArray()), copied);
    }

    /// <summary>An embedded number keeps its digits in order.</summary>
    /// <remarks>
    /// The case §4a names: <c>"1,234"</c> reversed is <c>"432,1"</c> — still five characters, still
    /// plausible, and wrong in a way that a length or a character-set check cannot see.
    /// </remarks>
    [Fact]
    public void AnEmbeddedNumberIsNotReversed()
    {
        string copied = PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(MixedLine("1,234"));

        Assert.Contains("1,234", copied);
        Assert.DoesNotContain("432,1", copied);
    }

    /// <summary>
    /// <b>The Hebrew is still right</b> — the fix must not disturb what already worked.
    /// </summary>
    /// <remarks>
    /// This is the assertion the register's warning is about: the naive fix reverses this.
    /// </remarks>
    [Fact]
    public void TheHebrewIsStillInReadingOrder()
    {
        string copied = PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(MixedLine(Latin));

        // The first two letters of the word sit at the right-hand end and must lead.
        Assert.StartsWith(Hebrew.Substring(0, 2), copied);
        Assert.EndsWith(Hebrew.Substring(2, 2), copied);
    }

    /// <summary>
    /// Lines are told apart by the glyphs' own geometry, not by the canvas boxes a turned view moves:
    /// two lines whose canvas boxes are those of a quarter-turned view (each glyph a box height below
    /// the last, the second line beside the first) still copy as two lines in page order, and under
    /// a half turn (the second line's boxes above the first's) the same. Judged by the canvas boxes,
    /// the quarter turn made every glyph a line and the half turn one interleaved line.
    /// </summary>
    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    public void TwoLines_StayTwoLines_WhateverTheViewsTurn(int turn)
    {
        var glyphs = new List<(PdfTextChar, RectangleF)>();
        const float width = 10f, height = 12f;
        void Line(string text, float baseline, int index)
        {
            for (int i = 0; i < text.Length; i++)
            {
                var g = new PdfTextChar(text[i].ToString(), 20f + i * width, baseline, width, height);
                RectangleF box = turn == 90
                    ? new RectangleF(300f - index * 20f, 20f + i * width, height, width)     // down the canvas, lines side by side
                    : new RectangleF(300f - i * width, 100f + index * 20f, width, height);   // the second line above the first
                glyphs.Add((g, box));
            }
        }
        Line("ab", 100f, 0);
        Line("cd", 80f, 1);

        Assert.Equal("ab\ncd", PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(glyphs));
    }

    /// <summary>
    /// A wholly right-to-left selection is unchanged by the new path.
    /// </summary>
    /// <remarks>
    /// The control for the paragraph above: if the two paths disagreed here, switching the option on
    /// would change text that was already correct.
    /// </remarks>
    [Fact]
    public void AWhollyHebrewSelectionReadsTheSameEitherWay()
    {
        var glyphs = new List<(PdfTextChar, RectangleF)>();
        float x = 200f;
        foreach (char c in Hebrew)
        {
            var g = new PdfTextChar(c.ToString(), x, 100f, 10f, 12f);
            glyphs.Add((g, new RectangleF(x, 100f, 10f, 12f)));
            x -= 10f;
        }

        string logical = PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(glyphs);
        string geometric = PdfViewerControl.JoinSelectedGlyphs(
            glyphs.Select(g => (g.Item1.Char, g.Item2)).ToList());

        Assert.Equal(Hebrew, logical);
        Assert.Equal(geometric, logical);
    }

    /// <summary>
    /// <b>Each line is ordered on its own</b>, because visual order is a single X axis.
    /// </summary>
    /// <remarks>
    /// Handing a whole multi-line selection to one reading-order pass interleaves the lines by X and
    /// produces nonsense. The line break comes from the same rule the geometric path uses.
    /// </remarks>
    [Fact]
    public void EachLineIsOrderedSeparately()
    {
        var glyphs = new List<(PdfTextChar, RectangleF)>();

        void Line(string text, float y)
        {
            float x = 200f;
            foreach (char c in text)
            {
                var g = new PdfTextChar(c.ToString(), x, y, 10f, 12f);
                glyphs.Add((g, new RectangleF(x, y, 10f, 12f)));
                x -= 10f;
            }
        }

        Line(Hebrew, 100f);
        Line(Hebrew, 140f);   // far enough down to be a new line

        string copied = PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(glyphs);

        Assert.Equal(Hebrew + "\n" + Hebrew, copied);
    }

    /// <summary>An empty selection copies nothing rather than throwing.</summary>
    [Fact]
    public void AnEmptySelectionCopiesNothing()
    {
        Assert.Equal("", PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(
            new List<(PdfTextChar, RectangleF)>()));
        Assert.Equal("", PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(null));
    }

    /// <summary>
    /// The viewer reaches the same rule structured extraction does, rather than a second copy.
    /// </summary>
    /// <remarks>
    /// The claim that matters for the long run: two implementations of a bidi rule would drift, and
    /// the viewer's copy silently disagreeing with `ExtractStructuredText` is precisely the kind of
    /// divergence this repository keeps finding.
    /// </remarks>
    [Fact]
    public void TheViewerUsesTheSharedReadingOrderRule()
    {
        var line = MixedLine(Latin);
        var glyphs = line.Select(g => g.Glyph).ToList();

        Assert.Equal(
            PdfOperations.ToReadingOrder(glyphs, applyBidi: true),
            PdfViewerControl.JoinSelectedGlyphsInLogicalOrder(line));
    }

    /// <summary>
    /// <b>The option actually selects the path</b>, in both directions.
    /// </summary>
    /// <remarks>
    /// A mutation that ignored <c>CopyUsesLogicalOrder</c> and always took the geometric branch
    /// survived every test above, because they all called the ordering method directly and nothing
    /// could reach the decision without a synthesised drag. Asserting both branches is what closes
    /// that — and it has to be a mixed line, because for anything wholly one direction the two
    /// paths agree and the choice is unobservable.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheOptionSelectsWhichOrderingRuleIsUsed(bool logicalOrder)
    {
        var line = MixedLine(Latin);

        string copied = PdfViewerControl.ClipboardTextFor(logicalOrder, line);

        if (logicalOrder)
            Assert.Contains(Latin, copied);
        else
            Assert.Contains(new string(Latin.Reverse().ToArray()), copied);
    }

    /// <summary>An empty selection copies nothing whichever rule is chosen.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEmptySelectionCopiesNothingEitherWay(bool logicalOrder)
    {
        Assert.Equal("", PdfViewerControl.ClipboardTextFor(
            logicalOrder, new List<(PdfTextChar, RectangleF)>()));
    }

    /// <summary>The option is off by default, matching `WordAssemblyOptions.ApplyBidi`.</summary>
    [Fact]
    public void TheOptionIsOffByDefault()
    {
        using var viewer = new PdfViewerControl();

        Assert.False(viewer.CopyUsesLogicalOrder);
    }
}
