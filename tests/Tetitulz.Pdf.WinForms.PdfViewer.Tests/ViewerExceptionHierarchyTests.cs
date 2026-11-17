// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using Tetitulz.Pdf.Core;
using Xunit;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The viewer's own exception is inside the <see cref="PdfException"/> hierarchy.
/// </summary>
/// <remarks>
/// <para><b>The repository-wide guard cannot see this package.</b>
/// <c>Base.Tests.TestInfrastructure.ExceptionHierarchyTests</c> sweeps the loaded
/// <c>Tetitulz.Pdf.*</c> assemblies, and it is a <c>net10.0</c> project that cannot reference a
/// <c>net462</c> / <c>net10.0-windows</c> one — so this assembly is never loaded there and the
/// sweep would report a clean sheet over a package it never looked at. That hole is documented on
/// the guard and closed here, in the only project that <i>can</i> reference the viewer.</para>
///
/// <para><c>PdfPasswordRequiredException</c> is one of the five the penetration test found
/// bypassing <c>PdfException</c>. A host wrapping the viewer's document load in
/// <c>catch (PdfException)</c> was not catching the password prompt.</para>
/// </remarks>
public class ViewerExceptionHierarchyTests
{
    [Fact]
    public void ThePasswordRequiredExceptionDerivesFromPdfException()
    {
        Assert.True(typeof(PdfException).IsAssignableFrom(typeof(PdfPasswordRequiredException)));
    }

    /// <summary>
    /// The behaviour a host gets, rather than the metadata: one <c>catch</c> sees it.
    /// </summary>
    [Fact]
    public void OneCatchOfPdfExceptionSeesThePasswordPrompt()
    {
        bool caught = false;

        try { throw new PdfPasswordRequiredException("a password is required"); }
        catch (PdfException) { caught = true; }
        catch (Exception) { /* deliberately second: this arm must not be the one that runs */ }

        Assert.True(caught, "PdfPasswordRequiredException escaped catch (PdfException)");
    }
}
