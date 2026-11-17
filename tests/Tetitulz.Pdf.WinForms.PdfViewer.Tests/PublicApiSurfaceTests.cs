// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Tetitulz.Pdf.TestHelpers;
using Xunit;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// Pins <c>Tetitulz.Pdf.WinForms.PdfViewer</c>'s exported surface against <c>tests/ApiSnapshots</c>.
///
/// <para><b>This one guards a different kind of contract from the library packages.</b> The others
/// export methods a caller invokes; this exports an embeddable <c>UserControl</c>, whose surface is
/// what a host <i>data-binds and subscribes to</i> — designer-visible properties, and the events a
/// host must handle to supply a password or intercept a link. Renaming one of those does not break
/// a compile in every host: a designer file that sets a property by name, or a host wiring
/// <c>PasswordRequested</c>, fails at load rather than at build. A baseline is the only thing that
/// makes such a rename visible before it ships.</para>
///
/// <para><b>Scope caveat.</b> The package multi-targets <c>net462</c> and <c>net10.0-windows</c>;
/// this test project is <c>net10.0-windows</c>, so the baseline records the modern leg only. The
/// two legs compile from one source set with no <c>#if</c> around any public member today, so they
/// agree — but if a target-conditional public member is ever added, this guard will not see the
/// net462 side of it.</para>
///
/// <para>A failure is not automatically a bug — an intended API change fails here once, and the fix
/// is to accept the regenerated <c>.actual</c> file <i>in the same commit</i>.</para>
/// </summary>
public class PublicApiSurfaceTests
{
    private static Assembly ViewerAssembly => typeof(PdfViewerControl).Assembly;

    [ApiBaselineFact]
    public void Viewer_exported_surface_matches_its_baseline()
    {
        string? report = PublicApiSnapshot.Compare(ViewerAssembly);
        Assert.True(report == null, report);
    }

    /// <summary>
    /// A floor for the guard itself. A baseline that rendered as nothing would compare equal to an
    /// empty file and pass forever, so this asserts the render actually reached the control and the
    /// host-facing event surface around it — including the password path, which is the one a host
    /// cannot work around if it silently disappears.
    /// </summary>
    [ApiBaselineFact]
    public void The_renderer_produces_a_substantial_surface()
    {
        string rendered = PublicApiSnapshot.Render(ViewerAssembly);

        Assert.Contains("Tetitulz.Pdf.WinForms.PdfViewer.PdfViewerControl", rendered);
        Assert.Contains("Tetitulz.Pdf.WinForms.PdfViewer.PdfViewLayoutMode", rendered);
        Assert.Contains("Tetitulz.Pdf.WinForms.PdfViewer.PdfViewerLoadedEventArgs", rendered);
        Assert.Contains("Tetitulz.Pdf.WinForms.PdfViewer.PdfViewerPageChangedEventArgs", rendered);
        Assert.Contains("Tetitulz.Pdf.WinForms.PdfViewer.PdfViewerPasswordRequestedEventArgs", rendered);
        Assert.Contains("Tetitulz.Pdf.WinForms.PdfViewer.PdfViewerLinkClickedEventArgs", rendered);
        Assert.Contains("Tetitulz.Pdf.WinForms.PdfViewer.PdfPasswordRequiredException", rendered);

        // 59 lines today, 31 of them PdfViewerControl's own members. A render that collapsed to
        // type declarations would be 7 lines, and one that produced nothing would be 0.
        int lines = rendered.Split('\n').Length;
        Assert.True(lines > 40,
            $"Only {lines} lines rendered for Tetitulz.Pdf.WinForms.PdfViewer; " +
            "the renderer is filtering too much.");
    }
}
