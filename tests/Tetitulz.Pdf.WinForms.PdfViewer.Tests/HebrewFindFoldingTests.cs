// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using Tetitulz.Pdf.Core.Text;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The find box folds Hebrew on the same terms the library does — and the folded match still
/// highlights the glyphs the document actually spells the word with.
///
/// <para><b>Two independent things can break here, and only one of them is "did it match".</b>
/// Folding changes the string's length, so a match found in folded space lands at a different
/// offset from the one <c>RectForRange</c> needs: it indexes <c>PageTextIndex.Starts</c>, which is
/// keyed to the ORIGINAL extracted text. A fold that reported folded offsets would find the right
/// word and paint a rectangle over its neighbour — a defect that reads as a rendering bug. So every
/// test here asserts a <b>position</b> (the exact substring the highlight covers) or a
/// <b>rectangle</b>, never that a match was found.</para>
///
/// <para><b>The fixture is a real PDF</b> — a Type0/Identity-H font with a <c>/ToUnicode</c> CMap,
/// which is how the repository's own OCR layer writes text — so the glyph boxes come from the same
/// extraction path the viewer uses at runtime, at coordinates this file chose. A synthetic index
/// would prove the arithmetic and not the wiring.</para>
/// </summary>
public sealed class HebrewFindFoldingTests
{
    // ── The fixture's four words, in reading order ────────────────────────────
    // Three of them carry niqqud, which is invisible in most diffs — each is named letter by
    // letter below, because a combining mark silently lost in an editor would leave a test that
    // still passes and no longer tests folding.

    /// <summary>
    /// alef-qamats lamed-segol fe-sofit — pointed, and FIRST in the extracted text, which is what
    /// makes the index map observable: its two marks are exactly the offset the target word shifts
    /// by once they are folded away.
    /// </summary>
    private const string Decoy = "אָלֶף";

    /// <summary>shin-qamats lamed vav-holam mem-sofit — six characters, four of them letters.</summary>
    private const string Target = "שָלוֹם";

    /// <summary>The same word unpointed, as a person types it into a find box.</summary>
    private const string Query = "שלום";

    /// <summary>
    /// bet-yod-tav — three letters, no niqqud. The geometric ruler: it is found with folding off,
    /// so its rectangle is the one measurement no folding defect can have moved.
    /// </summary>
    private const string Anchor = "בית";

    /// <summary>gimel-hiriq yod mem-segol lamed — pointed, and last, so the target word has a
    /// neighbour on both sides.</summary>
    private const string Tail = "גִימֶל";

    /// <summary>Point size, and the font's <c>/DW 1000</c> advance: every glyph is 12 pt wide.</summary>
    private const int GlyphPts = 12;

    /// <summary>Leftmost drawn x, and the gap between word origins (see <see cref="HebrewPage"/>).</summary>
    private const int FirstWordX = 20, WordPitch = 120;

    // ── The matcher, in isolation ─────────────────────────────────────────────

    /// <summary>
    /// A pointed word found by an unpointed query is reported at the pointed word's own offset and
    /// length — not the folded ones.
    /// </summary>
    /// <remarks>
    /// The numbers are the whole test. In folded space the hit is at 4 and is 4 long; in the real
    /// text it starts at 6 (three letters and two marks of <see cref="Decoy"/>, plus the word gap)
    /// and is 6 long. Returning the folded pair compiles, matches, and moves the highlight two
    /// characters left and two characters short.
    /// </remarks>
    [Fact]
    public void FoldedMatch_IsReportedAtThePointedWordsOwnOffsetAndLength()
    {
        string text = Decoy + " " + Target + " " + Anchor;

        var hits = PdfViewerControl.FoldedMatches(text, Query, HebrewFolding.Default, 16,
                                                  out bool truncated);

        var hit = Assert.Single(hits);
        Assert.False(truncated);
        Assert.Equal(6, hit.Start);
        Assert.Equal(6, hit.Length);
        Assert.Equal(Target, text.Substring(hit.Start, hit.Length));
    }

