// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tetitulz.Pdf.Core.Text;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    public sealed partial class PdfViewerControl
    {
        // FindMatchCount, FindMatchText and FindMatchRect report the search's ANSWER — which word
        // was matched and which glyphs are highlighted — which there is no other way to ask.
        // Folding changes string offsets, so "it found something" and "it highlighted the right
        // letters" are two different claims, and only the second can see a broken index map.
        // FindMatchRect is also the production path: ScrollToMatch centres the viewport on it.

        /// <summary>
        /// Which Hebrew spelling differences the find box folds away before matching.
        /// </summary>
        /// <remarks>
        /// <para><b>One search decision.</b> The find box folds through
        /// <see cref="HebrewSearchNormalizer"/>, as <c>PdfTextSearch</c>, the redaction path and the
        /// autolinker do, so the viewer finds a word in a pointed document wherever the library
        /// does.</para>
        ///
        /// <para><b>Defaults to <see cref="HebrewFolding.Default"/>, which deliberately excludes
        /// <see cref="HebrewFolding.FinalForms"/>.</b> Folding a final form away erases a word
        /// boundary, so a search for <c>ארץ</c> would highlight the middle of <c>ארצות</c> — a hit
        /// the document does not contain. Someone typing in a find box is looking for what is on
        /// the page, and a highlight over text that does not match it is worse than a miss.</para>
        ///
        /// <para><b>Public, because an internal setting is one a host cannot change.</b> A host
        /// embedding this control may reasonably want folding off (to match a
        /// library caller that has not opted in) or <c>FinalForms</c> on (for a corpus known to use
        /// the wrong letter forms).</para>
        ///
        /// <para><b>The library's own <c>TextSearchOptions.HebrewFolding</c> defaults to
        /// <c>None</c>, and the difference is deliberate rather than drift.</b> A find box is driven
        /// by a human who just typed the word and can see the page, so a miss is the visible failure
        /// and folding is the safe default. A library search feeds code that cannot tell a fold from
        /// a match, and its result may already be depended on — so there the safe default is to
        /// change nothing. Same fold, opposite risk.</para>
        /// </remarks>
        [DefaultValue(HebrewFolding.Default)]
        public HebrewFolding FindHebrewFolding { get; set; } = HebrewFolding.Default;

        /// <summary>Matches currently highlighted.</summary>
        internal int FindMatchCount => _findMatches.Count;

        /// <summary>
        /// The text one highlight covers, spelled as the page's extracted text spells it — pointed,
        /// with its final forms — never the folded form that was matched.
        /// </summary>
        internal string? FindMatchText(int index)
        {
            if (index < 0 || index >= _findMatches.Count) return null;
            var m = _findMatches[index];
            string text = GetTextIndex(m.Page).Text;
            if (m.Start < 0 || m.Length < 0 || m.Start + m.Length > text.Length) return null;
            return text.Substring(m.Start, m.Length);
        }

        /// <summary>
        /// The canvas rectangle painted for one match — the same call <c>DrawPage</c> makes for it,
        /// and the one <see cref="ScrollToMatch"/> centres the viewport on.
        /// </summary>
        internal RectangleF? FindMatchRect(int index)
        {
            if (index < 0 || index >= _findMatches.Count) return null;
            var m = _findMatches[index];
            if (!TryGetBounds(m.Page, out Rectangle bounds)) return null;
            return RectForRange(m.Page, bounds, m.Start, m.Length);
        }

        /// <summary>
        /// Every occurrence of <paramref name="term"/> in <paramref name="text"/>, reported in
        /// <b>the original text's</b> coordinates.
        /// </summary>
        /// <param name="text">The page's extracted text, exactly as the glyph index spells it.</param>
        /// <param name="term">What the user typed.</param>
        /// <param name="folding">Which Hebrew differences to erase before matching.</param>
        /// <param name="limit">
        /// Keep at most this many matches. <b>Zero keeps none and still probes for one</b>, so a
        /// caller whose budget ran out on an earlier page still learns that this one had a match —
        /// which is the difference between the result label reading "2000" and "2000+".
        /// </param>
        /// <param name="truncated">Set when a further match existed and was dropped at the limit.</param>
        /// <remarks>
        /// <para><b>The index map is the whole point.</b> Matching happens in folded space, where
        /// niqqud has been stripped and every offset past the first stripped mark has moved. Each
        /// highlight is computed by <c>RectForRange</c> from offsets into the ORIGINAL string, via
        /// <c>PageTextIndex.Starts</c> — so returning a folded offset would find the right word and
        /// paint the wrong glyphs, a defect that reads as a rendering bug rather than a search one.
        /// </para>
        ///
        /// <para>The end likewise comes from the map, never from <c>pos + needle.Length</c>: for
        /// pointed Hebrew the folded match is shorter than the span it covers, so a highlight sized
        /// in folded units stops short of the word's last letters.</para>
        ///
        /// <para>Stepping is non-overlapping, as this box has always been: one keystroke rescans
        /// every page, and overlapping matches of a one-character term multiply the repaint cost for
        /// a distinction nobody reading the page can see.</para>
        /// </remarks>
        internal static List<(int Start, int Length)> FoldedMatches(
            string text, string term, HebrewFolding folding, int limit, out bool truncated)
        {
            var found = new List<(int Start, int Length)>();
            truncated = false;
            if (limit < 0 || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term)) return found;

            string haystack = HebrewSearchNormalizer.Fold(text, folding, out int[] source);
            string needle = HebrewSearchNormalizer.Fold(term, folding);
            // A term of nothing but niqqud folds to the empty string, and IndexOf answers that with
            // 0 at every position — every page would highlight from end to end.
            if (needle.Length == 0) return found;

            int start = 0;
            while (start <= haystack.Length - needle.Length)
            {
                int pos = haystack.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase);
                if (pos < 0) break;
                if (found.Count >= limit) { truncated = true; break; }

                int origStart = source[pos];
                int origEnd = source[pos + needle.Length - 1] + 1;
                found.Add((origStart, origEnd - origStart));

                start = pos + needle.Length;
            }
            return found;
        }

        private void ShowFindBar()
        {
            if (_doc == null) return;
            LayoutFindBar();
            _findBar.Visible = true;
            _findBar.BringToFront();
            _findBox.Focus();
            _findBox.SelectAll();
            if (!string.IsNullOrEmpty(_findBox.Text)) RunSearch();
        }

        private void HideFindBar()
        {
            CancelSearch();
            _findBar.Visible = false;
            _findMatches.Clear();
            _findByPage.Clear();
            _findTruncated = false;
            _findIndex = -1;
            _findResult.Text = "";
            _surface.Invalidate();
            _surface.Focus();
        }

        private void FindBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = e.SuppressKeyPress = true;
                FindStep((e.Modifiers & Keys.Shift) != 0 ? -1 : +1);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = e.SuppressKeyPress = true;
                HideFindBar();
            }
        }

        /// <summary>
        /// Searches for the find box's term: in the background when there is a window to post results
        /// to, page by page in page order, the count growing as it goes; otherwise here and now. A new
        /// term cancels the search under way.
        /// </summary>
        private void RunSearch()
        {
            CancelSearch();
            if (_doc == null || !IsHandleCreated || IsDisposed) { RunSearchNow(); return; }

            _findMatches.Clear();
            _findByPage.Clear();
            _findTruncated = false;
            _findIndex = -1;
            string term = _findBox.Text;
            if (string.IsNullOrEmpty(term))
            {
                _findResult.Text = "";
                _surface.Invalidate();
                return;
            }

            int id = _searchId;
            var cancel = new CancellationTokenSource();
            _searchCancel = cancel;
            _searchRunning = true;
            _findResult.Text = _strings.Searching;
            var job = new SearchJob(id, _doc.ShownBytes, _pageCount, new Dictionary<int, string>(_pageTexts), term,
                FindHebrewFolding, cancel.Token);
            // A thread of its own: a search of a long document runs for seconds, and a pool thread
            // waits behind whatever else the host's pool is busy with before it starts.
            Task.Factory.StartNew(() => SearchInBackground(job), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>Stops the search under way; whatever it still posts is dropped.</summary>
        private void CancelSearch()
        {
            _searchId++;
            _searchCancel?.Cancel();
            _searchCancel?.Dispose();
            _searchCancel = null;
            _searchRunning = false;
        }

        private int _searchId;
        private CancellationTokenSource? _searchCancel;
        private bool _searchRunning;

        /// <summary>Pages the worker searches before it posts what it found.</summary>
        private const int SearchBatchPages = 16;

        /// <summary>One background search: what it reads, all of it taken on the UI thread when it started.</summary>
        private sealed class SearchJob
        {
            public SearchJob(int id, byte[] bytes, int pageCount, Dictionary<int, string> knownTexts, string term,
                HebrewFolding folding, CancellationToken cancel)
            {
                Id = id; Bytes = bytes; PageCount = pageCount; KnownTexts = knownTexts; Term = term;
                Folding = folding; Cancel = cancel;
            }
            public readonly int Id;
            public readonly byte[] Bytes;
            public readonly int PageCount;
            public readonly Dictionary<int, string> KnownTexts;
            public readonly string Term;
            public readonly HebrewFolding Folding;
            public readonly CancellationToken Cancel;
        }

        /// <summary>What a run of pages gave: each page's matches, and the text of a page it had to extract.</summary>
        private sealed class SearchBatch
        {
            public readonly List<(int Page, string? ExtractedText, List<(int Start, int Length)> Matches)> Pages
                = new List<(int, string?, List<(int, int)>)>();
            public bool Done;
            public bool Truncated;
            public string? Error;
        }

        /// <summary>
        /// On a pool thread: every page's text, through a document of its own (the text layouts'
        /// document is the UI thread's), searched in page order, posted a batch at a time.
        /// </summary>
        private void SearchInBackground(SearchJob job)
        {
            Tetitulz.Pdf.Core.Document.PdfDocument? document = null;
            var batch = new SearchBatch();
            int budget = MaxFindMatches;
            try
            {
                for (int p = 0; p < job.PageCount; p++)
                {
                    if (job.Cancel.IsCancellationRequested) return;
                    string? extracted = null;
                    if (!job.KnownTexts.TryGetValue(p, out string? text))
                    {
                        try
                        {
                            document ??= Tetitulz.Pdf.Core.Document.PdfDocument.Open(new System.IO.MemoryStream(job.Bytes, false));
                            text = extracted = BuildTextIndex(
                                Tetitulz.Pdf.Base.Operations.Extraction.PdfStructuredTextExtractor.Extract(document, p)).Text;
                            string lastError = Tetitulz.Pdf.Base.Operations.Extraction.PdfStructuredTextExtractor.LastError;
                            if (!string.IsNullOrEmpty(lastError)) batch.Error ??= lastError;
                        }
                        catch (Exception ex)
                        {
                            text = "";
                            batch.Error ??= $"page {p + 1}: {ex.Message}";
                        }
                    }
                    var matches = FoldedMatches(text!, job.Term, job.Folding, budget, out bool truncated);
                    budget -= matches.Count;
                    batch.Pages.Add((p, extracted, matches));
                    if (truncated) { batch.Truncated = true; break; }
                    if (batch.Pages.Count >= SearchBatchPages)
                    {
                        PostSearch(job, batch);
                        batch = new SearchBatch { Error = batch.Error };
                    }
                }
            }
            catch (Exception ex)
            {
                batch.Error ??= ex.Message;
            }
            finally
            {
                document?.Dispose();
            }
            batch.Done = true;
            PostSearch(job, batch);
        }

        private void PostSearch(SearchJob job, SearchBatch batch)
        {
            if (job.Cancel.IsCancellationRequested) return;
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() => OnSearchBatch(job, batch)));
            }
            catch (InvalidOperationException) { /* the window went: nobody to show the result to */ }
        }

        /// <summary>On the UI thread: a batch of a search still current joins the matches.</summary>
        private void OnSearchBatch(SearchJob job, SearchBatch batch)
        {
            if (job.Id != _searchId || _doc == null) return;
            bool sameDocument = ReferenceEquals(job.Bytes, _doc.ShownBytes);
            foreach (var (page, extracted, matches) in batch.Pages)
            {
                // The text is the spelling the page's own index builds, so the next search reads it here.
                if (sameDocument && extracted != null && !_pageTexts.ContainsKey(page)) _pageTexts[page] = extracted;
                foreach (var m in matches) AddMatch(new FindMatch { Page = page, Start = m.Start, Length = m.Length });
            }
            if (!string.IsNullOrEmpty(batch.Error) && sameDocument) _doc.ReportTextError(batch.Error!);

            if (_findIndex < 0 && _findMatches.Count > 0)
            {
                _findIndex = 0;
                ScrollToMatch(0);
            }
            if (batch.Done)
            {
                _searchRunning = false;
                _findTruncated = batch.Truncated;
            }
            ShowFindResult();
            _surface.Invalidate();
        }

        /// <summary>The find bar's result: the current match of how many, still running, or none.</summary>
        private void ShowFindResult()
        {
            if (_findMatches.Count == 0)
            {
                _findResult.Text = _searchRunning ? _strings.Searching : _strings.NotFound;
                if (!_searchRunning)
                {
                    string? err = _doc?.FirstExtractionErrorIn(_strings);
                    if (!string.IsNullOrEmpty(err)) _statusLabel.Text = err;
                }
                return;
            }
            string format = _searchRunning ? _strings.FindResultSearchingFormat
                : _findTruncated ? _strings.FindResultTruncatedFormat : _strings.FindResultFormat;
            _findResult.Text = PdfViewerStrings.Format(format, _findIndex + 1, _findMatches.Count);
        }

        /// <summary>Whether a background search is still running — what a test waits on.</summary>
        internal bool SearchRunning => _searchRunning;

        /// <summary>Pumps messages until the search under way is done, or the time runs out; true when done.</summary>
        internal bool DrainSearch(int timeoutMs = 10_000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (_searchRunning && DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(2);
            }
            return !_searchRunning;
        }

        /// <summary>The search, here and now — a control with no window has nobody to post to.</summary>
        private void RunSearchNow()
        {
            _findMatches.Clear();
            _findByPage.Clear();
            _findTruncated = false;
            _findIndex = -1;

            string term = _findBox.Text;
            if (_doc == null || string.IsNullOrEmpty(term))
            {
                _findResult.Text = "";
                _surface.Invalidate();
                return;
            }

            for (int p = 0; p < _pageCount && !_findTruncated; p++)
            {
                string text = PageText(p);
                if (text.Length == 0) continue;

                // The list is unbounded and its content is file-controlled: a one-character term
                // against a dense document produces hundreds of matches per page, and every one is
                // repainted on every scroll notch. The remaining budget is the limit.
                var matches = FoldedMatches(text, term, FindHebrewFolding,
                                            MaxFindMatches - _findMatches.Count, out _findTruncated);

                // Start and Length are offsets into `text` — the page's own extracted string — even
                // when folding matched a shorter one. RectForRange indexes PageTextIndex.Starts
                // with them, so anything else moves every highlight rectangle on the page.
                foreach (var m in matches)
                    AddMatch(new FindMatch { Page = p, Start = m.Start, Length = m.Length });
            }

            if (_findMatches.Count > 0)
            {
                _findIndex = 0;
                _findResult.Text = PdfViewerStrings.Format(
                    _findTruncated ? _strings.FindResultTruncatedFormat : _strings.FindResultFormat,
                    1, _findMatches.Count);
                ScrollToMatch(0);
            }
            else
            {
                _findResult.Text = _strings.NotFound;
                // Every tolerated extraction failure, not only the text one: "no matches" and
                // "the text layer never loaded" must not read the same in the status bar.
                string? err = _doc?.FirstExtractionErrorIn(_strings);
                if (!string.IsNullOrEmpty(err)) _statusLabel.Text = err;
            }
            _surface.Invalidate();
        }

        /// <summary>Appends a match and indexes it by page, so painting never scans the whole list.</summary>
        private void AddMatch(FindMatch match)
        {
            int index = _findMatches.Count;
            _findMatches.Add(match);
            if (!_findByPage.TryGetValue(match.Page, out var list))
            {
                list = new System.Collections.Generic.List<int>();
                _findByPage[match.Page] = list;
            }
            list.Add(index);
        }

        private void FindStep(int delta)
        {
            if (_findMatches.Count == 0) return;
            _findIndex = (_findIndex + delta + _findMatches.Count) % _findMatches.Count;
            ShowFindResult();
            ScrollToMatch(_findIndex);
            _surface.Invalidate();
        }

        private void ScrollToMatch(int i)
        {
            if (i < 0 || i >= _findMatches.Count) return;
            var m = _findMatches[i];

            bool pageChanged = m.Page != _currentPage;
            if (_layoutMode == PdfViewLayoutMode.SinglePage)
            {
                if (pageChanged) GoToPage(m.Page, forceScroll: true);   // raises PageChanged itself
            }
            else
            {
                // In Continuous layout this assigned _currentPage directly and updated only the
                // control's own toolbar, so the documented PageChanged event never fired for search
                // navigation: a host wiring it to its own page label sat on page 1 while the viewer
                // scrolled to page 40, with no way to notice the disagreement.
                _currentPage = m.Page;
                if (pageChanged) RaisePageChanged();
            }

            var r = FindMatchRect(i);
            if (r.HasValue)
            {
                // Centre the match horizontally, but put it a third of the way down rather
                // than dead centre so the text that follows it stays on screen.
                int targetX = SaturatingCast.ToInt(r.Value.Left) - _surface.ClientSize.Width / 2;
                int targetY = SaturatingCast.ToInt(r.Value.Top) - _surface.ClientSize.Height / 3;
                _surface.AutoScrollPosition = new Point(Math.Max(0, targetX), Math.Max(0, targetY));
            }
            UpdatePageBox();
            HighlightCurrentThumb();
        }
    }
}
