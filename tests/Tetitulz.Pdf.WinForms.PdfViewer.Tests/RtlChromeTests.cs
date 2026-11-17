// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Right-to-left <b>chrome</b>: the mirror of the toolbar, sidebar, status line and find bar that
/// <see cref="Control.RightToLeft"/> asks for. Text selection and extraction order are a separate
/// concern and are covered by <c>RtlSelectionTextTests</c>.
/// </summary>
/// <remarks>
/// <para><b>Every case asserts a measured position or a real property value in <i>both</i>
/// directions, and the two expectations always differ.</b> A chrome test that asserted only the
/// right-to-left half would pass with the mirror deleted whenever the left-to-right value happened
/// to coincide, which is the "assertion that cannot fail" this repo bans.</para>
///
/// <para><b>What is deliberately not tested here.</b> WinForms mirrors a
/// <see cref="SplitContainer"/>, a <see cref="ToolStrip"/>'s item order, a
/// <see cref="StatusStrip"/>'s item placement and a <see cref="FlowLayoutPanel"/>'s flow by
/// itself; those are framework behaviour, not this control's. They appear below only where an
/// assertion about them would catch <i>us</i> pinning something to left-to-right — the splitter
/// and the toolbar — and never as a bare restatement of the framework's contract.</para>
///
/// <para><b>No case needs a window, a handle or a message loop.</b> WinForms runs its real
/// docking, splitter-mirroring and ToolStrip layout from <see cref="Control.PerformLayout"/>
/// alone — measured: with no handle ever created, a <c>Dock=Fill</c> SplitContainer in a 700px
/// host reports <c>Panel1.Left = 0</c> left-to-right and <c>500</c> right-to-left, and a
/// ToolStrip's first item moves from x=7 to x=669. An earlier draft called
/// <c>Control.CreateControl()</c> to force that and it <b>crashed the test host</b>, so realising
/// a handle here is not just unnecessary, it is actively wrong. The cases still run on an STA
/// thread, like the rest of this assembly.</para>
/// </remarks>
public sealed class RtlChromeTests
{
    private const string GlyphLeft = "◀";    // ◀
    private const string GlyphRight = "▶";   // ▶

    // ── The sidebar: mirrored by the framework, so it must NOT be reparented ──────────────

    /// <summary>
    /// The tempting "fix" for a left-pinned sidebar is to move it into <c>Panel2</c>. That would
    /// be wrong twice: a <see cref="SplitContainer"/> already mirrors itself, and
    /// <c>ShowSidebar</c> is written in terms of <c>Panel1Collapsed</c>, so after the swap
    /// hiding the sidebar would hide the page canvas instead.
    /// </summary>
    [Fact]
    public void SidebarLivesInPanel1_SoShowSidebarStillHidesTheSidebarWhenMirrored()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();

