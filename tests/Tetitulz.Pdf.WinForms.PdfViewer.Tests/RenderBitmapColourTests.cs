// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Drawing;
using System.Text;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// <see cref="PdfDocumentSource.RenderBitmap"/> — the path that replaced a PNG encode plus an
/// immediate PNG decode on the UI thread.
/// <para>
/// The renderer produces RGBA8888 premultiplied and <c>Format32bppPArgb</c> is the same layout
/// with red and blue exchanged, so the conversion swizzles. <b>A wrong swizzle is invisible to
/// any test that only checks the bitmap's size or that it is non-null</b> — every page would
/// simply display with red and blue swapped. These assert the actual colours.
/// </para>
/// </summary>
public sealed class RenderBitmapColourTests
{
    /// <summary>
    /// Left half pure red, right half pure blue. Fails loudly if the channels are exchanged,
    /// which is the one mistake the conversion can make.
    /// </summary>
    [Fact]
    public void RenderBitmap_PreservesRedAndBlue_NotSwapped()
    {
        var src = PdfDocumentSource.FromBytes(TwoColourPage());
        using Bitmap bmp = src.RenderBitmap(0, 96);

        Color left  = bmp.GetPixel(bmp.Width / 4, bmp.Height / 2);
        Color right = bmp.GetPixel(bmp.Width * 3 / 4, bmp.Height / 2);

        Assert.True(left.R > 200 && left.G < 60 && left.B < 60,
            $"left half should be red, was {left}");
        Assert.True(right.B > 200 && right.R < 60 && right.G < 60,
            $"right half should be blue, was {right}");
    }

    /// <summary>
    /// The bitmap matches what the PNG path produces, pixel for pixel. This is the guard that
    /// the two routes cannot drift — the viewer now uses one and the library still offers the
    /// other, which is exactly the shape that has gone wrong in this repo before.
    /// </summary>
    [Fact]
    public void RenderBitmap_MatchesTheDecodedPngPixelForPixel()
    {
        var src = PdfDocumentSource.FromBytes(TwoColourPage());

        using Bitmap direct = src.RenderBitmap(0, 96);
        using var ms = new System.IO.MemoryStream(src.RenderPng(0, 96));
        using var viaPng = new Bitmap(ms);

        Assert.Equal(viaPng.Width, direct.Width);
        Assert.Equal(viaPng.Height, direct.Height);

        // Sample a grid rather than every pixel — enough to catch a swizzle, an off-by-one
        // stride or a row-order flip, without a million GetPixel calls.
        for (int gy = 1; gy < 8; gy++)
        for (int gx = 1; gx < 8; gx++)
        {
            int x = direct.Width * gx / 8;
            int y = direct.Height * gy / 8;
            Color a = direct.GetPixel(x, y);
            Color b = viaPng.GetPixel(x, y);
            Assert.True(a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A,
                $"pixel ({x},{y}) differs: direct {a}, via PNG {b}");
        }
    }

    /// <summary>A 200x200 page: red over the left half, blue over the right.</summary>
    private static byte[] TwoColourPage()
    {
        const string content = "1 0 0 rg 0 0 100 200 re f\n0 0 1 rg 100 0 100 200 re f";

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        int o1 = sb.Length;
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        int o2 = sb.Length;
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        int o3 = sb.Length;
        sb.Append("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] " +
                  "/Contents 4 0 R /Resources << >> >>\nendobj\n");
        int o4 = sb.Length;
        sb.AppendFormat("4 0 obj\n<< /Length {0} >>\nstream\n{1}\nendstream\nendobj\n",
                        content.Length, content);
        int xref = sb.Length;
        sb.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (int o in new[] { o1, o2, o3, o4 })
            sb.AppendFormat("{0:D10} 00000 n \n", o);
        sb.Append("trailer\n<< /Size 5 /Root 1 0 R >>\n");
        sb.AppendFormat("startxref\n{0}\n%%EOF\n", xref);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
