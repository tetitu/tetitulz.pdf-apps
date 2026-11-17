// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Tetitulz.Pdf.Core.Document;
using Tetitulz.Pdf.Core.Objects;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// What a document's <c>/ViewerPreferences</c> asks of the print dialog (ISO 32000-1 §12.2,
    /// Table 150), read into values that outlive the document they came from.
    /// </summary>
    internal sealed class DocumentPrintPreferences
    {
        internal static readonly DocumentPrintPreferences None = new DocumentPrintPreferences(false, null, null, Array.Empty<(int, int)>());

        private DocumentPrintPreferences(bool actualSize, int? copies, string? duplex, IReadOnlyList<(int From, int To)> ranges)
        {
            ActualSize = actualSize;
            Copies = copies;
            Duplex = duplex;
            Ranges = ranges;
        }

        /// <summary><c>/PrintScaling /None</c>: print at the page's own size, not fitted.</summary>
        internal bool ActualSize { get; }

        /// <summary><c>/NumCopies</c> when it is a positive integer, kept within what a printer takes.</summary>
        internal int? Copies { get; }

        /// <summary><c>/Duplex</c>: <c>Simplex</c>, <c>DuplexFlipShortEdge</c> or <c>DuplexFlipLongEdge</c>, else null.</summary>
        internal string? Duplex { get; }

        /// <summary>
        /// <c>/PrintPageRange</c>'s pairs as zero-based page indices, each within the document and in
        /// order; empty when the entry is absent or any pair is not two such page numbers.
        /// </summary>
        internal IReadOnlyList<(int From, int To)> Ranges { get; }

        /// <param name="prefs">The document's viewer preferences.</param>
        /// <param name="pageCount">The document's pages, which a range must lie within.</param>
        /// <param name="resolve">Follows an indirect range element (§7.3.10); null when every element is direct.</param>
        internal static DocumentPrintPreferences From(PdfViewerPreferences prefs, int pageCount, Func<PdfObject, PdfObject>? resolve)
        {
            int? copies = prefs.NumCopies is int n && n >= 1 ? Math.Min(n, short.MaxValue) : (int?)null;
            string? duplex = prefs.Duplex is "Simplex" or "DuplexFlipShortEdge" or "DuplexFlipLongEdge" ? prefs.Duplex : null;
            return new DocumentPrintPreferences(prefs.PrintScaling == "None", copies, duplex,
                RangesOf(prefs.PrintPageRange, pageCount, resolve ?? (o => o)));
        }

        /// <summary>
        /// Table 150: "an array containing an even number of integers … the first and last pages in a
        /// sub-range", page numbers from 1. One pair that is not a sub-range of the document voids
        /// the entry, rather than a dialog filled with part of what was asked.
        /// </summary>
        private static IReadOnlyList<(int, int)> RangesOf(PdfArray? range, int pageCount, Func<PdfObject, PdfObject> resolve)
        {
            if (range == null || range.Count == 0 || range.Count % 2 != 0) return Array.Empty<(int, int)>();
            var pairs = new List<(int, int)>(range.Count / 2);
            for (int i = 0; i < range.Count; i += 2)
            {
                if (!(resolve(range[i]) is PdfNumber a) || !a.IsInteger
                    || !(resolve(range[i + 1]) is PdfNumber b) || !b.IsInteger)
                    return Array.Empty<(int, int)>();
                long from = a.LongValue, to = b.LongValue;
                if (from < 1 || to < from || to > pageCount) return Array.Empty<(int, int)>();
                pairs.Add(((int)from - 1, (int)to - 1));
            }
            return pairs;
        }
    }
}
