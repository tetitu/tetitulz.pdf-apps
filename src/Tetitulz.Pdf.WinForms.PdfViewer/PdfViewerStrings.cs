// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Every user-visible string the viewer shows, so a host can present it in another language.
    /// </summary>
    /// <remarks>
    /// <para><b>Settable properties rather than a resource assembly, and that is the whole design
    /// decision.</b> Satellite resources pick the language from the thread's culture, which is the
    /// wrong source for this control twice over: a viewer is often embedded in an application whose
    /// UI language is a user setting rather than a machine one, and the language a document should
    /// be presented in is not the language the operating system was installed with. A property the
    /// host assigns puts the choice where the host already keeps it — and needs no
    /// <c>.resx</c>, no culture fallback chain, and nothing extra in the package.</para>
    ///
    /// <para><b>Nothing here is a format string with named holes.</b> Each one is a small piece of
    /// text or a <see cref="string.Format(string, object[])"/> template whose placeholders are
    /// documented on the property. A translator reordering <c>{0}</c> and <c>{1}</c> is normal and
    /// works; a translator who drops one gets that value missing from the message rather than an
    /// exception, which is the failure mode to prefer in a UI string.</para>
    ///
    /// <para><b>This is not the reading direction.</b> Mirroring the chrome is
    /// <see cref="System.Windows.Forms.Control.RightToLeft"/>, which the framework already
    /// inherits down the control tree — a Hebrew string set here does not mirror the toolbar, and a
    /// mirrored toolbar does not translate itself. The two are independent on purpose: an Arabic or
    /// Hebrew host may want mirrored chrome with English labels, and a Hebrew-labelled viewer inside
    /// a left-to-right application is equally legitimate.</para>
    /// </remarks>
    public sealed class PdfViewerStrings
    {
        /// <summary>The "Open" toolbar button.</summary>
        public string Open { get; set; } = "Open";

        /// <summary>Its tooltip.</summary>
        public string OpenTip { get; set; } = "Open a PDF (Ctrl+O)";

        /// <summary>Tooltip of the previous-page arrow. The glyph itself is not translated.</summary>
        public string PreviousPageTip { get; set; } = "Previous page (PageUp)";

        /// <summary>Tooltip of the next-page arrow.</summary>
        public string NextPageTip { get; set; } = "Next page (PageDown)";

        /// <summary>Tooltip of the zoom-out button.</summary>
        public string ZoomOutTip { get; set; } = "Zoom out (Ctrl+-)";

        /// <summary>Tooltip of the zoom-in button.</summary>
        public string ZoomInTip { get; set; } = "Zoom in (Ctrl++)";

        /// <summary>The "Fit Width" button.</summary>
        public string FitWidth { get; set; } = "Fit Width";

        /// <summary>Its tooltip.</summary>
        public string FitWidthTip { get; set; } = "Fit page width";

        /// <summary>The "Fit Page" button.</summary>
        public string FitPage { get; set; } = "Fit Page";

        /// <summary>Its tooltip.</summary>
        public string FitPageTip { get; set; } = "Fit whole page";

        /// <summary>The layout-toggle button.</summary>
        public string Layout { get; set; } = "Continuous";

        /// <summary>Its tooltip.</summary>
        public string LayoutTip { get; set; } = "Toggle continuous / single-page layout";

        /// <summary>The sidebar-toggle button.</summary>
        public string Sidebar { get; set; } = "Sidebar";

        /// <summary>Its tooltip.</summary>
        public string SidebarTip { get; set; } = "Show/hide thumbnails &amp; bookmarks";

        /// <summary>The "Find" button.</summary>
        public string Find { get; set; } = "Find";

        /// <summary>Its tooltip.</summary>
        public string FindTip { get; set; } = "Find text (Ctrl+F)";

        /// <summary>The "Print" button.</summary>
        public string Print { get; set; } = "Print";

        /// <summary>Its tooltip.</summary>
        public string PrintTip { get; set; } = "Print / preview (Ctrl+P)";

        /// <summary>The thumbnails tab.</summary>
        public string PagesTab { get; set; } = "Pages";

        /// <summary>The bookmarks tab.</summary>
        public string BookmarksTab { get; set; } = "Bookmarks";

        /// <summary>The page counter beside the page box. <c>{0}</c> is the page count.</summary>
        public string PageCountFormat { get; set; } = "/ {0}";

        /// <summary>The page counter before a document is open.</summary>
        public string PageCountUnknown { get; set; } = "/ –";

        /// <summary>The status line before a document is open.</summary>
        public string Ready { get; set; } = "Ready";

        /// <summary>
        /// The status line after opening a titled document. <c>{0}</c> is the title, <c>{1}</c> the
        /// page count.
        /// </summary>
        public string OpenedWithTitleFormat { get; set; } = "{0} — {1} page(s)";

        /// <summary>An untitled document. <c>{0}</c> is the page count.</summary>
        public string OpenedFormat { get; set; } = "{0} page(s)";

        /// <summary>
        /// Appended when the sidebar showed fewer thumbnails than the document has pages.
        /// <c>{0}</c> is the ceiling.
        /// </summary>
        public string ThumbnailsLimitedFormat { get; set; }
            = " — thumbnails limited to the first {0}";

        /// <summary>The find bar's result when the text is not in the document.</summary>
        public string NotFound { get; set; } = "Not found";

        /// <summary>
        /// The find bar's result. <c>{0}</c> is the one-based match number and <c>{1}</c> the total.
        /// </summary>
        public string FindResultFormat { get; set; } = "{0} / {1}";

        /// <summary>
        /// The same when the search stopped at its ceiling, so the total is a floor. <c>{0}</c> is
        /// the match number, <c>{1}</c> the count found so far.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="FindResultFormat"/> rather than a suffix, because the marker for
        /// "at least this many" is not <c>+</c> in every language and in several it goes before the
        /// number.
        /// </remarks>
        public string FindResultTruncatedFormat { get; set; } = "{0} / {1}+";

        /// <summary>Status while text is selected. <c>{0}</c> is the character count.</summary>
        public string SelectedCharactersFormat { get; set; }
            = "Selected {0} characters — Ctrl+C to copy";

        /// <summary>Status after a copy. <c>{0}</c> is the character count.</summary>
        public string CopiedCharactersFormat { get; set; } = "Copied {0} characters.";

        /// <summary>Status when the clipboard refused the text. <c>{0}</c> is the reason.</summary>
        public string CopyFailedFormat { get; set; } = "Copy failed: {0}";

        /// <summary>
        /// Status when a link was not followed because its scheme is not allowed. <c>{0}</c> is the
        /// URI.
        /// </summary>
        public string BlockedLinkFormat { get; set; }
            = "Blocked link (unsupported scheme): {0}";

        /// <summary>Status when launching a link failed. <c>{0}</c> is the reason.</summary>
        public string LinkFailedFormat { get; set; } = "Could not open link: {0}";

        /// <summary>
        /// Status for a link to another file or program, which the viewer never opens itself.
        /// <c>{0}</c> is the file.
        /// </summary>
        public string ExternalFileLinkFormat { get; set; } = "Link to another file, not opened: {0}";

        /// <summary>Status for a link whose named action the viewer does not perform. <c>{0}</c> is the action's name.</summary>
        public string UnsupportedLinkActionFormat { get; set; } = "Link action not supported: {0}";

        /// <summary>Tooltip of a link to a page of this document. <c>{0}</c> is the one-based page number.</summary>
        public string LinkToPageFormat { get; set; } = "Go to page {0}";

        /// <summary>The "Save" toolbar button (form filling).</summary>
        public string Save { get; set; } = "Save";

        /// <summary>Its tooltip.</summary>
        public string SaveTip { get; set; } = "Save the filled form (Ctrl+S)";

        /// <summary>Status after a save. <c>{0}</c> is the file name.</summary>
        public string SavedFormat { get; set; } = "Saved: {0}";

        /// <summary>Status when a save failed. <c>{0}</c> is the reason.</summary>
        public string SaveFailedFormat { get; set; } = "Could not save: {0}";

        /// <summary>Status when a field value could not be filled. <c>{0}</c> is the reason.</summary>
        public string FormFillFailedFormat { get; set; } = "Could not fill the field: {0}";

        /// <summary>
        /// Status when values were stored but could not be drawn (a viewer will draw them).
        /// <c>{0}</c> lists the fields.
        /// </summary>
        public string FieldsNotDrawnFormat { get; set; } = "Stored but not drawn: {0}";

        /// <summary>Status when a field's script refused a value. {0} = field name, {1} = the script's reason.</summary>
        public string FieldValueRefusedFormat { get; set; } = "{0}: value refused. {1}";

        /// <summary>Status when a field's scripts could not be read. {0} = reason.</summary>
        public string FormScriptsFailedFormat { get; set; } = "The form's scripts could not run: {0}";

        /// <summary>Status when a value was taken without running its scripts. {0} = reason.</summary>
        public string FormScriptsNotRunFormat { get; set; } = "Form scripts not run — {0}";

        /// <summary>The sidebar tab listing the document's comments.</summary>
        public string CommentsTab { get; set; } = "Comments";

        /// <summary>A page's group in the comments list. {0} = page number.</summary>
        public string CommentsPageFormat { get; set; } = "Page {0}";

        /// <summary>Status when the comments list is incomplete. {0} = reason.</summary>
        public string CommentsIncompleteFormat { get; set; } = "Not every comment is listed: {0}";

        /// <summary>The sidebar tab listing the document's signatures.</summary>
        public string SignaturesTab { get; set; } = "Signatures";

        /// <summary>Banner: every signature is valid and trusted.</summary>
        public string SignaturesAllValid { get; set; } = "Signed, and all signatures are valid.";

        /// <summary>Banner: none is invalid, but the validity of at least one is unknown.</summary>
        public string SignaturesSomeUnknown { get; set; } = "Signed; the validity of at least one signature is unknown.";

        /// <summary>Banner: at least one signature is invalid.</summary>
        public string SignaturesSomeInvalid { get; set; } = "At least one signature is invalid.";

        /// <summary>Banner and status when verification could not run. {0} = reason.</summary>
        public string SignaturesNotVerifiedFormat { get; set; } = "The signatures could not be verified: {0}";

        /// <summary>A signature's verdict: valid.</summary>
        public string SignatureValid { get; set; } = "Valid";

        /// <summary>A signature's verdict: intact, but the signer's identity or algorithm is not confirmed.</summary>
        public string SignatureUnknown { get; set; } = "Validity unknown";

        /// <summary>A signature's verdict: invalid.</summary>
        public string SignatureInvalid { get; set; } = "Invalid";

        /// <summary>A signature's detail: the time it claims. {0} = time.</summary>
        public string SignatureSignedAtFormat { get; set; } = "Signed: {0}";

        /// <summary>A signature's detail: its reason. {0} = reason.</summary>
        public string SignatureReasonFormat { get; set; } = "Reason: {0}";

        /// <summary>A signature's detail: its location. {0} = location.</summary>
        public string SignatureLocationFormat { get; set; } = "Location: {0}";

        /// <summary>A signature's detail: the certificate chain. {0} = status.</summary>
        public string SignatureChainFormat { get; set; } = "Certificate chain: {0}";

        /// <summary>A signature's detail: revocation. {0} = status.</summary>
        public string SignatureRevocationFormat { get; set; } = "Revocation: {0}";

        /// <summary>A signature's detail: a trusted timestamp. {0} = time.</summary>
        public string SignatureTimestampFormat { get; set; } = "Timestamp: {0}";

        /// <summary>A signature's detail: it certifies the document.</summary>
        public string SignatureCertifies { get; set; } = "Certification signature";

        /// <summary>A signature's detail: the document has later revisions.</summary>
        public string SignatureChangedAfter { get; set; } = "The document was changed after this signature.";

        /// <summary>A signature's detail: a weak algorithm. {0} = reason.</summary>
        public string SignatureWeakFormat { get; set; } = "Weak algorithm: {0}";

        /// <summary>The title of the window a click on a signed field opens.</summary>
        public string SignatureStatusTitle { get; set; } = "Signature Validation Status";

        /// <summary>Status line after signing a field from the viewer. {0} = field name.</summary>
        public string FieldSignedFormat { get; set; } = "Signed: {0}. Save to keep the signature.";

        /// <summary>Status line when the signer refused. {0} = reason.</summary>
        public string SigningFailedFormat { get; set; } = "Could not sign: {0}";

        /// <summary>Status when the document's opening view could not be read. {0} = reason.</summary>
        public string DocumentViewUnavailableFormat { get; set; } = "The document's opening view could not be read: {0}";

        /// <summary>The page menu's Copy.</summary>
        public string CopyMenu { get; set; } = "Copy";

        /// <summary>The page menu's Select All: the text of the page being read.</summary>
        public string SelectAllMenu { get; set; } = "Select All";
        /// <summary>The page menu's one page to a row.</summary>
        public string SpreadNoneMenu { get; set; } = "One Page";
        /// <summary>The page menu's two pages side by side.</summary>
        public string SpreadFacingMenu { get; set; } = "Two Pages";
        /// <summary>The page menu's two pages side by side with the first page alone.</summary>
        public string SpreadCoverMenu { get; set; } = "Two Pages, Cover Alone";
        /// <summary>The page menu's turn of the view a quarter clockwise.</summary>
        public string RotateClockwiseMenu { get; set; } = "Rotate Clockwise";
        /// <summary>The page menu's turn of the view a quarter counter-clockwise.</summary>
        public string RotateCounterclockwiseMenu { get; set; } = "Rotate Counterclockwise";

        /// <summary>What a screen reader calls the page box.</summary>
        public string PageBoxName { get; set; } = "Page number";

        /// <summary>What a screen reader calls the zoom box.</summary>
        public string ZoomBoxName { get; set; } = "Zoom";

        /// <summary>What a screen reader calls the find box.</summary>
        public string FindBoxName { get; set; } = "Text to find";

        /// <summary>The find bar's up arrow, named for a screen reader and as its tooltip.</summary>
        public string FindPreviousName { get; set; } = "Previous match (Shift+F3)";

        /// <summary>The find bar's down arrow.</summary>
        public string FindNextName { get; set; } = "Next match (F3)";

        /// <summary>The find bar's close button.</summary>
        public string FindCloseName { get; set; } = "Close the find bar (Esc)";

        /// <summary>What a screen reader calls the page area. {0} = the page shown (its label), {1} = the page count.</summary>
        public string PageSurfaceFormat { get; set; } = "Page {0} of {1}";

        /// <summary>A thumbnail, for a screen reader. {0} = the page (its label).</summary>
        public string ThumbnailFormat { get; set; } = "Page {0}";

        /// <summary>Tooltip of the Back button.</summary>
        public string BackTip { get; set; } = "Back (Alt+Left)";

        /// <summary>Tooltip of the Forward button.</summary>
        public string ForwardTip { get; set; } = "Forward (Alt+Right)";

        /// <summary>
        /// The page counter when the page box shows a page's label ("iii"). <c>{0}</c> is the page's
        /// number, <c>{1}</c> the page count.
        /// </summary>
        public string PageCountWithNumberFormat { get; set; } = "({0} / {1})";

        /// <summary>The find bar's result while a search has found nothing yet.</summary>
        public string Searching { get; set; } = "Searching…";

        /// <summary>
        /// The find bar's result while a search is still running. <c>{0}</c> is the match number,
        /// <c>{1}</c> the count found so far.
        /// </summary>
        public string FindResultSearchingFormat { get; set; } = "{0} / {1}…";

        /// <summary>The layout button in single-page layout; <see cref="Layout"/> names the continuous one.</summary>
        public string LayoutSinglePage { get; set; } = "Single page";

        /// <summary>Status when a page could not be rendered. {0} = page number, {1} = reason.</summary>
        public string PageFailedFormat { get; set; } = "Page {0}: {1}";

        /// <summary>Status when a page is refused as too large at the zoom. {0} = page number, {1} = megabytes.</summary>
        public string PageTooLargeFormat { get; set; } = "Page {0} is too large to render at this zoom ({1} MB).";

        /// <summary>Drawn on a page that could not be rendered.</summary>
        public string PageCannotRender { get; set; } = "(cannot render)";

        /// <summary>Status when the built-in open failed. {0} = reason.</summary>
        public string OpenFailedFormat { get; set; } = "Could not open the file: {0}";

        /// <summary>The open dialog's filter, in the dialog's own <c>description|pattern</c> form.</summary>
        public string OpenFileFilter { get; set; } = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*";

        /// <summary>The save dialog's filter.</summary>
        public string SaveFileFilter { get; set; } = "PDF files (*.pdf)|*.pdf";

        /// <summary>Status when the text layer could not be read. {0} = reason.</summary>
        public string TextUnavailableFormat { get; set; } = "Text layer unavailable: {0}";

        /// <summary>Status when the links could not be read. {0} = reason.</summary>
        public string LinksUnavailableFormat { get; set; } = "Links unavailable: {0}";

        /// <summary>Status when the outline could not be read. {0} = reason.</summary>
        public string BookmarksUnavailableFormat { get; set; } = "Bookmarks unavailable: {0}";

        /// <summary>A bookmark with no title.</summary>
        public string UntitledBookmark { get; set; } = "(untitled)";

        /// <summary>The print job's name when the document came from no file.</summary>
        public string PrintJobName { get; set; } = "PDF Document";

        /// <summary>Status when a page was not printed. {0} = page number, {1} = reason.</summary>
        public string PageNotPrintedFormat { get; set; } = "Page {0} was not printed: {1}";

        /// <summary>Printed in place of a page that could not be rendered. {0} = page number, {1} = reason.</summary>
        public string PagePrintFailedFormat { get; set; } = "Page {0} could not be printed: {1}";

        /// <summary>
        /// Formats <paramref name="template"/>, returning it unchanged if it has no placeholders or
        /// too few for the arguments given.
        /// </summary>
        /// <remarks>
        /// <b>A translated string is caller-supplied data, and it reaches
        /// <see cref="string.Format(string, object[])"/>.</b> A stray <c>{</c> — easy to type, easy
        /// for a translation tool to introduce — throws <see cref="FormatException"/>, and a viewer
        /// that crashes when a page finishes loading because someone mistyped a label is a worse
        /// outcome than a label that reads oddly. So the template is used when it works and shown
        /// literally when it does not.
        /// </remarks>
        internal static string Format(string template, params object[] args)
        {
            if (string.IsNullOrEmpty(template)) return "";
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }
        }

        /// <summary>The default English strings.</summary>
        public static PdfViewerStrings English() => new PdfViewerStrings();

        /// <summary>
        /// Hebrew strings, for the market this control's right-to-left chrome was built for.
        /// </summary>
        /// <remarks>
        /// <para>Shipped rather than left to the host because a Hebrew host that has to write out
        /// thirty labels usually does not, and the control then presents mirrored chrome with
        /// English text — which is the half-finished state this and the RTL chrome work exist
        /// together to avoid. It is a starting point a host may edit, not a policy: every property
        /// stays settable.</para>
        ///
        /// <para><b>Set <see cref="System.Windows.Forms.Control.RightToLeft"/> as well.</b> These
        /// strings do not mirror anything on their own — see the remarks on this type.</para>
        /// </remarks>
        public static PdfViewerStrings Hebrew() => new PdfViewerStrings
        {
            Open = "פתח",
            OpenTip = "פתיחת קובץ PDF (Ctrl+O)",
            PreviousPageTip = "העמוד הקודם (PageUp)",
            NextPageTip = "העמוד הבא (PageDown)",
            ZoomOutTip = "התרחקות (Ctrl+-)",
            ZoomInTip = "התקרבות (Ctrl++)",
            FitWidth = "רוחב",
            FitWidthTip = "התאמה לרוחב העמוד",
            FitPage = "עמוד",
            FitPageTip = "התאמה לעמוד מלא",
            Layout = "רציף",
            LayoutTip = "מעבר בין תצוגה רציפה לעמוד בודד",
            Sidebar = "סרגל צד",
            SidebarTip = "הצגה והסתרה של תמונות ממוזערות וסימניות",
            Find = "חיפוש",
            FindTip = "חיפוש טקסט (Ctrl+F)",
            Print = "הדפסה",
            PrintTip = "הדפסה או תצוגה מקדימה (Ctrl+P)",
            PagesTab = "עמודים",
            BookmarksTab = "סימניות",
            PageCountFormat = "/ {0}",
            PageCountUnknown = "/ –",
            Ready = "מוכן",
            OpenedWithTitleFormat = "{0} — {1} עמודים",
            OpenedFormat = "{0} עמודים",
            ThumbnailsLimitedFormat = " — תמונות ממוזערות מוגבלות ל-{0} הראשונות",
            NotFound = "לא נמצא",
            FindResultFormat = "{0} / {1}",
            FindResultTruncatedFormat = "{0} / {1}+",
            SelectedCharactersFormat = "נבחרו {0} תווים — Ctrl+C להעתקה",
            CopiedCharactersFormat = "הועתקו {0} תווים.",
            CopyFailedFormat = "ההעתקה נכשלה: {0}",
            BlockedLinkFormat = "קישור חסום (סכימה לא נתמכת): {0}",
            LinkFailedFormat = "לא ניתן לפתוח את הקישור: {0}",
            ExternalFileLinkFormat = "קישור לקובץ אחר, לא נפתח: {0}",
            UnsupportedLinkActionFormat = "פעולת הקישור אינה נתמכת: {0}",
            LinkToPageFormat = "מעבר לעמוד {0}",
            Save = "שמור",
            SaveTip = "שמירת הטופס המלא (Ctrl+S)",
            SavedFormat = "נשמר: {0}",
            SaveFailedFormat = "לא ניתן לשמור: {0}",
            FormFillFailedFormat = "לא ניתן למלא את השדה: {0}",
            FieldsNotDrawnFormat = "נשמר אך לא צויר: {0}",
            FieldValueRefusedFormat = "{0}: הערך נדחה. {1}",
            FormScriptsFailedFormat = "לא ניתן להריץ את הסקריפטים של הטופס: {0}",
            FormScriptsNotRunFormat = "סקריפטי הטופס לא הורצו — {0}",
            CommentsTab = "הערות",
            CommentsPageFormat = "עמוד {0}",
            CommentsIncompleteFormat = "לא כל ההערות מוצגות: {0}",
            SignaturesTab = "חתימות",
            SignaturesAllValid = "המסמך חתום, וכל החתימות תקינות.",
            SignaturesSomeUnknown = "המסמך חתום; תקינותה של חתימה אחת לפחות אינה ידועה.",
            SignaturesSomeInvalid = "חתימה אחת לפחות אינה תקינה.",
            SignaturesNotVerifiedFormat = "לא ניתן לאמת את החתימות: {0}",
            SignatureValid = "תקינה",
            SignatureUnknown = "תקינות לא ידועה",
            SignatureInvalid = "לא תקינה",
            SignatureSignedAtFormat = "נחתם: {0}",
            SignatureReasonFormat = "סיבה: {0}",
            SignatureLocationFormat = "מיקום: {0}",
            SignatureChainFormat = "שרשרת אישורים: {0}",
            SignatureRevocationFormat = "ביטול: {0}",
            SignatureTimestampFormat = "חותמת זמן: {0}",
            SignatureCertifies = "חתימת אישור",
            SignatureChangedAfter = "המסמך שונה אחרי חתימה זו.",
            SignatureWeakFormat = "אלגוריתם חלש: {0}",
            SignatureStatusTitle = "מצב אימות החתימה",
            FieldSignedFormat = "נחתם: {0}. החתימה תישמר רק לאחר שמירת הקובץ.",
            SigningFailedFormat = "לא ניתן לחתום: {0}",
            DocumentViewUnavailableFormat = "לא ניתן לקרוא את תצוגת הפתיחה של המסמך: {0}",
            CopyMenu = "העתקה",
            SelectAllMenu = "בחירת הכול",
            SpreadNoneMenu = "עמוד אחד",
            SpreadFacingMenu = "שני עמודים",
            SpreadCoverMenu = "שני עמודים, עמוד שער לבד",
            RotateClockwiseMenu = "סיבוב עם כיוון השעון",
            RotateCounterclockwiseMenu = "סיבוב נגד כיוון השעון",
            PageBoxName = "מספר עמוד",
            ZoomBoxName = "הגדלה",
            FindBoxName = "טקסט לחיפוש",
            FindPreviousName = "ההתאמה הקודמת (Shift+F3)",
            FindNextName = "ההתאמה הבאה (F3)",
            FindCloseName = "סגירת סרגל החיפוש (Esc)",
            PageSurfaceFormat = "עמוד {0} מתוך {1}",
            ThumbnailFormat = "עמוד {0}",
            BackTip = "חזרה (Alt+←)",
            ForwardTip = "קדימה (Alt+→)",
            PageCountWithNumberFormat = "({0} / {1})",
            Searching = "מחפש…",
            FindResultSearchingFormat = "{0} / {1}…",
            LayoutSinglePage = "עמוד בודד",
            PageFailedFormat = "עמוד {0}: {1}",
            PageTooLargeFormat = "עמוד {0} גדול מכדי להציג אותו בהגדלה זו ({1} MB).",
            PageCannotRender = "(לא ניתן להציג)",
            OpenFailedFormat = "לא ניתן לפתוח את הקובץ: {0}",
            OpenFileFilter = "קובצי PDF (*.pdf)|*.pdf|כל הקבצים (*.*)|*.*",
            SaveFileFilter = "קובצי PDF (*.pdf)|*.pdf",
            TextUnavailableFormat = "שכבת הטקסט אינה זמינה: {0}",
            LinksUnavailableFormat = "הקישורים אינם זמינים: {0}",
            BookmarksUnavailableFormat = "הסימניות אינן זמינות: {0}",
            UntitledBookmark = "(ללא כותרת)",
            PrintJobName = "מסמך PDF",
            PageNotPrintedFormat = "עמוד {0} לא הודפס: {1}",
            PagePrintFailedFormat = "לא ניתן להדפיס את עמוד {0}: {1}",
        };
    }
}