    /// <summary>Without folding, the same query finds nothing at all — which is the defect this
    /// change closes, stated as a fact about the fixture rather than about the code.</summary>
    [Fact]
    public void UnfoldedMatch_MissesThePointedWordEntirely()
    {
        string text = Decoy + " " + Target + " " + Anchor;

        Assert.Empty(PdfViewerControl.FoldedMatches(text, Query, HebrewFolding.None, 16, out _));
    }

    /// <summary>
    /// Final forms are NOT folded by default, so a search for ארץ does not highlight the middle of
    /// ארצות — a hit the document does not contain.
    /// </summary>
    /// <remarks>
    /// Both halves are needed: the second asserts the flag works, so the first is a statement about
    /// the chosen default rather than about a folding the code cannot do.
    /// </remarks>
    [Fact]
    public void FinalForms_AreNotFoldedByDefault_SoNoSubstringIsInvented()
    {
        const string haystack = "ארצות";   // aleph resh tsadi vav tav — "lands"
        const string needle = "ארץ";       // aleph resh tsadi-sofit — "land"

        Assert.Empty(PdfViewerControl.FoldedMatches(haystack, needle, HebrewFolding.Default, 16, out _));

        var withFinalForms = PdfViewerControl.FoldedMatches(
            haystack, needle, HebrewFolding.Default | HebrewFolding.FinalForms, 16, out _);
        Assert.Equal(0, Assert.Single(withFinalForms).Start);
    }

    /// <summary>
    /// Latin text is unaffected: folding is a no-op over it, and the box stays case-insensitive and
    /// non-overlapping the way it always was.
    /// </summary>
    [Fact]
    public void LatinSearch_KeepsItsOffsetsAndItsCaseInsensitivity()
    {
        var hits = PdfViewerControl.FoldedMatches("Hello World hello", "hello",
                                                  HebrewFolding.Default, 16, out _);

        Assert.Equal(2, hits.Count);
        Assert.Equal(0, hits[0].Start);
        Assert.Equal(12, hits[1].Start);
        Assert.Equal(5, hits[0].Length);
    }

    /// <summary>
    /// A term made only of niqqud folds to nothing, and an empty needle is at every position:
    /// the answer is no matches, not a highlight over the whole page.
    /// </summary>
    [Fact]
    public void TermOfNothingButNiqqud_MatchesNowhere()
        => Assert.Empty(PdfViewerControl.FoldedMatches(
            Target, "ָֹ", HebrewFolding.Default, 16, out _));

    /// <summary>The match list is bounded, and the caller is told it was cut short.</summary>
    [Fact]
    public void MatchesAreCappedAtTheCallersLimit_AndReportTruncation()
    {
        string text = Target + " " + Target + " " + Target;

        var hits = PdfViewerControl.FoldedMatches(text, Query, HebrewFolding.Default, 2,
                                                  out bool truncated);

        Assert.Equal(2, hits.Count);
        Assert.True(truncated, "a third match existed and was dropped at the limit");
    }

    /// <summary>
    /// A budget of zero keeps nothing and still reports that this page had a match — the difference
    /// between a result label reading "2000" and one reading "2000+".
    /// </summary>
    /// <remarks>
    /// Worth its own test because the obvious <c>if (limit &lt;= 0) return</c> is silent instead:
    /// the page loop would walk the rest of the document finding nothing to say, and the count
    /// would be presented as a total.
    /// </remarks>
    [Fact]
    public void AnExhaustedBudget_StillReportsThatMoreMatchesExist()
    {
        var hits = PdfViewerControl.FoldedMatches(Target, Query, HebrewFolding.Default, 0,
                                                  out bool truncated);

        Assert.Empty(hits);
        Assert.True(truncated, "the page held a match the exhausted budget could not keep");
    }

    // ── The find box, end to end, over a real page ────────────────────────────

