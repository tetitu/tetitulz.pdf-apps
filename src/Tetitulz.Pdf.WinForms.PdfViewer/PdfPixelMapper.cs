// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using Tetitulz.Pdf.Core.Constants;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Pure geometry: maps a PDF user-space rectangle (origin bottom-left, points,
    /// absolute coordinates) to a pixel rectangle inside the rendered page bitmap
    /// (origin top-left, y-down). The transform reproduces the renderer's pipeline:
    /// shift by the crop-box origin, scale by dpi / <see cref="PdfUnits.PointsPerInch"/>,
    /// flip Y, then apply the page /Rotate as a clockwise bitmap rotation (with the
    /// 90/270 dimension swap).
    /// </summary>
    internal static class PdfPixelMapper
    {
        /// <summary>Pixels per PDF point for a given render DPI.</summary>
        public static float ScaleForDpi(int dpi) => PdfUnits.DpiScale(dpi);

        /// <summary>How far a glyph's visual box drops below its baseline, as a fraction of size.</summary>
        public const float GlyphDescentFraction = 0.25f;

        /// <summary>Total height of a glyph's visual box, as a fraction of the font size.</summary>
        public const float GlyphBoxHeightFraction = 1.2f;

        /// <summary>
        /// Maps a glyph's <b>baseline-anchored</b> box — <c>PdfTextChar.X/Y/Width/Height</c>, where
        /// <c>Y</c> is the baseline origin and <c>Height</c> the font size — to the pixel rectangle
        /// covering its ink.
        /// </summary>
        /// <remarks>
        /// Passing those four numbers straight to <see cref="MapRect"/> describes
        /// [baseline, baseline + size]: a box entirely <i>above</i> the baseline with no room for a
        /// descender. Selection and find highlights are drawn from this rectangle and hit-tested by
        /// its centre, so a box that does not cover the ink both draws short of the glyphs and, for
        /// a horizontal sweep, matches the wrong line. It lives here beside <see cref="MapRect"/>
        /// rather than in the control so the control and its tests share one definition of where a
        /// glyph is.
        /// </remarks>
        public static RectangleF MapGlyph(in PageGeometry geo, float scale,
            float x, float baselineY, float width, float fontSize)
            => MapRect(geo, scale, x, baselineY - GlyphDescentFraction * fontSize,
                       width, GlyphBoxHeightFraction * fontSize);

        /// <summary>
        /// Maps the PDF rectangle (<paramref name="pdfX"/>,<paramref name="pdfY"/>,
        /// <paramref name="pdfW"/>,<paramref name="pdfH"/>) into rendered-bitmap pixels.
        /// </summary>
        public static RectangleF MapRect(in PageGeometry geo, float scale,
            float pdfX, float pdfY, float pdfW, float pdfH)
        {
            // Pixels per point to pixels per user-space unit, the unit the rectangle is in.
            scale *= geo.UserUnit;

            // Un-rotated raw-bitmap dimensions in pixels (before the /Rotate is applied).
            float rawW = geo.WidthPts * scale;
            float rawH = geo.HeightPts * scale;

            // Shift by crop origin, scale, and flip Y to raw pixel rect (top-left, y-down).
            float ux = pdfX - geo.CropX0;
            float uy = pdfY - geo.CropY0;
            float rawLeft = ux * scale;
            float rawRight = (ux + pdfW) * scale;
            float rawTop = rawH - (uy + pdfH) * scale;
            float rawBottom = rawH - uy * scale;

            // Map the four corners through the rotation, then take the bounding box.
            MapRaw(geo.Rotate, rawW, rawH, rawLeft, rawTop, out float ax, out float ay);
            MapRaw(geo.Rotate, rawW, rawH, rawRight, rawTop, out float bx, out float by);
            MapRaw(geo.Rotate, rawW, rawH, rawLeft, rawBottom, out float cx, out float cy);
            MapRaw(geo.Rotate, rawW, rawH, rawRight, rawBottom, out float dx, out float dy);

            float minX = Min4(ax, bx, cx, dx);
            float maxX = Max4(ax, bx, cx, dx);
            float minY = Min4(ay, by, cy, dy);
            float maxY = Max4(ay, by, cy, dy);

            return new RectangleF(minX, minY, Math.Max(maxX - minX, 0f), Math.Max(maxY - minY, 0f));
        }

        /// <summary>
        /// Maps a point from raw (un-rotated) bitmap pixels to the rotated bitmap's
        /// pixel space. Derived directly from the renderer's
        /// <c>Translate(out/2) · RotateDegrees(r) · Translate(-raw/2)</c> composition.
        /// </summary>
        private static void MapRaw(int rotate, float rawW, float rawH, float rx, float ry,
            out float ox, out float oy)
        {
            switch (rotate)
            {
                case 90:  ox = rawH - ry; oy = rx;        break;
                case 180: ox = rawW - rx; oy = rawH - ry; break;
                case 270: ox = ry;        oy = rawW - rx; break;
                default:  ox = rx;        oy = ry;        break;
            }
        }

        /// <summary>
        /// The inverse of <see cref="MapRect"/> for one point: a pixel inside the rendered page
        /// bitmap (origin top-left, y-down) to PDF user space (origin bottom-left, absolute).
        /// </summary>
        public static PointF MapPointToPdf(in PageGeometry geo, float scale, float px, float py)
        {
            scale *= geo.UserUnit;
            float rawW = geo.WidthPts * scale;
            float rawH = geo.HeightPts * scale;
            float rx, ry;
            switch (geo.Rotate)
            {
                case 90:  rx = py;        ry = rawH - px; break;
                case 180: rx = rawW - px; ry = rawH - py; break;
                case 270: rx = rawW - py; ry = px;        break;
                default:  rx = px;        ry = py;        break;
            }
            return new PointF(geo.CropX0 + rx / scale, geo.CropY0 + (rawH - ry) / scale);
        }

        private static float Min4(float a, float b, float c, float d)
            => Math.Min(Math.Min(a, b), Math.Min(c, d));

        private static float Max4(float a, float b, float c, float d)
            => Math.Max(Math.Max(a, b), Math.Max(c, d));
    }
}
