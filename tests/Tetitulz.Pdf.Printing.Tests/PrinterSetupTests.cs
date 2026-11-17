// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text;

namespace Tetitulz.Pdf.Printing.Tests;

/// <summary>
/// The command the installer hands an elevated PowerShell: every value in it is a quoted literal,
/// whatever characters it holds, and the whole travels as one encoded argument.
/// </summary>
public sealed class PrinterSetupTests
{
    /// <summary>
    /// PowerShell ends a single-quoted string at five characters, not one. Each is doubled — which
    /// PowerShell reads as the character — and nothing else is touched.
    /// </summary>
    [Theory]
    [InlineData('\'')]
    [InlineData((char)0x2018)]   // left single quotation mark
    [InlineData((char)0x2019)]   // right single quotation mark
    [InlineData((char)0x201A)]   // single low-9 quotation mark
    [InlineData((char)0x201B)]   // single high-reversed-9 quotation mark
    public void Literal_DoublesEveryCharacterThatEndsASingleQuotedString(char quote)
    {
        string value = "a" + quote + "; calc; " + quote + "b";

        string literal = PrinterSetup.Literal(value);

        Assert.Equal("'a" + quote + quote + "; calc; " + quote + quote + "b'", literal);
    }

    /// <summary>The control: characters that end nothing — a double quote, a dollar, a backtick, the neighbours of the quote range — are left as they are.</summary>
    [Fact]
    public void Literal_LeavesEveryOtherCharacterAlone()
    {
        string value = "Tetitulz \"PDF\" $env:X `n " + (char)0x2017 + (char)0x201C;

        Assert.Equal("'" + value + "'", PrinterSetup.Literal(value));
    }

    /// <summary>
    /// The command names the script, the printer, the pipe and the transcript, each as a literal: a
    /// printer name carrying a quote and a command stays inside its own string, and an apostrophe
    /// in a path — a profile folder called O'Brien — is part of the path.
    /// </summary>
    [Fact]
    public void ScriptCommand_QuotesThePathsAndThePrinterName()
    {
        string name = "x" + (char)0x2019 + "; Remove-Item C:\\ -Recurse; " + (char)0x2018 + "y";

        string command = PrinterSetup.ScriptCommand(@"C:\Users\O'Brien\app\install-printer.ps1", name, "TetitulzPdfPrinter", @"C:\Users\O'Brien\t.txt");

        Assert.Equal(
            @"& 'C:\Users\O''Brien\app\install-printer.ps1' -Name 'x" + (char)0x2019 + (char)0x2019 +
            @"; Remove-Item C:\ -Recurse; " + (char)0x2018 + (char)0x2018 + @"y' -Pipe 'TetitulzPdfPrinter' *> 'C:\Users\O''Brien\t.txt'; exit $LASTEXITCODE",
            command);
    }

    /// <summary>
    /// The install script is told the driver, its package and its signer as literals too: a package
    /// path with an apostrophe is part of the path. The uninstall script is told the signer alone,
    /// whose trust it withdraws; without any of them the command is as it was.
    /// </summary>
    [Fact]
    public void ScriptCommand_QuotesTheDriverItsPackageAndItsSigner()
    {
        const string signer = "0123456789ABCDEF0123456789ABCDEF01234567";
        string command = PrinterSetup.ScriptCommand(@"C:\app\install-printer.ps1", "Contoso PDF", "TetitulzPdfPrinter", @"C:\t.txt",
                                                    "Contoso PostScript Driver", @"C:\Program Files\O'Brien\driver\contoso.inf", signer);

        Assert.Equal(
            @"& 'C:\app\install-printer.ps1' -Name 'Contoso PDF' -Pipe 'TetitulzPdfPrinter' -Driver 'Contoso PostScript Driver' " +
            @"-DriverInf 'C:\Program Files\O''Brien\driver\contoso.inf' -DriverSigner '" + signer + @"' *> 'C:\t.txt'; exit $LASTEXITCODE",
            command);

        string uninstall = PrinterSetup.ScriptCommand(@"C:\app\uninstall-printer.ps1", "Contoso PDF", "TetitulzPdfPrinter", @"C:\t.txt", driverSigner: signer);
        Assert.Contains("-DriverSigner '" + signer + "'", uninstall, StringComparison.Ordinal);
        Assert.DoesNotContain("-DriverInf", uninstall, StringComparison.Ordinal);
        Assert.DoesNotContain("-Driver '", uninstall, StringComparison.Ordinal);
        Assert.DoesNotContain("-Driver", PrinterSetup.ScriptCommand(@"C:\app\uninstall-printer.ps1", "Contoso PDF", "TetitulzPdfPrinter", @"C:\t.txt"), StringComparison.Ordinal);
    }

    /// <summary>An identity carries its driver's signer to the install and the uninstall alike, so the trust the one gives the other withdraws.</summary>
    [Fact]
    public void AnIdentitysDriverSigner_IsCarried()
    {
        var identity = new PrinterIdentity("P", "PPipe", "PData", "POut", "P Driver", @"driver\p.inf", "ABCD");
        Assert.Equal("ABCD", identity.DriverSigner);
        Assert.Null(PrinterIdentity.TetitulzPdf.DriverSigner);
    }

    /// <summary>
    /// A printer installed on another driver than the product's wants installing again; one on the
    /// product's driver — whatever its case — does not, nor one not installed (null) or whose driver
    /// Windows did not say ("").
    /// </summary>
    [Theory]
    [InlineData("Some Other Driver", true)]
    [InlineData(PrinterIdentity.ClassDriverName, false)]
    [InlineData("MICROSOFT PS CLASS DRIVER", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void NeedsDriverUpdate_WhenInstalledOnAnotherDriver(string? installed, bool expected)
        => Assert.Equal(expected, new PrinterSetup(PrinterIdentity.TetitulzPdf).NeedsDriverUpdate(installed));

    /// <summary>The encoded form is the command's UTF-16LE bytes in Base64, which is what <c>-EncodedCommand</c> reads back.</summary>
    [Fact]
    public void EncodedCommand_IsTheCommandInUtf16AsBase64()
    {
        string command = PrinterSetup.ScriptCommand(@"C:\app\install-printer.ps1", "Tetitulz PDF " + (char)0x05D0, "TetitulzPdfPrinter", @"C:\t.txt");

        string encoded = PrinterSetup.EncodedCommand(command);

        Assert.Equal(command, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
        Assert.DoesNotContain(' ', encoded);
        Assert.DoesNotContain('"', encoded);
    }

    /// <summary>
    /// Elevated or through the consent prompt, the script's PowerShell is the system's, named by its full
    /// path and started in the system directory: a bare name is sought in the current directory first,
    /// where a planted <c>powershell.exe</c> would run as administrator.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheElevatedScript_StartsWindowsPowerShellByItsFullPath(bool elevated)
    {
        var start = PrinterSetup.ElevatedStart("-NoProfile", elevated);

        Assert.True(Path.IsPathFullyQualified(start.FileName));
        Assert.StartsWith(Environment.SystemDirectory + Path.DirectorySeparatorChar, start.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(@"\WindowsPowerShell\v1.0\powershell.exe", start.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(start.FileName));
        Assert.Equal(Environment.SystemDirectory, start.WorkingDirectory);
        Assert.Equal(!elevated, start.UseShellExecute);
        Assert.Equal(elevated ? "" : "runas", start.Verb);
    }
}
