// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Runs a test's body on a single-threaded-apartment thread, as every WinForms test here must — the
/// one implementation behind each class's <c>RunSta</c>.
/// </summary>
/// <remarks>
/// An exception a window procedure raises on that thread (a layout, a paint, a message handler)
/// would otherwise open WinForms' thread-exception dialog, which nothing dismisses in a test host:
/// the test hangs with no message. Here the thread's own exception mode catches it, and it is
/// rethrown as the test's failure — after the body's own exception when both occurred, since the
/// body's assertion is the more direct account.
/// </remarks>
internal static class StaThread
{
    internal static void Run(Action action)
    {
        Exception? failed = null;
        Exception? inWindowProcedure = null;
        var t = new Thread(() =>
        {
            ThreadExceptionEventHandler onException = (s, e) => inWindowProcedure ??= e.Exception;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, threadScope: true);
            Application.ThreadException += onException;
            try { action(); }
            catch (Exception e) { failed = e; }
            finally { Application.ThreadException -= onException; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        if (failed == null && inWindowProcedure == null) return;
        string message = failed != null ? "STA test failed: " + failed.Message : "";
        if (inWindowProcedure != null)
            message += (message.Length > 0 ? "; and " : "") + "a window procedure threw: " + inWindowProcedure.Message;
        throw new Exception(message, failed ?? inWindowProcedure);
    }
}
