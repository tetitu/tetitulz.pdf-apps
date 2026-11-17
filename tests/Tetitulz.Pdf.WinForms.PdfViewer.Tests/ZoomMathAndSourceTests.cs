// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using Tetitulz.Pdf.Base.Operations;
using Tetitulz.Pdf.Base.Operations.Extraction;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

public sealed class ZoomMathTests
{
    [Fact]
    public void FitWidth_612ptInto816px_Is100Percent()
    {
        // US-Letter width 612pt at 96 dpi == 816px → exactly 100%.
        Assert.Equal(1.0, ZoomMath.FitWidth(816, 612), 4);
    }

    [Fact]
    public void FitWidth_ScalesLinearlyWithAvailableWidth()
    {
        Assert.Equal(0.5, ZoomMath.FitWidth(408, 612), 4);
        Assert.Equal(2.0, ZoomMath.FitWidth(1632, 612), 4);
    }

    [Fact]
    public void FitPage_PicksTheSmallerOfWidthAndHeightFit()
    {
        // Tall page: height fit is the binding (smaller) constraint.
        double w = ZoomMath.FitWidth(816, 612);
        double h = ZoomMath.FitWidth(500, 792);
        Assert.Equal(System.Math.Min(w, h), ZoomMath.FitPage(816, 500, 612, 792), 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void FitWidth_NonPositiveInputs_FallBackTo100Percent(double widthPts)
        => Assert.Equal(1.0, ZoomMath.FitWidth(816, widthPts), 4);

    // ── Anchoring the scroll position across a zoom ───────────────────────────

    /// <summary>
    /// Zooming while reading near the top of a long document must stay near the top. The old
    /// re-centre hard-coded the fraction to 0.5, so on a 200-page canvas it landed on page 100.
    /// </summary>
    [Fact]
    public void AnchorScroll_NearTheTopOfALongDocument_StaysNearTheTop()
    {
        // 200 pages ≈ 100 000 px of canvas; the reader is 1 000 px down, viewport 800 px.
        int anchored = ZoomMath.AnchorScroll(1000, 100_000, 800, 200_000);

        // centre fraction (1000 + 400) / 100000 = 0.014 → 0.014 * 200000 − 400
        Assert.Equal(2400, anchored);
        Assert.True(anchored < 200_000 / 2,
            "the viewport must not jump to the middle of the document");
    }

    /// <summary>
    /// The contract is fraction preservation, not end-snapping: the last page's centre sits at
    /// fraction 0.996 of a 100 000 px canvas, so after doubling it lands 400 px short of the new
    /// maximum. What matters is that it stays within one viewport of the end — the old behaviour
    /// put it at 99 600, half a document away.
    /// </summary>
    [Fact]
    public void AnchorScroll_AtTheEndOfTheDocument_StaysWithinOneViewportOfTheEnd()
    {
        const int viewport = 800, newMax = 200_000 - viewport;
        int anchored = ZoomMath.AnchorScroll(99_200, 100_000, viewport, 200_000);

        Assert.Equal(198_800, anchored);
        Assert.InRange(anchored, newMax - viewport, newMax);
    }

    /// <summary>An unchanged canvas must leave the scroll position exactly where it was.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(45_678)]
    public void AnchorScroll_UnchangedCanvas_IsTheIdentity(int offset)
        => Assert.Equal(offset, ZoomMath.AnchorScroll(offset, 100_000, 800, 100_000));

    [Fact]
    public void AnchorScroll_NeverGoesNegativeOrPastTheEnd()
    {
        Assert.Equal(0, ZoomMath.AnchorScroll(0, 100_000, 800, 50_000));
        Assert.Equal(0, ZoomMath.AnchorScroll(1000, 100_000, 0, 0));
        Assert.InRange(ZoomMath.AnchorScroll(99_999, 100_000, 800, 1_000), 0, 200);
    }
}

public sealed class PdfDocumentSourceTests
{
    [Fact]
    public void FromBytes_ReadsPageCountAndGeometry()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePage());
        Assert.Equal(1, src.PageCount);

        var geo = src.Geometry(0);
        Assert.Equal(200f, geo.WidthPts, 1);
        Assert.Equal(200f, geo.HeightPts, 1);
        Assert.Equal(0, geo.Rotate);
    }

    [Fact]
    public void RenderPng_ProducesAPngBitmap()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePage());
        byte[] png = src.RenderPng(0, 96);

        Assert.True(png.Length > 8, "expected non-empty PNG");
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
    }

    [Fact]
    public void LinksFor_ReturnsUriLink()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePageWithUriLink("https://example.com/"));
        var links = src.LinksFor(0);

        var link = Assert.Single(links);
        Assert.Equal(PdfLinkKind.Uri, link.Kind);
        Assert.Equal("https://example.com/", link.Uri);
        Assert.Equal(10f, link.X, 2);
        Assert.Equal(20f, link.Y, 2);
        Assert.Equal(100f, link.Width, 2);
        Assert.Equal(40f, link.Height, 2);
    }

    [Fact]
    public void Bookmarks_EmptyWhenNone()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePage());
        Assert.Empty(src.Bookmarks);
    }
}
