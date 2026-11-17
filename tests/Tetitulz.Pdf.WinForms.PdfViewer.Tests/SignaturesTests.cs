// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Tetitulz.Pdf.Base.Encryption;
using Tetitulz.Pdf.Base.Operations.Forms;
using Tetitulz.Pdf.Encryption;
using Tetitulz.Pdf.Signing;
using Tetitulz.Pdf.Signing.Models;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// <see cref="PdfViewerFeatures.Signatures"/>: each signature verified through the library, the
/// overall verdict in a banner, each signature in a Signatures tab, and each signed field marked
/// with its verdict on hover.
/// </summary>
/// <remarks>
/// The certificates are made here, in memory — a CA and a signer it issues — as the signing tests
/// make theirs; test projects do not share helpers. Supplying the CA as a trust anchor is what turns
/// "validity unknown" into "valid".
/// </remarks>
public sealed class SignaturesTests
{
    private const PdfViewerFeatures WithSignatures = PdfViewerFeatures.Default | PdfViewerFeatures.Signatures;

    private static readonly Lazy<(X509Certificate Root, byte[] Pfx)> Keys = new(() =>
        Tetitulz.Pdf.TestHelpers.RsaTestKeys.Shared(() =>
        {
            var rootKeys = Tetitulz.Pdf.TestHelpers.RsaTestKeys.Next();
            var root = Certificate("CN=Viewer Test Root", "CN=Viewer Test Root", rootKeys.Public, rootKeys.Private, ca: true);
            var leafKeys = Tetitulz.Pdf.TestHelpers.RsaTestKeys.Next();
            var leaf = Certificate("CN=Viewer Test Root", "CN=Dana Signer", leafKeys.Public, rootKeys.Private, ca: false);
            return (root, Pfx(leaf, leafKeys.Private, root));
        }));

    /// <summary>
    /// The verdict's rules, case by case: revoked or a broken chain is invalid however intact; a weak
    /// algorithm keeps a trusted signature from "valid"; the overall verdict is the worst one.
    /// </summary>
    [Fact]
    public void TheVerdict_FollowsTheRules()
    {
        static SignatureVerificationResult Result(PdfSignatureChainStatus chain, PdfRevocationStatus revocation,
            bool intact = true, bool weak = false)
            => new SignatureVerificationResult { IsIntegrityValid = intact, ChainStatus = chain, RevocationStatus = revocation, WeakAlgorithm = weak };

        var trusted = Result(PdfSignatureChainStatus.Trusted, PdfRevocationStatus.Good);
        Assert.Equal(SignatureVerdict.Valid, PdfViewerControl.VerdictOf(trusted));
        Assert.Equal(SignatureVerdict.Invalid, PdfViewerControl.VerdictOf(Result(PdfSignatureChainStatus.Trusted, PdfRevocationStatus.Revoked)));
        Assert.Equal(SignatureVerdict.Invalid, PdfViewerControl.VerdictOf(Result(PdfSignatureChainStatus.Invalid, PdfRevocationStatus.Good)));
        Assert.Equal(SignatureVerdict.Invalid, PdfViewerControl.VerdictOf(Result(PdfSignatureChainStatus.Trusted, PdfRevocationStatus.Good, intact: false)));
        Assert.Equal(SignatureVerdict.Unknown, PdfViewerControl.VerdictOf(Result(PdfSignatureChainStatus.Trusted, PdfRevocationStatus.Good, weak: true)));
        Assert.Equal(SignatureVerdict.Unknown, PdfViewerControl.VerdictOf(Result(PdfSignatureChainStatus.SelfSigned, PdfRevocationStatus.NotChecked)));

        var unknown = Result(PdfSignatureChainStatus.UntrustedRoot, PdfRevocationStatus.NotChecked);
        var invalid = Result(PdfSignatureChainStatus.Trusted, PdfRevocationStatus.Revoked);
        Assert.Equal(SignatureVerdict.Unknown, PdfViewerControl.OverallVerdict(new[] { trusted, unknown, trusted }));
        Assert.Equal(SignatureVerdict.Invalid, PdfViewerControl.OverallVerdict(new[] { invalid, unknown }));
        Assert.Equal(SignatureVerdict.Valid, PdfViewerControl.OverallVerdict(new[] { trusted }));
        Assert.Null(PdfViewerControl.OverallVerdict(Array.Empty<SignatureVerificationResult>()));
    }

