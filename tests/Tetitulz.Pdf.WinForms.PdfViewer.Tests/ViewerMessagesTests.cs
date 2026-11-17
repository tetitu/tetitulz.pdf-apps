// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// What the control says and shows comes from <see cref="PdfViewerControl.Strings"/>, the messages
/// written at run time as well as the labels; and the bookmark tree's styled fonts are owned.
/// </summary>
public sealed class ViewerMessagesTests
{
    [Fact]
    public void TheLayoutButton_NamesTheLayoutShown_InTheCurrentStrings()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            var button = LayoutButton(viewer);
            Assert.Equal("Continuous", button.Text);

            viewer.LayoutMode = PdfViewLayoutMode.SinglePage;
            Assert.Equal("Single page", button.Text);

            viewer.Strings = PdfViewerStrings.Hebrew();
            Assert.Equal("עמוד בודד", button.Text);              // the strings follow the layout shown

            viewer.LayoutMode = PdfViewLayoutMode.Continuous;
            Assert.Equal("רציף", button.Text);
        });
    }

    [Fact]
    public void APageTooLargeToRender_IsReportedInTheCurrentStrings()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Strings = PdfViewerStrings.Hebrew() };
            viewer.LoadBytes(TestPdf.HugeUserUnitPage());

            Assert.Null(viewer.EnsurePage(0));
            Assert.Contains("עמוד 1 גדול מכדי", viewer.StatusLabel.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("too large", viewer.StatusLabel.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void APageThatRenders_SaysNothingOfARefusal()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Strings = PdfViewerStrings.Hebrew() };
            viewer.LoadBytes(TestPdf.OnePage());

            Assert.NotNull(viewer.EnsurePage(0));
            Assert.DoesNotContain("גדול מכדי", viewer.StatusLabel.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AnExtractionFailure_IsPhrasedInTheCurrentStrings()
    {
        var source = PdfDocumentSource.FromBytes(TestPdf.OnePage());
        source.ReportPageTextError(4, "boom");

        Assert.Equal("שכבת הטקסט אינה זמינה: page 5: boom", source.FirstExtractionErrorIn(PdfViewerStrings.Hebrew()));
        Assert.Equal("Text layer unavailable: page 5: boom", source.FirstExtractionError);
    }

    [Theory]
    [InlineData("קובצי PDF (*.pdf)|*.pdf", "קובצי PDF (*.pdf)|*.pdf")]   // well formed: kept
    [InlineData("PDF only", "E|*.e")]                                       // no pattern
    [InlineData("a|*.pdf|b", "E|*.e")]                                      // an odd count
    [InlineData("a||b|*.x", "E|*.e")]                                       // an empty part
    [InlineData("", "E|*.e")]
    [InlineData(null, "E|*.e")]
    public void ADialogFilter_FallsBackToEnglish_WhenTheTranslationIsMalformed(string? translated, string expected)
        => Assert.Equal(expected, PdfViewerControl.DialogFilter(translated, "E|*.e"));

    [Fact]
    public void ThePrintJob_IsNamedAfterTheFile_ElseInTheCurrentStrings()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Strings = PdfViewerStrings.Hebrew() };
            viewer.LoadBytes(TestPdf.OnePage());
            using (var job = viewer.BuildPrintDocument()) Assert.Equal("מסמך PDF", job.DocumentName);

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "viewer-print-name-" + Guid.NewGuid().ToString("N") + ".pdf");
            System.IO.File.WriteAllBytes(path, TestPdf.OnePage());
            try
            {
                viewer.LoadFile(path);
                using var job = viewer.BuildPrintDocument();
                Assert.Equal(System.IO.Path.GetFileName(path), job.DocumentName);
            }
            finally { System.IO.File.Delete(path); }
        });
    }

    [Fact]
    public void BookmarkFonts_AreSharedByStyle_AndDisposedWhenTheTreeIsRebuilt()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(TestPdf.StyledOutline());                   // one bold item, one italic
            Assert.Equal(2, viewer.BookmarkFontCount);
            Font first = Node(viewer, "One").NodeFont!;
            IntPtr handle = first.ToHfont();                             // potency: a live font has a handle
            Assert.NotEqual(IntPtr.Zero, handle);

            viewer.LoadBytes(TestPdf.StyledOutline());

            Assert.Equal(2, viewer.BookmarkFontCount);
            Assert.NotSame(first, Node(viewer, "One").NodeFont);
            Assert.ThrowsAny<Exception>(() => first.ToHfont());          // the old tree's font was disposed
        });
    }

    private static ToolStripItem LayoutButton(PdfViewerControl viewer)
        => viewer.Toolbar.Items.Cast<ToolStripItem>().Single(i => i.ToolTipText == viewer.Strings.LayoutTip);

    private static TreeNode Node(PdfViewerControl viewer, string title)
    {
        static System.Collections.Generic.IEnumerable<TreeNode> All(TreeNodeCollection nodes)
            => nodes.Cast<TreeNode>().SelectMany(n => new[] { n }.Concat(All(n.Nodes)));
        return All(viewer.BookmarkTree.Nodes).Single(n => n.Text == title);
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
