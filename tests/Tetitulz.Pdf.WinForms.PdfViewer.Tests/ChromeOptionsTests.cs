// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The host's three levers over the chrome: <see cref="PdfViewerControl.Features"/> switches a
/// capability off everywhere, a <c>Show*</c> property hides one control and keeps its command, and
/// <see cref="PdfViewerControl.CommandExecuting"/> replaces a command's built-in action.
/// </summary>
public sealed class ChromeOptionsTests
{
    // ── Features ──────────────────────────────────────────────────────────────

    [Fact]
    public void Features_DefaultToEveryCapability_AndRefuseAnUnknownBit()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            Assert.Equal(PdfViewerFeatures.Default, ctrl.Features);

            Assert.Throws<ArgumentOutOfRangeException>(() => ctrl.Features = (PdfViewerFeatures)8192);   // the first bit no feature has
            Assert.Equal(PdfViewerFeatures.Default, ctrl.Features);
        });
    }

    /// <summary>
    /// A capability off: its button is gone and its shortcut is not consumed, so the key reaches the
    /// host's form. The control in each row is the same key with the capability on.
    /// </summary>
    [Fact]
    public void AFeatureOff_HidesItsButton_AndLetsItsShortcutPass()
    {
        RunSta(() =>
        {
            var rows = new (PdfViewerFeatures Feature, Func<PdfViewerControl, ToolStripItem> Button,
                            PdfViewerControl.CmdKeyAction Key, PdfViewerCommand Command)[]
            {
                (PdfViewerFeatures.OpenFile, c => c.OpenButton,  PdfViewerControl.CmdKeyAction.OpenFile,    PdfViewerCommand.Open),
                (PdfViewerFeatures.Search,   c => c.FindButton,  PdfViewerControl.CmdKeyAction.ShowFindBar, PdfViewerCommand.Find),
                (PdfViewerFeatures.Print,    c => c.PrintButton, PdfViewerControl.CmdKeyAction.Print,       PdfViewerCommand.Print),
            };
            foreach (var row in rows)
            {
                using var ctrl = new PdfViewerControl();
                ctrl.LoadBytes(TestPdf.OnePage());
                var seen = new List<PdfViewerCommand>();
                ctrl.CommandExecuting += (s, e) => { seen.Add(e.Command); e.Handled = true; };   // no dialogs

                Assert.True(row.Button(ctrl).Available, $"{row.Feature}: the button starts hidden");
                Assert.True(ctrl.ExecuteCmdKey(row.Key), $"{row.Feature}: the key was not taken with the feature on");
                Assert.Equal(new[] { row.Command }, seen);

                ctrl.Features &= ~row.Feature;
                seen.Clear();

                Assert.False(row.Button(ctrl).Available, $"{row.Feature}: the button is still offered");
                Assert.False(ctrl.ExecuteCmdKey(row.Key), $"{row.Feature}: the key was swallowed");
                Assert.Empty(seen);
            }
        });
    }

    /// <summary>A feature off is off at its public method too — the search finds nothing.</summary>
    [Fact]
    public void SearchOff_TheFindMethodDoesNotSearch()
    {
        RunSta(() =>
        {
            int Matches(PdfViewerFeatures features)
            {
                using var ctrl = new PdfViewerControl { Features = features };
                ctrl.LoadBytes(TestPdf.OnePageWithText());
                ctrl.Find("Hello");
                return CollectionFieldCount(ctrl, "_findMatches");
            }

            Assert.Equal(1, Matches(PdfViewerFeatures.Default));   // potency
            Assert.Equal(0, Matches(PdfViewerFeatures.Default & ~PdfViewerFeatures.Search));
        });
    }

    [Fact]
    public void TextSelectionOff_ADragSelectsNothing()
    {
        RunSta(() =>
        {
            int Selected(PdfViewerFeatures features)
            {
                using var form = new Form { ClientSize = new Size(800, 700) };
                var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = features };
                form.Controls.Add(viewer);
                form.CreateControl();
                viewer.LoadBytes(TestPdf.TwoLinesOfText());

                Assert.True(viewer.TryPageToClient(0, new RectangleF(0, 0, 200, 200), out RectangleF page));
                Point a = SurfacePoint(viewer, new Point((int)page.X + 1, (int)page.Y + 1));
                Point b = SurfacePoint(viewer, new Point((int)page.Right - 1, (int)page.Bottom - 1));
                viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
                viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0));
                viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0));
                return viewer.SelectedPageCount;
            }

            Assert.Equal(1, Selected(PdfViewerFeatures.Default));   // potency
            Assert.Equal(0, Selected(PdfViewerFeatures.Default & ~PdfViewerFeatures.TextSelection));
        });
    }

    [Fact]
    public void LinksOff_AClickOnALinkIsNotFollowed()
    {
        RunSta(() =>
        {
            int Clicks(PdfViewerFeatures features)
            {
                using var form = new Form { ClientSize = new Size(800, 700) };
                var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = features };
                form.Controls.Add(viewer);
                form.CreateControl();
                viewer.LoadBytes(TestPdf.OnePageWithUriLink("https://example.com/"));
                int clicks = 0;
                viewer.LinkClicked += (s, e) => { clicks++; e.Handled = true; };   // no browser

                // The link's rect is [10 20 110 60]; click its middle.
                Assert.True(viewer.TryPageToClient(0, new RectangleF(60, 40, 1, 1), out RectangleF at));
                Point p = SurfacePoint(viewer, new Point((int)at.X, (int)at.Y));
                viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
                viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
                return clicks;
            }

            Assert.Equal(1, Clicks(PdfViewerFeatures.Default));   // potency
            Assert.Equal(0, Clicks(PdfViewerFeatures.Default & ~PdfViewerFeatures.Links));
        });
    }

    [Fact]
    public void SidebarFeatures_TakeOutTheirTab_AndBothOffCollapseTheSidebar()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(3));
            Assert.Equal(2, ctrl.SidebarTabs.TabPages.Count);
            Assert.Equal(3, ctrl.ThumbnailItemCount);

            ctrl.Features &= ~PdfViewerFeatures.Thumbnails;
            Assert.Equal(1, ctrl.SidebarTabs.TabPages.Count);
            Assert.Same(ctrl.BookmarkTree.Parent, ctrl.SidebarTabs.TabPages[0]);
            Assert.Equal(0, ctrl.ThumbnailItemCount);              // the strip is not built for a hidden tab
            Assert.False(ctrl.SidebarSplit.Panel1Collapsed);

            ctrl.Features &= ~PdfViewerFeatures.Bookmarks;
            Assert.Equal(0, ctrl.SidebarTabs.TabPages.Count);
            Assert.True(ctrl.SidebarSplit.Panel1Collapsed);
            Assert.False(ctrl.SidebarButton.Available);
            Assert.True(ctrl.ShowSidebar);                          // the host's own switch is kept

            ctrl.Features = PdfViewerFeatures.Default;
            Assert.Equal(2, ctrl.SidebarTabs.TabPages.Count);
            Assert.Equal(3, ctrl.ThumbnailItemCount);
            Assert.False(ctrl.SidebarSplit.Panel1Collapsed);
        });
    }

    /// <summary>
    /// A host that sizes the viewer after loading it (a WPF <c>WindowsFormsHost</c>) still gets the
    /// sidebar's first width once the control is wide enough; after that the user's position stays.
    /// The control is the same control loaded wide.
    /// </summary>
    [Fact]
    public void TheSidebarTakesItsWidth_WhenTheHostSizesTheViewerAfterLoading()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(120, 120) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            form.Controls.Add(viewer);
            form.CreateControl();
            Assert.NotEqual(240, viewer.SidebarSplit.SplitterDistance);   // too narrow at load

            form.ClientSize = new Size(900, 700);
            Assert.Equal(240, viewer.SidebarSplit.SplitterDistance);

            viewer.SidebarSplit.SplitterDistance = 300;                  // the user moves it
            form.ClientSize = new Size(1000, 700);
            Assert.Equal(300, viewer.SidebarSplit.SplitterDistance);

            using var wide = new Form { ClientSize = new Size(900, 700) };
            var control = new PdfViewerControl { Dock = DockStyle.Fill };
            wide.Controls.Add(control);
            wide.CreateControl();
            Assert.Equal(240, control.SidebarSplit.SplitterDistance);
        });
    }

    // ── Show* ─────────────────────────────────────────────────────────────────

    /// <summary>Hiding a control keeps its capability: the shortcut still runs the command.</summary>
    [Fact]
    public void ShowPrintButtonOff_HidesTheButton_ButCtrlPStillPrints()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.OnePage());
            var seen = new List<PdfViewerCommand>();
            ctrl.CommandExecuting += (s, e) => { seen.Add(e.Command); e.Handled = true; };

            ctrl.ShowPrintButton = false;

            Assert.False(ctrl.PrintButton.Available);
            Assert.True(ctrl.ExecuteCmdKey(PdfViewerControl.CmdKeyAction.Print));
            Assert.Equal(new[] { PdfViewerCommand.Print }, seen);

            ctrl.ShowPrintButton = true;
            Assert.True(ctrl.PrintButton.Available);
        });
    }

    [Fact]
    public void ShowGroupsOff_HideEveryItemOfTheGroup_AndLeaveNoStraySeparator()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.OnePage());
            Assert.Equal(4, VisibleSeparators(ctrl));               // five groups, four separators

            ctrl.ShowNavigationControls = false;
            ctrl.ShowZoomControls = false;

            Assert.False(ctrl.PreviousPageButton.Available);
            Assert.False(ctrl.NextPageButton.Available);
            Assert.False(ctrl.PageCountLabel.Available);
            Assert.False(ctrl.ZoomInButton.Available);
            Assert.False(ctrl.ZoomCombo.Available);
            Assert.Equal(2, VisibleSeparators(ctrl));
            AssertNoStraySeparator(ctrl);

            ctrl.ShowOpenButton = false;                            // the first group goes too
            Assert.Equal(1, VisibleSeparators(ctrl));
            AssertNoStraySeparator(ctrl);
        });
    }

    [DesktopFact]
    public void ShowToolbarAndShowStatusBar_HideTheBars()
    {
        RunSta(() =>
        {
            // Shown off-screen: a bar's Visible reads false on a form never shown, whatever was set.
            using var form = new Form
            {
                ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual,
                Location = new Point(-5000, -5000), ShowInTaskbar = false,
            };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            form.Controls.Add(viewer);
            form.Show();
            Assert.True(viewer.Toolbar.Visible);
            Assert.True(viewer.StatusBar.Visible);

            viewer.ShowToolbar = false;
            viewer.ShowStatusBar = false;

            Assert.False(viewer.Toolbar.Visible);
            Assert.False(viewer.StatusBar.Visible);
            Assert.False(viewer.ShowToolbar);
            Assert.False(viewer.ShowStatusBar);
        });
    }

    // ── CommandExecuting ──────────────────────────────────────────────────────

    [Fact]
    public void AHandledCommand_SkipsTheBuiltInAction_AndAnUnhandledOneRunsIt()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(3));
            bool take = true;
            var seen = new List<PdfViewerCommand>();
            ctrl.CommandExecuting += (s, e) => { seen.Add(e.Command); e.Handled = take; };

            ctrl.NextPageButton.PerformClick();
            Assert.Equal(0, ctrl.CurrentPage);
            ctrl.ZoomInButton.PerformClick();
            Assert.Equal(1.0, ctrl.Zoom);

            take = false;
            ctrl.NextPageButton.PerformClick();
            Assert.Equal(1, ctrl.CurrentPage);
            ctrl.ZoomInButton.PerformClick();
            Assert.True(ctrl.Zoom > 1.0);

            Assert.Equal(new[] { PdfViewerCommand.NextPage, PdfViewerCommand.ZoomIn,
                                 PdfViewerCommand.NextPage, PdfViewerCommand.ZoomIn }, seen);
        });
    }

    /// <summary>The host's own calls are not chrome commands, so a handler may make them.</summary>
    [Fact]
    public void ThePublicMethods_RaiseNoCommand()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.ManyPages(3));
            int raised = 0;
            ctrl.CommandExecuting += (s, e) => raised++;

            ctrl.NextPage();
            ctrl.ZoomIn();
            ctrl.FitWidth();
            ctrl.GoToPage(2);   // last: a zoom re-reads the current page from the scroll position

            Assert.Equal(0, raised);
            Assert.Equal(2, ctrl.CurrentPage);                      // potency: they all ran
        });
    }

    [Fact]
    public void CtrlWheel_IsAZoomCommand_ThatAHostCanTake()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl { Size = new Size(900, 700) };
            ctrl.LoadBytes(TestPdf.OnePage());
            PdfViewerCommand? seen = null;
            ctrl.CommandExecuting += (s, e) => { seen = e.Command; e.Handled = true; };

            Assert.True(ctrl.SurfaceMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 100, 100, -120), Keys.Control));

            Assert.Equal(PdfViewerCommand.ZoomOut, seen);
            Assert.Equal(1.0, ctrl.Zoom);                           // the host took it
        });
    }

    /// <summary>
    /// The zoom box's selection is the user's command; the viewer writing its own zoom into the box
    /// is not. Writing "100%" selects the 100% preset, which used to raise a SetZoom nobody gave.
    /// </summary>
    [Fact]
    public void TheZoomBox_RaisesSetZoomForTheUser_NotForTheViewersOwnWrites()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.LoadBytes(TestPdf.OnePage());
            var seen = new List<PdfViewerCommandEventArgs>();
            ctrl.CommandExecuting += (s, e) => seen.Add(e);

            ctrl.Zoom = 2.0;
            ctrl.Zoom = 1.0;                                        // writes "100%", a preset
            Assert.Equal("100%", ctrl.ZoomCombo.Text);
            Assert.Empty(seen);

            ctrl.ZoomCombo.SelectedIndex = ctrl.ZoomCombo.Items.IndexOf("200%");
            var e = Assert.Single(seen);
            Assert.Equal(PdfViewerCommand.SetZoom, e.Command);
            Assert.Equal(2.0, e.Zoom!.Value, 6);
            Assert.Equal(2.0, ctrl.Zoom, 6);
        });
    }

    // ----------------------------------------------------------------

    private static int VisibleSeparators(PdfViewerControl ctrl)
        => ctrl.Toolbar.Items.OfType<ToolStripSeparator>().Count(s => s.Available);

    private static void AssertNoStraySeparator(PdfViewerControl ctrl)
    {
        var shown = ctrl.Toolbar.Items.Cast<ToolStripItem>().Where(i => i.Available).ToList();
        Assert.NotEmpty(shown);
        Assert.IsNotType<ToolStripSeparator>(shown[0]);
        Assert.IsNotType<ToolStripSeparator>(shown[shown.Count - 1]);
        for (int i = 1; i < shown.Count; i++)
            Assert.False(shown[i] is ToolStripSeparator && shown[i - 1] is ToolStripSeparator, "two separators side by side");
    }

    private static int CollectionFieldCount(PdfViewerControl ctrl, string fieldName)
    {
        FieldInfo? f = typeof(PdfViewerControl).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(f);
        var value = f!.GetValue(ctrl) as ICollection;
        Assert.NotNull(value);
        return value!.Count;
    }

    private static Point SurfacePoint(PdfViewerControl viewer, Point client)
    {
        Control surface = viewer.PageSurface;
        return surface.PointToClient(viewer.PointToScreen(client));
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
