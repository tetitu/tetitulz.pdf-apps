// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Tetitulz.Pdf.Base;
using Tetitulz.Pdf.Base.Encryption;
using Tetitulz.Pdf.Base.Operations;
using Tetitulz.Pdf.Base.Operations.Extraction;
using Tetitulz.Pdf.Base.Operations.Forms;
using Tetitulz.Pdf.Base.Operations.Structure;
using Tetitulz.Pdf.Base.Rendering;
using Tetitulz.Pdf.Core.Constants;
using Tetitulz.Pdf.Core.Document;
using Tetitulz.Pdf.Core.Objects;
using Tetitulz.Pdf.Encryption;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Supplies a candidate password for an encrypted document. Called with the 1-based
    /// <paramref name="attempt"/> number and the parsed <paramref name="info"/>; returns the
    /// password to try, or <c>null</c> to give up (which surfaces a
    /// <see cref="PdfPasswordRequiredException"/>).
    /// </summary>
    internal delegate string? PasswordProvider(int attempt, EncryptionInfo info);

    /// <summary>
    /// Owns the loaded PDF bytes and is the single seam between the viewer control and
    /// the Tetitulz.Pdf library. It reads page geometry once, renders pages on demand,
    /// and lazily caches the structured-text layout, link annotations and bookmarks.
    /// Everything here is net462-safe (no .NET 6+ BCL surface).
    /// </summary>
    internal sealed class PdfDocumentSource : IDisposable
    {
        private readonly byte[] _bytes;
        private readonly PageGeometry[] _geometry;

        private PdfDocument? _textDocument;
        private Dictionary<int, List<PdfLink>>? _linksByPage;
        private IReadOnlyList<PdfBookmark>? _bookmarks;

        public int PageCount { get; }

        /// <summary>Non-null when the text layer could not be fully extracted (exception or partial failure).</summary>
        public string? TextExtractionError { get; private set; }

        /// <summary>Non-null when link extraction threw, rather than finding no links.</summary>
        /// <remarks>
        /// <para><b>"No links" and "extraction threw" are different answers.</b> Without this a PDF
        /// whose <c>/Annots</c> is an unresolvable reference would simply appear to have no links: no
        /// hand cursor, no navigation, no message — as text extraction failures are told apart
        /// too.</para>
        /// </remarks>
        public string? LinkExtractionError { get; private set; }

        /// <summary>Non-null when outline extraction threw, rather than finding no bookmarks.</summary>
        public string? BookmarkExtractionError { get; private set; }

        /// <summary>
        /// The first tolerated failure of this load, phrased for a status bar, or <c>null</c>.
        /// </summary>
        public string? FirstExtractionError => FirstExtractionErrorIn(PdfViewerStrings.English());

        /// <summary>The first tolerated failure of this load, phrased with <paramref name="strings"/>, or <c>null</c>.</summary>
        internal string? FirstExtractionErrorIn(PdfViewerStrings strings) =>
            !string.IsNullOrEmpty(TextExtractionError) ? PdfViewerStrings.Format(strings.TextUnavailableFormat, TextExtractionError!)
            : !string.IsNullOrEmpty(LinkExtractionError) ? PdfViewerStrings.Format(strings.LinksUnavailableFormat, LinkExtractionError!)
            : !string.IsNullOrEmpty(BookmarkExtractionError) ? PdfViewerStrings.Format(strings.BookmarksUnavailableFormat, BookmarkExtractionError!)
            : null;

        /// <summary>Records a per-page text-index failure, which is otherwise indistinguishable from a blank page.</summary>
        internal void ReportPageTextError(int pageIndex, string message)
            => ReportTextError($"page {pageIndex + 1}: {message}");

        /// <summary>Records a text-layer failure already phrased with its page, the first one kept.</summary>
        internal void ReportTextError(string message)
        {
            if (string.IsNullOrEmpty(TextExtractionError)) TextExtractionError = message;
        }

        private PdfDocumentSource(byte[] bytes, int pageCount, PageGeometry[] geometry,
            byte[] originalBytes, string? password, byte[] openedBytes)
        {
            _bytes = bytes;
            PageCount = pageCount;
            _geometry = geometry;
            OriginalBytes = originalBytes;
            Password = password;
            OpenedBytes = openedBytes;
        }

        /// <summary>The file exactly as it was opened — encrypted if it was — which a save appends to.</summary>
        internal byte[] OriginalBytes { get; }

        /// <summary>The password that opened the file; null when it is not encrypted.</summary>
        internal string? Password { get; }

        /// <summary>The decrypted file as opened, before any fill: what every fill starts from.</summary>
        internal byte[] OpenedBytes { get; }

        public static PdfDocumentSource FromBytes(byte[] bytes, PasswordProvider? passwordProvider = null)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            byte[] opened = DecryptIfNeeded(bytes, passwordProvider, out string? password, out PdfPermissions? permissions);
            PdfDocumentSource source = Build(opened, bytes, password, opened);
            source.Permissions = permissions;
            return source;
        }

        /// <summary>
        /// The permissions the document grants a user (<c>/P</c>, ISO 32000-2 Table 22) when its user
        /// password opened it — the empty one, or one the host supplied; null when it is not encrypted,
        /// its owner password opened it (§7.6.4.2: the owner is not restricted), or under AES-256 the
        /// authenticated <c>/Perms</c> did not vouch for the bits, which are then not relied on.
        /// </summary>
        internal PdfPermissions? Permissions { get; private set; }

        private HashSet<string>? _lockedFields;
        private bool _everyFieldLocked;

        /// <summary>
        /// Whether a signature already in the document forbids changing the field
        /// <paramref name="fullyQualifiedName"/>: a certification at DocMDP level 1 (no change at all),
        /// or one the catalog declares and the analysis could not read, locks every field; a FieldMDP
        /// lock (ISO 32000-2 Table 259) locks the fields it names, all but them, or all. A viewer that
        /// filled such a field would append the revision the signature forbids, and the signature over
        /// the original would no longer verify. Read once a document.
        /// </summary>
        internal bool IsFieldLocked(string fullyQualifiedName)
        {
            if (_lockedFields == null) ReadLocks();
            return _everyFieldLocked || _lockedFields!.Contains(fullyQualifiedName);
        }

        /// <summary>Why the locks could not be read, or null; none is then honoured.</summary>
        internal string? LockAnalysisError { get; private set; }

        private void ReadLocks()
        {
            var locked = new HashSet<string>(StringComparer.Ordinal);
            bool all = false;
            try
            {
                using (var doc = OpenDocument())
                {
                    DocMdpAnalysis certification = DocMdpAnalyzer.Analyze(doc, _bytes.Length);
                    if (certification.CertificationDeclarationUnreadable
                        || (certification.HasCertification && certification.PermissionLevel == 1))
                        all = true;

                    foreach (FieldMdpAnalysis fieldLock in FieldMdpAnalyzer.Analyze(doc, _bytes.Length))
                    {
                        if (fieldLock.Action == PdfName.All.Value) all = true;
                        else if (fieldLock.Action == PdfName.Include.Value) locked.UnionWith(fieldLock.Fields);
                        else if (fieldLock.Action == PdfName.Exclude.Value)
                        {
                            var free = new HashSet<string>(fieldLock.Fields, StringComparer.Ordinal);
                            foreach (var field in FormFields)
                                if (!free.Contains(field.FullyQualifiedName)) locked.Add(field.FullyQualifiedName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LockAnalysisError = ex.Message;
            }
            _everyFieldLocked = all;
            _lockedFields = locked;
        }

        /// <summary>
        /// The same document with <paramref name="values"/> filled into its form: a new source over
        /// the decrypted bytes as opened plus one revision, keeping the original and its password
        /// for <see cref="SaveFilled"/>.
        /// </summary>
        internal PdfDocumentSource Filled(IDictionary<string, string> values, out FormFillResult result,
            IDictionary<string, string>? displayValues = null)
        {
            using (var input = new MemoryStream(OpenedBytes, false))
            using (var output = new MemoryStream())
            {
                result = PdfFormFiller.Fill(input, output, values, FillOptions(displayValues));
                return Build(output.ToArray(), OriginalBytes, Password, OpenedBytes);
            }
        }

        /// <summary>
        /// Writes the original file with <paramref name="values"/> appended as one revision — so a
        /// signature over the original still verifies — under the original's own encryption when it
        /// had one. No values: the original, unchanged. A field in <paramref name="displayValues"/>
        /// is drawn with that text (its Format script's) and stores its value.
        /// </summary>
        internal FormFillResult? SaveFilled(Stream output, IDictionary<string, string> values,
            IDictionary<string, string>? displayValues = null)
        {
            if (values.Count == 0)
            {
                output.Write(OriginalBytes, 0, OriginalBytes.Length);
                return null;
            }
            using (var input = new MemoryStream(OriginalBytes, false))
                return Password == null
                    ? PdfFormFiller.Fill(input, output, values, FillOptions(displayValues))
                    : PdfEncryptedForms.Fill(input, output, Password, values, FillOptions(displayValues));
        }

        /// <summary>The one fill policy, the shown document and the saved file alike.</summary>
        private static FormFillOptions FillOptions(IDictionary<string, string>? displayValues)
            => new FormFillOptions { DisplayValues = displayValues };

        private static PdfDocumentSource Build(byte[] bytes, byte[] originalBytes, string? password, byte[] openedBytes)
        {
            using (var ms = new MemoryStream(bytes, false))
            using (var doc = PdfDocument.Open(ms))
            // One session, purely to reach the renderer's own page-box decision once per page
            // without re-opening the document per page. It is disposed here and NOTHING renders
            // through it: for whole-page renders a session measured slower than the one-shot entry
            // points (383 ms against 354 ms), so painting does not use one.
            using (var geoStream = new MemoryStream(bytes, false))
            using (var geometry = PdfRenderSession.Open(geoStream))
            {
                int count = doc.Pages.Count;
                var geo = new PageGeometry[count];
                for (int i = 0; i < count; i++)
                    geo[i] = ReadGeometry(doc.Pages[i], geometry, i);
                return new PdfDocumentSource(bytes, count, geo, originalBytes, password, openedBytes);
            }
        }

        public static PdfDocumentSource FromFile(string path, PasswordProvider? passwordProvider = null)
            => FromBytes(File.ReadAllBytes(path), passwordProvider);

        public static PdfDocumentSource FromStream(Stream stream, PasswordProvider? passwordProvider = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (stream.CanSeek) stream.Position = 0;
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return FromBytes(ms.ToArray(), passwordProvider);
            }
        }

        // ----------------------------------------------------------------
        //  Encryption handling
        // ----------------------------------------------------------------

        private const int MaxPasswordAttempts = 8;

        /// <summary>
        /// If the document is encrypted, resolves a password and returns the decrypted bytes, with the
        /// permissions the password leaves (<see cref="Permissions"/>); otherwise returns
        /// <paramref name="bytes"/> unchanged.
        /// </summary>
        private static byte[] DecryptIfNeeded(byte[] bytes, PasswordProvider? provider, out string? password,
            out PdfPermissions? permissions)
        {
            password = null;
            permissions = null;
            bool encrypted;
            try
            {
                using (var probe = PdfDocument.Open(new MemoryStream(bytes, false)))
                    encrypted = probe.IsEncrypted;
            }
            catch
            {
                return bytes;   // let the normal open path surface any parse error
            }
            if (!encrypted) return bytes;

            EncryptionInfo info;
            using (var ms = new MemoryStream(bytes, false))
                info = PdfDecryptor.GetEncryptionInfo(ms);

            password = ResolvePassword(info, provider);
            PdfPasswordRole role = PdfDecryptor.IdentifyPassword(info, password);

            byte[] decrypted;
            using (var src = new MemoryStream(bytes, false))
            using (var dec = new MemoryStream())
            {
                PdfDecryptor.Decrypt(src, dec, password);
                decrypted = dec.ToArray();
            }

            // /P binds a user (§7.6.4.2); the owner opened the document unrestricted. Under AES-256 the
            // bits are read only where /Perms authenticated them (SECURITY.md's rule, "OK"); below V5
            // nothing authenticates them ("SkippedNonAes256Key") and the document's own statement is taken.
            string permsValidation = PdfDecryptor.LastPermsValidation;
            if (role == PdfPasswordRole.User && (permsValidation == "OK" || permsValidation == "SkippedNonAes256Key"))
                permissions = (PdfPermissions)info.PValue;

            return decrypted;
        }

        private static string ResolvePassword(EncryptionInfo info, PasswordProvider? provider)
        {
            // The common "owner-only" case: an empty user password opens the document.
            if (PdfDecryptor.TryPassword(info, "")) return "";

            for (int attempt = 1; attempt <= MaxPasswordAttempts; attempt++)
            {
                string? candidate = provider?.Invoke(attempt, info);
                if (candidate == null)
                    throw new PdfPasswordRequiredException("A password is required to open this PDF.");
                if (PdfDecryptor.TryPassword(info, candidate))
                    return candidate;
            }
            throw new PdfPasswordRequiredException("The supplied password was incorrect.");
        }

        public PageGeometry Geometry(int pageIndex) => _geometry[pageIndex];

        /// <summary>Renders a page to PNG bytes at the requested DPI.</summary>
        /// <remarks>
        /// Kept for callers that genuinely want encoded bytes. For anything that is going to be
        /// drawn, use <see cref="RenderBitmap"/> — see its remarks for why.
        /// </remarks>
        public byte[] RenderPng(int pageIndex, int dpi)
        {
            using (var ms = new MemoryStream(_bytes, false))
                return PdfOperations.RenderPageToPng(ms, pageIndex, dpi);
        }

        /// <summary>
        /// Bytes a <see cref="RenderBitmap"/> of this page at this dpi will occupy, before it is
        /// rendered.
        /// </summary>
        /// <remarks>
        /// <para>Per-page pixel extent is the product of the zoom, the page's box and its
        /// <c>/UserUnit</c> — the last two entirely file-controlled — and the renderer's own guard
        /// is <c>PdfLimits.MaxImagePixelArea</c> = 100 000 000 px, i.e. 400 MB of RGBA. A viewer
        /// that decides whether to render from the page count alone will happily ask for that, and
        /// then hold several of them.</para>
        /// <para>Answered through the same <c>GetRenderSize</c> the geometry pass uses, so the
        /// number is the renderer's post-clamp extent rather than an estimate that could differ
        /// from what it actually allocates. Returns <c>-1</c> when the page cannot be measured,
        /// which callers must treat as "unknown", never as "small".</para>
        /// </remarks>
        public long EstimatedBitmapBytes(int pageIndex, int dpi, PdfRenderOptions? options = null)
        {
            try
            {
                var size = Session(options).GetRenderSize(pageIndex, dpi);
                return (long)size.WidthPixels * size.HeightPixels * 4L;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>The bytes as shown — decrypted, a fill's revision included — for a renderer on another thread.</summary>
        internal byte[] ShownBytes => _bytes;

        /// <summary>
        /// The render sessions over the shown bytes, one for each set of options the viewer renders
        /// under (the screen with or without comments, the print intent), opened when first asked for.
        /// </summary>
        /// <remarks>
        /// <para><b>Held because a one-shot render parses the whole document.</b> Measured in Release,
        /// a 45-line text page at 96 dpi: the size check and the render as two one-shot calls cost
        /// 24 ms a page at 50 pages, 80 ms at 800 and 200 ms at 3 200; through a held session, 10 to
        /// 12 ms at every size. A page re-rendered at a new zoom costs the same either way in a small
        /// document (GAPS §13b's measurement), so nothing is lost there.</para>
        /// <para><b>One thread.</b> A session is not thread-safe; these belong to the thread that
        /// owns the control. A renderer on another thread opens its own over
        /// <see cref="ShownBytes"/>.</para>
        /// </remarks>
        private readonly Dictionary<(RenderIntent Intent, AnnotationRendering Annotations), PdfRenderSession> _sessions
            = new Dictionary<(RenderIntent, AnnotationRendering), PdfRenderSession>();

        private PdfRenderSession Session(PdfRenderOptions? options)
        {
            options ??= PdfRenderOptions.Default;
            var key = (options.Intent, options.Annotations);
            if (_sessions.TryGetValue(key, out var session)) return session;
            session = PdfRenderSession.Open(new MemoryStream(_bytes, false), options);
            _sessions[key] = session;
            SessionsOpened++;
            return session;
        }

        /// <summary>Sessions opened over this document so far — the evidence a render reused one.</summary>
        internal int SessionsOpened { get; private set; }

        /// <summary>Sessions open now.</summary>
        internal int OpenSessionCount => _sessions.Count;

        /// <summary>Closes the render sessions; the source still answers, reopening one when asked.</summary>
        public void Dispose()
        {
            foreach (var session in _sessions.Values) session.Dispose();
            _sessions.Clear();
        }

        /// <summary>
        /// The options a printed page renders under: the print intent, so annotations follow
        /// their Print flag rather than NoView (§12.5.3) and <c>/ViewerPreferences /PrintArea</c>
        /// chooses the box, not <c>/ViewArea</c>.
        /// </summary>
        public static readonly PdfRenderOptions PrintOptions = new PdfRenderOptions { Intent = RenderIntent.Print };

        /// <summary>
        /// Renders a page when its bitmap would fit in <paramref name="maxBytes"/>, and refuses
        /// before allocating anything when it would not — the one guarded entry the page view,
        /// the thumbnails and printing share.
        /// </summary>
        /// <remarks>
        /// The page view, the thumbnails and printing all ask <see cref="EstimatedBitmapBytes"/>
        /// first: a page refused as too large would otherwise still render at the renderer's 100 MP
        /// ceiling, 400 MB, for its thumbnail, inside <c>OnPaint</c>. An estimate of -1 (the page
        /// cannot be measured) is not a refusal; the render then says why itself.
        /// </remarks>
        /// <param name="pageIndex">Zero-based page index.</param>
        /// <param name="dpi">Render resolution.</param>
        /// <param name="maxBytes">The most the bitmap may occupy, as RGBA.</param>
        /// <param name="options">Render options; null for the screen's.</param>
        /// <param name="refusal">Why nothing was rendered; null when a bitmap came back.</param>
        public System.Drawing.Bitmap? RenderWithin(int pageIndex, int dpi, long maxBytes,
            PdfRenderOptions? options, out string? refusal)
        {
            var bmp = RenderWithinBudget(pageIndex, dpi, maxBytes, options, out long refusedBytes);
            refusal = bmp == null ? $"too large to render at this zoom ({Megabytes(refusedBytes)} MB)." : null;
            return bmp;
        }

        /// <summary>
        /// <see cref="RenderWithin(int,int,long,PdfRenderOptions,out string)"/> with the refusal as the
        /// bytes the bitmap would have taken, for a caller that words it in its own language.
        /// </summary>
        internal System.Drawing.Bitmap? RenderWithinBudget(int pageIndex, int dpi, long maxBytes,
            PdfRenderOptions? options, out long refusedBytes)
        {
            refusedBytes = 0;
            long bytes = EstimatedBitmapBytes(pageIndex, dpi, options);
            if (bytes > maxBytes)
            {
                refusedBytes = bytes;
                return null;
            }
            return RenderBitmap(pageIndex, dpi, options);
        }

        /// <summary>Whole megabytes, as a refusal names them.</summary>
        internal static long Megabytes(long bytes) => bytes / (1024 * 1024);

        /// <summary>
        /// Renders a page straight to a <see cref="System.Drawing.Bitmap"/>, with no image
        /// encoding step at all.
        /// </summary>
        /// <remarks>
        /// <para>No PNG encode and decode to move pixels the renderer already has: through
        /// <see cref="RenderPng"/> and <c>new Bitmap(stream)</c>, encoding alone measured ~80 ms for a
        /// 1275x1650 page (about 97% of the whole render), on the UI thread inside
        /// <c>OnPaint</c>.</para>
        /// <para>What remains is one swizzle pass: the renderer produces RGBA8888 premultiplied,
        /// and <c>Format32bppPArgb</c> is premultiplied <b>B,G,R,A</b> in memory. That is a few
        /// milliseconds over the whole surface against the ~110 ms of an encode-decode round trip.</para>
        /// </remarks>
        public System.Drawing.Bitmap RenderBitmap(int pageIndex, int dpi, PdfRenderOptions? options = null)
        {
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
            return ToGdiBitmap(Session(options).RenderPageToPixels(pageIndex, dpi));
        }

        /// <summary>
        /// Converts RGBA8888-premultiplied pixels to a <c>Format32bppPArgb</c> bitmap, which is
        /// the same premultiplied 32-bit layout with red and blue exchanged.
        /// </summary>
        internal static System.Drawing.Bitmap ToGdiBitmap(RenderedPage page)
        {
            // Swizzle in place: RenderedPage.Pixels is a fresh copy owned by this call, so there
            // is no second buffer and no caller to surprise.
            byte[] px = page.Pixels;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                byte r = px[i];
                px[i] = px[i + 2];   // B
                px[i + 2] = r;       // R
            }

            var bmp = new System.Drawing.Bitmap(page.Width, page.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var rect = new System.Drawing.Rectangle(0, 0, page.Width, page.Height);
            System.Drawing.Imaging.BitmapData? data = null;
            try
            {
                data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                    System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

                // GDI+ picks its own stride, so copy row by row rather than in one block.
                int rowBytes = page.Width * 4;
                for (int y = 0; y < page.Height; y++)
                {
                    IntPtr dest = data.Scan0 + y * data.Stride;
                    System.Runtime.InteropServices.Marshal.Copy(px, y * page.Stride, dest, rowBytes);
                }
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
            finally
            {
                if (data != null) bmp.UnlockBits(data);
            }
            return bmp;
        }

        /// <summary>
        /// A document over the shown bytes for the caller to dispose — the form scripts read their
        /// actions from it. Not the text layouts' document, which a search may be reading.
        /// </summary>
        internal PdfDocument OpenDocument() => PdfDocument.Open(new MemoryStream(_bytes, false));

        private IReadOnlyList<PdfAnnotationInfo>? _annotations;

        /// <summary>Why <see cref="Annotations"/> is incomplete, or null when it is complete.</summary>
        internal string? AnnotationListError { get; private set; }

        /// <summary>
        /// Every annotation of the shown document, through the library's one inventory
        /// (<see cref="PdfAnnotationExtractor"/>), read once; a document it cannot list has none, and
        /// <see cref="AnnotationListError"/> says why.
        /// </summary>
        internal IReadOnlyList<PdfAnnotationInfo> Annotations
        {
            get
            {
                if (_annotations != null) return _annotations;
                try
                {
                    _annotations = PdfAnnotationExtractor.ListAnnotations(new MemoryStream(_bytes, false));
                    AnnotationListError = PdfAnnotationExtractor.LastError;
                }
                catch (Exception ex)
                {
                    _annotations = Array.Empty<PdfAnnotationInfo>();
                    AnnotationListError = ex.Message;
                }
                return _annotations;
            }
        }

        private HashSet<string>? _unsignedSignatureFields;

        /// <summary>
        /// The fully qualified names of the signature fields that hold no signature yet, read once;
        /// an unreadable document has none. A signature field's value is a dictionary, which
        /// <see cref="FormFields"/>' string value cannot tell from none.
        /// </summary>
        internal ISet<string> UnsignedSignatureFields
        {
            get
            {
                if (_unsignedSignatureFields != null) return _unsignedSignatureFields;
                var names = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    using (var doc = OpenDocument())
                        if (doc.AcroForm != null)
                            foreach (var signatureField in doc.AcroForm.GetSignatureFields())
                                if (!signatureField.HasSignature) names.Add(signatureField.FullyQualifiedName);
                }
                catch
                {
                    names.Clear();
                }
                return _unsignedSignatureFields = names;
            }
        }

        /// <summary>
        /// The document signed into <paramref name="parameters"/>' field: the file as
        /// <see cref="SaveFilled"/> writes it — the original, then <paramref name="values"/> as one
        /// revision — with the signature appended, so it covers the values and leaves an earlier
        /// signature valid. An encrypted file is signed under its own key through
        /// <c>PdfEncryptionOperations.Sign</c> and stays openable by the same password. The new
        /// source's original is the signed file.
        /// </summary>
        internal PdfDocumentSource Signed(IDictionary<string, string> values, IDictionary<string, string>? displayValues,
            Tetitulz.Pdf.Signing.Models.SignatureParameters parameters)
        {
            byte[] filled;
            using (var ms = new MemoryStream())
            {
                SaveFilled(ms, values, displayValues);
                filled = ms.ToArray();
            }
            using (var input = new MemoryStream(filled, false))
            using (var output = new MemoryStream())
            {
                if (Password == null) new Tetitulz.Pdf.Signing.PdfSigner().Sign(input, output, parameters);
                else PdfEncryptionOperations.Sign(input, output, Password, parameters);
                string? password = Password;
                return FromBytes(output.ToArray(), password == null ? null : new PasswordProvider((_, _) => password));
            }
        }

        private IReadOnlyList<Tetitulz.Pdf.Signing.Models.SignatureVerificationResult>? _signatures;
        private Tetitulz.Pdf.Signing.Models.SignatureVerificationOptions? _signaturesVerifiedWith;
        private string? _signatureError;

        /// <summary>
        /// Every signature of the file <b>as opened</b> — the original bytes, not the shown ones a fill
        /// has added to — verified under <paramref name="options"/> (null: the library's defaults),
        /// once per options object. An encrypted file is verified through
        /// <c>PdfEncryptionOperations.VerifySignatures</c>, which takes the object graph from a
        /// decrypted copy and every byte from the original. A file that cannot be verified has no
        /// results, and <paramref name="error"/> says why.
        /// </summary>
        internal IReadOnlyList<Tetitulz.Pdf.Signing.Models.SignatureVerificationResult> VerifySignatures(
            Tetitulz.Pdf.Signing.Models.SignatureVerificationOptions? options, out string? error)
        {
            if (_signatures == null || !ReferenceEquals(options, _signaturesVerifiedWith))
            {
                _signaturesVerifiedWith = options;
                try
                {
                    using (var input = new MemoryStream(OriginalBytes, false))
                        _signatures = Password == null
                            ? Tetitulz.Pdf.Signing.PdfSigningOperations.VerifySignatures(input, options)
                            : PdfEncryptionOperations.VerifySignatures(input, Password, options);
                    _signatureError = null;
                }
                catch (Exception ex)
                {
                    _signatures = Array.Empty<Tetitulz.Pdf.Signing.Models.SignatureVerificationResult>();
                    _signatureError = ex.Message;
                }
            }
            error = _signatureError;
            return _signatures;
        }

        private IReadOnlyList<string>? _pageLabels;

        /// <summary>
        /// Each page's label (§12.4.2, <c>/PageLabels</c>), read once; empty when the document labels
        /// none or its labels cannot be read — a page is then known by its number.
        /// </summary>
        internal IReadOnlyList<string> PageLabels
        {
            get
            {
                if (_pageLabels != null) return _pageLabels;
                try
                {
                    using (var ms = new MemoryStream(_bytes, false))
                        _pageLabels = PdfOperations.GetPageLabels(ms) ?? Array.Empty<string>();
                }
                catch
                {
                    _pageLabels = Array.Empty<string>();
                }
                return _pageLabels;
            }
        }

        private InitialViewSettings? _initialView;
        private bool _initialViewRead;

        /// <summary>
        /// How the document asks to be opened (§7.7.2 <c>/PageMode</c>, <c>/PageLayout</c>,
        /// <c>/OpenAction</c>; §12.2 <c>/ViewerPreferences</c>), read once; null when it cannot be read.
        /// </summary>
        internal InitialViewSettings? InitialView
        {
            get
            {
                if (_initialViewRead) return _initialView;
                _initialViewRead = true;
                try
                {
                    using (var ms = new MemoryStream(_bytes, false))
                        _initialView = PdfOperations.GetInitialView(ms);
                }
                catch (Exception ex)
                {
                    _initialView = null;
                    InitialViewError = ex.Message;
                }
                return _initialView;
            }
        }

        /// <summary>Why <see cref="InitialView"/> is null for a document that may ask for a view; null otherwise.</summary>
        internal string? InitialViewError { get; private set; }

        private DocumentPrintPreferences? _printPreferences;

        /// <summary>
        /// The print settings the document asks for in <c>/ViewerPreferences</c> (§12.2 Table 150),
        /// read once; an unreadable document asks for none.
        /// </summary>
        internal DocumentPrintPreferences PrintPreferences
        {
            get
            {
                if (_printPreferences != null) return _printPreferences;
                try
                {
                    using (var doc = OpenDocument())
                        _printPreferences = DocumentPrintPreferences.From(
                            PdfViewerPreferences.Read(doc.Catalog?.Dictionary, doc.ResolveObject), PageCount, doc.ResolveObject);
                }
                catch
                {
                    _printPreferences = DocumentPrintPreferences.None;
                }
                return _printPreferences;
            }
        }

        /// <summary>
        /// Positioned text for one page (origin bottom-left, PDF points), extracted when asked and
        /// kept by nobody here.
        /// </summary>
        /// <remarks>
        /// Each page is extracted on demand from one document held open for the purpose, and the
        /// control keeps what it indexes: extracting every page and keeping the lot would hold some
        /// 10^7 glyph objects for a 5 000-page document, and the control's bound on its per-page
        /// text index would bound nothing. The document is over a
        /// memory stream of these bytes and holds nothing a collector cannot take.
        /// </remarks>
        public PdfPageTextLayout? LayoutFor(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= PageCount) return null;
            try
            {
                _textDocument ??= PdfDocument.Open(new MemoryStream(_bytes, false));
                var layout = PdfStructuredTextExtractor.Extract(_textDocument, pageIndex);
                // A per-page non-fatal error the extractor tolerated.
                string lastErr = PdfStructuredTextExtractor.LastError;
                if (!string.IsNullOrEmpty(lastErr) && string.IsNullOrEmpty(TextExtractionError))
                    TextExtractionError = lastErr;
                return layout;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(TextExtractionError))
                    TextExtractionError = ex.Message;
                return null;
            }
        }

        /// <summary>Link annotations for a page (empty if none / unavailable).</summary>
        public IReadOnlyList<PdfLink> LinksFor(int pageIndex)
        {
            if (_linksByPage == null)
            {
                _linksByPage = new Dictionary<int, List<PdfLink>>();
                try
                {
                    using (var ms = new MemoryStream(_bytes, false))
                    {
                        foreach (var link in PdfOperations.ExtractLinks(ms))
                        {
                            if (!_linksByPage.TryGetValue(link.PageIndex, out var list))
                            {
                                list = new List<PdfLink>();
                                _linksByPage[link.PageIndex] = list;
                            }
                            list.Add(link);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal, but SAID: an empty list must not be able to mean two things.
                    LinkExtractionError = ex.Message;
                }
            }
            return _linksByPage.TryGetValue(pageIndex, out var found)
                ? (IReadOnlyList<PdfLink>)found
                : Array.Empty<PdfLink>();
        }

        /// <summary>Non-null when the form could not be read, rather than having no fields.</summary>
        public string? FormExtractionError { get; private set; }

        /// <summary>The AcroForm's terminal fields with their widgets, or an empty list.</summary>
        public IReadOnlyList<PdfFormFieldInfo> FormFields
        {
            get
            {
                if (_formFields == null)
                {
                    try
                    {
                        using (var ms = new MemoryStream(_bytes, false))
                            _formFields = PdfFormReader.GetFields(ms);
                    }
                    catch (Exception ex)
                    {
                        FormExtractionError = ex.Message;
                        _formFields = Array.Empty<PdfFormFieldInfo>();
                    }
                }
                return _formFields;
            }
        }

        private IReadOnlyList<PdfFormFieldInfo>? _formFields;

        /// <summary>The document outline (bookmarks), or an empty list.</summary>
        public IReadOnlyList<PdfBookmark> Bookmarks
        {
            get
            {
                if (_bookmarks == null)
                {
                    try
                    {
                        using (var ms = new MemoryStream(_bytes, false))
                            _bookmarks = PdfOperations.GetBookmarks(ms);
                    }
                    catch (Exception ex)
                    {
                        BookmarkExtractionError = ex.Message;
                        _bookmarks = Array.Empty<PdfBookmark>();
                    }
                }
                return _bookmarks;
            }
        }

        /// <summary>
        /// The rectangle of the page the viewer lays out, hit-tests and overlays — taken from the
        /// renderer's own decision rather than re-derived.
        /// </summary>
        /// <remarks>
        /// <para>Every bitmap the viewer draws comes from <c>PdfPageRenderer.ResolveRenderBox</c>,
        /// which clips the chosen box to the <c>/MediaBox</c> (§14.11.2) and honours
        /// <c>/ViewerPreferences /ViewArea</c>; reading <c>page.CropBox</c> directly would lay a page
        /// with <c>/MediaBox [0 0 612 792]</c> and <c>/CropBox [0 0 612 1584]</c> out 1584 pt tall
        /// around a 792 pt render, with every link, selection and search rectangle at half its
        /// proper distance down the page. <c>PdfRenderSession.GetRenderSize</c> reaches
        /// <c>MeasurePage</c> → the same <c>ResolveRenderBox</c>, so the extent and the rotation here
        /// are the renderer's answer by construction, <c>/UserUnit</c> and <c>/Rotate</c>
        /// included.</para>
        /// <para><b>The origin is the one number that answer does not carry</b> —
        /// <see cref="PageRenderSize"/> reports extent, rotation and dpi but not the corner, and
        /// <c>ResolveRenderBox</c> is <c>internal</c> to <c>Tetitulz.Pdf.Base</c>. It is derived in
        /// <see cref="ClipOriginToMedia"/> instead, which reproduces the §14.11.2 intersection
        /// exactly. What it cannot see is a document that also sets <c>/ViewArea</c> to a box with a
        /// <i>different corner</i>: such a page is the right size and its overlays are offset by
        /// the difference between the two corners. Delete that helper and read the origin off
        /// <see cref="PageRenderSize"/> the day it carries one.</para>
        /// </remarks>
        private static PageGeometry ReadGeometry(PdfPage page, PdfRenderSession renderGeometry, int pageIndex)
        {
            float w = PdfPageSizes.DefaultWidth, h = PdfPageSizes.DefaultHeight, userUnit = 1f;
            int rotate = 0;
            try
            {
                PageRenderSize size = renderGeometry.GetRenderSize(pageIndex);
                w = size.WidthPoints;
                h = size.HeightPoints;
                rotate = size.Rotation;
                userUnit = size.UserUnit;
            }
            catch
            {
                // The renderer could not measure the page; fall back to the page's own boxes so a
                // damaged page still lays out rather than taking the whole document down.
                try
                {
                    PdfArray fallback = page.CropBox;
                    if (fallback != null && fallback.Count >= 4)
                    {
                        w = Math.Abs((float)fallback.GetDouble(2) - (float)fallback.GetDouble(0));
                        h = Math.Abs((float)fallback.GetDouble(3) - (float)fallback.GetDouble(1));
                    }
                    rotate = page.Rotate ?? 0;
                }
                catch { /* fall back to US-Letter defaults */ }
            }

            ClipOriginToMedia(page, w, h, out float x0, out float y0);
            return new PageGeometry(x0, y0, w, h, rotate, userUnit);
        }

        /// <summary>
        /// The bottom-left corner, in PDF points, of the rectangle whose extent is
        /// <paramref name="w"/> x <paramref name="h"/> — the candidate box's own corner moved inside
        /// the <c>/MediaBox</c> so the extent fits, which is what §14.11.2's intersection does to it.
        /// </summary>
        private static void ClipOriginToMedia(PdfPage page, float w, float h, out float x0, out float y0)
        {
            x0 = 0f;
            y0 = 0f;
            try
            {
                // The candidate corner is the CropBox's own; the §14.11.2 intersection with the
                // /MediaBox is applied by the clamp below, against the extent the renderer already
                // resolved. Intersecting the rectangles here first would be the same arithmetic
                // twice — and it is the second opinion this class exists to stop having.
                // A box the renderer reads as none — an element that is not finite — leaves the
                // page at the default size from (0, 0), so the corner is (0, 0) too; an element that
                // is not a number reads as 0, as the renderer's array reader takes it.
                if (!TryReadBox(page.CropBox, out float cx0, out float cy0, out _, out _)) return;
                if (!TryReadBox(page.MediaBox, out float mx0, out float my0, out float mx1, out float my1)) return;

                x0 = Clamp(cx0, mx0, Math.Max(mx0, mx1 - w));
                y0 = Clamp(cy0, my0, Math.Max(my0, my1 - h));
            }
            catch { x0 = 0f; y0 = 0f; }
        }

        /// <summary>
        /// A page box's corners, normalised (§7.9.5): false when the array is absent or short, or an
        /// element is not finite. An element that is not a number reads as 0.
        /// </summary>
        private static bool TryReadBox(PdfArray box, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = x1 = y1 = 0f;
            if (box == null || box.Count < 4) return false;
            var v = new float[4];
            for (int i = 0; i < 4; i++)
            {
                double d = box[i] is PdfNumber n ? n.DoubleValue : 0.0;
                if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                v[i] = (float)d;
                if (float.IsInfinity(v[i])) return false;
            }
            x0 = Math.Min(v[0], v[2]); x1 = Math.Max(v[0], v[2]);
            y0 = Math.Min(v[1], v[3]); y1 = Math.Max(v[1], v[3]);
            return true;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
