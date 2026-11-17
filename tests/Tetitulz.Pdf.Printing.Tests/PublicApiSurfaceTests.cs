// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Tetitulz.Pdf.TestHelpers;

namespace Tetitulz.Pdf.Printing.Tests;

/// <summary>
/// Pins <c>Tetitulz.Pdf.Printing</c>'s exported surface against
/// <c>tests/ApiSnapshots/Tetitulz.Pdf.Printing.txt</c>, so a breaking change to the package fails a
/// test instead of reaching the programs built on it.
/// </summary>
public sealed class PublicApiSurfaceTests
{
    private static Assembly PrintingAssembly => typeof(PrinterSetup).Assembly;

    [ApiBaselineFact]
    public void Printing_exported_surface_matches_its_baseline()
    {
        string? report = PublicApiSnapshot.Compare(PrintingAssembly);
        Assert.True(report == null, report);
    }

    /// <summary>
    /// A floor for the guard itself: a baseline that rendered as nothing would compare equal to an
    /// empty file and pass forever after. The members a printer app is built from are named.
    /// </summary>
    [ApiBaselineFact]
    public void The_rendered_surface_covers_what_a_printer_app_uses()
    {
        string rendered = PublicApiSnapshot.Render(PrintingAssembly);

        Assert.Contains("class Tetitulz.Pdf.Printing.PrinterIdentity", rendered);
        Assert.Contains("PrinterSetup::Install(", rendered);
        Assert.Contains("PrinterSetup::NeedsDriverUpdate(", rendered);
        Assert.Contains("JobListener::RunAsync(", rendered);
        Assert.Contains("JobConverter::Convert(", rendered);
        Assert.Contains("SpoolerJobs::CurrentJob(", rendered);
        Assert.Contains("DocumentNames::Suggest(", rendered);
    }
}
