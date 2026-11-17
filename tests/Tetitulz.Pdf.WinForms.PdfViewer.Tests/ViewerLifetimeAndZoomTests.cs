// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Reflection;
using System.Threading;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// What the control still holds after <see cref="IDisposable.Dispose"/>, and what its public
/// <see cref="PdfViewerControl.Zoom"/> setter accepts.
/// </summary>
public sealed class ViewerLifetimeAndZoomTests
{
    /// <summary>
    /// Dispose released the bitmap cache and nothing else, so the decrypted document bytes, the
    /// per-glyph text index of every page searched and the last selection stayed reachable from a
    /// disposed control for as long as the host kept the reference.
    /// </summary>
    [Fact]
    public void Dispose_ReleasesTheDocumentAndThePerPageCaches()
    {
        RunSta(() =>
        {
            var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.OnePageWithText());
            ctrl.Find("Hello");                       // populates the text index and the match list

            // Preconditions — without these the assertions after Dispose would be vacuous.
            Assert.True(ctrl.HasDocument);
            Assert.Equal(1, ctrl.PageCount);
            Assert.Equal(1, CollectionFieldCount(ctrl, "_textCache"));
            Assert.Equal(1, CollectionFieldCount(ctrl, "_findMatches"));

            ctrl.Dispose();

            Assert.False(ctrl.HasDocument);           // the decrypted byte[] is no longer held
            Assert.Equal(0, ctrl.PageCount);
            Assert.Equal(0, CollectionFieldCount(ctrl, "_textCache"));
            Assert.Equal(0, CollectionFieldCount(ctrl, "_findMatches"));
        });
    }

    /// <summary>
    /// NaN is not clamped by <c>Math.Min</c>/<c>Math.Max</c> — it propagates — so it used to be
    /// stored, driving every page to the minimum render dpi while the toolbar read "NaN%". The
    /// setter must refuse a non-finite value instead of half-accepting it.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Zoom_RefusesNonFiniteInput_AndKeepsItsPreviousValue(double bad)
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.OnePageWithText());

            Assert.Throws<ArgumentOutOfRangeException>(() => ctrl.Zoom = bad);
            Assert.Equal(1.0, ctrl.Zoom);
        });
    }

    /// <summary>Refusing non-finite input must not have disturbed ordinary clamping.</summary>
    [Fact]
    public void Zoom_StillClampsFiniteInputToTheSupportedRange()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.OnePageWithText());

            ctrl.Zoom = 2.0;
            Assert.Equal(2.0, ctrl.Zoom);

            ctrl.Zoom = 99.0;                 // above MaxZoom
            Assert.Equal(8.0, ctrl.Zoom);

            ctrl.Zoom = -5.0;                 // below MinZoom
            Assert.Equal(0.1, ctrl.Zoom, 6);
        });
    }

    // ----------------------------------------------------------------

    /// <summary>
    /// Reads a private collection field's size. Used rather than a production test seam because
    /// the caches are an implementation detail with no reason to be visible; the alternative is
    /// leaving two of the lines under test unasserted. A rename fails this loudly.
    /// </summary>
    private static int CollectionFieldCount(PdfViewerControl ctrl, string fieldName)
    {
        FieldInfo? f = typeof(PdfViewerControl).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(f);
        var value = f!.GetValue(ctrl) as ICollection;
        Assert.NotNull(value);
        return value!.Count;
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