    [Fact]
    public void Signatures_AreOffByDefault()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl();
            viewer.LoadBytes(Signed());
            Assert.Empty(viewer.SignatureResults);
            Assert.DoesNotContain(viewer.SidebarTabs.TabPages.Cast<TabPage>(), t => t.Controls.Contains(viewer.SignatureTree));
            Assert.False(viewer.SignatureBanner.Visible);
        });
    }

    [Fact]
    public void ATrustedSignature_IsValid_InTheBannerTheTabAndTheEvent()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = WithSignatures, SignatureVerification = Trusting() };
            var raised = new List<PdfViewerSignaturesVerifiedEventArgs>();
            viewer.SignaturesVerified += (_, e) => raised.Add(e);
            viewer.LoadBytes(Signed());

            var r = Assert.Single(viewer.SignatureResults);
            Assert.True(r.IsFullyTrusted);
            Assert.Equal(SignatureVerdict.Valid, PdfViewerControl.VerdictOf(r));
            Assert.True(viewer.SignatureBanner.Visible);
            Assert.Equal("Signed, and all signatures are valid.", viewer.SignatureBanner.Text);

            TreeNode row = Assert.Single(viewer.SignatureTree.Nodes.Cast<TreeNode>());
            Assert.Equal("✓ Dana Signer — Valid", row.Text);
            Assert.Contains(row.Nodes.Cast<TreeNode>(), n => n.Text == "Reason: Approved");
            Assert.Contains(row.Nodes.Cast<TreeNode>(), n => n.Text == "Certificate chain: Trusted");

            var e = Assert.Single(raised);
            Assert.Same(viewer.SignatureResults, e.Results);
            Assert.Null(e.Error);
        });
    }

    /// <summary>Without a trust anchor the signer is not confirmed: intact, validity unknown — as Acrobat says.</summary>
    [Fact]
    public void WithoutATrustAnchor_TheValidityIsUnknown_AndSettingOneVerifiesAgain()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = WithSignatures };
            viewer.LoadBytes(Signed());

            var r = Assert.Single(viewer.SignatureResults);
            Assert.True(r.IsValid);
            Assert.Equal(SignatureVerdict.Unknown, PdfViewerControl.VerdictOf(r));
            Assert.Equal("Signed; the validity of at least one signature is unknown.", viewer.SignatureBanner.Text);
            Assert.StartsWith("⚠ Dana Signer", viewer.SignatureTree.Nodes[0].Text);

            viewer.SignatureVerification = Trusting();
            Assert.Equal(SignatureVerdict.Valid, PdfViewerControl.VerdictOf(Assert.Single(viewer.SignatureResults)));
            Assert.Equal("Signed, and all signatures are valid.", viewer.SignatureBanner.Text);
        });
    }

    /// <summary>A byte of the signed revision changed: the signature is invalid, whatever trusts it.</summary>
    [Fact]
    public void AlteredBytes_MakeTheSignatureInvalid()
    {
        RunSta(() =>
        {
            byte[] pdf = Signed();
            string text = Encoding.Latin1.GetString(pdf);
            int at = text.IndexOf("/T (note)", StringComparison.Ordinal);
            Assert.True(at > 0);
            pdf[at + 4] = (byte)'N';                                             // (note) → (Note), inside the signed bytes

            using var viewer = new PdfViewerControl { Features = WithSignatures, SignatureVerification = Trusting() };
            viewer.LoadBytes(pdf);

            var r = Assert.Single(viewer.SignatureResults);
            Assert.False(r.IsIntegrityValid);
            Assert.Equal(SignatureVerdict.Invalid, PdfViewerControl.VerdictOf(r));
            Assert.Equal("At least one signature is invalid.", viewer.SignatureBanner.Text);
            Assert.StartsWith("✗ ", viewer.SignatureTree.Nodes[0].Text);
        });
    }

    /// <summary>A later revision (a field filled after signing) is said; the signature over its revision stands.</summary>
    [Fact]
    public void ARevisionAfterSigning_IsSaid_AndTheSignatureStands()
    {
        RunSta(() =>
        {
            using var input = new MemoryStream(Signed());
            using var output = new MemoryStream();
            PdfFormFiller.Fill(input, output, new Dictionary<string, string> { ["note"] = "later" }, new FormFillOptions());

            using var viewer = new PdfViewerControl { Features = WithSignatures, SignatureVerification = Trusting() };
            viewer.LoadBytes(output.ToArray());

            var r = Assert.Single(viewer.SignatureResults);
            Assert.True(r.IsIntegrityValid);
            Assert.False(r.CoversEntireDocument);
            Assert.Contains(viewer.SignatureTree.Nodes[0].Nodes.Cast<TreeNode>(),
                n => n.Text == "The document was changed after this signature.");

            // The control: the signed file as it was says nothing of the kind.
            viewer.LoadBytes(Signed());
            Assert.DoesNotContain(viewer.SignatureTree.Nodes[0].Nodes.Cast<TreeNode>(),
                n => n.Text == "The document was changed after this signature.");
        });
    }

    /// <summary>An encrypted signed file is verified under its password — the graph decrypted, the bytes the original's.</summary>
    [Fact]
    public void AnEncryptedSignedFile_IsVerifiedUnderItsPassword()
    {
        RunSta(() =>
        {
            byte[] encrypted;
            using (var plain = new MemoryStream(TestPdf.SignableForm()))
            using (var output = new MemoryStream())
            {
                PdfEncryptor.Encrypt(plain, output, new EncryptionParameters
                {
                    UserPassword = "user", OwnerPassword = "owner", Algorithm = EncryptionAlgorithm.AES128,
                });
                encrypted = output.ToArray();
            }
            byte[] signed;
            using (var input = new MemoryStream(encrypted))
            using (var output = new MemoryStream())
            {
                PdfEncryptionOperations.Sign(input, output, "user", Parameters());
                signed = output.ToArray();
            }

            using var viewer = new PdfViewerControl { Features = WithSignatures, SignatureVerification = Trusting(), Password = "user" };
            viewer.LoadBytes(signed);

            var r = Assert.Single(viewer.SignatureResults);
            Assert.True(r.IsIntegrityValid);
            Assert.Equal(SignatureVerdict.Valid, PdfViewerControl.VerdictOf(r));
        });
    }

    [Fact]
    public void ClickingASignature_GoesToItsField_AndHoveringItsFieldDescribesIt()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = WithSignatures, SignatureVerification = Trusting() };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(Signed());

            viewer.GoToPage(1, 350f);
            int expected = viewer.LastDestinationScroll;
            viewer.GoToPage(0, 100f);
            Assert.NotEqual(expected, viewer.LastDestinationScroll);

            TreeNode row = viewer.SignatureTree.Nodes[0];
            viewer.SignatureTree_NodeMouseClick(null, new TreeNodeMouseClickEventArgs(row, MouseButtons.Left, 1, 0, 0));
            Assert.Equal(1, viewer.CurrentPage);
            Assert.Equal(expected, viewer.LastDestinationScroll);

            Point onField = SurfacePoint(viewer, 1, 150, 325);
            viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, onField.X, onField.Y, 0));
            Assert.StartsWith("✓ Dana Signer — Valid", viewer.SignatureToolTipText);
            Assert.Contains("Reason: Approved", viewer.SignatureToolTipText);

            Point off = SurfacePoint(viewer, 1, 350, 50);
            viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, off.X, off.Y, 0));
            Assert.Equal("", viewer.SignatureToolTipText);
        });
    }

    /// <summary>
    /// A click on a signed field, as in Acrobat: the hand over it, its row selected in the Signatures
    /// tab and its validation status shown; off the field, nothing; a host that handles the click
    /// gets the signature and the viewer shows nothing.
    /// </summary>
    [Fact]
    public void ClickingASignedField_SelectsItsRow_AndShowsItsStatus_UnlessTheHostHandlesIt()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = WithSignatures, SignatureVerification = Trusting() };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(Signed());
            viewer.GoToPage(1, 350f);

            var shown = new List<(string Title, string Text, SignatureVerdict Verdict)>();
            viewer.ShowSignatureStatus = (title, text, verdict) => shown.Add((title, text, verdict));
            var clicks = new List<PdfViewerSignatureClickedEventArgs>();
            bool handle = false;
            viewer.SignatureClicked += (_, e) => { clicks.Add(e); e.Handled = handle; };

            Point off = SurfacePoint(viewer, 1, 350, 50);
            viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, off.X, off.Y, 0));
            Assert.NotEqual(Cursors.Hand, viewer.PageSurface.Cursor);
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, off.X, off.Y, 0));
            viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, off.X, off.Y, 0));
            Assert.Empty(clicks);
            Assert.Empty(shown);
            Assert.Null(viewer.SignatureTree.SelectedNode);

            Point onField = SurfacePoint(viewer, 1, 150, 325);
            viewer.Surface_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, onField.X, onField.Y, 0));
            Assert.Equal(Cursors.Hand, viewer.PageSurface.Cursor);
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, onField.X, onField.Y, 0));

            var click = Assert.Single(clicks);
            Assert.Same(viewer.SignatureResults[0], click.Result);
            Assert.Equal(1, click.PageIndex);
            var status = Assert.Single(shown);
            Assert.Equal("Signature Validation Status", status.Title);
            Assert.StartsWith("✓ Dana Signer — Valid", status.Text);
            Assert.Contains("Reason: Approved", status.Text);
            Assert.Equal(SignatureVerdict.Valid, status.Verdict);
            Assert.Same(viewer.SignatureTree.Nodes[0], viewer.SignatureTree.SelectedNode);

            handle = true;
            viewer.SignatureTree.SelectedNode = null;
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, onField.X, onField.Y, 0));
            Assert.Equal(2, clicks.Count);
            Assert.Single(shown);                                                 // handled: nothing shown,
            Assert.Null(viewer.SignatureTree.SelectedNode);                       // nothing selected
        });
    }

    [Fact]
    public void TheBanner_CanBeHidden_AndAnUnsignedDocumentHasNone()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = WithSignatures };
            viewer.LoadBytes(Signed());
            Assert.True(viewer.SignatureBanner.Visible);
            viewer.ShowSignatureBanner = false;
            Assert.False(viewer.SignatureBanner.Visible);
            viewer.ShowSignatureBanner = true;
            Assert.True(viewer.SignatureBanner.Visible);

            viewer.LoadBytes(TestPdf.SignableForm());                             // a signature field, unsigned
            Assert.Empty(viewer.SignatureResults);
            Assert.False(viewer.SignatureBanner.Visible);
        });
    }

    // ── Signing from the viewer ─────────────────────────────────────────

    private const PdfViewerFeatures Signing =
        PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling | PdfViewerFeatures.Signatures | PdfViewerFeatures.Signing;

    [Fact]
    public void SignField_IsOffByDefault_AndRefusesAFieldThatIsNotAnEmptySignatureField()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling };
            viewer.LoadBytes(TestPdf.SignableForm());
            Assert.False(viewer.SignField("Sig1", Parameters()));
            Assert.False(viewer.IsModified);

            viewer.Features = Signing;
            Assert.False(viewer.SignField("note", Parameters()));                // a text field
            Assert.False(viewer.SignField("nothing", Parameters()));
            Assert.False(viewer.IsModified);
            Assert.True(viewer.SignField("Sig1", Parameters()));                // the control: it signs

            Assert.False(viewer.SignField("Sig1", Parameters()));               // signed now: not again
        });
    }

    /// <summary>The value typed before signing is in the signed file, and the signature covers it.</summary>
    [Fact]
    public void SignField_SignsTheTypedValues_ShowsTheSignature_AndSaveWritesIt()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = Signing, SignatureVerification = Trusting() };
            viewer.LoadBytes(TestPdf.SignableForm());
            Assert.True(viewer.SetFieldValue("note", "typed before signing"));
            Assert.Empty(viewer.SignatureResults);

            var parameters = Parameters();
            parameters.SignatureFieldName = "Elsewhere";                         // SignField's field wins
            Assert.True(viewer.SignField("Sig1", parameters));
            Assert.True(viewer.IsModified);
            var shown = Assert.Single(viewer.SignatureResults);
            Assert.Equal("Sig1", shown.FieldName);
            Assert.Equal(SignatureVerdict.Valid, PdfViewerControl.VerdictOf(shown));
            Assert.True(shown.CoversEntireDocument);

            byte[] saved;
            using (var ms = new MemoryStream())
            {
                viewer.SaveAs(ms);
                saved = ms.ToArray();
            }
            Assert.False(viewer.IsModified);

            using (var input = new MemoryStream(saved))
            {
                var r = Assert.Single(PdfSigningOperations.VerifySignatures(input, Trusting()));
                Assert.True(r.IsIntegrityValid);
                Assert.True(r.CoversEntireDocument);
                Assert.Equal("Approved", r.Reason);
            }
            using (var input = new MemoryStream(saved))
                Assert.Equal("typed before signing", PdfFormReader.GetFields(input).Single(f => f.FullyQualifiedName == "note").Value);
        });
    }

    /// <summary>A signature over the original survives a second signature made in the viewer.</summary>
    [Fact]
    public void SigningASecondField_KeepsTheFirstSignatureValid()
    {
        RunSta(() =>
        {
            byte[] twoFields;
            using (var input = new MemoryStream(TestPdf.SignableForm()))
            using (var output = new MemoryStream())
            {
                var first = Parameters();
                first.SignatureFieldName = "First";                              // a new, invisible field
                new PdfSigner().Sign(input, output, first);
                twoFields = output.ToArray();
            }

            using var viewer = new PdfViewerControl { Features = Signing, SignatureVerification = Trusting() };
            viewer.LoadBytes(twoFields);
            Assert.True(viewer.SignField("Sig1", Parameters()));

            Assert.Equal(2, viewer.SignatureResults.Count);
            Assert.All(viewer.SignatureResults, r => Assert.True(r.IsIntegrityValid));
            Assert.Contains(viewer.SignatureResults, r => r.FieldName == "First" && !r.CoversEntireDocument);
            Assert.Contains(viewer.SignatureResults, r => r.FieldName == "Sig1" && r.CoversEntireDocument);
        });
    }

    [Fact]
    public void AnEncryptedFile_IsSignedUnderItsOwnPassword_AndStaysEncrypted()
    {
        RunSta(() =>
        {
            byte[] encrypted;
            using (var plain = new MemoryStream(TestPdf.SignableForm()))
            using (var output = new MemoryStream())
            {
                PdfEncryptor.Encrypt(plain, output, new EncryptionParameters
                {
                    UserPassword = "user", OwnerPassword = "owner", Algorithm = EncryptionAlgorithm.AES128,
                });
                encrypted = output.ToArray();
            }

            using var viewer = new PdfViewerControl { Features = Signing, SignatureVerification = Trusting(), Password = "user" };
            viewer.LoadBytes(encrypted);
            Assert.True(viewer.SetFieldValue("note", "secret"));
            Assert.True(viewer.SignField("Sig1", Parameters()));
            Assert.Equal(SignatureVerdict.Valid, PdfViewerControl.VerdictOf(Assert.Single(viewer.SignatureResults)));

            byte[] saved;
            using (var ms = new MemoryStream())
            {
                viewer.SaveAs(ms);
                saved = ms.ToArray();
            }
            using (var probe = Tetitulz.Pdf.Core.Document.PdfDocument.Open(new MemoryStream(saved)))
                Assert.True(probe.IsEncrypted);
            using (var input = new MemoryStream(saved))
                Assert.True(Assert.Single(PdfEncryptionOperations.VerifySignatures(input, "user", Trusting())).IsIntegrityValid);
            using (var input = new MemoryStream(saved))
            using (var plain = new MemoryStream())
            {
                PdfDecryptor.Decrypt(input, plain, "user");                      // as the viewer opens it, with no global registration
                plain.Position = 0;
                Assert.Equal("secret", PdfFormReader.GetFields(plain).Single(f => f.FullyQualifiedName == "note").Value);
            }
        });
    }

    [Fact]
    public void AWrongCertificatePassword_IsRefused_AndTheDocumentIsUnchanged()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = Signing };
            viewer.LoadBytes(TestPdf.SignableForm());
            var events = new List<PdfViewerFieldSignedEventArgs>();
            viewer.FieldSigned += (_, e) => events.Add(e);

            var wrong = Parameters();
            wrong.CertificatePassword = "not the password";
            var thrown = Assert.ThrowsAny<Tetitulz.Pdf.Core.PdfException>(() => viewer.SignField("Sig1", wrong));

            var refused = Assert.Single(events);
            Assert.Equal("Sig1", refused.FieldName);
            Assert.Equal(thrown.Message, refused.Error);
            Assert.False(viewer.IsModified);
            Assert.Empty(viewer.SignatureResults);

            Assert.True(viewer.SignField("Sig1", Parameters()));                // the control: the field is still empty
            Assert.Null(events[1].Error);
        });
    }

    /// <summary>A press on the empty field asks the host; no answer signs nothing, an answer signs.</summary>
    [Fact]
    public void ClickingAnEmptySignatureField_AsksTheHost_AndSignsWithWhatItSupplies()
    {
        RunSta(() =>
        {
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = Signing, SignatureVerification = Trusting() };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(TestPdf.SignableForm());
            viewer.GoToPage(1, 350f);

            var requests = new List<PdfViewerSignatureRequestEventArgs>();
            var signed = new List<PdfViewerFieldSignedEventArgs>();
            SignatureParameters? answer = null;
            viewer.SignatureRequested += (_, e) => { requests.Add(e); e.Parameters = answer; };
            viewer.FieldSigned += (_, e) => signed.Add(e);

            Point onField = SurfacePoint(viewer, 1, 150, 325);
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, onField.X, onField.Y, 0));
            var request = Assert.Single(requests);
            Assert.Equal("Sig1", request.FieldName);
            Assert.Equal(1, request.PageIndex);
            Assert.False(viewer.IsModified);                                    // no answer: nothing signed,
            Assert.Empty(signed);                                               // nor tried

            Point off = SurfacePoint(viewer, 1, 350, 50);
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, off.X, off.Y, 0));
            viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, off.X, off.Y, 0));
            Assert.Single(requests);                                            // off the field: not asked

            answer = Parameters();
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, onField.X, onField.Y, 0));
            Assert.Equal(2, requests.Count);
            Assert.True(viewer.IsModified);
            Assert.Equal("Sig1", Assert.Single(viewer.SignatureResults).FieldName);
            Assert.Null(Assert.Single(signed).Error);
        });
    }

    /// <summary>An empty signature field is marked as a place to sign while signing is on, and not once signed.</summary>
    [Fact]
    public void AnEmptySignatureField_IsMarked_OnlyWhileSigningIsOnAndUntilItIsSigned()
    {
        RunSta(() =>
        {
            using var viewer = new PdfViewerControl { Features = Signing };
            viewer.LoadBytes(TestPdf.SignableForm());

            Assert.True(AmberPixels(viewer, page: 1) > 50);
            Assert.Equal(0, AmberPixels(viewer, page: 0));                      // the field is on page 2

            viewer.Features &= ~PdfViewerFeatures.Signing;
            Assert.Equal(0, AmberPixels(viewer, page: 1));

            viewer.Features = Signing;
            Assert.True(viewer.SignField("Sig1", Parameters()));
            Assert.Equal(0, AmberPixels(viewer, page: 1));
        });

        static int AmberPixels(PdfViewerControl viewer, int page)
        {
            using var bmp = new Bitmap(1200, 1200);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                viewer.PaintUnsignedSignatureFields(g, page, new Rectangle(0, 0, 1200, 1200));
            }
            int n = 0;
            for (int x = 0; x < bmp.Width; x += 2)
                for (int y = 0; y < bmp.Height; y += 2)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.R > 200 && c.B < 230 && c.R - c.B > 20) n++;
                }
            return n;
        }
    }

    // ----------------------------------------------------------------

    private static SignatureVerificationOptions Trusting()
        => new SignatureVerificationOptions { TrustAnchors = new[] { Keys.Value.Root } };

    private static SignatureParameters Parameters() => new SignatureParameters
    {
        CertificateStream = new MemoryStream(Keys.Value.Pfx),
        CertificatePassword = "test123",
        SignatureFieldName = "Sig1",
        Reason = "Approved",
    };

    /// <summary>
    /// A certification at DocMDP level 1 forbids every change, so no field is fillable afterwards: a
    /// save would append the revision the certification forbids, and the signature over the original
    /// would no longer verify — against what SaveAs promises. The control: a certification at level 2
    /// (forms and signatures) leaves the field fillable.
    /// </summary>
    [Theory]
    [InlineData(DocMdpPermission.NoChanges, false)]
    [InlineData(DocMdpPermission.FormsAndSignatures, true)]
    public void ACertification_DecidesWhetherAFieldIsStillFillable(DocMdpPermission level, bool fillable)
    {
        RunSta(() =>
        {
            var parameters = Parameters();
            parameters.DocMdpPermission = level;
            byte[] certified = Signed(TestPdf.SignableForm(), parameters);

            using var viewer = new PdfViewerControl { Features = PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling };
            viewer.LoadBytes(certified);

            Assert.Equal(fillable, viewer.SetFieldValue("note", "x"));
            Assert.Null(viewer.DocumentSource!.LockAnalysisError);
        });
    }

    /// <summary>
    /// A signature field's <c>/Lock</c> (Table 236) becomes the signature's FieldMDP reference, and
    /// the fields it names are no longer fillable: "note" under <c>/Include [(note)]</c>; every field
    /// under <c>/All</c>. The control: the same form signed without a lock keeps "note" fillable.
    /// </summary>
    [Theory]
    [InlineData("/Lock << /Type /SigFieldLock /Action /Include /Fields [(note)] >>", false)]
    [InlineData("/Lock << /Type /SigFieldLock /Action /All >>", false)]
    [InlineData("/Lock << /Type /SigFieldLock /Action /Exclude /Fields [(note)] >>", true)]
    [InlineData("", true)]
    public void AFieldLockOfASignedField_DecidesWhetherAFieldIsStillFillable(string lockEntry, bool fillable)
    {
        RunSta(() =>
        {
            byte[] signed = Signed(TestPdf.SignableForm(lockEntry), Parameters());

            using var viewer = new PdfViewerControl { Features = PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling };
            viewer.LoadBytes(signed);

            Assert.Equal(fillable, viewer.SetFieldValue("note", "x"));
            Assert.Null(viewer.DocumentSource!.LockAnalysisError);
        });
    }

    private static byte[] Signed() => Signed(TestPdf.SignableForm(), Parameters());

    private static byte[] Signed(byte[] form, SignatureParameters parameters)
    {
        using var input = new MemoryStream(form);
        using var output = new MemoryStream();
        new PdfSigner().Sign(input, output, parameters);
        return output.ToArray();
    }

    private static Point SurfacePoint(PdfViewerControl viewer, int page, float x, float y)
    {
        Assert.True(viewer.TryPageToClient(page, new RectangleF(x, y, 1, 1), out RectangleF c));
        return viewer.PageSurface.PointToClient(viewer.PointToScreen(new Point((int)c.X, (int)c.Y)));
    }

    private static X509Certificate Certificate(string issuer, string subject, AsymmetricKeyParameter publicKey,
        AsymmetricKeyParameter signingKey, bool ca)
    {
        var gen = new X509V3CertificateGenerator();
        gen.SetSerialNumber(BigInteger.ValueOf(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
        gen.SetIssuerDN(new X509Name(issuer));
        gen.SetSubjectDN(new X509Name(subject));
        gen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        gen.SetNotAfter(DateTime.UtcNow.AddYears(5));
        gen.SetPublicKey(publicKey);
        if (ca)
        {
            gen.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));
            gen.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign | KeyUsage.DigitalSignature));
        }
        else
        {
            gen.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.DigitalSignature));
        }
        return gen.Generate(new Asn1SignatureFactory("SHA256WithRSA", signingKey, new SecureRandom()));
    }

    private static byte[] Pfx(X509Certificate leaf, AsymmetricKeyParameter key, X509Certificate root)
    {
        var store = new Org.BouncyCastle.Pkcs.Pkcs12StoreBuilder().Build();
        store.SetKeyEntry("signer", new Org.BouncyCastle.Pkcs.AsymmetricKeyEntry(key),
            new[] { new Org.BouncyCastle.Pkcs.X509CertificateEntry(leaf), new Org.BouncyCastle.Pkcs.X509CertificateEntry(root) });
        using var ms = new MemoryStream();
        store.Save(ms, "test123".ToCharArray(), new SecureRandom());
        return ms.ToArray();
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
