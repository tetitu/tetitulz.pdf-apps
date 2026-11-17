// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations.Forms;
using Tetitulz.Pdf.Core.Forms;
using Tetitulz.Pdf.Signing;
using Tetitulz.Pdf.Signing.Models;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>How a signature stands, as a reader is shown it.</summary>
    internal enum SignatureVerdict
    {
        /// <summary>Intact, chained to a trusted root, not revoked, no weak algorithm.</summary>
        Valid,
        /// <summary>Intact, but the signer's identity or its algorithm is not confirmed.</summary>
        Unknown,
        /// <summary>Altered, malformed, changed against its certification, revoked or with a broken chain.</summary>
        Invalid,
    }

    /// <summary>
    /// The document's digital signatures (<see cref="PdfViewerFeatures.Signatures"/>, off by default):
    /// verified through the library (<c>PdfSigningOperations</c>, or <c>PdfEncryptionOperations</c>
    /// for an encrypted file), a banner with the overall verdict, a Signatures tab with each
    /// signature's details, and each signed field described on hover and opened by a click — its row
    /// selected and its validation status shown — with its appearance drawn as the file draws it.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        private SignatureVerificationOptions? _signatureVerification;
        private bool _showSignatureBanner = true;
        private IReadOnlyList<SignatureVerificationResult> _signatureResults = Array.Empty<SignatureVerificationResult>();
        private string? _signatureError;
        private readonly ToolTip _signatureTip = new ToolTip();
        private SignatureVerificationResult? _hoverSignature;

        /// <summary>Raised when a document's signatures have been verified.</summary>
        public event EventHandler<PdfViewerSignaturesVerifiedEventArgs>? SignaturesVerified;

        /// <summary>Raised when the user clicks a signed signature field; see <see cref="PdfViewerSignatureClickedEventArgs"/>.</summary>
        public event EventHandler<PdfViewerSignatureClickedEventArgs>? SignatureClicked;

        /// <summary>
        /// The policy signatures are verified under: trust anchors (without them no signer is
        /// confirmed, and a verdict is at best "validity unknown"), online revocation, weak-algorithm
        /// rules. Null (the default) is the library's defaults. Setting it verifies again.
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public SignatureVerificationOptions? SignatureVerification
        {
            get => _signatureVerification;
            set
            {
                _signatureVerification = value;
                if (_doc != null) PopulateSignatures();
            }
        }

        /// <summary>The banner above the pages with the signatures' overall verdict. Default on.</summary>
        [Category("PDF"), DefaultValue(true), Description("Show the signatures' verdict above the pages.")]
        public bool ShowSignatureBanner
        {
            get => _showSignatureBanner;
            set { _showSignatureBanner = value; UpdateSignatureBanner(); }
        }

        /// <summary>The document's signatures as last verified; empty with the feature off or no signature.</summary>
        [Browsable(false)]
        public IReadOnlyList<SignatureVerificationResult> SignatureResults => _signatureResults;

        /// <summary>The tree of signatures — what a test reads.</summary>
        internal TreeView SignatureTree => _signatureTree;

        /// <summary>The banner's text and whether it shows — what a test reads.</summary>
        internal Label SignatureBanner => _signatureBanner;

        /// <summary>The hover text the page shows now, or empty — what a test reads.</summary>
        internal string SignatureToolTipText => _signatureTip.GetToolTip(_surface) ?? "";

        /// <summary>
        /// A signature's verdict. Invalid when the bytes or structure fail (a disallowed change after
        /// a certification included), the chain is broken or the certificate revoked; valid only when
        /// fully trusted with no weak algorithm; otherwise unknown — Acrobat's three.
        /// </summary>
        internal static SignatureVerdict VerdictOf(SignatureVerificationResult r)
        {
            if (!r.IsValid || r.ChainStatus == PdfSignatureChainStatus.Invalid
                || r.RevocationStatus == PdfRevocationStatus.Revoked)
                return SignatureVerdict.Invalid;
            return r.IsFullyTrusted && !r.WeakAlgorithm ? SignatureVerdict.Valid : SignatureVerdict.Unknown;
        }

        /// <summary>The worst verdict among the document's signatures; null for none.</summary>
        internal static SignatureVerdict? OverallVerdict(IEnumerable<SignatureVerificationResult> results)
        {
            SignatureVerdict? worst = null;
            foreach (var r in results)
            {
                var v = VerdictOf(r);
                if (worst == null || v > worst) worst = v;
            }
            return worst;
        }

        /// <summary>Verifies the shown document's signatures and fills the tab and the banner.</summary>
        private void PopulateSignatures()
        {
            _signatureTree.BeginUpdate();
            _signatureTree.Nodes.Clear();
            _signatureResults = Array.Empty<SignatureVerificationResult>();
            _signatureError = null;
            if (_doc != null && Has(PdfViewerFeatures.Signatures))
            {
                _signatureResults = _doc.VerifySignatures(_signatureVerification, out _signatureError);
                foreach (var r in _signatureResults)
                    _signatureTree.Nodes.Add(SignatureNode(r));
                if (_signatureError != null)
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.SignaturesNotVerifiedFormat, _signatureError);
            }
            _signatureTree.EndUpdate();
            UpdateSignatureBanner();
            _surface.Invalidate();
            if (_doc != null && Has(PdfViewerFeatures.Signatures))
                SignaturesVerified?.Invoke(this, new PdfViewerSignaturesVerifiedEventArgs(_signatureResults, _signatureError));
        }

        /// <summary>A signature's row — its verdict, its signer — and its details beneath.</summary>
        private TreeNode SignatureNode(SignatureVerificationResult r)
        {
            var node = new TreeNode(SignatureTitle(r)) { Tag = r, ForeColor = _palette.VerdictText(VerdictOf(r)) };
            foreach (string line in SignatureDetails(r))
                node.Nodes.Add(new TreeNode(line) { Tag = r });
            return node;
        }

        /// <summary>"✓ Dana Levi — Valid": the verdict's mark, the signer, the verdict.</summary>
        internal string SignatureTitle(SignatureVerificationResult r)
        {
            var v = VerdictOf(r);
            string mark = v == SignatureVerdict.Valid ? "✓" : v == SignatureVerdict.Unknown ? "⚠" : "✗";
            string signer = !string.IsNullOrEmpty(r.SignerCommonName) ? r.SignerCommonName!
                          : !string.IsNullOrEmpty(r.FieldName) ? r.FieldName! : "?";
            return mark + " " + signer + " — " + VerdictText(v);
        }

        private string VerdictText(SignatureVerdict v)
            => v == SignatureVerdict.Valid ? _strings.SignatureValid
             : v == SignatureVerdict.Unknown ? _strings.SignatureUnknown
             : _strings.SignatureInvalid;

        /// <summary>A signature's details, one line each: when, why, where, its chain, revocation, timestamp, and what is wrong.</summary>
        internal IEnumerable<string> SignatureDetails(SignatureVerificationResult r)
        {
            DateTime? when = r.ClaimedSigningTime;
            if (when.HasValue)
                yield return PdfViewerStrings.Format(_strings.SignatureSignedAtFormat, when.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            if (!string.IsNullOrEmpty(r.Reason)) yield return PdfViewerStrings.Format(_strings.SignatureReasonFormat, r.Reason!);
            if (!string.IsNullOrEmpty(r.Location)) yield return PdfViewerStrings.Format(_strings.SignatureLocationFormat, r.Location!);
            if (r.IsCertification) yield return _strings.SignatureCertifies;
            yield return PdfViewerStrings.Format(_strings.SignatureChainFormat, r.ChainStatus.ToString());
            yield return PdfViewerStrings.Format(_strings.SignatureRevocationFormat, r.RevocationStatus.ToString());
            if (r.TimestampTime.HasValue)
                yield return PdfViewerStrings.Format(_strings.SignatureTimestampFormat, r.TimestampTime.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            if (!r.CoversEntireDocument) yield return _strings.SignatureChangedAfter;
            if (r.WeakAlgorithm) yield return PdfViewerStrings.Format(_strings.SignatureWeakFormat, r.WeakAlgorithmReason ?? "");
            foreach (string e in r.ValidationErrors) yield return e;
            foreach (string w in r.Warnings) yield return w;
        }

        /// <summary>The banner: the overall verdict, or why verification could not run; hidden for an unsigned document.</summary>
        private void UpdateSignatureBanner()
        {
            SignatureVerdict? overall = OverallVerdict(_signatureResults);
            bool show = _showSignatureBanner && _doc != null && Has(PdfViewerFeatures.Signatures)
                        && (overall != null || _signatureError != null);
            if (show)
            {
                _signatureBanner.Text = _signatureError != null
                    ? PdfViewerStrings.Format(_strings.SignaturesNotVerifiedFormat, _signatureError)
                    : overall == SignatureVerdict.Valid ? _strings.SignaturesAllValid
                    : overall == SignatureVerdict.Unknown ? _strings.SignaturesSomeUnknown
                    : _strings.SignaturesSomeInvalid;
                _bannerTone = _signatureError != null ? SignatureVerdict.Invalid : overall!.Value;
                _signatureBanner.Invalidate();
            }
            _signatureBanner.Visible = show;
        }

        /// <summary>The signed field of <paramref name="r"/> — matched by its fully qualified name, then its own.</summary>
        private PdfFormFieldInfo? SignatureField(SignatureVerificationResult r)
        {
            if (_doc == null || string.IsNullOrEmpty(r.FieldName)) return null;
            var signatures = _doc.FormFields.Where(f => f.FieldType == PdfFieldType.Signature).ToList();
            return signatures.FirstOrDefault(f => f.FullyQualifiedName == r.FieldName)
                ?? signatures.FirstOrDefault(f => f.PartialName == r.FieldName);
        }

        internal void SignatureTree_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (!(e.Node?.Tag is SignatureVerificationResult r)) return;
            PdfFormWidgetInfo? w = SignatureField(r)?.Widgets.FirstOrDefault();
            if (w == null) return;
            double top = w.Y + w.Height;
            float? at = double.IsNaN(top) || double.IsInfinity(top) || Math.Abs(top) > float.MaxValue ? (float?)null : (float)top;
            RunCommand(PdfViewerCommand.GoToPage, () => JumpTo(w.PageIndex, at), pageIndex: w.PageIndex);
        }

        /// <summary>
        /// Shows a signature's validation status — its title, then its details — as a modal window.
        /// A test replaces it, since a modal window on a test thread has no one to close it.
        /// </summary>
        internal Action<string, string, SignatureVerdict> ShowSignatureStatus;

        private void ShowSignatureStatusWindow(string title, string text, SignatureVerdict verdict)
        {
            MessageBoxIcon icon = verdict == SignatureVerdict.Valid ? MessageBoxIcon.Information
                                : verdict == SignatureVerdict.Unknown ? MessageBoxIcon.Warning
                                : MessageBoxIcon.Error;
            MessageBoxOptions rtl = RightToLeft == RightToLeft.Yes
                ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
            MessageBox.Show(this, text, title, MessageBoxButtons.OK, icon, MessageBoxDefaultButton.Button1, rtl);
        }

        /// <summary>
        /// Acts on a press over a signed field, as Acrobat does: raises <see cref="SignatureClicked"/>,
        /// then, unless the host handled it, selects the signature's row in the Signatures tab and
        /// shows its validation status. True when the press was the signature's.
        /// </summary>
        private bool OpenSignature(Point v)
        {
            SignatureVerificationResult? r = HitTestSignature(v);
            if (r == null) return false;
            var args = new PdfViewerSignatureClickedEventArgs(r, PageAtVirtual(v));
            SignatureClicked?.Invoke(this, args);
            if (args.Handled) return true;

            foreach (TreeNode row in _signatureTree.Nodes)
            {
                if (!ReferenceEquals(row.Tag, r)) continue;
                if (_sidebarTabs.TabPages.Contains(_tabSignatures)) _sidebarTabs.SelectedTab = _tabSignatures;
                row.Expand();
                _signatureTree.SelectedNode = row;
                break;
            }
            ShowSignatureStatus(_strings.SignatureStatusTitle,
                SignatureTitle(r) + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, SignatureDetails(r)),
                VerdictOf(r));
            return true;
        }

        /// <summary>The signature whose field is under a virtual-space point, or null.</summary>
        private SignatureVerificationResult? HitTestSignature(Point v)
        {
            if (_doc == null || !Has(PdfViewerFeatures.Signatures) || _signatureResults.Count == 0) return null;
            int page = PageAtVirtual(v);
            if (page < 0 || !TryGetBounds(page, out Rectangle bounds)) return null;
            foreach (var r in _signatureResults)
            {
                var field = SignatureField(r);
                if (field == null) continue;
                foreach (var w in field.Widgets)
                    if (w.PageIndex == page && WidgetToCanvas(w, bounds).Contains(v)) return r;
            }
            return null;
        }

        /// <summary>Hover over a signed field: its title and details as the tooltip.</summary>
        private void UpdateSignatureHover(Point v)
        {
            SignatureVerificationResult? r = HitTestSignature(v);
            if (ReferenceEquals(r, _hoverSignature)) return;
            _hoverSignature = r;
            _signatureTip.SetToolTip(_surface, r == null ? null
                : SignatureTitle(r) + Environment.NewLine + string.Join(Environment.NewLine, SignatureDetails(r)));
        }
    }
}
