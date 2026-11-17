// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Tetitulz.Pdf.Base.Rendering;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The viewer stretches the rendered bitmap onto the rectangle it laid out from
/// <see cref="PageGeometry"/>, so those two must describe the <b>same</b> rectangle of the page.
/// When they disagree the image is scaled non-uniformly and every link, selection and search
/// highlight is positioned from the wrong box — a divergence a pixel-comparing test cannot see,
/// because a wrong-sized page is still internally consistent.
/// </summary>
public sealed class RenderGeometryParityTests
{
    private const int Dpi = 96;

    /// <summary>
    /// §14.11.2: a boundary box extending outside the <c>/MediaBox</c> is used as the
    /// intersection. The renderer applies that; the viewer read <c>/CropBox</c> raw, so it laid
    /// out 612x1584 pt for a bitmap the renderer produced at 612x792 pt — the page drawn at double
    /// height, with a link at the top of the page highlighting halfway down it.
    /// </summary>
    [Fact]
    public void CropBoxLargerThanMediaBox_IsIntersectedWithIt()
    {
        var src = PdfDocumentSource.FromBytes(
            TestPdf.OnePageWithBoxes("[0 0 612 792]", "[0 0 612 1584]"));

        var geo = src.Geometry(0);
        Assert.Equal(612f, geo.WidthPts, 1);
        Assert.Equal(792f, geo.HeightPts, 1);
    }

    /// <summary>
    /// The invariant behind it, stated as parity rather than as a number: the bitmap the viewer
    /// draws must be exactly the size the layout reserved for it.
    /// </summary>
    [Theory]
    [InlineData("[0 0 612 792]", "[0 0 612 1584]")]   // CropBox taller than the MediaBox
    [InlineData("[0 0 612 792]", "[-90 0 900 792]")]  // …and wider, on both edges
    [InlineData("[0 0 612 792]", "[36 36 576 756]")]  // an ordinary inset CropBox: unchanged
    public void LaidOutSize_EqualsTheRenderedBitmapSize(string media, string crop)
    {
        byte[] pdf = TestPdf.OnePageWithBoxes(media, crop);
        var src = PdfDocumentSource.FromBytes(pdf);
        var geo = src.Geometry(0);

        using var bmp = src.RenderBitmap(0, Dpi);
        float scale = PdfPixelMapper.ScaleForDpi(Dpi);

        Assert.Equal((int)Math.Round(geo.DisplayWidthPts * scale), bmp.Width);
        Assert.Equal((int)Math.Round(geo.DisplayHeightPts * scale), bmp.Height);
    }

    /// <summary>
    /// The same parity against the renderer's own pre-render answer, which is the shared decision
    /// point (<c>PdfPageRenderer.ResolveRenderBox</c>) rather than a second opinion about it.
    /// </summary>
    [Theory]
    [InlineData("[0 0 612 792]", "[0 0 612 1584]")]
    [InlineData("[0 0 612 792]", "[-90 0 900 792]")]
    [InlineData("[0 0 612 792]", "[36 36 576 756]")]
    [InlineData("[0 0 200 200]", "[0 0 200 200]")]
    public void GeometryPointSize_MatchesPdfPageRendererGetRenderSize(string media, string crop)
    {
        byte[] pdf = TestPdf.OnePageWithBoxes(media, crop);
        var geo = PdfDocumentSource.FromBytes(pdf).Geometry(0);

        using var ms = new MemoryStream(pdf, false);
        PageRenderSize size = PdfPageRenderer.GetRenderSize(ms, 0, Dpi);

        Assert.Equal(size.WidthPoints, geo.WidthPts, 2);
        Assert.Equal(size.HeightPoints, geo.HeightPts, 2);
        Assert.Equal(size.Rotation, geo.Rotate);
    }

    /// <summary>
    /// A <c>/UserUnit</c> page is laid out at the size it renders: the layout read the box in
    /// user-space units and the render was <c>/UserUnit</c> times larger, so a <c>/UserUnit 10</c>
    /// drawing was laid out at a tenth of its size and refused as too large at 100% (Wave 22,
    /// SmallPlugins+Viewer-1:7). A rotated page and the unusable 0 (rendered at 1) alike.
    /// </summary>
    [Theory]
    [InlineData("4", 0, 4f)]
    [InlineData("4", 90, 4f)]
    [InlineData("3.0", 0, 3f)]
    [InlineData("0", 0, 1f)]
    public void AUserUnitPage_IsLaidOutAtTheSizeItRenders(string userUnit, int rotate, float expectedUnit)
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.UserUnitPage(userUnit, rotate));
        var geo = src.Geometry(0);
        Assert.Equal(expectedUnit, geo.UserUnit, 3);

        using var bmp = src.RenderBitmap(0, Dpi);
        float scale = PdfPixelMapper.ScaleForDpi(Dpi);
        Assert.Equal((int)Math.Round(geo.DisplayWidthPts * scale), bmp.Width);
        Assert.Equal((int)Math.Round(geo.DisplayHeightPts * scale), bmp.Height);
    }

    /// <summary>
    /// Overlay positions are in user-space units, so a rectangle maps through the unit: the
    /// right half of a <c>/UserUnit 2</c> page's 100-unit width starts 100 points in.
    /// </summary>
    [Fact]
    public void AnOverlayOnAUserUnitPage_MapsThroughTheUnit()
    {
        var geo = new PageGeometry(0f, 0f, 100f, 50f, 0, userUnit: 2f);
        var r = PdfPixelMapper.MapRect(geo, 1f, 50f, 0f, 50f, 50f);

        Assert.Equal(100f, r.X, 3);
        Assert.Equal(100f, r.Width, 3);
        Assert.Equal(0f, r.Y, 3);
        Assert.Equal(100f, r.Height, 3);
    }
}
