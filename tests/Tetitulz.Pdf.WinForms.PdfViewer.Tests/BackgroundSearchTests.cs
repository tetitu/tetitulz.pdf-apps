// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The find box searches in the background once the control has a window: a keystroke returns at
/// once, the matches arrive page by page, and a newer term cancels the older one's search.
/// </summary>
public sealed class BackgroundSearchTests
{
    [Fact]
    public void ASearch_ReturnsAtOnce_AndItsMatchesArriveWhenSearched()
    {
        RunSta(() =>
        {
            using var viewer = ViewerWithWindow();
            viewer.LoadBytes(TestPdf.ManyTextPages(40, "QUOKKA"));

            viewer.Find("QUOKKA");

            Assert.True(viewer.SearchRunning);                           // the keystroke did not search 40 pages
            Assert.Equal(0, viewer.FindMatchCount);
            Assert.Equal(0, viewer.TextIndexBuilds);
            Assert.True(viewer.DrainSearch());
            Assert.Equal(40, viewer.FindMatchCount);
            Assert.Equal("QUOKKA", viewer.FindMatchText(39));
            Assert.Equal(40, viewer.CachedPageTextCount);                // kept: the next search reads them here
        });
    }

    [Fact]
    public void ANewerTerm_CancelsTheOlderSearch()
    {
        RunSta(() =>
        {
            using var viewer = ViewerWithWindow();
            viewer.LoadBytes(TestPdf.TwoPagesOfText());

            viewer.Find("PAGEONE");
            viewer.Find("PAGETWO");                                      // before the first was taken

            Assert.True(viewer.DrainSearch());
            Assert.Equal(1, viewer.FindMatchCount);
            Assert.Equal("PAGETWO", viewer.FindMatchText(0));
        });
    }

    [Fact]
    public void ASearchBeingRun_SaysSo_AndThenTheResult()
    {
        RunSta(() =>
        {
            using var viewer = ViewerWithWindow();
            viewer.LoadBytes(TestPdf.TwoPagesOfText());

            viewer.Find("PAGETWO");
            Assert.Equal("Searching…", FindResultText(viewer));
            Assert.True(viewer.DrainSearch());
            Assert.Equal("1 / 1", FindResultText(viewer));

            viewer.Find("NOWHERE");
            Assert.True(viewer.DrainSearch());
            Assert.Equal("Not found", FindResultText(viewer));
        });
    }

    [Fact]
    public void ClosingTheDocument_DropsTheSearchUnderWay()
    {
        RunSta(() =>
        {
            using var viewer = ViewerWithWindow();
            viewer.LoadBytes(TestPdf.ManyTextPages(40, "QUOKKA"));
            viewer.Find("QUOKKA");

            viewer.CloseDocument();

            Assert.False(viewer.SearchRunning);
            Application.DoEvents();
            System.Threading.Thread.Sleep(200);
            Application.DoEvents();
            Assert.Equal(0, viewer.FindMatchCount);
        });
    }

    private static string FindResultText(PdfViewerControl viewer)
    {
        foreach (Control c in viewer.FindBarPanel.Controls)
            if (c is Label label) return label.Text;
        throw new InvalidOperationException("the find bar has no result label");
    }

    /// <summary>A control with a window handle — what the background needs to post to — and never shown.</summary>
    private static PdfViewerControl ViewerWithWindow()
    {
        var viewer = new PdfViewerControl { Size = new Size(900, 700) };
        viewer.CreateControl();
        Assert.True(viewer.IsHandleCreated);
        return viewer;
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
