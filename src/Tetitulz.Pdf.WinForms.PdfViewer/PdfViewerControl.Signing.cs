// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations.Forms;
using Tetitulz.Pdf.Core.Forms;
using Tetitulz.Pdf.Signing.Models;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Signing from the viewer (<see cref="PdfViewerFeatures.Signing"/>, off by default): an empty
    /// signature field is marked on its page, a click on it asks the host for the signer
    /// (<see cref="SignatureRequested"/>), and <see cref="SignField"/> signs from code. The signed
    /// document replaces the shown one and is unsaved until <see cref="SaveAs(System.IO.Stream)"/>.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        private bool _unsavedSignature;

        /// <summary>
        /// Raised when the user clicks an empty signature field with <see cref="PdfViewerFeatures.Signing"/>
        /// on. Set <see cref="PdfViewerSignatureRequestEventArgs.Parameters"/> to sign it.
        /// </summary>
        public event EventHandler<PdfViewerSignatureRequestEventArgs>? SignatureRequested;

        /// <summary>Raised after a signature made from the viewer, or the signer's refusal of one.</summary>
        public event EventHandler<PdfViewerFieldSignedEventArgs>? FieldSigned;

        /// <summary>
        /// Signs the empty signature field <paramref name="fieldName"/> with <paramref name="parameters"/>
        /// (whose <see cref="SignatureParameters.SignatureFieldName"/> is set to it): the original file,
        /// then the values typed into the form as one revision, then the signature — so it covers the
        /// values and leaves an earlier signature valid; an encrypted file is signed under its own key.
        /// The signed document is shown and unsaved until <see cref="SaveAs(System.IO.Stream)"/>.
        /// False, and nothing signed, when no document is loaded, <see cref="PdfViewerFeatures.Signing"/>
        /// is off or the field is not an empty signature field.
        /// </summary>
        /// <exception cref="Tetitulz.Pdf.Core.PdfException">The signer refused (a wrong certificate
        /// password, a certification that forbids another signature, …); the shown document is unchanged.</exception>
        public bool SignField(string fieldName, SignatureParameters parameters)
        {
            if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (_doc == null || !Has(PdfViewerFeatures.Signing) || !_doc.UnsignedSignatureFields.Contains(fieldName))
                return false;

            EndFieldEdit(commit: true);
            parameters.SignatureFieldName = fieldName;
            PdfDocumentSource signed;
            try
            {
                signed = _doc.Signed(_fieldValues, _displayValues, parameters);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = PdfViewerStrings.Format(_strings.SigningFailedFormat, ex.Message);
                FieldSigned?.Invoke(this, new PdfViewerFieldSignedEventArgs(fieldName, ex.Message));
                throw;
            }

            // The typed values are in the signed file now: the form starts again from it.
            ResetFormState();
            ReplaceDocument(signed);
            _unsavedSignature = true;
            ClearRenderCache();
            _textCache.Clear();
            _pageTexts.Clear();
            PopulateSidebar();
            _surface.Invalidate();
            UpdateSaveButton();
            _statusLabel.Text = PdfViewerStrings.Format(_strings.FieldSignedFormat, fieldName);
            FieldSigned?.Invoke(this, new PdfViewerFieldSignedEventArgs(fieldName, null));
            return true;
        }

        /// <summary>Whether the document has something to save: values typed, or a signature made.</summary>
        private bool CanSave => Has(PdfViewerFeatures.FormFilling) || Has(PdfViewerFeatures.Signing);

        // ── A press on an empty signature field ───────────────────────────────

        /// <summary>The empty signature field and widget under a canvas point, the last drawn first.</summary>
        private (PdfFormFieldInfo Field, PdfFormWidgetInfo Widget)? HitTestUnsignedSignature(Point v)
        {
            if (_doc == null || !Has(PdfViewerFeatures.Signing)) return null;
            int page = PageAtVirtual(v);
            if (page < 0 || !TryGetBounds(page, out Rectangle bounds)) return null;
            (PdfFormFieldInfo, PdfFormWidgetInfo)? hit = null;
            foreach (var (field, widget) in UnsignedSignatureWidgetsOn(page))
                if (WidgetToCanvas(widget, bounds).Contains(v)) hit = (field, widget);
            return hit;
        }

        /// <summary>
        /// The empty signature fields' widgets on <paramref name="page"/> a user may sign: shown by the
        /// page (not <c>/F</c> Hidden or NoView), permitted by the document's permissions, and not under a
        /// certification or lock that forbids the change.
        /// </summary>
        private System.Collections.Generic.IEnumerable<(PdfFormFieldInfo Field, PdfFormWidgetInfo Widget)> UnsignedSignatureWidgetsOn(int page)
        {
            if (_doc == null) yield break;
            if (!Permits(Tetitulz.Pdf.Base.Encryption.PdfPermissions.FillForms | Tetitulz.Pdf.Base.Encryption.PdfPermissions.AnnotateAndForms)) yield break;
            var unsigned = _doc.UnsignedSignatureFields;
            if (unsigned.Count == 0) yield break;
            foreach (var field in _doc.FormFields)
            {
                if (field.FieldType != PdfFieldType.Signature || !unsigned.Contains(field.FullyQualifiedName)) continue;
                if (_doc.IsFieldLocked(field.FullyQualifiedName)) continue;
                foreach (var widget in field.Widgets)
                    if (widget.PageIndex == page && !widget.IsHiddenOnScreen) yield return (field, widget);
            }
        }

        /// <summary>
        /// Acts on a press over an empty signature field: asks the host for the signer and signs.
        /// True when the press was the field's, so no editor, link or selection starts; a refusal is
        /// said in the status line and through <see cref="FieldSigned"/>.
        /// </summary>
        private bool BeginSigning(Point v)
        {
            var hit = HitTestUnsignedSignature(v);
            if (hit == null) return false;
            var (field, widget) = hit.Value;

            var request = new PdfViewerSignatureRequestEventArgs(field.FullyQualifiedName, widget.PageIndex);
            SignatureRequested?.Invoke(this, request);
            if (request.Parameters == null) return true;
            try
            {
                SignField(field.FullyQualifiedName, request.Parameters);
            }
            catch (Exception)
            {
                // Said in the status line and through FieldSigned by SignField.
            }
            return true;
        }

        // ── Painting ──────────────────────────────────────────────────────────

        private static readonly Color UnsignedFieldColor = ViewerPalette.UnsignedField;

        /// <summary>The page's empty signature fields drawn onto <paramref name="g"/> — what a test paints without a window.</summary>
        internal void PaintUnsignedSignatureFields(Graphics g, int pageIndex, Rectangle pageBounds)
            => DrawUnsignedSignatureFields(g, pageIndex, pageBounds);

        /// <summary>Each empty signature field tinted and outlined in dashes: a place to sign.</summary>
        private void DrawUnsignedSignatureFields(Graphics g, int pageIndex, Rectangle bounds)
        {
            if (!Has(PdfViewerFeatures.Signing) || _doc == null) return;
            using (var tint = new SolidBrush(UnsignedFieldColor))
            using (var pen = new Pen(ViewerPalette.UnsignedFieldBorder, 1.5f) { DashStyle = DashStyle.Dash })
                foreach (var (_, widget) in UnsignedSignatureWidgetsOn(pageIndex))
                {
                    RectangleF r = WidgetToCanvas(widget, bounds);
                    g.FillRectangle(tint, r);
                    g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                }
        }
    }
}
