// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// A test that shows a window. Skipped, saying why, where the session has no desktop a shown window
/// can paint on — a locked screen, a service session: WinForms' own layout of a shown
/// <see cref="SplitContainer"/> then throws from GDI+ inside a window procedure, which nothing in
/// the control can prevent, and the test would hang on the error dialog or fail on its message.
/// </summary>
internal sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        string? reason = ShownWindowDesktop.FailureReason;
        if (reason != null) Skip = "the session's desktop cannot paint a shown window: " + reason;
    }
}

/// <summary>Whether this session can show a window and lay it out — probed once, by showing one off-screen.</summary>
internal static class ShownWindowDesktop
{
    private static readonly Lazy<string?> Failure = new(Probe);

    /// <summary>Null when a shown window paints; the exception's message otherwise.</summary>
    internal static string? FailureReason => Failure.Value;

    private static string? Probe()
    {
        try
        {
            StaThread.Run(() =>
            {
                using var form = new Form
                {
                    ClientSize = new Size(300, 200), StartPosition = FormStartPosition.Manual,
                    Location = new Point(-5000, -5000), ShowInTaskbar = false,
                };
                form.Controls.Add(new SplitContainer { Dock = DockStyle.Fill });
                form.Show();
                Application.DoEvents();
            });
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }
}
