// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Drawing;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Verifies the PDF-user-space → rendered-bitmap-pixel transform, including the crop-box
/// origin shift, the Y-flip, and each page /Rotate (matching the renderer's pipeline).
/// </summary>
public sealed class PdfPixelMapperTests
{
    [Theory]
    [InlineData(72, 1f)]
    [InlineData(144, 2f)]
    [InlineData(96, 96f / 72f)]
    public void ScaleForDpi_IsDpiOver72(int dpi, float expected)
        => Assert.Equal(expected, PdfPixelMapper.ScaleForDpi(dpi), 4);

    [Fact]
    public void Rotate0_FlipsYAndKeepsXY()
    {
        // 100×200 page, origin (0,0), scale 1. Rect at (10,20) 30×40 (origin bottom-left).
        var geo = new PageGeometry(0, 0, 100, 200, 0);
        var r = PdfPixelMapper.MapRect(geo, 1f, 10, 20, 30, 40);
        // top = H - (y + h) = 200 - 60 = 140.
        AssertRect(r, 10, 140, 30, 40);
    }

    [Fact]
    public void Rotate0_HonoursCropOrigin()
    {
        // Crop box starts at (50,60); a rect at (60,80) sits 10pt right / 20pt up of the origin.
        var geo = new PageGeometry(50, 60, 100, 200, 0);
        var r = PdfPixelMapper.MapRect(geo, 1f, 60, 80, 30, 40);
        // ux=10, uy=20 → same as the origin-0 case.
        AssertRect(r, 10, 140, 30, 40);
    }

    [Fact]
    public void Rotate180_MirrorsBothAxes()
    {
        var geo = new PageGeometry(0, 0, 100, 200, 180);
        var r = PdfPixelMapper.MapRect(geo, 1f, 10, 20, 30, 40);
        // unrotated left=10 right=40 top=140 bottom=180; 180 → (rawW-x, rawH-y).
        AssertRect(r, 60, 20, 30, 40);
    }

    [Fact]
    public void Rotate90_SwapsDimensions()
    {
        var geo = new PageGeometry(0, 0, 100, 200, 90);
        Assert.Equal(200, geo.DisplayWidthPts);
        Assert.Equal(100, geo.DisplayHeightPts);
        var r = PdfPixelMapper.MapRect(geo, 1f, 10, 20, 30, 40);
        // 90 → ox = rawH - ry, oy = rx. x∈[20,60], y∈[10,40].
        AssertRect(r, 20, 10, 40, 30);
    }

    [Fact]
    public void Rotate270_SwapsDimensions()
    {
        var geo = new PageGeometry(0, 0, 100, 200, 270);
        var r = PdfPixelMapper.MapRect(geo, 1f, 10, 20, 30, 40);
        // 270 → ox = ry, oy = rawW - rx. x∈[140,180], y∈[60,90].
        AssertRect(r, 140, 60, 40, 30);
    }

    private static void AssertRect(RectangleF r, float x, float y, float w, float h)
    {
        Assert.Equal(x, r.X, 2);
        Assert.Equal(y, r.Y, 2);
        Assert.Equal(w, r.Width, 2);
        Assert.Equal(h, r.Height, 2);
    }
}
