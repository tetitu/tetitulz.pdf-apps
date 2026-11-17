// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Text;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>Builds tiny self-contained PDFs in memory for the viewer tests.</summary>
internal static class TestPdf
{
    /// <summary>
    /// A page whose <c>/CropBox</c> extends past its <c>/MediaBox</c> — legal input that
    /// §14.11.2 requires a consumer to intersect.
    /// </summary>
    public static byte[] CropBoxLargerThanMediaBox() => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /CropBox [0 0 612 1584] >>",
    });

    /// <summary>
    /// A page declaring an enormous <c>/UserUnit</c> (§14.11.1, PDF 1.6+) — legal, a few hundred
    /// bytes, and enough on its own to drive the renderer to its 100 MP ceiling.
    /// </summary>
    public static byte[] HugeUserUnitPage() => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /UserUnit 75000 >>",
    });

    /// <summary>
    /// A 150 x 75 unit page with the given <c>/UserUnit</c> and <c>/Rotate</c> — whole pixels at
    /// 96 dpi (200 x 100), so the renderer's rounding up cannot differ from the layout's rounding.
    /// </summary>
    public static byte[] UserUnitPage(string userUnit, int rotate) => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 150 75] /UserUnit {userUnit} /Rotate {rotate} >>",
    });

    /// <summary>
    /// N identical empty 100x100 pt pages — enough of them to overflow the render cache.
    /// </summary>
    /// <remarks>
    /// The size is load-bearing for <c>RenderCacheEvictionTests</c>, which reads
    /// <see cref="System.Drawing.Bitmap.Width"/> off the returned bitmap (a disposed one throws)
    /// and therefore has to name the exact pixel extent 100 pt at 96 dpi produces. Every other
    /// caller only wants the page count.
    /// </remarks>
    /// <summary>
    /// Two pages of different sizes: a 200 × 200 square, then a 400 × 100 landscape strip — so a
    /// width fit and a page fit each give the two pages different zooms.
    /// </summary>
    public static byte[] MixedPageSizes() => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 100] >>",
    });

    /// <summary>
    /// Two 200 × 200 pages (objects 3 and 4); the first carries one link at rect [10 20 110 60]
    /// whose dictionary ends with <paramref name="target"/> — a <c>/Dest</c> or an <c>/A</c> entry.
    /// </summary>
    public static byte[] TwoPagesWithLink(string target) => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [5 0 R] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
        "<< /Type /Annot /Subtype /Link /Rect [10 20 110 60] " + target + " >>",
    });

    /// <summary><paramref name="count"/> pages of 300 x 100 pt: a quarter turn makes each three times as tall.</summary>
    public static byte[] WidePages(int count)
    {
        var kids = new StringBuilder();
        for (int i = 0; i < count; i++) kids.AppendFormat("{0} 0 R ", i + 3);
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{kids.ToString().TrimEnd()}] /Count {count} >>",
        };
        for (int i = 0; i < count; i++)
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 100] >>");
        return Assemble(objects);
    }

    /// <summary><paramref name="count"/> 100 x 100 pt pages whose catalog asks for <c>/PageLayout /<paramref name="layout"/></c>.</summary>
    public static byte[] WithPageLayout(string layout, int count)
    {
        var kids = new StringBuilder();
        for (int i = 0; i < count; i++) kids.AppendFormat("{0} 0 R ", i + 3);
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /PageLayout /" + layout + " >>",
            $"<< /Type /Pages /Kids [{kids.ToString().TrimEnd()}] /Count {count} >>",
        };
        for (int i = 0; i < count; i++)
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>");
        return Assemble(objects);
    }

    public static byte[] ManyPages(int count)
    {
        var kids = new StringBuilder();
        for (int i = 0; i < count; i++) kids.AppendFormat("{0} 0 R ", i + 3);

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{kids.ToString().TrimEnd()}] /Count {count} >>",
        };
        for (int i = 0; i < count; i++)
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>");
        return Assemble(objects);
    }

    /// <summary>
    /// Two lines of 12 pt Helvetica, baselines 14 pt apart — the geometry a horizontal
    /// sweep-selection has to tell apart.
    /// </summary>
    public static byte[] TwoLinesOfText()
    {
        const string content =
            "BT /F1 12 Tf 20 150 Td (First line here) Tj 0 -14 Td (Second line here) Tj ET";
        return Assemble(new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200]" +
            " /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        });
    }

    /// <summary>Two 300×200 pages: PAGEONE near the foot of the first, PAGETWO near the head of the second.</summary>
    public static byte[] TwoPagesOfText()
    {
        const string one = "BT /F1 12 Tf 20 30 Td (PAGEONE) Tj ET";
        const string two = "BT /F1 12 Tf 20 170 Td (PAGETWO) Tj ET";
        return Assemble(new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200]" +
            " /Resources << /Font << /F1 7 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200]" +
            " /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>",
            $"<< /Length {one.Length} >>\nstream\n{one}\nendstream",
            $"<< /Length {two.Length} >>\nstream\n{two}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        });
    }

    /// <summary>
    /// A 100x100 page covered by a Square annotation whose appearance fills it red, with the
    /// given <c>/F</c> flags — none (shown on screen, not printed), 4 (Print) or 36 (Print | NoView).
    /// </summary>
    public static byte[] RedAnnotationPage(int? flags)
    {
        const string red = "1 0 0 rg 0 0 100 100 re f";
        string f = flags.HasValue ? $" /F {flags.Value}" : "";
        return Assemble(new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [4 0 R] >>",
            $"<< /Type /Annot /Subtype /Square /Rect [0 0 100 100]{f} /AP << /N 5 0 R >> >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 100 100] /Length {red.Length} >>\nstream\n{red}\nendstream",
        });
    }

    /// <summary>N 300×200 pages, each drawing <paramref name="word"/> once in Helvetica, all sharing one content stream.</summary>
    public static byte[] ManyTextPages(int count, string word)
    {
        string content = $"BT /F1 12 Tf 20 150 Td ({word}) Tj ET";
        var kids = new StringBuilder();
        for (int i = 0; i < count; i++) kids.AppendFormat("{0} 0 R ", i + 5);

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{kids.ToString().TrimEnd()}] /Count {count} >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        for (int i = 0; i < count; i++)
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 3 0 R"
                        + " /Resources << /Font << /F1 4 0 R >> >> >>");
        return Assemble(objects);
    }

    /// <summary>
    /// A 400 × 400 page with text fields "a", "b" and "total", <c>/CO [total]</c>; "a" carries
    /// <paramref name="keystroke"/> and <paramref name="validate"/> and "total"
    /// <paramref name="calculate"/> as JavaScript actions (null: none).
    /// </summary>
    internal static byte[] ScriptedForm(string? keystroke, string? validate, string? calculate,
        string? formatA = null, string? formatTotal = null)
    {
        static string Js(string key, string? script)
            => script == null ? "" : "/" + key + " << /S /JavaScript /JS (" + script + ") >> ";
        static string Field(string name, int y, string aa)
            => "<< /Type /Annot /Subtype /Widget /FT /Tx /T (" + name + ") /F 4 /P 3 0 R /Rect [20 " + y
               + " 220 " + (y + 30) + "]" + (aa.Length > 0 ? " /AA << " + aa + ">>" : "") + " >>";

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R 5 0 R 6 0 R] /CO [6 0 R]"
                + " /DA (/Helv 12 Tf 0 g) /DR << /Font << /Helv 7 0 R >> >> >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [4 0 R 5 0 R 6 0 R] /Resources << >> >>",
            Field("a", 300, Js("K", keystroke) + Js("V", validate) + Js("F", formatA)),
            Field("b", 250, ""),
            Field("total", 200, Js("C", calculate) + Js("F", formatTotal)),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        return Assemble(objects);
    }

    /// <summary>
    /// Three 612 × 792 pages and an outline: "One" (page 1, bold, red, open) with "One-mid" (page 1,
    /// y 300); "Two" (page 2, closed) with "Two-sub" (page 2, y 400); "Three" (page 3, italic).
    /// </summary>
    internal static byte[] StyledOutline()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            "<< /Type /Outlines /First 7 0 R /Last 11 0 R /Count 4 >>",
            "<< /Title (One) /Parent 6 0 R /Next 9 0 R /First 8 0 R /Last 8 0 R /Count 1 /C [1 0 0] /F 2 /Dest [3 0 R /Fit] >>",
            "<< /Title (One-mid) /Parent 7 0 R /Dest [3 0 R /XYZ null 300 null] >>",
            "<< /Title (Two) /Parent 6 0 R /Prev 7 0 R /Next 11 0 R /First 10 0 R /Last 10 0 R /Count -1 /Dest [4 0 R /Fit] >>",
            "<< /Title (Two-sub) /Parent 9 0 R /Dest [4 0 R /XYZ null 400 null] >>",
            "<< /Title (Three) /Parent 6 0 R /Prev 9 0 R /F 1 /Dest [5 0 R /Fit] >>",
        };
        return Assemble(objects);
    }

    /// <summary>
    /// Three 72 × 72 pages (one inch), each filled red, with <paramref name="viewerPreferences"/>
    /// (the dictionary's body, or null for none) in the catalog.
    /// </summary>
    internal static byte[] RedInchPages(string? viewerPreferences)
    {
        const string red = "1 0 0 rg 0 0 72 72 re f";
        string prefs = viewerPreferences == null ? "" : " /ViewerPreferences << " + viewerPreferences + " >>";
        return Assemble(new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R" + prefs + " >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] /Contents 6 0 R >>",
            $"<< /Length {red.Length} >>\nstream\n{red}\nendstream",
            "3",
        });
    }

    /// <summary>
    /// Two 400 × 400 pages. Page 1: a Text note "note-1" by "Dana" ("Check this total", dated
    /// 2026-10-27 08:30 UTC) over [50 300 70 320]; a red Square by "Avi" over [100 100 200 200]; a
    /// Link over [300 300 350 320]; a text-field Widget painting blue over [250 100 350 150]; a
    /// Popup; a Text note flagged Hidden; a reply by "Reply Guy" ("Agreed") to the note, over
    /// [50 270 70 290]. Page 2: a Highlight ("Second page") over [50 50 150 70].
    /// </summary>
    internal static byte[] Commented()
    {
        const string red = "1 0 0 rg 0 0 100 100 re f";
        const string blue = "0 0 1 rg 0 0 100 50 re f";
        return Assemble(new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [8 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [5 0 R 6 0 R 7 0 R 8 0 R 9 0 R 10 0 R 14 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [11 0 R] >>",
            "<< /Type /Annot /Subtype /Text /NM (note-1) /Rect [50 300 70 320] /T (Dana) /Contents (Check this total) /M (D:20261027083000Z) /F 4 >>",
            "<< /Type /Annot /Subtype /Square /Rect [100 100 200 200] /T (Avi) /F 4 /AP << /N 12 0 R >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [300 300 350 320] /A << /S /URI /URI (https://example.com) >> >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /F 4 /Rect [250 100 350 150] /AP << /N 13 0 R >> >>",
            "<< /Type /Annot /Subtype /Popup /Rect [50 200 150 250] /Parent 5 0 R >>",
            "<< /Type /Annot /Subtype /Text /Rect [10 10 30 30] /T (Ghost) /Contents (hidden) /F 2 >>",
            "<< /Type /Annot /Subtype /Highlight /Rect [50 50 150 70] /QuadPoints [50 70 150 70 50 50 150 50] /Contents (Second page) /F 4 >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 100 100] /Length {red.Length} >>\nstream\n{red}\nendstream",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 100 50] /Length {blue.Length} >>\nstream\n{blue}\nendstream",
            "<< /Type /Annot /Subtype /Text /NM (reply-1) /Rect [50 270 70 290] /T (Reply Guy) /Contents (Agreed) /IRT 5 0 R /RT /R /F 4 >>",
        });
    }

    /// <summary>
    /// Two 400 × 400 pages: a text field "note" on page 1 over [20 300 220 330], and an empty
    /// signature field "Sig1" on page 2 over [50 300 250 350] — what a signer signs into.
    /// <paramref name="signatureFieldExtra"/> goes into the signature field's dictionary (a <c>/Lock</c>).
    /// </summary>
    internal static byte[] SignableForm(string signatureFieldExtra = "") => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R 6 0 R] /DA (/Helv 12 Tf 0 g) /DR << /Font << /Helv 7 0 R >> >> >> >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [5 0 R] /Resources << >> >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [6 0 R] /Resources << >> >>",
        "<< /Type /Annot /Subtype /Widget /FT /Tx /T (note) /F 4 /P 3 0 R /Rect [20 300 220 330] >>",
        "<< /Type /Annot /Subtype /Widget /FT /Sig /T (Sig1) /F 4 /P 4 0 R /Rect [50 300 250 350] " + signatureFieldExtra + " >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
    });

    /// <summary>
    /// One 400 × 400 page with a text field "h" over [20 100 220 130] whose one widget carries
    /// <c>/F</c> <paramref name="flags"/> (Table 167: 2 Hidden, 4 Print, 32 NoView).
    /// </summary>
    internal static byte[] FormWithWidgetFlags(int flags) => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (/Helv 12 Tf 0 g) /DR << /Font << /Helv 5 0 R >> >> >> >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [4 0 R] /Resources << >> >>",
        "<< /Type /Annot /Subtype /Widget /FT /Tx /T (h) /F " + flags + " /P 3 0 R /Rect [20 100 220 130] >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
    });

    /// <summary>One 100 × 100 page carrying one annotation of <paramref name="entries"/> (its <c>/Type /Annot</c> added).</summary>
    internal static byte[] OneAnnotation(string entries) => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [4 0 R] >>",
        "<< /Type /Annot " + entries + " >>",
    });

    /// <summary>
    /// Four 100 × 100 pages labelled (§12.4.2) "i", "ii", then "1", "2": lower-case roman from the
    /// first page, decimal from the third.
    /// </summary>
    internal static byte[] LabelledPages() => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R /PageLabels << /Nums [0 << /S /r >> 2 << /S /D >>] >> >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R 6 0 R] /Count 4 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
    });

    /// <summary>
    /// Three 200 × 200 pages and a one-item outline, the catalog asking to open at the third page,
    /// top 150, zoom 2, one page at a time, with the bookmarks shown and the chrome right to left.
    /// </summary>
    internal static byte[] AsksForItsOwnView() => Assemble(new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R /Outlines 6 0 R /OpenAction [5 0 R /XYZ null 150 2] /PageLayout /SinglePage"
            + " /PageMode /UseOutlines /ViewerPreferences << /Direction /R2L >> >>",
        "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
        "<< /Type /Outlines /First 7 0 R /Last 7 0 R /Count 1 >>",
        "<< /Title (Start) /Parent 6 0 R /Dest [3 0 R /Fit] >>",
    });

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

    /// <summary>A single 200×200 page, no content.</summary>
    public static byte[] OnePage()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        int o1 = sb.Length;
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        int o2 = sb.Length;
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        int o3 = sb.Length;
        sb.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>\nendobj\n");
        int xref = sb.Length;
        sb.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (int o in new[] { o1, o2, o3 })
            sb.AppendFormat("{0:D10} 00000 n \n", o);
        sb.Append("trailer\n<< /Size 4 /Root 1 0 R >>\n");
        sb.AppendFormat("startxref\n{0}\n%%EOF\n", xref);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// A single page with explicit <c>/MediaBox</c> and <c>/CropBox</c> arrays, written exactly as
    /// given (e.g. <c>"[0 0 612 1584]"</c>) so a test can build a page whose CropBox extends past
    /// its MediaBox — the §14.11.2 case a viewer must intersect.
    /// </summary>
    public static byte[] OnePageWithBoxes(string mediaBox, string cropBox)
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        int o1 = sb.Length;
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        int o2 = sb.Length;
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        int o3 = sb.Length;
        sb.AppendFormat("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox {0} /CropBox {1} >>\nendobj\n",
            mediaBox, cropBox);
        int xref = sb.Length;
        sb.Append("xref\n0 4\n0000000000 65535 f \n");
        foreach (int o in new[] { o1, o2, o3 })
            sb.AppendFormat("{0:D10} 00000 n \n", o);
        sb.Append("trailer\n<< /Size 4 /Root 1 0 R >>\n");
        sb.AppendFormat("startxref\n{0}\n%%EOF\n", xref);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>A single page that draws the text "Hello World" with Helvetica.</summary>
    public static byte[] OnePageWithText()
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        int o1 = sb.Length;
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        int o2 = sb.Length;
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        int o3 = sb.Length;
        sb.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200]" +
                  " /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");
        string content = "BT /F1 24 Tf 20 150 Td (Hello World) Tj ET";
        int o4 = sb.Length;
        sb.AppendFormat("4 0 obj\n<< /Length {0} >>\nstream\n{1}\nendstream\nendobj\n", content.Length, content);
        int o5 = sb.Length;
        sb.Append("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        int xref = sb.Length;
        sb.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (int o in new[] { o1, o2, o3, o4, o5 })
            sb.AppendFormat("{0:D10} 00000 n \n", o);
        sb.Append("trailer\n<< /Size 6 /Root 1 0 R >>\n");
        sb.AppendFormat("startxref\n{0}\n%%EOF\n", xref);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>A single page carrying one URI /Link annotation at rect [10 20 110 60].</summary>
    public static byte[] OnePageWithUriLink(string uri)
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        int o1 = sb.Length;
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        int o2 = sb.Length;
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        int o3 = sb.Length;
        sb.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [4 0 R] >>\nendobj\n");
        int o4 = sb.Length;
        sb.AppendFormat(
            "4 0 obj\n<< /Type /Annot /Subtype /Link /Rect [10 20 110 60]" +
            " /A << /S /URI /URI ({0}) >> >>\nendobj\n", uri);
        int xref = sb.Length;
        sb.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (int o in new[] { o1, o2, o3, o4 })
            sb.AppendFormat("{0:D10} 00000 n \n", o);
        sb.Append("trailer\n<< /Size 5 /Root 1 0 R >>\n");
        sb.AppendFormat("startxref\n{0}\n%%EOF\n", xref);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
