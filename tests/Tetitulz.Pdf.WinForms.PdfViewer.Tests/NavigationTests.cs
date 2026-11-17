// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Windows.Forms;
using Action = Tetitulz.Pdf.WinForms.PdfViewer.PdfViewerControl.CmdKeyAction;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Reading conveniences: the keyboard scrolls and turns pages, the wheel turns the page in
/// single-page layout, Back and Forward return across jumps, and the page box speaks the document's
/// page labels.
/// </summary>
public sealed class NavigationTests
{
    // ── Keys ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.Up, nameof(Action.LineUp))]
    [InlineData(Keys.Down, nameof(Action.LineDown))]
    [InlineData(Keys.Left, nameof(Action.LineLeft))]
    [InlineData(Keys.Right, nameof(Action.LineRight))]
    [InlineData(Keys.Space, nameof(Action.ScreenDown))]
    [InlineData(Keys.Shift | Keys.Space, nameof(Action.ScreenUp))]
    [InlineData(Keys.Home, nameof(Action.FirstPage))]
    [InlineData(Keys.Control | Keys.Home, nameof(Action.FirstPage))]
    [InlineData(Keys.End, nameof(Action.LastPage))]
    [InlineData(Keys.Control | Keys.End, nameof(Action.LastPage))]
    public void TheReadingKeys_MoveThePage_WhenThePageHasTheFocus(Keys key, string expectedAction)
    {
        var expected = (Action)Enum.Parse(typeof(Action), expectedAction);
        Assert.Equal(expected, PdfViewerControl.ClassifyCmdKey(key, false, false, true, pageFocused: true));
        // Elsewhere — the sidebar's tree — the key is the tree's.
        Assert.Equal(Action.None, PdfViewerControl.ClassifyCmdKey(key, false, false, true, pageFocused: false));
        Assert.Equal(Action.None, PdfViewerControl.ClassifyCmdKey(key, false, false, documentLoaded: false, pageFocused: true));
    }

    [Fact]
    public void LeftAndRight_TurnThePage_InSinglePageLayout()
    {
        Assert.Equal(Action.PreviousPage, PdfViewerControl.ClassifyCmdKey(Keys.Left, false, false, true, true, singlePage: true));
        Assert.Equal(Action.NextPage, PdfViewerControl.ClassifyCmdKey(Keys.Right, false, false, true, true, singlePage: true));
    }

    [Fact]
    public void AltLeftAndAltRight_AreBackAndForward_ButNotWhileTyping()
    {
        Assert.Equal(Action.Back, PdfViewerControl.ClassifyCmdKey(Keys.Alt | Keys.Left, false, false, true));
        Assert.Equal(Action.Forward, PdfViewerControl.ClassifyCmdKey(Keys.Alt | Keys.Right, false, false, true));
        Assert.Equal(Action.None, PdfViewerControl.ClassifyCmdKey(Keys.Alt | Keys.Left, false, textEntryFocused: true, documentLoaded: true));
    }

    // ── History ───────────────────────────────────────────────────────────────

    [Fact]
    public void BackAndForward_ReturnAcrossJumps_AndANewJumpForgetsForward()
    {
        RunSta(() =>
        {
            using var viewer = SinglePageViewer(TestPdf.ManyPages(8));
            Assert.False(viewer.CanGoBack);

            viewer.JumpTo(3, null);
            viewer.JumpTo(6, null);
            Assert.Equal(6, viewer.CurrentPage);

            Assert.True(viewer.GoBack());
            Assert.Equal(3, viewer.CurrentPage);
            Assert.True(viewer.GoBack());
            Assert.Equal(0, viewer.CurrentPage);
            Assert.False(viewer.GoBack());                               // nowhere further
            Assert.True(viewer.CanGoForward);

            Assert.True(viewer.GoForward());
            Assert.Equal(3, viewer.CurrentPage);

            viewer.JumpTo(1, null);
            Assert.False(viewer.CanGoForward);                           // a new jump forgets forward
            Assert.Equal(3, viewer.BackTarget);
        });
    }

    [Fact]
    public void PagingIsNotAJump_SoBackSkipsIt()
    {
        RunSta(() =>
        {
            using var viewer = SinglePageViewer(TestPdf.ManyPages(8));
            viewer.JumpTo(5, null);
            viewer.NextPage();
            viewer.NextPage();

            Assert.True(viewer.GoBack());
            Assert.Equal(0, viewer.CurrentPage);
        });
    }

    [Fact]
    public void TheHistory_KeepsTheLatestPlacesOnly()
    {
        RunSta(() =>
        {
            using var viewer = SinglePageViewer(TestPdf.ManyPages(3));
            for (int i = 0; i < PdfViewerControl.MaxHistory + 20; i++) viewer.JumpTo(i % 2 == 0 ? 1 : 2, null);

            int back = 0;
            while (viewer.GoBack()) back++;
            Assert.Equal(PdfViewerControl.MaxHistory, back);
        });
    }

    [Fact]
    public void TheBackKey_GoesThroughCommandExecuting()
    {
        RunSta(() =>
        {
            using var viewer = SinglePageViewer(TestPdf.ManyPages(4));
            viewer.JumpTo(2, null);
            viewer.CommandExecuting += (s, e) => { if (e.Command == PdfViewerCommand.Back) e.Handled = true; };

            Assert.True(viewer.ExecuteCmdKey(Action.Back));
            Assert.Equal(2, viewer.CurrentPage);                         // the host took it
            Assert.True(viewer.CanGoBack);
        });
    }

