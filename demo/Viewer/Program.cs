// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Windows.Forms;

namespace Viewer
{
    /// <summary>
    /// Minimal runnable host that embeds <see cref="Tetitulz.Pdf.WinForms.PdfViewer.PdfViewerControl"/>
    /// — a smoke/verification harness for the reusable control.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }
}
