// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

namespace Tetitulz.Pdf.Printing.Tests;

/// <summary>The file name a print suggests: the spooler's document name without the application's prefix or the document's extension.</summary>
public sealed class DocumentNamesTests
{
    [Theory]
    [InlineData("Microsoft Word - Invoice 42.docx", "Invoice 42")]
    [InlineData("microsoft excel - Budget.XLSX", "Budget")]
    [InlineData("Microsoft PowerPoint - Deck.pptx", "Deck")]
    [InlineData("  Notes.txt  ", "Notes")]
    [InlineData("report.final", "report.final")]
    [InlineData("Microsoft Word - a/b: c.docx", "a_b_ c")]
    [InlineData("Microsoft Word - .docx", "Print")]
    [InlineData("", "Print")]
    [InlineData(null, "Print")]
    public void Suggest_TakesOffThePrefixAndTheExtension(string? documentName, string expected)
        => Assert.Equal(expected, DocumentNames.Suggest(documentName));

    /// <summary>One prefix and one extension come off, not every one that follows.</summary>
    [Fact]
    public void Suggest_TakesOffOnePrefixAndOneExtension()
        => Assert.Equal("Microsoft Word - Notes.txt", DocumentNames.Suggest("Microsoft Word - Microsoft Word - Notes.txt.docx"));
}
