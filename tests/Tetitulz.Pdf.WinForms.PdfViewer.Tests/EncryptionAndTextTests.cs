// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Linq;
using Tetitulz.Pdf.Base.Encryption;
using Tetitulz.Pdf.Encryption;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// The viewer's encrypted-PDF path: detect /Encrypt, resolve a password (empty → preset →
/// host-supplied), decrypt, and render. Plus the positioned-text data path that backs
/// text search and text selection (works the same on decrypted documents).
/// </summary>
public sealed class EncryptionAndTextTests
{
    [Fact]
    public void Encrypted_WithUserPassword_OpensWhenProviderSuppliesIt()
    {
        byte[] enc = Encrypt(TestPdf.OnePageWithText(), user: "secret", owner: "owner");

        int calls = 0;
        PasswordProvider provider = (attempt, info) => { calls++; return "secret"; };

        var src = PdfDocumentSource.FromBytes(enc, provider);

        Assert.Equal(1, src.PageCount);
        Assert.True(src.RenderPng(0, 96).Length > 8);
        Assert.True(calls >= 1, "the provider should have been asked for the user password");
    }

    [Fact]
    public void Encrypted_OwnerOnly_OpensWithEmptyUserPassword()
    {
        // Empty user password (owner-protected only) — opens without a provider.
        byte[] enc = Encrypt(TestPdf.OnePageWithText(), user: "", owner: "owner");

        var src = PdfDocumentSource.FromBytes(enc, passwordProvider: null);

        Assert.Equal(1, src.PageCount);
        Assert.True(src.RenderPng(0, 96).Length > 8);
    }

    [Fact]
    public void Encrypted_OwnerOnly_TriesEmptyPassword_BeforeAskingProvider()
    {
        // Owner-only protection (empty user password). The empty password must be tried first,
        // so the provider (which is what raises PasswordRequested) is never consulted.
        byte[] enc = Encrypt(TestPdf.OnePageWithText(), user: "", owner: "owner");

        int providerCalls = 0;
        PasswordProvider provider = (attempt, info) => { providerCalls++; return "ignored"; };

        var src = PdfDocumentSource.FromBytes(enc, provider);

        Assert.Equal(1, src.PageCount);
        Assert.Equal(0, providerCalls);   // empty password opened it; the event was never needed
    }

    [Fact]
    public void Encrypted_WrongPassword_Throws()
    {
        byte[] enc = Encrypt(TestPdf.OnePageWithText(), user: "secret", owner: "owner");
        PasswordProvider provider = (attempt, info) => "definitely-wrong";

        Assert.Throws<PdfPasswordRequiredException>(
            () => PdfDocumentSource.FromBytes(enc, provider));
    }

    [Fact]
    public void Encrypted_ProviderCancels_Throws()
    {
        byte[] enc = Encrypt(TestPdf.OnePageWithText(), user: "secret", owner: "owner");
        PasswordProvider provider = (attempt, info) => null;   // give up immediately

        Assert.Throws<PdfPasswordRequiredException>(
            () => PdfDocumentSource.FromBytes(enc, provider));
    }

    [Fact]
    public void Layouts_ExtractText_FromPlainPdf()
    {
        var src = PdfDocumentSource.FromBytes(TestPdf.OnePageWithText());
        Assert.Contains("Hello", AllText(src));
    }

    [Fact]
    public void Layouts_ExtractText_AfterDecryption()
    {
        byte[] enc = Encrypt(TestPdf.OnePageWithText(), user: "secret", owner: "owner");
        var src = PdfDocumentSource.FromBytes(enc, (attempt, info) => "secret");

        // Text search + selection both read this layer; it must survive decryption.
        Assert.Contains("Hello", AllText(src));
    }

    // ----------------------------------------------------------------

    private static string AllText(PdfDocumentSource src)
        => string.Join(" ", Enumerable.Range(0, src.PageCount)
            .SelectMany(p => src.LayoutFor(p)?.Lines ?? Array.Empty<Tetitulz.Pdf.Base.Operations.Extraction.PdfTextLine>())
            .Select(ln => ln.Text));

    private static byte[] Encrypt(byte[] plain, string user, string owner)
    {
        using var input = new MemoryStream(plain);
        using var output = new MemoryStream();
        PdfEncryptor.Encrypt(input, output, new EncryptionParameters
        {
            UserPassword = user,
            OwnerPassword = owner,
            Algorithm = EncryptionAlgorithm.AES128
        });
        return output.ToArray();
    }
}
