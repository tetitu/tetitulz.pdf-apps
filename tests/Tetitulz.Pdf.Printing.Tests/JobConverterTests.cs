// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text;
using Tetitulz.Pdf.Core.Document;
using Tetitulz.Pdf.Core.Text;

namespace Tetitulz.Pdf.Printing.Tests;

/// <summary>
/// A spooled job to a PDF: named from the spooler's document name, else the job's <c>%%Title</c>, and
/// the time; written as <c>.partial</c> and renamed; a job with no page kept in the failed folder.
/// </summary>
public sealed class JobConverterTests : IDisposable
{
    private const string OnePage =
        "%!PS-Adobe-3.0\n%%Title: (Quarterly report)\n%%EndComments\n"
        + "/Helvetica findfont 12 scalefont setfont 72 720 moveto (Hello) show showpage\n%%EOF\n";

    private static readonly DateTime Received = new(2026, 10, 31, 12, 0, 5);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vp-tests-" + Guid.NewGuid().ToString("N"));
    private string Output => Path.Combine(_root, "out");
    private string Failed => Path.Combine(_root, "failed");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Job(string postScript)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "job-1.ps");
        File.WriteAllText(path, postScript, Encoding.Latin1);
        return path;
    }

    /// <summary>The job's title names the PDF; its page and text come through; the spooled job is removed and no partial file stays.</summary>
    [Fact]
    public void AJobWithAPage_IsWrittenUnderItsTitle()
    {
        string job = Job(OnePage);
        string? pdf = new JobConverter(Output, Failed, keepPostScript: false).Convert(job, Received);

        Assert.Equal(Path.Combine(Output, "Quarterly report 2026-10-31 120005.pdf"), pdf);
        using (var doc = PdfDocument.Open(pdf!))
        {
            Assert.Single(doc.Pages);
            Assert.Contains("Hello", PdfTextExtractor.ExtractText(doc));
        }
        Assert.False(File.Exists(job));
        Assert.Equal(new[] { pdf }, Directory.GetFiles(Output));
    }

    /// <summary>
    /// Sized for print jobs, the ceilings admit a driver's job: its images, and the fonts it downloads
    /// once a page under names of its own, which a long job makes far more of than a document names.
    /// </summary>
    [Fact]
    public void SizedForPrintJobs_TheImageAndFontCeilingsAdmitADriversJob()
    {
        try
        {
            JobConverter.SizeForPrintJobs();

            Assert.Equal(65_536, Tetitulz.Pdf.Plugins.PostScript.PsLimits.MaxImageXObjects);
            Assert.Equal(4096, Tetitulz.Pdf.Plugins.PostScript.PsLimits.MaxFontResources);
        }
        finally { Tetitulz.Pdf.Plugins.PostScript.PsLimits.ResetToDefaults(); }
    }

    /// <summary>
    /// A job kept for diagnosis is a printed document, so it is not kept for good: one older than
    /// the keeping time is gone when a converter next starts, one inside it stays, and a file that
    /// is not a job is not touched.
    /// </summary>
    [Fact]
    public void AFailedJobPastItsKeepingTime_IsRemovedWhenAConverterStarts()
    {
        Directory.CreateDirectory(Failed);
        string old = Path.Combine(Failed, "old.ps"), recent = Path.Combine(Failed, "recent.ps"), other = Path.Combine(Failed, "notes.txt");
        foreach (string path in new[] { old, recent, other }) File.WriteAllText(path, "%!PS\n");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow - JobConverter.KeepFailedJobsFor - TimeSpan.FromHours(1));
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow - JobConverter.KeepFailedJobsFor + TimeSpan.FromHours(1));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddYears(-1));

        _ = new JobConverter(Output, Failed, keepPostScript: false);

        Assert.Equal(new[] { other, recent }, Directory.GetFiles(Failed).Order());
    }

    /// <summary>The spooler's document name wins over the job's title; a kept job sits beside its PDF.</summary>
    [Fact]
    public void TheDocumentName_WinsOverTheTitle_AndAKeptJobSitsBesideThePdf()
    {
        string job = Job(OnePage);
        string? pdf = new JobConverter(Output, Failed, keepPostScript: true).Convert(job, Received, "Invoice: 42");

        Assert.Equal(Path.Combine(Output, "Invoice_ 42 2026-10-31 120005.pdf"), pdf);
        Assert.True(File.Exists(Path.ChangeExtension(pdf!, ".ps")));
        Assert.False(File.Exists(job));
    }

    /// <summary>A job that draws no page writes no PDF and is kept in the failed folder; the control is a job with a page.</summary>
    [Fact]
    public void AJobWithNoPage_IsKeptAsFailed()
    {
        string job = Job("%!PS-Adobe-3.0\n%%Title: (Empty)\n%%EOF\n");
        Assert.Null(new JobConverter(Output, Failed, keepPostScript: false).Convert(job, Received));

        Assert.Empty(Directory.GetFiles(Output));
        Assert.Equal(new[] { Path.Combine(Failed, "job-1.ps") }, Directory.GetFiles(Failed));
        Assert.False(File.Exists(job));
        Assert.NotNull(new JobConverter(Output, Failed, keepPostScript: false).Convert(Job(OnePage), Received));
    }

    [Theory]
    [InlineData("%!PS\n%%Title: (Report 7)\n", "Report 7")]
    [InlineData("%!PS\r%%Title: Plain words\r%%EndComments\r", "Plain words")]
    [InlineData("%!PS\n%%Creator: x\n%%Title: a/b:c?\n", "a_b_c_")]
    [InlineData("%!PS\n%%Creator: x\n", null)]
    [InlineData("%!PS\n%%Title: ()\n", null)]
    [InlineData("%!PS\n%%Title: (Ends in dots...)\n", "Ends in dots")]
    public void TitleFrom_ReadsTheDscTitle(string head, string? expected)
        => Assert.Equal(expected, JobConverter.TitleFrom(Encoding.Latin1.GetBytes(head)));

    /// <summary>A title is UTF-8 when its bytes are valid UTF-8, Latin-1 otherwise.</summary>
    [Fact]
    public void TitleFrom_ReadsUtf8_ElseLatin1()
    {
        byte[] hebrew = Encoding.UTF8.GetBytes("%%Title: (דוח)\n");
        byte[] latin1 = Encoding.Latin1.GetBytes("%%Title: (Café)\n");
        Assert.Equal("דוח", JobConverter.TitleFrom(hebrew));
        Assert.Equal("Café", JobConverter.TitleFrom(latin1));
    }

    /// <summary>A name is cut to a hundred characters.</summary>
    [Fact]
    public void Clean_CutsALongName()
    {
        Assert.Equal(new string('x', 100), JobConverter.Clean(new string('x', 150)));
        Assert.Equal(new string('x', 99), JobConverter.Clean(new string('x', 99)));
    }

    /// <summary>A taken name is numbered — a finished PDF or one still being written.</summary>
    [Fact]
    public void UniquePath_NumbersATakenName()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(Path.Combine(_root, "a.pdf"), JobConverter.UniquePath(_root, "a", ".pdf"));
        File.WriteAllText(Path.Combine(_root, "a.pdf"), "");
        Assert.Equal(Path.Combine(_root, "a (2).pdf"), JobConverter.UniquePath(_root, "a", ".pdf"));
        File.WriteAllText(Path.Combine(_root, "a (2).pdf.partial"), "");
        Assert.Equal(Path.Combine(_root, "a (3).pdf"), JobConverter.UniquePath(_root, "a", ".pdf"));
    }
}
