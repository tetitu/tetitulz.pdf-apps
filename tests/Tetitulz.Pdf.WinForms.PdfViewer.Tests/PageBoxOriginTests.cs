// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Text;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The page corner the viewer lays its overlays out from follows the renderer's reading of the
/// boxes: a box with an element that is not finite is no box, so the page is the default size from
/// (0, 0); an element that is not a number reads as 0; a plain cropped page keeps its corner.
/// </summary>
public sealed class PageBoxOriginTests
{
    [Theory]
    [InlineData("/MediaBox [100 100 1000000000000000000000000000000000000000.0 792]", 0f, 0f)]
    [InlineData("/MediaBox [-1000000000000000000000000000000000000000.0 0 612 792]", 0f, 0f)]
    [InlineData("/MediaBox [0 0 612 792] /CropBox [/X 50 612 700]", 0f, 50f)]
    [InlineData("/MediaBox [0 0 612 792] /CropBox [36 36 200 100]", 36f, 36f)]
    public void TheCorner_FollowsTheRenderersReadingOfTheBoxes(string boxes, float x0, float y0)
    {
        var src = PdfDocumentSource.FromBytes(Pdf(boxes));
        PageGeometry geometry = src.Geometry(0);

        Assert.Equal(x0, geometry.CropX0);
        Assert.Equal(y0, geometry.CropY0);
        Assert.False(float.IsInfinity(geometry.CropX0) || float.IsNaN(geometry.CropX0));
    }

    private static byte[] Pdf(string boxes)
    {
        var bodies = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R " + boxes + " >>",
        };
        var sb = new StringBuilder("%PDF-1.6\n");
        var offsets = new List<int>();
        for (int i = 0; i < bodies.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(i + 1).Append(" 0 obj\n").Append(bodies[i]).Append("\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append("xref\n0 ").Append(bodies.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (int o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(bodies.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
