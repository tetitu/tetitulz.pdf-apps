// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using Tetitulz.Pdf.Core.Document;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Immutable geometry of one PDF page in user space (points): the crop-box origin,
    /// the crop-box size, and the normalized /Rotate angle. This is everything the
    /// coordinate mapper needs to convert a PDF rectangle to a rendered-bitmap pixel
    /// rectangle, matching the renderer's box + rotation handling.
    /// </summary>
    internal readonly struct PageGeometry
    {
        /// <summary>Crop-box left edge in PDF points (the renderer shifts the origin here).</summary>
        public readonly float CropX0;
        /// <summary>Crop-box bottom edge in PDF points.</summary>
        public readonly float CropY0;
        /// <summary>Crop-box width in PDF points (un-rotated).</summary>
        public readonly float WidthPts;
        /// <summary>Crop-box height in PDF points (un-rotated).</summary>
        public readonly float HeightPts;
        /// <summary>Page /Rotate, normalized to 0, 90, 180 or 270.</summary>
        public readonly int Rotate;
        /// <summary>
        /// Points per user-space unit — the page's <c>/UserUnit</c> as the renderer resolved it.
        /// The box and every overlay position are in user-space units; the page is drawn this many
        /// times larger.
        /// </summary>
        public readonly float UserUnit;

        public PageGeometry(float cropX0, float cropY0, float widthPts, float heightPts, int rotate, float userUnit = 1f)
        {
            CropX0 = cropX0;
            CropY0 = cropY0;
            WidthPts = widthPts <= 0 ? 1f : widthPts;
            HeightPts = heightPts <= 0 ? 1f : heightPts;
            UserUnit = userUnit > 0f && !float.IsInfinity(userUnit) ? userUnit : 1f;
            // Core.Document.PdfPageRotation — the one reader, which both renderers use too: a viewer
            // quantising the rotation on its own would place hotspots for an upright page while the
            // renderer draws /Rotate 45 at 45 degrees.
            Rotate = PdfPageRotation.Normalize(rotate);
        }

        /// <summary>
        /// The page as a view turned clockwise by <paramref name="degrees"/> shows it: the box kept,
        /// the rotation added to the page's own — the mapping the renderer's bitmap turned as much
        /// follows.
        /// </summary>
        public PageGeometry Turned(int degrees)
            => degrees == 0 ? this : new PageGeometry(CropX0, CropY0, WidthPts, HeightPts, Rotate + degrees, UserUnit);

        // Through PdfPageRotation.SwapsAxes, the same reader Rotate above comes from.
        /// <summary>Displayed (post-rotation) width in points, <c>/UserUnit</c> applied.</summary>
        public float DisplayWidthPts => (PdfPageRotation.SwapsAxes(Rotate) ? HeightPts : WidthPts) * UserUnit;

        /// <summary>Displayed (post-rotation) height in points, <c>/UserUnit</c> applied.</summary>
        public float DisplayHeightPts => (PdfPageRotation.SwapsAxes(Rotate) ? WidthPts : HeightPts) * UserUnit;
    }
}
