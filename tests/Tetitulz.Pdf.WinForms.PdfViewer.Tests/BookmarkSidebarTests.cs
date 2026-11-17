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
/// The bookmarks tab (GAPS.md §10a step 3): each item in its style and open state, a click landing
/// at its position, and the section being read selected as the view moves.
/// </summary>
public sealed class BookmarkSidebarTests
{
    [Fact]
    public void EachItem_HasItsStyleColourAndOpenState()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.StyledOutline());
            TreeNode one = Node(viewer, "One"), two = Node(viewer, "Two"), three = Node(viewer, "Three");

            Assert.True(one.NodeFont!.Bold);
            Assert.False(one.NodeFont.Italic);
            Assert.Equal(Color.FromArgb(255, 0, 0).ToArgb(), one.ForeColor.ToArgb());
            Assert.True(three.NodeFont!.Italic);
            Assert.Null(two.NodeFont);                                   // plain: the tree's own font
            Assert.True(one.IsExpanded);                                 // /Count 1
            Assert.False(two.IsExpanded);                                // /Count -1
        });
    }

    [Fact]
    public void ClickingAnItem_GoesToItsPosition()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, LayoutMode = PdfViewLayoutMode.Continuous };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(TestPdf.StyledOutline());

            viewer.GoToPage(0, 300f);                                    // where the API lands for One-mid's position
            int expected = viewer.LastDestinationScroll;

            viewer.GoToPage(2, 700f);                                    // somewhere else first
            TreeNode mid = Node(viewer, "One-mid");
            viewer.BookmarkTree_NodeMouseClick(null, new TreeNodeMouseClickEventArgs(mid, MouseButtons.Left, 1, 0, 0));

            Assert.Equal(0, viewer.CurrentPage);
            Assert.Equal(expected, viewer.LastDestinationScroll);
            Assert.NotEqual(-1, expected);
        });
    }

    [DesktopFact]
    public void TheSectionBeingRead_IsSelected_AndUnderAClosedParentItsParent()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, LayoutMode = PdfViewLayoutMode.Continuous, ZoomMode = PdfZoomMode.FitWidth };
            form.Controls.Add(viewer);
            form.Show();                                                 // a control never shown drops its scroll
            viewer.LoadBytes(TestPdf.StyledOutline());
            viewer.SidebarTabs.SelectedTab = (TabPage)viewer.BookmarkTree.Parent!;   // where a user expands a node
            Application.DoEvents();

            string? Current() => viewer.CurrentBookmarkNode?.Text;

            viewer.GoToPage(0);
            Assert.Equal("One", Current());
            Assert.Same(viewer.CurrentBookmarkNode, viewer.BookmarkTree.SelectedNode);

            viewer.GoToPage(0, 290f);                                    // just below One-mid's 300
            Assert.Equal("One-mid", Current());

            viewer.GoToPage(1);
            Assert.Equal("Two", Current());

            viewer.GoToPage(1, 390f);                                    // Two-sub reached, its parent closed
            Assert.Equal("Two", Current());
            Node(viewer, "Two").Expand();
            Assert.Equal("Two-sub", Current());

            viewer.GoToPage(2);
            Assert.Equal("Three", Current());

            viewer.HighlightCurrentBookmark = false;
            Assert.Null(viewer.CurrentBookmarkNode);
            Assert.Null(viewer.BookmarkTree.SelectedNode);
            viewer.HighlightCurrentBookmark = true;                      // the control
            Assert.Equal("Three", Current());
        });
    }

    private static TreeNode Node(PdfViewerControl viewer, string title)
    {
        static System.Collections.Generic.IEnumerable<TreeNode> All(TreeNodeCollection nodes)
            => nodes.Cast<TreeNode>().SelectMany(n => new[] { n }.Concat(All(n.Nodes)));
        return All(viewer.BookmarkTree.Nodes).Single(n => n.Text == title);
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
