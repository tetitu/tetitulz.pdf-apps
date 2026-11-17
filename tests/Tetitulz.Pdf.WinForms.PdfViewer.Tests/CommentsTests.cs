// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations.Extraction;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The document's comments (GAPS.md §10a step 5): a Comments tab by page, a click going to one and
/// outlining it, its author, date and text on hover, and <see cref="PdfViewerControl.ShowComments"/>.
/// </summary>
public sealed class CommentsTests
{
    private const PdfViewerFeatures WithComments = PdfViewerFeatures.Default | PdfViewerFeatures.Comments;

    [Fact]
    public void TheCommentsTab_ListsCommentsByPage_AndNothingElse()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = WithComments };
            viewer.LoadBytes(TestPdf.Commented());

            Assert.Contains(viewer.SidebarTabs.TabPages.Cast<TabPage>(), t => t.Controls.Contains(viewer.CommentTree));
            var groups = viewer.CommentTree.Nodes.Cast<TreeNode>().ToList();
            Assert.Equal(new[] { "Page 1", "Page 2" }, groups.Select(g => g.Text));
            Assert.Equal(new[] { "Text", "Square" },
                groups[0].Nodes.Cast<TreeNode>().Select(n => ((PdfAnnotationInfo)n.Tag!).Subtype));   // no link, widget, popup or hidden note
            Assert.Equal("Highlight", ((PdfAnnotationInfo)groups[1].Nodes[0].Tag!).Subtype);
            Assert.Equal("Dana: Check this total", groups[0].Nodes[0].Text);
            Assert.Equal("Avi", groups[0].Nodes[1].Text);                          // no text: the author alone
        });
    }

    /// <summary>A reply is listed under the comment it answers, not beside it, and a click on it chooses it.</summary>
    [Fact]
    public void AReply_IsListedUnderTheCommentItAnswers()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(WithComments);
            using (form)
            {
                TreeNode note = viewer.CommentTree.Nodes[0].Nodes[0];
                Assert.Equal("Dana: Check this total", note.Text);
                TreeNode reply = Assert.Single(note.Nodes.Cast<TreeNode>());
                Assert.Equal("Reply Guy: Agreed", reply.Text);
                Assert.Equal("note-1", ((PdfAnnotationInfo)reply.Tag!).InReplyToName);
                Assert.DoesNotContain(viewer.CommentTree.Nodes[0].Nodes.Cast<TreeNode>(), n => n.Text.StartsWith("Reply Guy", StringComparison.Ordinal));
                Assert.True(note.IsExpanded);

                viewer.CommentTree_NodeMouseClick(null, new TreeNodeMouseClickEventArgs(reply, MouseButtons.Left, 1, 0, 0));
                Assert.Same(reply.Tag, viewer.SelectedComment);
                Assert.Same(reply, viewer.CommentTree.SelectedNode);
            }
        });
    }

    [Fact]
    public void Comments_AreOffByDefault_AndTurningThemOnFillsTheTab()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.Commented());
            Assert.DoesNotContain(viewer.SidebarTabs.TabPages.Cast<TabPage>(), t => t.Controls.Contains(viewer.CommentTree));
            Assert.Empty(viewer.CommentTree.Nodes);

            viewer.Features |= PdfViewerFeatures.Comments;
            Assert.Equal(2, viewer.CommentTree.Nodes.Count);
            Assert.Equal(3, viewer.SidebarTabs.TabPages.Count);                   // Pages, Bookmarks, Comments
        });
    }

    [Fact]
    public void AHoverDescription_HasTheKindAuthorDateAndText()
    {
        var note = new PdfAnnotationInfoBuilder("Text", "Dana", "Check this total", "D:20261027083000Z").Build();
        string text = PdfViewerControl.DescribeComment(note);
        string[] lines = text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Text · Dana · ", lines[0]);
        Assert.Contains("2026", lines[0]);                                          // the date, read and shown
        Assert.DoesNotContain("D:", lines[0]);
        Assert.Equal("Check this total", lines[1]);

        // A date that does not parse is shown as written; no date, no date.
        Assert.Equal("Text · Dana · yesterday", PdfViewerControl.DescribeComment(
            new PdfAnnotationInfoBuilder("Text", "Dana", null, "yesterday").Build()));
        Assert.Equal("Square", PdfViewerControl.DescribeComment(new PdfAnnotationInfoBuilder("Square", null, null, null).Build()));

        string longText = new string('x', 100);
        Assert.Equal("Dana: " + new string('x', 60) + "…",
            PdfViewerControl.CommentRow(new PdfAnnotationInfoBuilder("Text", "Dana", longText, null).Build()));
    }

    [Fact]
    public void ClickingARow_GoesToTheComment_AndOutlinesIt()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(WithComments);
            using (form)
            {
                viewer.GoToPage(0, 320f);
                int expected = viewer.LastDestinationScroll;                       // where the note's top lands
                viewer.GoToPage(1, 200f);                                          // somewhere else, positioned
                Assert.NotEqual(expected, viewer.LastDestinationScroll);

                TreeNode row = viewer.CommentTree.Nodes[0].Nodes[0];
                viewer.CommentTree_NodeMouseClick(null, new TreeNodeMouseClickEventArgs(row, MouseButtons.Left, 1, 0, 0));

                Assert.Same(row.Tag, viewer.SelectedComment);
                Assert.Same(row, viewer.CommentTree.SelectedNode);
                Assert.Equal(0, viewer.CurrentPage);
                Assert.Equal(expected, viewer.LastDestinationScroll);
            }
        });
    }

    [Fact]
    public void HoveringAComment_DescribesIt_AndClickingItChoosesItsRow()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(WithComments);
            using (form)
            {
                Point square = SurfacePoint(viewer, 150, 150);
                viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, square.X, square.Y, 0));
                Assert.StartsWith("Square · Avi", viewer.CommentToolTipText);

                Point empty = SurfacePoint(viewer, 380, 380);
                viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, empty.X, empty.Y, 0));
                Assert.Equal("", viewer.CommentToolTipText);

                viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, square.X, square.Y, 0));
                viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, square.X, square.Y, 0));
                Assert.Equal("Square", viewer.SelectedComment?.Subtype);
                Assert.Same(viewer.SelectedComment, viewer.CommentTree.SelectedNode?.Tag);
            }

            // The control: with comments off, a hover says nothing.
            var (form2, plain) = Shown(PdfViewerFeatures.Default);
            using (form2)
            {
                Point square = SurfacePoint(plain, 150, 150);
                plain.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, square.X, square.Y, 0));
                Assert.Equal("", plain.CommentToolTipText);
            }
        });
    }

    /// <summary>Hidden, the comments leave the page; the form's field stays.</summary>
    [Fact]
    public void ShowCommentsOff_LeavesCommentsOffThePage_AndKeepsTheFields()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.Commented());

            Bitmap shown = viewer.EnsurePage(0)!;
            Assert.True(IsRed(At(shown, 150, 150)));
            Assert.True(IsBlue(At(shown, 300, 125)));

            viewer.ShowComments = false;
            Bitmap hidden = viewer.EnsurePage(0)!;
            Assert.False(IsRed(At(hidden, 150, 150)));
            Assert.True(IsBlue(At(hidden, 300, 125)));

            viewer.ShowComments = true;                                             // the control: back again
            Assert.True(IsRed(At(viewer.EnsurePage(0)!, 150, 150)));
        });
    }

    /// <summary>The thumbnails are drawn the way the page is: hidden comments leave them too.</summary>
    [Fact]
    public void ShowCommentsOff_LeavesCommentsOffTheThumbnails()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.Commented());
            Assert.True(IsRed(At(Thumbnail(viewer), 150, 150)));

            viewer.ShowComments = false;                                            // the strip is rebuilt
            Assert.False(IsRed(At(Thumbnail(viewer), 150, 150)));
            Assert.True(IsBlue(At(Thumbnail(viewer), 300, 125)));
        });

        static Bitmap Thumbnail(PdfViewerControl viewer) => viewer.ThumbnailAt(0, new Size(140, 140))!;
    }

    // ----------------------------------------------------------------

    /// <summary>A 400-point page's user-space point in a bitmap of the whole page.</summary>
    private static Color At(Bitmap page, float x, float y)
        => page.GetPixel((int)(x / 400f * page.Width), (int)((400 - y) / 400f * page.Height));

    private static bool IsRed(Color c) => c.R > 200 && c.G < 80 && c.B < 80;
    private static bool IsBlue(Color c) => c.B > 200 && c.R < 80 && c.G < 80;

    private static (Form, PdfViewerControl) Shown(PdfViewerFeatures features)
    {
        var form = new Form { ClientSize = new Size(800, 700) };
        var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = features };
        form.Controls.Add(viewer);
        form.CreateControl();
        viewer.LoadBytes(TestPdf.Commented());
        return (form, viewer);
    }

    /// <summary>A page-1 user-space point in the surface's client coordinates.</summary>
    private static Point SurfacePoint(PdfViewerControl viewer, float x, float y)
    {
        Assert.True(viewer.TryPageToClient(0, new RectangleF(x, y, 1, 1), out RectangleF c));
        return viewer.PageSurface.PointToClient(viewer.PointToScreen(new Point((int)c.X, (int)c.Y)));
    }

    /// <summary>Builds the inventory's record through the library's own listing of a one-annotation page.</summary>
    private sealed class PdfAnnotationInfoBuilder
    {
        private readonly string _subtype;
        private readonly string? _author, _contents, _date;

        internal PdfAnnotationInfoBuilder(string subtype, string? author, string? contents, string? date)
            => (_subtype, _author, _contents, _date) = (subtype, author, contents, date);

        internal PdfAnnotationInfo Build()
        {
            string entries = "/Subtype /" + _subtype + " /Rect [0 0 10 10]"
                + (_author == null ? "" : " /T (" + _author + ")")
                + (_contents == null ? "" : " /Contents (" + _contents + ")")
                + (_date == null ? "" : " /M (" + _date + ")");
            byte[] pdf = TestPdf.OneAnnotation(entries);
            return PdfAnnotationExtractor.ListAnnotations(new System.IO.MemoryStream(pdf)).Single();
        }
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
