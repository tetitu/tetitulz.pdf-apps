// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog;
using Tetitulz.Pdf.Plugins.PostScript;

namespace Tetitulz.Pdf.Printing;

/// <summary>
/// One spooled PostScript job to one PDF. The PDF is named from the document name the spooler gave
/// the job, else the job's DSC <c>%%Title</c> comment, and the time, written beside itself as <c>.partial</c> and
/// renamed when complete, so a program watching the folder never opens half a file. A job that
/// converts to no page keeps its PostScript in the <c>failed</c> folder for diagnosis, for
/// <see cref="KeepFailedJobsFor"/>.
/// </summary>
public sealed class JobConverter
{
    private const int TitleScanBytes = 64 * 1024;
    private const int MaxTitleLength = 100;
    private const int MaxLoggedDiagnostics = 20;

    /// <summary>
    /// The PostScript plugin's ceilings, sized for a print job rather than for untrusted PostScript
    /// embedded in a PDF. A driver's job runs a procedure for every glyph: a four-page Word document
    /// took 41 million interpreter steps — twice the default — in 1.2 seconds, so two billion is about a
    /// minute's conversion. The rest follow the job's own 1 GB cap. A driver downloads a font once a
    /// page under a name of its own, so a long job defines far more fonts than a document names; the
    /// document writer's promotion cap follows the image and font ceilings. Process-wide, which this
    /// process is.
    /// </summary>
    public static void SizeForPrintJobs()
    {
        PsLimits.MaxTotalOps = 2_000_000_000;
        PsLimits.MaxCompositeAllocationBytes = JobListener.MaxJobBytes;
        PsLimits.MaxEmittedContentBytes = 256 * 1024 * 1024;
        PsLimits.MaxEmittedDocumentBytes = JobListener.MaxJobBytes;
        PsLimits.MaxImageXObjects = 65_536;
        PsLimits.MaxFontResources = 4096;
    }

    private readonly string _outputDirectory;
    private readonly string _failedDirectory;
    private readonly bool _keepPostScript;

    /// <summary>
    /// How long a job that did not convert is kept for diagnosis. It is a document someone printed,
    /// in full, so it is not kept for good: the logs that explain it roll off after the same time.
    /// </summary>
    internal static readonly TimeSpan KeepFailedJobsFor = TimeSpan.FromDays(14);

    public JobConverter(string outputDirectory, string failedDirectory, bool keepPostScript)
    {
        _outputDirectory = outputDirectory;
        _failedDirectory = failedDirectory;
        _keepPostScript = keepPostScript;
        Directory.CreateDirectory(_outputDirectory);
        Directory.CreateDirectory(_failedDirectory);
        RemoveOlderThan(_failedDirectory, KeepFailedJobsFor, DateTime.UtcNow);
    }