    /// <summary>
    /// With folding off the viewer cannot find the pointed word — the state this change is closing,
    /// and the proof that the fixture is genuinely pointed rather than accidentally plain.
    /// </summary>
    [Fact]
    public void FindBox_WithFoldingOff_DoesNotFindThePointedWord()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.Size = new Size(900, 700);
            ctrl.LoadBytes(HebrewPage());

            ctrl.FindHebrewFolding = HebrewFolding.None;
            ctrl.Find(Query);
            Assert.True(ctrl.DrainSearch());

            Assert.Equal(0, ctrl.FindMatchCount);
        });
    }

    /// <summary>
    /// A viewer nobody configured folds the pointed word away and finds it.
    /// </summary>
    /// <remarks>
    /// Separate from the geometry test below, which sets the property explicitly and therefore
    /// cannot see the default at all: switching the default back to
    /// <see cref="HebrewFolding.None"/> leaves that test green and this one red. A host embedding
    /// the control writes no configuration, so the default is the behaviour that ships.
    /// </remarks>
    [Fact]
    public void FindBox_FoldsNiqqudByDefault_WithNoConfiguration()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.Size = new Size(900, 700);
            ctrl.LoadBytes(HebrewPage());

            ctrl.Find(Query);
            Assert.True(ctrl.DrainSearch());

            Assert.Equal(1, ctrl.FindMatchCount);
            Assert.Equal(Target, ctrl.FindMatchText(0));
        });
    }

    /// <summary>
    /// The unpointed query finds the pointed word AND highlights that word's glyphs — the whole
    /// word, and none of its neighbours'.
    /// </summary>
    /// <remarks>
    /// <para>The rectangle is checked against a ruler the fixture supplies: <see cref="Anchor"/> is
    /// unpointed, so it is found with folding switched off and its rectangle cannot have been moved
    /// by any folding defect. Three glyphs wide, it yields the canvas width of one glyph, and the
    /// page's drawn coordinates then say exactly where the target must be — six glyphs wide,
    /// starting seven glyphs to the right of the anchor's right edge.</para>
    /// <para>Reporting the folded offset instead puts the rectangle four glyphs further right and
    /// makes it seven glyphs wide, spanning from the middle of the target into the decoy beside it.
    /// Both assertions see that; <c>Assert.Equal(Target, …)</c> sees it in text coordinates first.</para>
    /// </remarks>
    [Fact]
    public void FindBox_HighlightsTheGlyphsOfThePointedWordItMatched()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.Size = new Size(900, 700);
            ctrl.LoadBytes(HebrewPage());

            // The ruler: an unpointed word, matched with no folding at all.
            ctrl.FindHebrewFolding = HebrewFolding.None;
            ctrl.Find(Anchor);
            Assert.True(ctrl.DrainSearch());
            Assert.Equal(1, ctrl.FindMatchCount);
            var ruler = ctrl.FindMatchRect(0);
            Assert.True(ruler.HasValue, "the anchor word produced no highlight rectangle");
            float glyph = ruler!.Value.Width / 3f;          // בית is three glyphs
            Assert.True(glyph > 1f, $"implausible glyph width {glyph} from the anchor rectangle");

            ctrl.FindHebrewFolding = HebrewFolding.Default;
            ctrl.Find(Query);
            Assert.True(ctrl.DrainSearch());

            Assert.Equal(1, ctrl.FindMatchCount);
            Assert.Equal(Target, ctrl.FindMatchText(0));    // the position claim, in text coordinates

            var hit = ctrl.FindMatchRect(0);
            Assert.True(hit.HasValue, "the folded match produced no highlight rectangle");

            // Target is six glyphs wide and its left edge is seven glyph-widths right of the
            // anchor's right edge — the two words are drawn 120 pt apart and the anchor is 36 pt.
            float tolerance = glyph / 2f;
            Assert.InRange(hit!.Value.Width, 6 * glyph - tolerance, 6 * glyph + tolerance);
            Assert.InRange(hit.Value.Left,
                ruler.Value.Right + 7 * glyph - tolerance,
                ruler.Value.Right + 7 * glyph + tolerance);
        });
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One line reading <c>Decoy Target Anchor Tail</c>, drawn the way a Hebrew PDF is: glyphs are
    /// placed left to right in <b>visual</b> order, so each word is emitted reversed and the words
    /// run right to left across the page. Extraction bidi-reorders that back into reading order,
    /// which is what the viewer's text index holds.
    /// </summary>
    /// <remarks>
    /// Every glyph advances 12 pt (<c>/DW 1000</c> at 12 pt), and word <i>i</i> of the reading order
    /// is drawn with its origin at <c>380 - 120i</c>, so the four bands are 380–440, 260–332,
    /// 140–176 and 20–92 pt. Those numbers are what the rectangle assertions are stated against.
    /// </remarks>
    private static byte[] HebrewPage()
    {
        string[] reading = { Decoy, Target, Anchor, Tail };

        // One CID per distinct character; /ToUnicode maps it back. No font program is needed —
        // extraction reads widths and /ToUnicode, exactly as the OCR layer writer relies on.
        var codes = new Dictionary<char, int>();
        foreach (string w in reading)
            foreach (char ch in w)
                if (!codes.ContainsKey(ch)) codes[ch] = codes.Count + 1;

        var content = new StringBuilder();
        content.AppendFormat("BT /F1 {0} Tf {1} 150 Td ",
            GlyphPts, FirstWordX + WordPitch * (reading.Length - 1));
        for (int i = 0; i < reading.Length; i++)
        {
            if (i > 0) content.AppendFormat("-{0} 0 Td ", WordPitch);   // leftwards, word by word
            content.Append('<');
            string visual = Reverse(reading[i]);
            foreach (char ch in visual) content.AppendFormat("{0:X4}", codes[ch]);
            content.Append("> Tj ");
        }
        content.Append("ET");

        var bfchar = new StringBuilder();
        foreach (var kv in codes) bfchar.AppendFormat("<{0:X4}> <{1:X4}>\n", kv.Value, (int)kv.Key);
        string cmap =
            "/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n" +
            "/CMapName /Test def\n/CMapType 2 def\n" +
            "1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n" +
            codes.Count + " beginbfchar\n" + bfchar + "endbfchar\nendcmap\n" +
            "CMapName currentdict /CMap defineresource pop\nend\nend";

        return Assemble(new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 480 200]" +
            " /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type0 /BaseFont /Test /Encoding /Identity-H" +
            " /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>",
            "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Test /CIDSystemInfo" +
            " << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >>" +
            " /FontDescriptor 8 0 R /DW 1000 >>",
            $"<< /Length {cmap.Length} >>\nstream\n{cmap}\nendstream",
            "<< /Type /FontDescriptor /FontName /Test /Flags 4 /FontBBox [0 -200 1000 900]" +
            " /ItalicAngle 0 /Ascent 900 /Descent -200 /CapHeight 700 /StemV 80 >>",
        });
    }

    private static string Reverse(string s)
    {
        var a = s.ToCharArray();
        Array.Reverse(a);
        return new string(a);
    }

    /// <summary>Writes numbered objects (index 0 becomes object 1) with a valid xref table.</summary>
    private static byte[] Assemble(List<string> objects)
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.6\n");
        var offsets = new List<int>(objects.Count);
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.AppendFormat("{0} 0 obj\n{1}\nendobj\n", i + 1, objects[i]);
        }
        int xref = sb.Length;
        sb.AppendFormat("xref\n0 {0}\n0000000000 65535 f \n", objects.Count + 1);
        foreach (int o in offsets) sb.AppendFormat("{0:D10} 00000 n \n", o);
        sb.AppendFormat("trailer\n<< /Size {0} /Root 1 0 R >>\n", objects.Count + 1);
        sb.AppendFormat("startxref\n{0}\n%%EOF\n", xref);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>WinForms controls want an STA thread; the exception is rethrown, not swallowed.</summary>
    private static void RunSta(Action action) => StaThread.Run(action);
}
