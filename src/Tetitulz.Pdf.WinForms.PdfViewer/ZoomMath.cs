// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using Tetitulz.Pdf.Core.Constants;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Pure zoom-fit arithmetic, factored out of the control so it can be unit-tested.
    /// 100% zoom maps one PDF point to <see cref="BaseDpi"/> / <see cref="PdfUnits.PointsPerInch"/>
    /// device pixels.
    /// </summary>
    internal static class ZoomMath
    {
        /// <summary>
        /// Device dots per inch at 100% zoom. This is the CSS reference resolution, so the
        /// viewer's "100%" means the same thing a browser's does.
        /// </summary>
        public const double BaseDpi = PdfUnits.CssPixelsPerInch;

        /// <summary>Zoom factor that fits <paramref name="displayWidthPts"/> into <paramref name="availPx"/> pixels.</summary>
        public static double FitWidth(double availPx, double displayWidthPts)
            => displayWidthPts <= 0 || availPx <= 0
                ? 1.0
                : availPx * PdfUnits.PointsPerInch / (BaseDpi * displayWidthPts);

        /// <summary>Zoom factor that fits the whole page (width and height) into the viewport.</summary>
        public static double FitPage(double availWpx, double availHpx, double dispWpts, double dispHpts)
            => Math.Min(FitWidth(availWpx, dispWpts), FitWidth(availHpx, dispHpts));

        /// <summary>
        /// The whole-number dpi at which a page of the given displayed size renders just large
        /// enough to fill a <paramref name="boxWpx"/> x <paramref name="boxHpx"/> box — at least 1.
        /// </summary>
        /// <remarks>
        /// A thumbnail rendered at one fixed dpi is as large as the page makes it: a page the main
        /// view refuses as too large would render at the renderer's 100 MP ceiling, 400 MB, to be
        /// shrunk to 140 pixels. From the page's own size, the raw render is about the box's
        /// whatever the page.
        /// </remarks>
        public static int DpiToFill(int boxWpx, int boxHpx, double dispWpts, double dispHpts)
        {
            if (boxWpx <= 0 || boxHpx <= 0 || !(dispWpts > 0) || !(dispHpts > 0)) return 1;
            double dpi = PdfUnits.PointsPerInch * Math.Min(boxWpx / dispWpts, boxHpx / dispHpts);
            return Math.Max(1, SaturatingCast.ToInt(Math.Ceiling(dpi)));
        }

        /// <summary>
        /// Where the viewport's centre sits along one axis of the canvas, as a fraction in [0, 1].
        /// </summary>
        /// <param name="scrollOffset">Scrolled distance from the canvas origin, in pixels
        /// (i.e. <c>-AutoScrollPosition</c>, which WinForms reports negated).</param>
        /// <param name="viewportExtent">Visible extent along this axis, in pixels.</param>
        /// <param name="canvasExtent">Total canvas extent along this axis, in pixels.</param>
        /// <remarks>
        /// This pair lets the zoom path say "keep me where I am": a constant fraction of <c>0.5</c>
        /// would mean, in the Continuous layout, the middle of the entire document.
        /// </remarks>
        public static double CenterFraction(int scrollOffset, int viewportExtent, int canvasExtent)
            => AnchorFraction(scrollOffset, viewportExtent / 2.0, canvasExtent);

        /// <summary>
        /// The scroll offset that puts <paramref name="fraction"/> of the canvas back at the centre
        /// of the viewport, clamped to the scrollable range.
        /// </summary>
        public static int ScrollForCenterFraction(double fraction, int viewportExtent, int canvasExtent)
            => ScrollForAnchorFraction(fraction, viewportExtent / 2.0, viewportExtent, canvasExtent);

        /// <summary>
        /// Where a point <paramref name="anchorPx"/> pixels into the viewport sits along one axis of
        /// the canvas, as a fraction — <see cref="CenterFraction"/> for any point, not just the middle.
        /// </summary>
        /// <remarks>
        /// Ctrl+wheel zoom anchors at the cursor, the way every document viewer and browser does, so the
        /// thing being pointed at stays under the pointer; anchored at the viewport centre, zooming
        /// towards a corner would push what is under the cursor away from it.
        /// </remarks>
        public static double AnchorFraction(int scrollOffset, double anchorPx, int canvasExtent)
            => canvasExtent <= 0 ? 0.0 : (scrollOffset + anchorPx) / canvasExtent;

        /// <summary>
        /// The scroll offset that puts <paramref name="fraction"/> of the canvas back
        /// <paramref name="anchorPx"/> pixels into the viewport, clamped to the scrollable range.
        /// </summary>
        public static int ScrollForAnchorFraction(double fraction, double anchorPx, int viewportExtent, int canvasExtent)
        {
            if (canvasExtent <= 0) return 0;
            double target = fraction * canvasExtent - anchorPx;
            double max = Math.Max(0, canvasExtent - viewportExtent);
            if (target < 0) target = 0;
            if (target > max) target = max;
            return SaturatingCast.ToInt(Math.Round(target));
        }

        /// <summary>
        /// The scroll offset that keeps the viewport centred on the same <b>fraction</b> of the
        /// canvas after a zoom has resized it — <see cref="CenterFraction"/> and
        /// <see cref="ScrollForCenterFraction"/> in one call.
        /// </summary>
        /// <remarks>
        /// A fraction hard-coded to 0.5 (<c>newCanvas / 2 - viewport / 2</c>) would, in Continuous
        /// layout where the canvas is the whole document, jump every Zoom In, Zoom Out, Actual Size
        /// and zoom-combo change to the middle page — zooming while reading page 3 of 200 would land
        /// on page 100.
        /// <para>It is a composition rather than a second implementation on purpose: the control
        /// reads the fraction before <c>RebuildLayout</c> and applies it after, so it needs the two
        /// halves separately and this one cannot be the only entry point.</para>
        /// </remarks>
        /// <param name="oldOffset">Scroll offset before the zoom, as a positive distance from the top/left.</param>
        /// <param name="oldCanvas">Canvas extent before the zoom.</param>
        /// <param name="viewport">Visible extent (unchanged by the zoom).</param>
        /// <param name="newCanvas">Canvas extent after the zoom.</param>
        public static int AnchorScroll(int oldOffset, int oldCanvas, int viewport, int newCanvas)
            => ScrollForCenterFraction(CenterFraction(oldOffset, viewport, oldCanvas), viewport, newCanvas);
    }
}
