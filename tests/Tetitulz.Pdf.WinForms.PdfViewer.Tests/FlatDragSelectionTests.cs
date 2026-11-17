// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Drawing;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// A flat (horizontal) drag has to select the line the pointer is sweeping. The two pieces of
/// geometry involved are the real ones: <see cref="PdfPixelMapper.MapGlyph"/> is the box the
/// control hit-tests (baseline-anchored, with room for a descender — not
/// <see cref="PdfPixelMapper.MapRect"/> applied to the extractor's numbers verbatim), and
/// <c>PdfViewerControl.SelectionBand</c> builds the drag rectangle. Selection is "the glyph's
/// centre is inside the band", so the two must overlap.
/// </summary>
public sealed class FlatDragSelectionTests
{
    // A 200x200 pt page, no rotation, rendered at 96 dpi.
    private static readonly PageGeometry Geo = new PageGeometry(0f, 0f, 200f, 200f, 0);
    private static readonly float Scale = PdfPixelMapper.ScaleForDpi(96);

    private const float FontSize = 12f;
    private const float Line1Baseline = 100f;         // PDF user space, origin bottom-left
    private const float Line2Baseline = 100f - 14f;   // the next line down, 14 pt leading

    /// <summary>Canvas y of the centre of a glyph sitting on <paramref name="baselinePdfY"/>.</summary>
    private static float GlyphCentreY(float baselinePdfY)
    {
        RectangleF r = PdfPixelMapper.MapGlyph(Geo, Scale, 20f, baselinePdfY, 6f, FontSize);
        return r.Y + r.Height / 2f;
    }

    /// <summary>Canvas y of a point on <paramref name="baselinePdfY"/> — where a sweep goes.</summary>
    private static float BaselineCanvasY(float baselinePdfY)
        => PdfPixelMapper.MapRect(Geo, Scale, 20f, baselinePdfY, 1f, 0f).Y;

    [Fact]
    public void FlatDragAlongABaseline_SelectsThatLine()
    {
        int y = (int)BaselineCanvasY(Line1Baseline);
        RectangleF band = PdfViewerControl.SelectionBand(new Point(20, y), new Point(160, y), Scale);

        Assert.True(band.Contains(60f, GlyphCentreY(Line1Baseline)),
            "a sweep along the baseline must cover the glyphs sitting on it");
    }

    [Fact]
    public void FlatDragAlongABaseline_DoesNotSelectTheLineBelow()
    {
        int y = (int)BaselineCanvasY(Line1Baseline);
        RectangleF band = PdfViewerControl.SelectionBand(new Point(20, y), new Point(160, y), Scale);

        Assert.False(band.Contains(60f, GlyphCentreY(Line2Baseline)),
            "the line below the one being swept must not be selected");
    }

    /// <summary>
    /// The other natural sweep: through the glyphs rather than along the baseline. Sampled
    /// <b>between</b> the glyph centre and the baseline, not exactly on the centre — a band hung
    /// downward from the centre happens to include the centre as its own top edge, so that one
    /// point is a boundary coincidence and cannot tell the two behaviours apart.
    /// </summary>
    [Fact]
    public void FlatDragThroughTheGlyphs_SelectsThatLineAndNotTheNext()
    {
        int y = (int)GlyphCentreY(Line1Baseline) + 4;
        RectangleF band = PdfViewerControl.SelectionBand(new Point(20, y), new Point(160, y), Scale);

        Assert.True(band.Contains(60f, GlyphCentreY(Line1Baseline)));
        Assert.False(band.Contains(60f, GlyphCentreY(Line2Baseline)));
    }

    /// <summary>
    /// The control: a drag with real vertical extent is untouched — it still spans exactly the
    /// pointer's own rectangle, so multi-line selection keeps its previous behaviour.
    /// </summary>
    [Fact]
    public void TallDrag_UsesThePointerRectangleUnchanged()
    {
        RectangleF band = PdfViewerControl.SelectionBand(new Point(20, 100), new Point(160, 300), Scale);

        Assert.Equal(20f, band.X);
        Assert.Equal(100f, band.Y);
        Assert.Equal(140f, band.Width);
        Assert.Equal(200f, band.Height);
    }
}