    [Fact]
    public void ANewDocument_StartsWithNoHistory()
    {
        RunSta(() =>
        {
            using var viewer = SinglePageViewer(TestPdf.ManyPages(4));
            viewer.JumpTo(2, null);
            Assert.True(viewer.CanGoBack);

            viewer.LoadBytes(TestPdf.ManyPages(4));

            Assert.False(viewer.CanGoBack);
        });
    }

    [Fact]
    public void EndAndHome_JumpToTheLastAndFirstPage_AndBackReturns()
    {
        RunSta(() =>
        {
            using var viewer = SinglePageViewer(TestPdf.ManyPages(5));

            Assert.True(viewer.ExecuteCmdKey(Action.LastPage));
            Assert.Equal(4, viewer.CurrentPage);
            Assert.True(viewer.ExecuteCmdKey(Action.FirstPage));
            Assert.Equal(0, viewer.CurrentPage);
            Assert.True(viewer.ExecuteCmdKey(Action.Back));
            Assert.Equal(4, viewer.CurrentPage);
        });
    }

    // ── The wheel and the arrows in single-page layout ────────────────────────

    [Fact]
    public void TheWheel_TurnsThePage_PastEitherEndInSinglePageLayout()
    {
        RunSta(() =>
        {
            using var viewer = SinglePageViewer(TestPdf.ManyPages(3));    // a page that fits: both ends at once

            Assert.True(viewer.SurfaceMouseWheel(Wheel(-120), Keys.None));
            Assert.Equal(1, viewer.CurrentPage);
            Assert.True(viewer.SurfaceMouseWheel(Wheel(+120), Keys.None));
            Assert.Equal(0, viewer.CurrentPage);
            Assert.False(viewer.SurfaceMouseWheel(Wheel(+120), Keys.None));   // the first page: nothing before it
            Assert.Equal(0, viewer.CurrentPage);
        });
    }

    [Fact]
    public void TheWheel_OnlyScrolls_InContinuousLayout()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Size = new System.Drawing.Size(900, 700) };
            viewer.LoadBytes(TestPdf.ManyPages(3));

            Assert.False(viewer.SurfaceMouseWheel(Wheel(-120), Keys.None));
            Assert.Equal(0, viewer.CurrentPage);
        });
    }

    [Fact]
    public void TheDownArrow_TurnsThePage_AtTheFootOfASinglePage()
    {
        RunSta(() =>
        {
            using var viewer = SinglePageViewer(TestPdf.ManyPages(3));
            Assert.True(viewer.ExecuteCmdKey(Action.LineDown));
            Assert.Equal(1, viewer.CurrentPage);
            Assert.True(viewer.ExecuteCmdKey(Action.ScreenUp));
            Assert.Equal(0, viewer.CurrentPage);
        });
    }

    // ── Page labels ───────────────────────────────────────────────────────────

    [Fact]
    public void ThePageBox_ShowsThePagesLabel_AndTheCounterItsNumber()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.LabelledPages());

            Assert.Equal("i", viewer.PageBoxText(0));
            Assert.Equal("ii", viewer.PageBoxText(1));
            Assert.Equal("1", viewer.PageBoxText(2));
            Assert.Equal("(1 / 4)", viewer.PageCountLabel.Text);

            viewer.GoToPage(3);                                          // "2": the label is not the number
            Assert.Equal("(4 / 4)", viewer.PageCountLabel.Text);
        });
    }

    [Fact]
    public void ADocumentWithoutLabels_ShowsNumbers()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.ManyPages(3));

            Assert.Equal("2", viewer.PageBoxText(1));
            Assert.Equal("/ 3", viewer.PageCountLabel.Text);
        });
    }

    [Theory]
    [InlineData("ii", 1)]
    [InlineData("II", 1)]          // a label, its case aside
    [InlineData("1", 2)]           // a label wins over the page number it spells
    [InlineData("4", 3)]           // no such label: the page number
    [InlineData("9", -1)]
    [InlineData("x", -1)]
    [InlineData("", -1)]
    public void ThePageBox_TakesALabelFirst_ThenANumber(string entry, int expected)
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.LabelledPages());
            Assert.Equal(expected, viewer.PageFromEntry(entry));
        });
    }

    /// <summary>
    /// The wheel scrolls the surface without raising its Scroll event, so the current page — and the
    /// thumbnail and page box that follow it — must follow the wheel itself.
    /// </summary>
    [DesktopFact]
    public void TheWheel_MovesTheCurrentPage_InContinuousLayout()
    {
        StaThread.Run(() =>
        {
            using var form = new Form
            {
                ClientSize = new System.Drawing.Size(600, 400), StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-2000, -2000), ShowInTaskbar = false,
            };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            form.Controls.Add(viewer);
            form.Show();
            Application.DoEvents();
            viewer.LoadBytes(TestPdf.ManyPages(30));
            int changed = -1;
            viewer.PageChanged += (_, e) => changed = e.PageIndex;
            Assert.Equal(0, viewer.CurrentPage);

            for (int notch = 0; notch < 10; notch++) viewer.WheelSurface(-120);

            Assert.True(viewer.CurrentPage > 0, "the wheel scrolled and the current page stayed at the first");
            Assert.Equal(viewer.CurrentPage, changed);                    // the host (and the thumbnails) are told
        });
    }

    private static MouseEventArgs Wheel(int delta) => new MouseEventArgs(MouseButtons.None, 0, 50, 50, delta);

    private static PdfViewerControl SinglePageViewer(byte[] pdf)
    {
        var viewer = new PdfViewerControl { Size = new System.Drawing.Size(900, 700), LayoutMode = PdfViewLayoutMode.SinglePage };
        viewer.LoadBytes(pdf);
        return viewer;
    }

    private static void RunSta(System.Action action) => StaThread.Run(action);
}
