// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// A status-line message is handed to the screen reader as it appears — once, not again while it
/// stands, only while the status line is on screen, and not at all when the host switches
/// announcements off.
/// </summary>
/// <remarks>
/// What these read is the control's decision to announce: whether the operating system's
/// notification event reached a screen reader takes a UI Automation client, which no test here runs.
/// </remarks>
public sealed class StatusAnnouncementTests
{
    [DesktopFact]
    public void ADocumentLoaded_IsAnnounced_InTheStatusLinesWords()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            Assert.True(viewer.AnnounceStatusChanges);

            viewer.LoadBytes(TestPdf.ManyPages(3));

            Assert.Equal(viewer.StatusBar.Items[0].Text, viewer.LastAnnouncement);
            Assert.Contains("3", viewer.LastAnnouncement!, StringComparison.Ordinal);
        });
    }

    [DesktopFact]
    public void TheSameMessageAgain_IsNotSpokenTwice_AndANewOneIs()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.LoadBytes(TestPdf.ManyPages(3));
            int after = viewer.AnnouncementCount;
            Assert.True(after > 0);                                  // potency: the first was spoken

            viewer.LoadBytes(TestPdf.ManyPages(3));                  // the same words
            Assert.Equal(after, viewer.AnnouncementCount);

            viewer.LoadBytes(TestPdf.ManyPages(5));                  // other words
            Assert.Equal(after + 1, viewer.AnnouncementCount);
            Assert.Contains("5", viewer.LastAnnouncement!, StringComparison.Ordinal);
        });
    }

    [DesktopFact]
    public void SwitchedOff_NothingIsAnnounced()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.AnnounceStatusChanges = false;
            int before = viewer.AnnouncementCount;

            viewer.LoadBytes(TestPdf.ManyPages(3));

            Assert.Contains("3", viewer.StatusBar.Items[0].Text, StringComparison.Ordinal);   // potency: there was a message
            Assert.Equal(before, viewer.AnnouncementCount);
        });
    }

    [DesktopFact]
    public void AHiddenStatusBar_SaysNothing()
    {
        StaThread.Run(() =>
        {
            using var form = ShownForm(out var viewer);
            viewer.ShowStatusBar = false;
            int before = viewer.AnnouncementCount;

            viewer.LoadBytes(TestPdf.ManyPages(3));

            Assert.Contains("3", viewer.StatusBar.Items[0].Text, StringComparison.Ordinal);
            Assert.Equal(before, viewer.AnnouncementCount);
        });
    }

    [Fact]
    public void AControlNeverShown_SaysNothing()
    {
        StaThread.Run(() =>
        {
            using var viewer = new PdfViewerControl { RenderPagesInBackground = false };

            viewer.LoadBytes(TestPdf.ManyPages(3));

            Assert.Contains("3", viewer.StatusBar.Items[0].Text, StringComparison.Ordinal);
            Assert.Null(viewer.LastAnnouncement);
        });
    }

    private static Form ShownForm(out PdfViewerControl viewer)
    {
        var form = new Form
        {
            ClientSize = new Size(800, 600), StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000), ShowInTaskbar = false,
        };
        viewer = new PdfViewerControl { Dock = DockStyle.Fill, RenderPagesInBackground = false };
        form.Controls.Add(viewer);
        form.Show();
        Application.DoEvents();
        return form;
    }
}