            foreach (var direction in new[] { RightToLeft.No, RightToLeft.Yes })
            {
                ctrl.RightToLeft = direction;

                Assert.Same(ctrl.SidebarSplit.Panel1, ctrl.SidebarTabs.Parent);
                Assert.Same(ctrl.SidebarSplit.Panel2, ctrl.PageSurface.Parent);

                ctrl.ShowSidebar = false;
                Assert.True(ctrl.SidebarSplit.Panel1Collapsed, $"sidebar panel not collapsed ({direction})");
                Assert.False(ctrl.SidebarSplit.Panel2Collapsed, $"page canvas collapsed instead ({direction})");
                Assert.False(ctrl.ShowSidebar);

                ctrl.ShowSidebar = true;
                Assert.False(ctrl.SidebarSplit.Panel1Collapsed, $"sidebar panel stayed collapsed ({direction})");
                Assert.True(ctrl.ShowSidebar);
            }
        });
    }

    /// <summary>
    /// The measured consequence of the decision above: the sidebar really does move to the right,
    /// and it does so because the splitter was left to inherit the direction.
    /// </summary>
    [Fact]
    public void SplitterPutsTheSidebarOnTheRight_UnderRightToLeftChrome()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Width = 800, Height = 600 };
            ctrl.PerformLayout();

            var split = ctrl.SidebarSplit;
            split.SplitterDistance = 200;
            ctrl.PerformLayout();

            // Measured on the SIDEBAR, through its own parent panel, rather than on Panel1
            // directly: "Panel1 moved right" would still hold after someone reparented the
            // sidebar into Panel2, which is the change this case exists to refuse.
            int sidebarLeftLtr = ctrl.SidebarTabs.Parent!.Left;
            int canvasLeftLtr = ctrl.PageSurface.Parent!.Left;
            Assert.True(sidebarLeftLtr < canvasLeftLtr,
                $"left-to-right: sidebar at {sidebarLeftLtr} should precede the canvas at {canvasLeftLtr}");

            ctrl.RightToLeft = RightToLeft.Yes;
            ctrl.PerformLayout();

            int sidebarLeftRtl = ctrl.SidebarTabs.Parent!.Left;
            int canvasLeftRtl = ctrl.PageSurface.Parent!.Left;
            Assert.True(sidebarLeftRtl > canvasLeftRtl,
                $"right-to-left: sidebar at {sidebarLeftRtl} should follow the canvas at {canvasLeftRtl}");

            // The splitter still sizes Panel1: the sidebar keeps its width, it only changes sides.
            Assert.Equal(200, ctrl.SidebarTabs.Parent!.Width);
        });
    }

    // ── RightToLeftLayout: a separate property that RightToLeft does not imply ────────────

    /// <summary>
    /// <see cref="TreeView.RightToLeftLayout"/> and <see cref="TabControl.RightToLeftLayout"/> are
    /// what actually move the indentation, expander glyphs and tab strip; setting
    /// <see cref="Control.RightToLeft"/> alone leaves a Hebrew bookmark tree indenting from the
    /// left.
    /// </summary>
    [Fact]
    public void BookmarkTreeAndSidebarTabsGetRightToLeftLayout_NotJustRightToLeft()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();

            Assert.False(ctrl.BookmarkTree.RightToLeftLayout);
            Assert.False(ctrl.SidebarTabs.RightToLeftLayout);

            ctrl.RightToLeft = RightToLeft.Yes;
            Assert.True(ctrl.BookmarkTree.RightToLeftLayout);
            Assert.True(ctrl.SidebarTabs.RightToLeftLayout);
            Assert.Equal(RightToLeft.Yes, ctrl.BookmarkTree.RightToLeft);

            ctrl.RightToLeft = RightToLeft.No;
            Assert.False(ctrl.BookmarkTree.RightToLeftLayout);
            Assert.False(ctrl.SidebarTabs.RightToLeftLayout);
        });
    }

    // ── Toolbar glyphs ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A <see cref="ToolStrip"/> reverses item <i>order</i> under right-to-left and never rewrites
    /// an item's text, so "previous page" ends up on the right still drawing a left-pointing
    /// arrow. The glyph has to be flipped by hand.
    /// </summary>
    [Fact]
    public void PageNavigationGlyphsFlip_ForTheMirroredToolbar()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();

            Assert.Equal(GlyphLeft, ctrl.PreviousPageButton.Text);
            Assert.Equal(GlyphRight, ctrl.NextPageButton.Text);

            ctrl.RightToLeft = RightToLeft.Yes;
            Assert.Equal(GlyphRight, ctrl.PreviousPageButton.Text);
            Assert.Equal(GlyphLeft, ctrl.NextPageButton.Text);

            ctrl.RightToLeft = RightToLeft.No;
            Assert.Equal(GlyphLeft, ctrl.PreviousPageButton.Text);
            Assert.Equal(GlyphRight, ctrl.NextPageButton.Text);
        });
    }

    /// <summary>
    /// The toolbar itself is left to inherit the direction — asserted because pinning it to
    /// left-to-right (as the page canvas deliberately is) would leave "Open" on the far left of a
    /// mirrored window with the flipped glyphs beside it, which is worse than not mirroring at all.
    /// </summary>
    [Fact]
    public void ToolbarItemOrderReverses_SoTheFlippedGlyphsSitOnTheCorrectSide()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Width = 900, Height = 600 };
            ctrl.PerformLayout();

            int prevLtr = ctrl.PreviousPageButton.Bounds.X;
            int nextLtr = ctrl.NextPageButton.Bounds.X;
            Assert.True(prevLtr < nextLtr,
                $"left-to-right: previous at {prevLtr} should precede next at {nextLtr}");

            ctrl.RightToLeft = RightToLeft.Yes;
            ctrl.PerformLayout();

            int prevRtl = ctrl.PreviousPageButton.Bounds.X;
            int nextRtl = ctrl.NextPageButton.Bounds.X;
            Assert.True(prevRtl > nextRtl,
                $"right-to-left: previous at {prevRtl} should follow next at {nextRtl}");
        });
    }

    // ── Status line ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The status label springs to the full width, so where the strip <i>places</i> it is not the
    /// question — its own <c>TextAlign</c> is the only thing that decides which edge the text
    /// starts at.
    /// </summary>
    [Fact]
    public void StatusLabelTextAlignsToTheLeadingEdge()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();

            Assert.Equal(ContentAlignment.MiddleLeft, ctrl.StatusLabel.TextAlign);

            ctrl.RightToLeft = RightToLeft.Yes;
            Assert.Equal(ContentAlignment.MiddleRight, ctrl.StatusLabel.TextAlign);

            ctrl.RightToLeft = RightToLeft.No;
            Assert.Equal(ContentAlignment.MiddleLeft, ctrl.StatusLabel.TextAlign);
        });
    }

    // ── Find bar: absolutely positioned, so nothing mirrors it for us ─────────────────────

    // At 96 dpi, the card's leading-edge offsets: the search box's frame at 8 and 240 wide, its text
    // after a 32-pixel icon, the count 8 after the box, each 32-pixel button 8 after the count (2
    // between the two steps), a 13-pixel divider before close, 8 of padding at the trailing end.
    private const int BoxText = 8 + 32, CountLead = 8 + 240 + 8;

    /// <summary>
    /// The card floats over the pages' trailing top corner — the right in left-to-right — 12 pixels
    /// in from the page area's edge and clear of its scroll bar, its items at their leading-edge
    /// offsets. This is the control for the mirroring case below: without it, "the card moved" and
    /// "the card mirrors" look the same.
    /// </summary>
    [Fact]
    public void FindCard_LeftToRight_SitsAtTheTopRightOfThePages_ItsItemsFromTheLeft()
    {
        RunSta(() =>
        {
            using var ctrl = ShownFindBar();

            var card = ctrl.FindBarPanel;
            var surface = ctrl.PageSurface;
            Assert.Same(surface.Parent, card.Parent);                     // over the pages, not docked below them
            Assert.Equal(surface.Top + 12, card.Top);
            Assert.Equal(surface.Left + surface.ClientSize.Width - 12, card.Right);

            var (prev, next, close) = Buttons(ctrl);
            Label count = card.Controls.OfType<Label>().Single();
            Assert.Equal(BoxText, ctrl.FindTextBox.Left);
            Assert.Equal(CountLead, count.Left);
            Assert.Equal(CountLead + count.Width + 8, prev.Left);
            Assert.Equal(prev.Left + 32 + 2, next.Left);
            Assert.Equal(next.Left + 32 + 13, close.Left);
            Assert.Equal(card.Width - 8 - 32, close.Left);
        });
    }

    /// <summary>
    /// A plain <see cref="Panel"/> mirrors nothing: a child at <c>Location.X = 6</c> is still at 6
    /// after the panel is set to <see cref="RightToLeft.Yes"/>. Every position in the card is
    /// therefore computed, and each one is asserted against the width actually in force; the card
    /// itself moves to the left of the pages, the trailing corner when mirrored.
    /// </summary>
    [Fact]
    public void FindCard_Mirrored_SitsAtTheTopLeftOfThePages_ItsItemsFromTheRight()
    {
        RunSta(() =>
        {
            using var ctrl = ShownFindBar();
            ctrl.RightToLeft = RightToLeft.Yes;
            ctrl.PerformLayout();

            var card = ctrl.FindBarPanel;
            Assert.Equal(ctrl.PageSurface.Left + 12, card.Left);
            int w = card.Width;
            var (prev, next, close) = Buttons(ctrl);
            Label count = card.Controls.OfType<Label>().Single();

            // Same offsets, measured from the right edge instead of the left.
            Assert.Equal(w - BoxText - ctrl.FindTextBox.Width, ctrl.FindTextBox.Left);
            Assert.Equal(w - CountLead - count.Width, count.Left);
            Assert.Equal(w - (CountLead + count.Width + 8) - prev.Width, prev.Left);
            Assert.Equal(8, close.Left);

            // The order the eye reads: close is now the leftmost item and the query box the
            // rightmost, the reverse of the left-to-right case above.
            Assert.True(close.Left < prev.Left && prev.Left < ctrl.FindTextBox.Left,
                $"expected close({close.Left}) < prev({prev.Left}) < box({ctrl.FindTextBox.Left})");
        });
    }

    /// <summary>
    /// The count is <c>AutoSize</c>, so its width changes when a search writes one — and the buttons
    /// after it, the card's width and, when mirrored, the count's own X all follow from that width:
    /// a stale layout would put the count under the buttons. The card keeps its trailing edge.
    /// Mirroring the control repaints the sidebar's splitter through its window, which a desktop that
    /// cannot paint refuses, so the test skips there as a shown window's does.
    /// </summary>
    [DesktopFact]
    public void FindCard_FollowsItsCountsWidth_InBothDirections()
    {
        RunSta(() =>
        {
            using var ctrl = ShownFindBar();
            var card = ctrl.FindBarPanel;
            Label count = card.Controls.OfType<Label>().Single();
            var (prev, _, _) = Buttons(ctrl);

            count.Text = "1 / 9";
            int narrowWidth = card.Width, right = card.Right;
            count.Text = "1 / 2000+";
            Assert.True(card.Width > narrowWidth, $"the card must widen with its count: {card.Width} vs {narrowWidth}");
            Assert.Equal(right, card.Right);                              // the trailing edge stays put
            Assert.Equal(CountLead + count.Width + 8, prev.Left);

            ctrl.RightToLeft = RightToLeft.Yes;
            ctrl.PerformLayout();
            count.Text = "1 / 9";
            int narrowLeft = count.Left;
            Assert.Equal(card.Width - CountLead - count.Width, narrowLeft);
            count.Text = "1 / 2000+";
            Assert.Equal(card.Width - CountLead - count.Width, count.Left);
            Assert.True(prev.Right <= count.Left, $"the steps must stay clear of the count: {prev.Right} vs {count.Left}");
        });
    }

    // ── The page canvas is content, not chrome ────────────────────────────────────────────

    /// <summary>
    /// The scrolling canvas keeps an unmirrored client origin, because the paint, hit-test and
    /// PDF→pixel maths are written against one. It is pinned explicitly rather than left to
    /// inherit, so a host flipping the chrome later cannot move it.
    /// </summary>
    [Fact]
    public void PageCanvasStaysLeftToRight_WhenTheChromeIsMirrored()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();

            ctrl.RightToLeft = RightToLeft.Yes;
            Assert.Equal(RightToLeft.Yes, ctrl.SidebarSplit.RightToLeft);
            Assert.Equal(RightToLeft.Yes, ctrl.SidebarSplit.Panel2.RightToLeft);
            Assert.Equal(RightToLeft.No, ctrl.PageSurface.RightToLeft);
        });
    }

    // ── The opt-in itself ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The opt-in is <see cref="Control.RightToLeft"/> rather than a viewer-specific flag, which
    /// is only worth choosing if it actually arrives by inheritance — a control dropped onto an
    /// already-mirrored host must mirror without the host naming the viewer at all.
    /// </summary>
    [Fact]
    public void ChromeMirrorsWhenAddedToAnAlreadyMirroredHost()
    {
        RunSta(() =>
        {
            using var host = new UserControl { RightToLeft = RightToLeft.Yes, Width = 700, Height = 500 };
            using var ctrl = new PdfViewerControl { Dock = DockStyle.Fill };

            Assert.Equal(GlyphLeft, ctrl.PreviousPageButton.Text);   // not yet parented
            Assert.False(ctrl.BookmarkTree.RightToLeftLayout);

            host.Controls.Add(ctrl);

            Assert.Equal(RightToLeft.Yes, ctrl.RightToLeft);
            Assert.Equal(GlyphRight, ctrl.PreviousPageButton.Text);
            Assert.True(ctrl.BookmarkTree.RightToLeftLayout);
            Assert.Equal(ContentAlignment.MiddleRight, ctrl.StatusLabel.TextAlign);
            Assert.Equal(RightToLeft.No, ctrl.PageSurface.RightToLeft);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A viewer wide enough to lay the find bar out, with the bar <b>shown through the public
    /// <c>Find()</c> entry point.
    /// </summary>
    /// <remarks>
    /// The bar is built hidden, and WinForms skips docking layout for a hidden control — so a
    /// freshly constructed viewer reports the bar at its 200px design width, not the host's. That
    /// is production behaviour, not a test artifact: the bar is first measured when <c>Find()</c>
    /// shows it. <c>ShowFindBar</c> refuses without a document, hence the load.
    /// </remarks>
    private static PdfViewerControl ShownFindBar()
    {
        var ctrl = new PdfViewerControl { Width = 700, Height = 500 };
        ctrl.LoadBytes(TestPdf.OnePage());
        ctrl.Find();
        ctrl.PerformLayout();
        Assert.True(ctrl.FindBarPanel.Visible, "Find() did not show the find bar");
        return ctrl;
    }

    private static (Control Prev, Control Next, Control Close) Buttons(PdfViewerControl ctrl)
    {
        var b = ctrl.FindBarButtons;
        return (b[0], b[1], b[2]);
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
