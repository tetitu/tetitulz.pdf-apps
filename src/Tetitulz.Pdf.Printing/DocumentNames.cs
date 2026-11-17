// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
namespace Tetitulz.Pdf.Printing;

/// <summary>
/// The file name a print suggests: the document name the spooler gave the job, without the
/// application's own prefix ("Microsoft Word - ") or the document's own extension, made a file name —
/// "Microsoft Word - Invoice 42.docx" suggests "Invoice 42".
/// </summary>
public static class DocumentNames
{
    private static readonly string[] ApplicationPrefixes =
    {
        "Microsoft Word - ", "Microsoft Excel - ", "Microsoft PowerPoint - ", "Microsoft Visio - ",
    };

    private static readonly string[] DocumentExtensions =
    {
        ".docx", ".docm", ".doc", ".rtf", ".odt", ".xlsx", ".xlsm", ".xls", ".ods", ".pptx", ".ppt",
        ".odp", ".vsdx", ".txt", ".pdf", ".htm", ".html", ".md",
    };

    public static string Suggest(string? documentName)
    {
        string name = documentName?.Trim() ?? "";
        foreach (string prefix in ApplicationPrefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { name = name[prefix.Length..]; break; }
        foreach (string extension in DocumentExtensions)
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) { name = name[..^extension.Length]; break; }
        return JobConverter.Clean(name) ?? "Print";
    }
}
