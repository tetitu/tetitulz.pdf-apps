// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;
using Tetitulz.Pdf.WinForms.PdfViewer;

namespace Viewer
{
    /// <summary>Host window for the embeddable <see cref="PdfViewerControl"/>.</summary>
    internal sealed class MainForm : Form
    {
        private readonly PdfViewerControl _viewer;

        public MainForm(string? initialPath)
        {
            Text = "Tetitulz.Pdf — WinForms Viewer";
            Width = 1100;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;

            _viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            _viewer.DocumentLoaded += (s, e) => Text = $"Tetitulz.Pdf — WinForms Viewer  ({e.PageCount} pages)";
            Controls.Add(_viewer);

            AllowDrop = true;
            DragEnter += (s, e) =>
                e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            DragDrop += (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                    TryLoad(files[0]);
            };

            if (!string.IsNullOrEmpty(initialPath))
                Shown += (s, e) => TryLoad(initialPath!);
        }

        private void TryLoad(string path)
        {
            try { _viewer.LoadFile(path); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Failed to open PDF",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