    /// <summary>
    /// Deletes the PostScript files in <paramref name="directory"/> last written more than
    /// <paramref name="age"/> before <paramref name="nowUtc"/>; how many went. A file that will not
    /// go is logged and left.
    /// </summary>
    internal static int RemoveOlderThan(string directory, TimeSpan age, DateTime nowUtc)
    {
        int removed = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "*.ps"))
        {
            try
            {
                if (nowUtc - File.GetLastWriteTimeUtc(path) <= age) continue;
                File.Delete(path);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "{Path} could not be removed", path);
            }
        }
        if (removed > 0) Log.Information("{Count} job file(s) older than {Age} removed from {Directory}", removed, age, directory);
        return removed;
    }

    /// <summary>
    /// Converts <paramref name="postScriptPath"/>, named from <paramref name="documentName"/> when it
    /// is given; the PDF's path, or null when no page came out. The spooled file is removed unless kept.
    /// </summary>
    public string? Convert(string postScriptPath, DateTime received, string? documentName = null)
    {
        string title = (documentName == null ? null : Clean(documentName)) ?? ReadTitle(postScriptPath) ?? "Print";
        string pdfPath = UniquePath(_outputDirectory, $"{title} {received:yyyy-MM-dd HHmmss}", ".pdf");
        string partial = pdfPath + ".partial";

        PostScriptDocumentResult result;
        var said = new List<string>();
        void Note(string message) { if (said.Count < MaxLoggedDiagnostics) said.Add(message); }
        PostScriptDiagnostics.OnEvent += Note;
        try
        {
            using (var source = File.OpenRead(postScriptPath))
            using (var pdf = File.Create(partial))
                result = PostScriptConversion.ConvertDocument(source, pdf,
                    new PostScriptConversionOptions { AutoRotatePages = PostScriptAutoRotate.PageByPage });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The job {Job} could not be converted", postScriptPath);
            TryDelete(partial);
            KeepFailed(postScriptPath);
            return null;
        }
        finally
        {
            PostScriptDiagnostics.OnEvent -= Note;
        }

        if (result.PageCount == 0 || result.BlankPageOnly)
        {
            Log.Error("The job {Job} converted to no page: {Error}", postScriptPath, result.LastError);
            TryDelete(partial);
            KeepFailed(postScriptPath);
            return null;
        }

        File.Move(partial, pdfPath);
        Log.Information("{Pdf}: {Pages} page(s){Clean}", pdfPath, result.PageCount, result.IsClean ? "" : " — not clean");
        if (!result.IsClean)
        {
            Log.Warning("  {Count} diagnostic(s):", result.DiagnosticCount);
            foreach (string message in said) Log.Warning("    {Message}", message);
            if (result.DiagnosticCount > said.Count) Log.Warning("    … and {More} more", result.DiagnosticCount - said.Count);
            if (result.UnimplementedOperators.Count > 0)
                Log.Warning("  not carried: {Operators}", string.Join(", ", result.UnimplementedOperators));
            if (result.PageLimitReached) Log.Warning("  the page limit was reached; later pages are missing");
            if (result.BudgetExhausted) Log.Warning("  the conversion's budget ran out; the output may be incomplete");
        }

        if (_keepPostScript) File.Move(postScriptPath, Path.ChangeExtension(pdfPath, ".ps"), overwrite: true);
        else TryDelete(postScriptPath);
        return pdfPath;
    }

    /// <summary>
    /// The job's <c>%%Title:</c> as a file name: a DSC text value, its parentheses taken off when it is
    /// a PostScript string, read as UTF-8 when it is valid UTF-8 and Latin-1 otherwise; characters a
    /// file name cannot hold replaced; null when the job names none.
    /// </summary>
    internal static string? ReadTitle(string postScriptPath)
    {
        byte[] head = new byte[TitleScanBytes];
        int length;
        using (var file = File.OpenRead(postScriptPath))
            length = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return TitleFrom(head.AsSpan(0, length));
    }

    internal static string? TitleFrom(ReadOnlySpan<byte> head)
    {
        ReadOnlySpan<byte> marker = "%%Title:"u8;
        int at = 0;
        while (at < head.Length)
        {
            int end = head[at..].IndexOfAny((byte)'\r', (byte)'\n');
            ReadOnlySpan<byte> line = end < 0 ? head[at..] : head.Slice(at, end);
            if (line.StartsWith(marker)) return Clean(Decode(line[marker.Length..]));
            if (end < 0) break;
            at += end + 1;
        }
        return null;
    }

    private static string Decode(ReadOnlySpan<byte> value)
    {
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(value); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(value); }
    }

    public static string? Clean(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '(' && value[^1] == ')') value = value[1..^1].Trim();
        var name = new StringBuilder(value.Length);
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in value) name.Append(Array.IndexOf(invalid, c) >= 0 || char.IsControl(c) ? '_' : c);
        string cleaned = name.ToString().Trim().TrimEnd('.');
        if (cleaned.Length > MaxTitleLength) cleaned = cleaned[..MaxTitleLength].TrimEnd();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary><paramref name="stem"/> + <paramref name="extension"/> in <paramref name="directory"/>, numbered when that name is taken.</summary>
    public static string UniquePath(string directory, string stem, string extension)
    {
        string path = Path.Combine(directory, stem + extension);
        for (int n = 2; File.Exists(path) || File.Exists(path + ".partial"); n++)
            path = Path.Combine(directory, $"{stem} ({n}){extension}");
        return path;
    }

    private void KeepFailed(string postScriptPath)
    {
        string kept = UniquePath(_failedDirectory, Path.GetFileNameWithoutExtension(postScriptPath), ".ps");
        try
        {
            File.Move(postScriptPath, kept);
            Log.Information("  the job is kept at {Kept}", kept);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "  the job could not be kept");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException ex) { Log.Warning(ex, "{Path} could not be deleted", path); }
    }
}
