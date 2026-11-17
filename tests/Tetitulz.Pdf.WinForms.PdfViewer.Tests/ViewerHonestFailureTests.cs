// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Xunit;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The viewer's MEDIUM-severity scan findings: an empty result that could mean two things, the
/// keyboard shortcuts that ran over the focused text field, the event that did not fire for search
/// navigation and fired for navigation that did not move, and the thumbnail strip that was removed
/// without being disposed.
/// </summary>
public sealed class ViewerHonestFailureTests
{
    private static void RunSta(Action action) => StaThread.Run(action);

    // ================================================================ honest failure

    /// <summary>
    /// A document that genuinely has no links reports no ERROR — the slot must distinguish
    /// "none" from "extraction threw", not merely be non-null whenever the list is empty.
    /// </summary>
    [Fact]
    public void DocumentWithNoLinks_ReportsNoLinkError()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePage());

        Assert.Empty(src.LinksFor(0));
        Assert.Null(src.LinkExtractionError);
        Assert.Null(src.BookmarkExtractionError);
        Assert.Null(src.FirstExtractionError);
    }

    [Fact]
    public void DocumentWithLinks_StillReportsNoError()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePageWithUriLink("https://example.com/"));

        Assert.Single(src.LinksFor(0));
        Assert.Null(src.FirstExtractionError);
    }

    /// <summary>
    /// <see cref="PdfDocumentSource.FirstExtractionError"/> phrases whichever slot is set, so one
    /// status-bar site can surface all three.
    /// </summary>
    [Fact]
    public void FirstExtractionError_NamesTheLayerThatFailed()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePage());
        src.ReportPageTextError(4, "content stream would not decode");

        Assert.Contains("page 5", src.FirstExtractionError);
        Assert.Contains("Text layer unavailable", src.FirstExtractionError);
    }

    // ================================================================ shortcuts vs. focus

    /// <summary>
    /// With no text field focused, the document shortcuts apply.
    /// </summary>
    /// <remarks>
    /// <para><c>ProcessCmdKey</c> runs for the whole control before the focused child sees the key,
    /// so Ctrl+C in the find box copied the page's drag-selection over what the user believed they
    /// were copying, and Ctrl+O discarded a half-typed term to open a file dialog. The guard is
    /// this predicate.</para>
    /// <para><b>Only the negative case is asserted here.</b> Driving real keyboard focus needs a
    /// message loop and a parented form; a headless assertion that a child "has the focus" is the
    /// kind of test that passes for the wrong reason. The positive case belongs in an interactive
    /// harness, and is named here so its absence is deliberate rather than overlooked.</para>
    /// </remarks>
    [Fact]
    public void WithNothingFocused_ShortcutsAreNotSuppressed()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.OnePageWithText());

            Assert.False(ctrl.IsTextEntryFocused());
        });
    }

    // ================================================================ PageChanged symmetry

    /// <summary>
    /// Navigating to the page already displayed raises no <c>PageChanged</c>.
    /// </summary>
    /// <remarks>
    /// The Continuous branch of <c>GoToPage</c> raised it unconditionally — so clicking a bookmark
    /// that targets the current page fired a spurious event with an unchanged index — while
    /// <c>ScrollToMatch</c>, which really did move the reader, raised none at all.
    /// </remarks>
    [Fact]
    public void GoToPage_ToTheSamePage_DoesNotRaisePageChanged()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(5));

            var raised = new List<int>();
            ctrl.PageChanged += (s, e) => raised.Add(e.PageIndex);

            ctrl.GoToPage(0);                       // already there

            Assert.Empty(raised);
        });
    }

    [Fact]
    public void GoToPage_ToADifferentPage_RaisesPageChangedOnce()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(5));

            var raised = new List<int>();
            ctrl.PageChanged += (s, e) => raised.Add(e.PageIndex);

            ctrl.GoToPage(3);

            Assert.Equal(new[] { 3 }, raised);
        });
    }

    // ================================================================ thumbnail lifetime

    /// <summary>
    /// Reloading disposes the previous document's thumbnail items.
    /// </summary>
    /// <remarks>
    /// <c>Controls.Clear()</c> removes without disposing, and each item owns a realised window
    /// handle plus a cached <see cref="Bitmap"/> released only by its <c>Dispose</c> override — so
    /// opening documents in sequence leaked both, per page, per load. Reading
    /// <see cref="Control.IsDisposed"/> on a captured item is the guard's own evidence.
    /// </remarks>
    [Fact]
    public void ReloadingADocument_DisposesTheOldThumbnailItems()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(4));

            var first = new List<System.Windows.Forms.Control>();
            foreach (System.Windows.Forms.Control c in ctrl.ThumbnailItems) first.Add(c);
            Assert.Equal(4, first.Count);

            ctrl.LoadBytes(TestPdf.ManyPages(3));

            Assert.All(first, c => Assert.True(c.IsDisposed,
                "a thumbnail item from the previous document was removed without being disposed"));
            Assert.Equal(3, ctrl.ThumbnailItemCount);
        });
    }

    [Fact]
    public void ClosingADocument_DisposesTheThumbnailItems()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(4));

            var items = new List<System.Windows.Forms.Control>();
            foreach (System.Windows.Forms.Control c in ctrl.ThumbnailItems) items.Add(c);

            ctrl.CloseDocument();

            Assert.All(items, c => Assert.True(c.IsDisposed));
            Assert.Equal(0, ctrl.ThumbnailItemCount);
        });
    }

    // ================================================================ text-index bound

    /// <summary>
    /// The per-glyph text index is bounded the way the bitmap cache is.
    /// </summary>
    /// <remarks>
    /// One keystroke in the find box walks every page, so an unbounded cache turned a long
    /// text-heavy document into hundreds of megabytes that nothing released until it closed.
    /// </remarks>
    [Fact]
    public void TextIndex_IsBoundedAcrossManyPages()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages((PdfViewerControl.MaxCachedTextPages + 8)));

            for (int p = 0; p < (PdfViewerControl.MaxCachedTextPages + 8); p++)
                ctrl.TouchTextIndex(p);

            Assert.True(ctrl.TextIndexCacheCount <= PdfViewerControl.MaxCachedTextPages,
                $"the text index held {ctrl.TextIndexCacheCount} pages");
        });
    }

    /// <summary>
    /// A document with no page is refused with the library's own type, as any other file the viewer cannot
    /// show: it was an <see cref="InvalidOperationException"/>, which a host catching
    /// <see cref="Tetitulz.Pdf.Core.PdfException"/> for "this file cannot be shown" missed. The control: a
    /// one-page document loads.
    /// </summary>
    [Fact]
    public void ADocumentWithNoPage_IsRefusedAsAPdfException()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            var ex = Assert.ThrowsAny<Tetitulz.Pdf.Core.PdfException>(() => ctrl.LoadBytes(TestPdf.ManyPages(0)));
            Assert.Contains("no renderable pages", ex.Message, StringComparison.Ordinal);

            ctrl.LoadBytes(TestPdf.OnePage());
            Assert.Equal(1, ctrl.PageCount);
        });
    }

    /// <summary>
    /// A second search over more pages than the index keeps rebuilds none of them, and still finds
    /// every page's word: the search reads the page texts, kept after their glyph indexes go. Before
    /// it, the cap made every keystroke re-index each page past it (Wave 22, SmallPlugins+Viewer-1:3);
    /// the bound itself is the test above.
    /// </summary>
    [Fact]
    public void ASecondSearch_RebuildsNoIndex_AndFindsEveryPage()
    {
        RunSta(() =>
        {
            int pages = PdfViewerControl.MaxCachedTextPages + 8;
            using var ctrl = new PdfViewerControl();
            ctrl.Size = new Size(900, 700);
            ctrl.LoadBytes(TestPdf.ManyTextPages(pages, "QUOKKA"));

            ctrl.Find("QUOKKA");
            Assert.True(ctrl.DrainSearch());
            Assert.Equal(pages, ctrl.FindMatchCount);
            int afterFirst = ctrl.TextIndexBuilds;
            Assert.Equal(pages, ctrl.CachedPageTextCount);                // every page's text was kept
            Assert.True(ctrl.TextIndexCacheCount <= PdfViewerControl.MaxCachedTextPages);

            ctrl.Find("QUOK");
            Assert.True(ctrl.DrainSearch());
            Assert.Equal(pages, ctrl.FindMatchCount);
            // The match it scrolls to may need its page's glyphs again; nothing else may.
            Assert.InRange(ctrl.TextIndexBuilds - afterFirst, 0, 1);
        });
    }
}
