// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Windows.Forms;
using Xunit;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// F3 / Shift+F3 must work <b>while the find box has the focus</b> — "type a term, press F3" is
/// what the find bar is for, and the term is necessarily typed into the focused box.
/// </summary>
/// <remarks>
/// <para><c>ProcessCmdKey</c> returned early whenever <c>IsTextEntryFocused()</c>, which is right
/// for the shortcuts it was written for (Ctrl+C was copying the page selection over what the user
/// was typing; Ctrl+O discarded a half-typed term) and took F3 with it as collateral — a behaviour
/// change no commit message named, with the viewer README still advertising the key. GAPS.md §16
/// item 7. The fix moves the two find keys ahead of the guard rather than removing it.</para>
/// <para><b>These test <c>ClassifyCmdKey</c> rather than <c>ProcessCmdKey</c>, and that is the
/// second attempt.</b> The first drove the real thing: a parented, shown <see cref="Form"/> on an
/// STA thread, asserting <c>IsTextEntryFocused()</c> was genuinely true before pressing the key.
/// It worked, and it also <b>wedged the test host</b> — a shown form on a thread with no message
/// pump blocks unpredictably, and once one test is abandoned still holding a live window every
/// later test in the assembly inherits it. Observed twice: one run past ten minutes, one at nine,
/// then reliably on a full-solution run. A bounded join turned the hang into a loud failure, which
/// is better than a silent one, but a test that wedges is not acceptable at any volume.</para>
/// <para>So the <i>policy</i> was extracted from the <i>mechanism</i>: <c>ClassifyCmdKey</c> is a
/// pure function of (key, find-bar visible, text-entry focused, document loaded), and
/// <c>ProcessCmdKey</c> is a switch over its result. The focus rule is now reachable with no
/// window, no STA thread and no timing. **What is no longer covered is the dispatch itself** — that
/// each <c>CmdKeyAction</c> calls the matching method — which is why <c>ProcessCmdKey</c> is kept
/// to a bare switch with a comment saying so.</para>
/// </remarks>
public sealed class FindShortcutFocusTests
{
    private const bool Focused = true;
    private const bool NotFocused = false;
    private const bool BarOpen = true;
    private const bool BarClosed = false;
    private const bool DocLoaded = true;

    // The expected values are internal, so they live in method bodies rather than in [InlineData]:
    // an internal type cannot appear in a public test method's signature, and xUnit only discovers
    // public classes.

    /// <summary>The fix: the two find keys are classified <b>before</b> the focus guard.</summary>
    [Fact]
    public void FindKeys_WithTheFindBoxFocused_StillStepTheSearch()
    {
        Assert.Equal(PdfViewerControl.CmdKeyAction.FindNext,
            PdfViewerControl.ClassifyCmdKey(Keys.F3, BarOpen, Focused, DocLoaded));

        Assert.Equal(PdfViewerControl.CmdKeyAction.FindPrevious,
            PdfViewerControl.ClassifyCmdKey(Keys.Shift | Keys.F3, BarOpen, Focused, DocLoaded));
    }

    /// <summary>
    /// The guard is unchanged: every other document-level shortcut still yields to the text field.
    /// </summary>
    /// <remarks>
    /// Without this the fix could have been "delete the guard", which passes the test above and
    /// reintroduces both defects it was written for.
    /// </remarks>
    [Theory]
    [InlineData(Keys.Control | Keys.C)]
    [InlineData(Keys.Control | Keys.O)]
    [InlineData(Keys.Control | Keys.F)]
    [InlineData(Keys.Control | Keys.P)]
    [InlineData(Keys.Control | Keys.Add)]
    [InlineData(Keys.PageDown)]
    public void OtherShortcuts_WithATextFieldFocused_AreSuppressed(Keys key)
    {
        Assert.Equal(PdfViewerControl.CmdKeyAction.None,
            PdfViewerControl.ClassifyCmdKey(key, BarOpen, Focused, DocLoaded));
    }

    /// <summary>...and are still live when nothing is focused, or the guard would be a deletion.</summary>
    [Fact]
    public void OtherShortcuts_WithNothingFocused_Apply()
    {
        Assert.Equal(PdfViewerControl.CmdKeyAction.Copy,
            PdfViewerControl.ClassifyCmdKey(Keys.Control | Keys.C, BarClosed, NotFocused, DocLoaded));

        Assert.Equal(PdfViewerControl.CmdKeyAction.OpenFile,
            PdfViewerControl.ClassifyCmdKey(Keys.Control | Keys.O, BarClosed, NotFocused, DocLoaded));

        Assert.Equal(PdfViewerControl.CmdKeyAction.NextPage,
            PdfViewerControl.ClassifyCmdKey(Keys.PageDown, BarClosed, NotFocused, DocLoaded));
    }

    /// <summary>
    /// F3 does nothing when the find bar is closed — the exception is scoped to the bar being open,
    /// not to the key.
    /// </summary>
    [Fact]
    public void FindKeys_WithTheBarClosed_DoNothing()
    {
        Assert.Equal(PdfViewerControl.CmdKeyAction.None,
            PdfViewerControl.ClassifyCmdKey(Keys.F3, BarClosed, NotFocused, DocLoaded));
    }

    /// <summary>
    /// Escape is deliberately <b>not</b> in the pre-guard exception: from inside the find box
    /// <c>FindBox_KeyDown</c> closes the bar, and classifying it here too would close it twice.
    /// </summary>
    [Fact]
    public void Escape_WithTheFindBoxFocused_IsLeftToTheFindBox()
    {
        Assert.Equal(PdfViewerControl.CmdKeyAction.None,
            PdfViewerControl.ClassifyCmdKey(Keys.Escape, BarOpen, Focused, DocLoaded));

        // ...but it does close the bar when the focus is on the page.
        Assert.Equal(PdfViewerControl.CmdKeyAction.HideFindBar,
            PdfViewerControl.ClassifyCmdKey(Keys.Escape, BarOpen, NotFocused, DocLoaded));
    }

    /// <summary>Page keys need a document; the rest do not.</summary>
    [Fact]
    public void PageKeys_WithNoDocument_DoNothing()
    {
        Assert.Equal(PdfViewerControl.CmdKeyAction.None,
            PdfViewerControl.ClassifyCmdKey(Keys.PageDown, BarClosed, NotFocused, documentLoaded: false));
    }
}
