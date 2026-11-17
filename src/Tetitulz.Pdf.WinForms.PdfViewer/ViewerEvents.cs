// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using Tetitulz.Pdf.Base.Operations;
using Tetitulz.Pdf.Base.Operations.Extraction;
using Tetitulz.Pdf.Core;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>Page presentation mode of the viewer.</summary>
    public enum PdfViewLayoutMode
    {
        /// <summary>One page at a time; navigate with prev/next/go-to.</summary>
        SinglePage = 0,
        /// <summary>All pages stacked vertically in one scrollable surface.</summary>
        Continuous = 1,
    }

    /// <summary>How many pages a row of the view shows, in either <see cref="PdfViewLayoutMode"/>.</summary>
    public enum PdfPageSpread
    {
        /// <summary>One page to a row.</summary>
        None = 0,
        /// <summary>Two pages side by side: pages 1 and 2, 3 and 4 (PDF's <c>TwoColumnLeft</c>, <c>TwoPageLeft</c>).</summary>
        Facing = 1,
        /// <summary>
        /// The first page alone, as a book's cover, then two side by side: 2 and 3, 4 and 5 (PDF's
        /// <c>TwoColumnRight</c>, <c>TwoPageRight</c>).
        /// </summary>
        FacingWithCover = 2,
    }

    /// <summary>How a printed page is sized on the paper.</summary>
    public enum PdfPrintScaling
    {
        /// <summary>
        /// As the document asks: its own size under <c>/ViewerPreferences /PrintScaling /None</c>,
        /// else fitted to the printable area.
        /// </summary>
        Document = 0,
        /// <summary>Fitted to the printable area, keeping its proportions.</summary>
        FitToPage = 1,
        /// <summary>The page's own size, centred; what does not fit the paper is cut off.</summary>
        ActualSize = 2,
    }

    /// <summary>How the viewer chooses its zoom.</summary>
    public enum PdfZoomMode
    {
        /// <summary>A fixed factor, kept as the window resizes and the page changes.</summary>
        Custom = 0,
        /// <summary>
        /// The current page's width fills the view, refitted when the view is resized, a document
        /// is loaded, or — in single-page layout — a page of another size is shown.
        /// </summary>
        FitWidth = 1,
        /// <summary>The whole current page fits the view, refitted as for <see cref="FitWidth"/>.</summary>
        FitPage = 2,
    }

    /// <summary>
    /// The viewer's capabilities, each of which a host can switch off. A capability that is off is off
    /// everywhere — its control is hidden, its shortcut does nothing and its public method returns
    /// without acting. To hide only a control and keep the capability for the host's own UI, use the
    /// control's <c>Show*</c> property instead.
    /// </summary>
    [Flags]
    public enum PdfViewerFeatures
    {
        /// <summary>Nothing beyond showing, scrolling, zooming and paging.</summary>
        None = 0,
        /// <summary>The Open button and Ctrl+O.</summary>
        OpenFile = 1,
        /// <summary>The find bar, the Find button, Ctrl+F and F3.</summary>
        Search = 2,
        /// <summary>Dragging to select text, and Ctrl+C.</summary>
        TextSelection = 4,
        /// <summary>Following link annotations, with the hand cursor and hover highlight.</summary>
        Links = 8,
        /// <summary>The sidebar's page thumbnails tab.</summary>
        Thumbnails = 16,
        /// <summary>The sidebar's bookmarks (outline) tab.</summary>
        Bookmarks = 32,
        /// <summary>The Print button, Ctrl+P, <c>Print</c> and <c>PrintPreview</c>.</summary>
        Print = 64,
        /// <summary>What the viewer enables by default — every capability it has had from the start.</summary>
        Default = OpenFile | Search | TextSelection | Links | Thumbnails | Bookmarks | Print,
        /// <summary>
        /// Filling the document's form fields, with the Save button and Ctrl+S. Off by default: a
        /// viewer that lets a user change a document is a host's decision.
        /// </summary>
        FormFilling = 128,
        /// <summary>
        /// Running a field's Keystroke and Validate scripts and the document's calculations when a
        /// value is committed (<c>Tetitulz.Pdf.Base.Operations.Forms.PdfFieldScripts</c>). Needs
        /// <see cref="FormFilling"/> as well, and a JavaScript engine the host registers
        /// (<c>Tetitulz.Pdf.Plugins.JavaScript</c>): the viewer references none. Off by default.
        /// </summary>
        FormScripts = 256,
        /// <summary>
        /// The document's comments: a Comments tab in the sidebar listing them by page (a click
        /// goes to one and outlines it), and a comment's author, date and text on hover. Off by
        /// default: a tab the host did not ask for.
        /// </summary>
        Comments = 512,
        /// <summary>
        /// The document's digital signatures: each verified (the file as opened — an encrypted one
        /// under its password), a banner with the overall verdict, a Signatures tab with each
        /// signature's signer, time, verdict and details, and a mark and hover text on each signed
        /// field. Off by default: verification reads every signed revision, and the trust a verdict
        /// rests on is the host's (<see cref="PdfViewerControl.SignatureVerification"/>).
        /// </summary>
        Signatures = 1024,
        /// <summary>
        /// Signing from the viewer: a click on an empty signature field raises
        /// <see cref="PdfViewerControl.SignatureRequested"/>, whose handler supplies the signer's
        /// certificate, and <see cref="PdfViewerControl.SignField"/> signs from code. The values
        /// typed into the form go into the file first, so the signature covers them. Off by
        /// default: a signature is the host's to ask for.
        /// </summary>
        Signing = 2048,
        /// <summary>
        /// Opening each document as it asks: at its <c>/OpenAction</c> page, top and zoom, in its
        /// <c>/PageLayout</c> (single page or continuous, two to a row for the two-page and two-column ones), with the sidebar
        /// tab its <c>/PageMode</c> names (bookmarks, thumbnails), and with the chrome mirrored for
        /// <c>/ViewerPreferences /Direction /R2L</c>. Off by default: a host that sets its own page,
        /// zoom and direction keeps them.
        /// </summary>
        DocumentView = 4096,
    }

    /// <summary>An action the user started from the viewer's own chrome — a button, a shortcut, a sidebar click.</summary>
    public enum PdfViewerCommand
    {
        /// <summary>The Open button or Ctrl+O: the built-in action shows an open-file dialog.</summary>
        Open = 0,
        /// <summary>The Print button or Ctrl+P: the built-in action shows the print preview.</summary>
        Print = 1,
        /// <summary>The Find button or Ctrl+F: the built-in action opens the find bar.</summary>
        Find = 2,
        /// <summary>Ctrl+C with text selected: the built-in action copies it to the clipboard.</summary>
        Copy = 3,
        /// <summary>The previous-page button or Page Up.</summary>
        PreviousPage = 4,
        /// <summary>The next-page button or Page Down.</summary>
        NextPage = 5,
        /// <summary>A page typed in the page box, or a thumbnail or bookmark clicked — <see cref="PdfViewerCommandEventArgs.PageIndex"/>.</summary>
        GoToPage = 6,
        /// <summary>The zoom-in button, Ctrl+plus or Ctrl+wheel up.</summary>
        ZoomIn = 7,
        /// <summary>The zoom-out button, Ctrl+minus or Ctrl+wheel down.</summary>
        ZoomOut = 8,
        /// <summary>A factor chosen or typed in the zoom box — <see cref="PdfViewerCommandEventArgs.Zoom"/>.</summary>
        SetZoom = 9,
        /// <summary>Ctrl+0.</summary>
        ActualSize = 10,
        /// <summary>The Fit Width button.</summary>
        FitWidth = 11,
        /// <summary>The Fit Page button.</summary>
        FitPage = 12,
        /// <summary>The layout button: continuous and single-page layout swap.</summary>
        ToggleLayout = 13,
        /// <summary>The sidebar button: the sidebar is shown or hidden.</summary>
        ToggleSidebar = 14,
        /// <summary>The Save button or Ctrl+S: the built-in action asks for a file and saves the filled form.</summary>
        Save = 15,
        /// <summary>Home or Ctrl+Home on the page: the first page — <see cref="PdfViewerCommandEventArgs.PageIndex"/>.</summary>
        FirstPage = 16,
        /// <summary>End or Ctrl+End on the page: the last page — <see cref="PdfViewerCommandEventArgs.PageIndex"/>.</summary>
        LastPage = 17,
        /// <summary>The Back button or Alt+Left: back to where the reader was before the last jump.</summary>
        Back = 18,
        /// <summary>The Forward button or Alt+Right: forward again after Back.</summary>
        Forward = 19,
        /// <summary>Ctrl+A or the page menu's Select All: the text of the page being read — <see cref="PdfViewerCommandEventArgs.PageIndex"/>.</summary>
        SelectAll = 20,
        /// <summary>Ctrl+Shift+Plus or the page menu: the view turned a quarter clockwise; the file is unchanged.</summary>
        RotateClockwise = 21,
        /// <summary>Ctrl+Shift+Minus or the page menu: the view turned a quarter counter-clockwise; the file is unchanged.</summary>
        RotateCounterclockwise = 22,
        /// <summary>The page menu's spread: one page to a row, or two — <see cref="PdfViewerCommandEventArgs.PageSpread"/>.</summary>
        SetPageSpread = 23,
    }

    /// <summary>What a drag across the page selects.</summary>
    public enum PdfTextSelectionMode
    {
        /// <summary>
        /// The text from the glyph under the press to the glyph under the release, in reading order
        /// across lines and pages, as every reader selects. Alt held while dragging selects a
        /// rectangle instead.
        /// </summary>
        ReadingOrder = 0,
        /// <summary>The glyphs inside the rectangle the drag spans, on each page it crosses — a column.</summary>
        Rectangle = 1,
    }

    /// <summary>The selection's part on one page: its text and where each selected glyph is.</summary>
    public sealed class PdfViewerSelectedText
    {
        internal PdfViewerSelectedText(int pageIndex, string text, System.Collections.Generic.IReadOnlyList<System.Drawing.RectangleF> glyphBoxes)
        {
            PageIndex = pageIndex;
            Text = text;
            GlyphBoxes = glyphBoxes;
        }

        /// <summary>The zero-based page.</summary>
        public int PageIndex { get; }

        /// <summary>The page's selected text, as it is copied.</summary>
        public string Text { get; }

        /// <summary>
        /// Each selected glyph's box in the page's user space (points, origin bottom-left), in reading
        /// order: from its descent to its ascent, so a highlight annotation drawn over these covers the ink.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<System.Drawing.RectangleF> GlyphBoxes { get; }
    }

    /// <summary>
    /// Raised before a form field takes a new value. Change <see cref="NewValue"/> to store another,
    /// or set <see cref="Cancel"/> to keep the old one.
    /// </summary>
    public sealed class PdfViewerFieldChangingEventArgs : EventArgs
    {
        /// <summary>The field's fully qualified name.</summary>
        public string FieldName { get; }

        /// <summary>The value the field holds now.</summary>
        public string OldValue { get; }

        /// <summary>The value about to be stored; a handler may replace it.</summary>
        public string NewValue { get; set; }

        /// <summary>Set to keep the old value.</summary>
        public bool Cancel { get; set; }

        public PdfViewerFieldChangingEventArgs(string fieldName, string oldValue, string newValue)
        {
            FieldName = fieldName;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    /// <summary>The document's signatures as verified, raised once a document's verification is done.</summary>
    public sealed class PdfViewerSignaturesVerifiedEventArgs : EventArgs
    {
        /// <summary>One result per signature, in the document's order; empty for an unsigned document.</summary>
        public System.Collections.Generic.IReadOnlyList<Tetitulz.Pdf.Signing.Models.SignatureVerificationResult> Results { get; }

        /// <summary>Why verification could not run, or null when it did.</summary>
        public string? Error { get; }

        public PdfViewerSignaturesVerifiedEventArgs(
            System.Collections.Generic.IReadOnlyList<Tetitulz.Pdf.Signing.Models.SignatureVerificationResult> results, string? error)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
            Error = error;
        }
    }

    /// <summary>
    /// Raised when the user clicks a signed signature field. Unless <see cref="Handled"/> is set, the
    /// viewer selects the signature's row in the Signatures tab and shows its validation status.
    /// </summary>
    public sealed class PdfViewerSignatureClickedEventArgs : EventArgs
    {
        /// <summary>The clicked signature as verified.</summary>
        public Tetitulz.Pdf.Signing.Models.SignatureVerificationResult Result { get; }

        /// <summary>The zero-based page the click was on.</summary>
        public int PageIndex { get; }

        /// <summary>Set to show nothing: the host answers the click itself.</summary>
        public bool Handled { get; set; }

        public PdfViewerSignatureClickedEventArgs(Tetitulz.Pdf.Signing.Models.SignatureVerificationResult result, int pageIndex)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            PageIndex = pageIndex;
        }
    }

    /// <summary>
    /// Raised when the user clicks an empty signature field. Set <see cref="Parameters"/> to sign it
    /// — the certificate (a PFX file or stream, or an external signer such as a smart card), and a
    /// reason or location if wanted; leave it null to sign nothing. The field's name is filled in.
    /// </summary>
    public sealed class PdfViewerSignatureRequestEventArgs : EventArgs
    {
        /// <summary>The empty signature field's fully qualified name.</summary>
        public string FieldName { get; }

        /// <summary>The zero-based page the click was on.</summary>
        public int PageIndex { get; }

        /// <summary>The signature to make; null signs nothing.</summary>
        public Tetitulz.Pdf.Signing.Models.SignatureParameters? Parameters { get; set; }

        public PdfViewerSignatureRequestEventArgs(string fieldName, int pageIndex)
        {
            FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
            PageIndex = pageIndex;
        }
    }

    /// <summary>A signature made from the viewer: the field signed, or why the signer refused.</summary>
    public sealed class PdfViewerFieldSignedEventArgs : EventArgs
    {
        /// <summary>The signature field's fully qualified name.</summary>
        public string FieldName { get; }

        /// <summary>Why the document could not be signed, or null when it was.</summary>
        public string? Error { get; }

        public PdfViewerFieldSignedEventArgs(string fieldName, string? error)
        {
            FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
            Error = error;
        }
    }

    /// <summary>What a form script said when a value was committed: an alert, or why the value was refused.</summary>
    public sealed class PdfViewerFormScriptEventArgs : EventArgs
    {
        /// <summary>The field whose value was being committed.</summary>
        public string FieldName { get; }

        /// <summary>The script's message.</summary>
        public string Message { get; }

        /// <summary>True when the value was refused; false for an alert beside an accepted value.</summary>
        public bool IsRejection { get; }

        public PdfViewerFormScriptEventArgs(string fieldName, string message, bool isRejection)
        {
            FieldName = fieldName;
            Message = message;
            IsRejection = isRejection;
        }
    }

    /// <summary>Raised after a form field took a new value and the page shows it.</summary>
    public sealed class PdfViewerFieldChangedEventArgs : EventArgs
    {
        /// <summary>The field's fully qualified name.</summary>
        public string FieldName { get; }

        /// <summary>The value it now holds.</summary>
        public string Value { get; }

        public PdfViewerFieldChangedEventArgs(string fieldName, string value)
        {
            FieldName = fieldName;
            Value = value;
        }
    }

    /// <summary>
    /// Raised before the viewer acts on a command from its chrome. Set <see cref="Handled"/> to stop
    /// the built-in action and do the host's own instead.
    /// </summary>
    public sealed class PdfViewerCommandEventArgs : EventArgs
    {
        /// <summary>The command.</summary>
        public PdfViewerCommand Command { get; }

        /// <summary>The zero-based page for <see cref="PdfViewerCommand.GoToPage"/>; otherwise null.</summary>
        public int? PageIndex { get; }

        /// <summary>The factor asked for (1.0 = 100%) for <see cref="PdfViewerCommand.SetZoom"/>; otherwise null.</summary>
        public double? Zoom { get; }

        /// <summary>The spread chosen for <see cref="PdfViewerCommand.SetPageSpread"/>; otherwise null.</summary>
        public PdfPageSpread? PageSpread { get; internal set; }

        /// <summary>Set to true to stop the built-in action.</summary>
        public bool Handled { get; set; }

        public PdfViewerCommandEventArgs(PdfViewerCommand command, int? pageIndex = null, double? zoom = null)
        {
            Command = command;
            PageIndex = pageIndex;
            Zoom = zoom;
        }
    }

    /// <summary>Raised after a document is successfully loaded.</summary>
    public sealed class PdfViewerLoadedEventArgs : EventArgs
    {
        public int PageCount { get; }
        public PdfViewerLoadedEventArgs(int pageCount) => PageCount = pageCount;
    }

    /// <summary>Raised when the current (topmost) page changes.</summary>
    public sealed class PdfViewerPageChangedEventArgs : EventArgs
    {
        public int PageIndex { get; }
        public int PageCount { get; }
        public PdfViewerPageChangedEventArgs(int pageIndex, int pageCount)
        {
            PageIndex = pageIndex;
            PageCount = pageCount;
        }
    }

    /// <summary>
    /// Raised when an encrypted PDF needs a password. The handler should set
    /// <see cref="Password"/> to the password to try (or set <see cref="Cancel"/> to give up).
    /// Raised once per attempt; if no handler answers (and no <c>Password</c> was preset), the
    /// load fails with a <see cref="PdfPasswordRequiredException"/>.
    /// </summary>
    public sealed class PdfViewerPasswordRequestedEventArgs : EventArgs
    {
        /// <summary>1-based attempt number (increments after each wrong password).</summary>
        public int Attempt { get; }
        /// <summary>The /Encrypt /V value (security-handler version).</summary>
        public int V { get; }
        /// <summary>The /Encrypt /R value (revision).</summary>
        public int R { get; }
        /// <summary>Key length in bits.</summary>
        public int KeyLengthBits { get; }

        /// <summary>Set this to the password to try.</summary>
        public string? Password { get; set; }
        /// <summary>Set this to true to stop prompting and fail the load.</summary>
        public bool Cancel { get; set; }

        public PdfViewerPasswordRequestedEventArgs(int attempt, int v, int r, int keyLengthBits)
        {
            Attempt = attempt;
            V = v;
            R = r;
            KeyLengthBits = keyLengthBits;
        }
    }

    /// <summary>Thrown when an encrypted PDF cannot be opened because no valid password was supplied.</summary>
    public sealed class PdfPasswordRequiredException : PdfException
    {
        public PdfPasswordRequiredException(string message) : base(message) { }
    }

    /// <summary>
    /// Raised when the user clicks a link annotation. Set <see cref="Handled"/> to
    /// <c>true</c> to suppress the control's default behaviour (open external URIs in
    /// the system browser / navigate to the in-document target page).
    /// </summary>
    public sealed class PdfViewerLinkClickedEventArgs : EventArgs
    {
        /// <summary>
        /// Whether the link points at a URI or an in-document destination.
        /// <see cref="PdfLinkKind.Unknown"/> is an action the viewer does not perform — JavaScript,
        /// SubmitForm — raised on a click so the host may judge it; the viewer runs nothing for it.
        /// </summary>
        public PdfLinkKind Kind { get; }

        /// <summary>The destination URI for <see cref="PdfLinkKind.Uri"/> links; otherwise null.</summary>
        public string? Uri { get; }

        /// <summary>Zero-based destination page for <see cref="PdfLinkKind.GoTo"/> links; otherwise -1.</summary>
        public int TargetPageIndex { get; }

        /// <summary>
        /// For a <see cref="PdfLinkKind.GoTo"/> link, the user-space y the destination puts at the top
        /// of the view; null for the page's top.
        /// </summary>
        public float? TargetTop { get; internal set; }

        /// <summary>The action's name for <see cref="PdfLinkKind.Named"/> links (<c>NextPage</c>, …); otherwise null.</summary>
        public string? NamedAction { get; internal set; }

        /// <summary>
        /// The file a <see cref="PdfLinkKind.RemoteGoTo"/> or <see cref="PdfLinkKind.Launch"/> link
        /// names; otherwise null. The viewer never opens it: a host that wants to must handle the event.
        /// </summary>
        public string? File { get; internal set; }

        /// <summary>Set to true to take over handling and stop the default action.</summary>
        public bool Handled { get; set; }

        public PdfViewerLinkClickedEventArgs(PdfLinkKind kind, string? uri, int targetPageIndex)
        {
            Kind = kind;
            Uri = uri;
            TargetPageIndex = targetPageIndex;
        }
    }
}
