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
using Tetitulz.Pdf.Base.Encryption;
using Tetitulz.Pdf.Base.Operations.Forms;
using Tetitulz.Pdf.Core.Document;
using Tetitulz.Pdf.Encryption;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// <see cref="PdfViewerFeatures.FormFilling"/>: a press toggles a button or opens one editor, the page
/// is redrawn from the library's own fill, and a save appends one revision to the original — under
/// its own encryption when it has one.
/// </summary>
public sealed class FormFillingTests
{
    // ── Off by default ────────────────────────────────────────────────────────

    [Fact]
    public void FormFilling_IsOffByDefault_AndThenNoPressOrCallChangesAField()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm(), PdfViewerFeatures.Default);
            using (form)
            {
                Assert.False(viewer.Features.HasFlag(PdfViewerFeatures.FormFilling));
                Assert.False(viewer.SaveButton.Available);
                Press(viewer, "name");
                Assert.Null(viewer.FieldEditor);
                Assert.False(viewer.SetFieldValue("name", "x"));
                Assert.False(viewer.IsModified);

                viewer.Features |= PdfViewerFeatures.FormFilling;   // potency: on, the same call fills
                Assert.True(viewer.SaveButton.Available);
                Assert.True(viewer.SetFieldValue("name", "x"));
                Assert.True(viewer.IsModified);
            }
        });
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    [Fact]
    public void PressingACheckBox_TogglesIt()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Press(viewer, "agree");
                Assert.True(Saved(viewer).Single(f => f.FullyQualifiedName == "agree").IsChecked);
                Assert.Null(viewer.FieldEditor);                 // a button opens no editor

                Press(viewer, "agree");
                Assert.False(Saved(viewer).Single(f => f.FullyQualifiedName == "agree").IsChecked);
                Assert.Equal("Off", viewer.ChangedFieldValues["agree"]);
            }
        });
    }

    [Fact]
    public void PressingARadioButton_ChoosesIt_AndASecondPressKeepsIt()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                var changes = new List<string>();
                viewer.FieldValueChanged += (s, e) => changes.Add(e.FieldName + "=" + e.Value);

                PressAt(viewer, RadioCentre(viewer, "M"));
                PressAt(viewer, RadioCentre(viewer, "M"));

                Assert.Equal(new[] { "size=M" }, changes);       // the second press changed nothing
                Assert.Equal("M", Saved(viewer).Single(f => f.FullyQualifiedName == "size").Value);
            }
        });
    }

    // ── Editors ───────────────────────────────────────────────────────────────

    [Fact]
    public void PressingATextField_OpensAnEditor_ThatCommitsOrCancels()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Press(viewer, "name");
                var box = Assert.IsAssignableFrom<TextBox>(viewer.FieldEditor);
                Assert.Equal(16, box.MaxLength);
                box.Text = "Dana Levi";
                viewer.EndFieldEdit(commit: true);
                Assert.Null(viewer.FieldEditor);
                Assert.Equal("Dana Levi", viewer.ChangedFieldValues["name"]);

                Press(viewer, "name");
                Assert.Equal("Dana Levi", ((TextBox)viewer.FieldEditor!).Text);   // opens on the current value
                ((TextBox)viewer.FieldEditor!).Text = "discarded";
                viewer.EndFieldEdit(commit: false);
                Assert.Equal("Dana Levi", viewer.ChangedFieldValues["name"]);
                Assert.Equal("Dana Levi", Saved(viewer).Single(f => f.FullyQualifiedName == "name").Value);
            }
        });
    }

    /// <summary>
    /// A keystroke the editor inserts itself keeps the field's <c>MaxLen</c>, as typing would: text
    /// set from code is not limited by the box.
    /// </summary>
    [Fact]
    public void TypingIntoAField_KeepsItsMaxLength()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Press(viewer, "name");
                var box = (TextBox)viewer.FieldEditor!;
                Assert.True(viewer.TypeIntoEditor(new string('a', 15)));
                Assert.True(viewer.TypeIntoEditor("b"));                          // 16: full
                Assert.False(viewer.TypeIntoEditor("c"));                         // 17: refused
                Assert.Equal(new string('a', 15) + "b", box.Text);

                box.SelectionStart = 0;
                box.SelectionLength = 2;
                Assert.True(viewer.TypeIntoEditor("Z"));                          // replacing two: room for it
                Assert.Equal("Z" + new string('a', 13) + "b", box.Text);
            }
        });
    }

    /// <summary>Hebrew typed into a field is stored as typed — the fill embeds a system font for it.</summary>
    [Fact]
    public void AHebrewValue_IsStoredAsTyped()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Assert.True(viewer.SetFieldValue("name", "דנה לוי"));
                Assert.Equal("דנה לוי", Saved(viewer).Single(f => f.FullyQualifiedName == "name").Value);
            }
        });
    }

    [Fact]
    public void PressingAComboBox_OffersItsOptions_AndStoresTheExportValue()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Press(viewer, "color");
                var combo = Assert.IsType<ComboBox>(viewer.FieldEditor);
                Assert.Equal(new[] { "Red", "Green" }, combo.Items.Cast<object>().Select(o => o.ToString()));
                combo.SelectedIndex = 1;
                viewer.EndFieldEdit(commit: true);
                Assert.Equal("G", viewer.ChangedFieldValues["color"]);
                Assert.Equal("G", Saved(viewer).Single(f => f.FullyQualifiedName == "color").Value);
            }
        });
    }

    /// <summary>A read-only field takes no press, so the press falls through to the page; the text field beside it is the control.</summary>
    [Fact]
    public void AReadOnlyField_OpensNothing()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Press(viewer, "locked");
                Assert.Null(viewer.FieldEditor);
                Assert.False(viewer.SetFieldValue("locked", "x"));

                Press(viewer, "name");
                Assert.NotNull(viewer.FieldEditor);
            }
        });
    }

    // ── The host's say ────────────────────────────────────────────────────────

    [Fact]
    public void FieldValueChanging_CanCancel_OrReplaceTheValue()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                viewer.FieldValueChanging += (s, e) =>
                {
                    if (e.NewValue == "no") e.Cancel = true;
                    else e.NewValue = e.NewValue.ToUpperInvariant();
                };

                Assert.False(viewer.SetFieldValue("name", "no"));
                Assert.False(viewer.IsModified);

                Assert.True(viewer.SetFieldValue("name", "dana"));
                Assert.Equal("DANA", viewer.ChangedFieldValues["name"]);
            }
        });
    }

    // ── Saving ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The saved file is the original, byte for byte, followed by ONE revision however many edits
    /// were made — what keeps a signature over the original valid.
    /// </summary>
    [Fact]
    public void SaveAs_AppendsOneRevisionToTheOriginal()
    {
        RunSta(() =>
        {
            byte[] original = AForm();
            var (form, viewer) = Shown(original);
            using (form)
            {
                viewer.SetFieldValue("name", "a");
                viewer.SetFieldValue("name", "ab");
                viewer.SetFieldValue("agree", OnState(viewer, "agree"));

                byte[] saved = SaveBytes(viewer);

                Assert.True(saved.Length > original.Length);
                Assert.Equal(original, saved.Take(original.Length).ToArray());
                Assert.Equal(EofCount(original) + 1, EofCount(saved));
                Assert.False(viewer.IsModified);
                var fields = PdfFormReader.GetFields(new MemoryStream(saved));
                Assert.Equal("ab", fields.Single(f => f.FullyQualifiedName == "name").Value);
                Assert.True(fields.Single(f => f.FullyQualifiedName == "agree").IsChecked);
            }
        });
    }

    [Fact]
    public void SaveAs_WithNothingChanged_WritesTheOriginal()
    {
        RunSta(() =>
        {
            byte[] original = AForm();
            var (form, viewer) = Shown(original);
            using (form)
                Assert.Equal(original, SaveBytes(viewer));
        });
    }

    /// <summary>
    /// An encrypted form is saved encrypted, with its own key: the original bytes stay the prefix,
    /// and the value is readable only through the password.
    /// </summary>
    [Fact]
    public void SaveAs_OfAnEncryptedForm_StaysEncrypted_UnderTheSamePassword()
    {
        RunSta(() =>
        {
            byte[] original = Encrypt(AForm(), "user", "owner");
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Password = "user" };
            viewer.Features |= PdfViewerFeatures.FormFilling;
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(original);

            Assert.True(viewer.SetFieldValue("name", "secret value"));
            byte[] saved = SaveBytes(viewer);

            Assert.Equal(original, saved.Take(original.Length).ToArray());
            using (var doc = PdfDocument.Open(new MemoryStream(saved)))
                Assert.True(doc.IsEncrypted);
            Assert.DoesNotContain("secret value", Encoding.ASCII.GetString(saved), StringComparison.Ordinal);
            using var decrypted = new MemoryStream();
            PdfDecryptor.Decrypt(new MemoryStream(saved), decrypted, "user");
            decrypted.Position = 0;
            var fields = PdfFormReader.GetFields(decrypted);
            Assert.Equal("secret value", fields.Single(f => f.FullyQualifiedName == "name").Value);
        });
    }

    [Fact]
    public void CtrlS_IsASaveCommand_OnlyWithFormFilling()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm(), PdfViewerFeatures.Default);
            using (form)
            {
                var seen = new List<PdfViewerCommand>();
                viewer.CommandExecuting += (s, e) => { seen.Add(e.Command); e.Handled = true; };   // no dialog

                Assert.False(viewer.ExecuteCmdKey(PdfViewerControl.CmdKeyAction.Save));
                Assert.Empty(seen);

                viewer.Features |= PdfViewerFeatures.FormFilling;
                Assert.True(viewer.ExecuteCmdKey(PdfViewerControl.CmdKeyAction.Save));
                Assert.Equal(new[] { PdfViewerCommand.Save }, seen);
                Assert.Equal(PdfViewerControl.CmdKeyAction.Save,
                    PdfViewerControl.ClassifyCmdKey(Keys.Control | Keys.S, false, false, true));
            }
        });
    }

    [Fact]
    public void LoadingAnotherDocument_ForgetsTheValues()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                viewer.SetFieldValue("name", "x");
                Press(viewer, "name");
                Assert.NotNull(viewer.FieldEditor);

                viewer.LoadBytes(AForm());

                Assert.False(viewer.IsModified);
                Assert.Empty(viewer.ChangedFieldValues);
                Assert.Null(viewer.FieldEditor);
            }
        });
    }

    // ── Highlight ─────────────────────────────────────────────────────────────

    [DesktopFact]
    public void FillableFieldsAreTinted_UnlessHighlightingIsOff()
    {
        RunSta(() =>
        {
            using var form = new Form
            {
                ClientSize = new Size(800, 700), StartPosition = FormStartPosition.Manual,
                Location = new Point(-5000, -5000), ShowInTaskbar = false,
            };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill };
            viewer.Features |= PdfViewerFeatures.FormFilling;
            form.Controls.Add(viewer);
            form.Show();
            viewer.LoadBytes(AForm());
            Application.DoEvents();

            // A point inside the empty text field: its own drawing, bluer when highlighted.
            Assert.True(viewer.TryPageToClient(0, new RectangleF(30, 310, 1, 1), out RectangleF at));
            Color Pixel()
            {
                using var bmp = new Bitmap(viewer.Width, viewer.Height);
                viewer.DrawToBitmap(bmp, new Rectangle(Point.Empty, viewer.Size));
                return bmp.GetPixel((int)at.X, (int)at.Y);
            }

            Color tinted = Pixel();
            viewer.HighlightFormFields = false;
            Color plain = Pixel();

            Assert.True(Math.Abs(plain.B - plain.R) < 10, $"expected the field's neutral drawing, got {plain}");
            Assert.True(tinted.B - tinted.R > 20, $"expected a blue tint, got {tinted} over {plain}");
        });
    }

    // ----------------------------------------------------------------

    /// <summary>
    /// A 400 × 400 page with a text field "name" (max 16), a check box "agree", a radio group "size"
    /// (S, M), a combo box "color" (Red → R, Green → G), a read-only text field "locked" and a
    /// multi-select list box "langs" (English → en, Hebrew → he, French → fr).
    /// </summary>
    private static byte[] AForm()
    {
        using var input = new MemoryStream(TestPdf.OnePageWithBoxes("[0 0 400 400]", "[0 0 400 400]"));
        using var output = new MemoryStream();
        PdfFormFieldBuilder.AddFields(input, output, new FormFieldBase[]
        {
            new FormTextField { FieldName = "name", PageIndex = 0, X = 20, Y = 300, Width = 200, Height = 30, MaxLength = 16 },
            new FormCheckboxField { FieldName = "agree", PageIndex = 0, X = 20, Y = 250, Width = 20, Height = 20 },
            new FormRadioGroupField
            {
                FieldName = "size",
                Buttons = new List<RadioButtonOption>
                {
                    new RadioButtonOption("S", 0, 20, 200, 20, 20),
                    new RadioButtonOption("M", 0, 60, 200, 20, 20),
                },
            },
            new FormComboField
            {
                FieldName = "color", PageIndex = 0, X = 20, Y = 150, Width = 150, Height = 24,
                Options = new List<ChoiceOption> { new ChoiceOption("Red", "R"), new ChoiceOption("Green", "G") },
            },
            new FormTextField { FieldName = "locked", PageIndex = 0, X = 20, Y = 100, Width = 200, Height = 30, ReadOnly = true },
            new FormListField
            {
                FieldName = "langs", PageIndex = 0, X = 220, Y = 200, Width = 150, Height = 60, MultiSelect = true,
                Options = new List<ChoiceOption> { new ChoiceOption("English", "en"), new ChoiceOption("Hebrew", "he"), new ChoiceOption("French", "fr") },
            },
        });
        return output.ToArray();
    }

    /// <summary>
    /// A multi-select list box opens a list, several of whose items may be chosen; the choice is
    /// saved as every selection, and read back the same way.
    /// </summary>
    [Fact]
    public void PressingAMultiSelectListBox_OpensAList_AndSavesEverySelection()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Press(viewer, "langs");
                var list = Assert.IsAssignableFrom<ListBox>(viewer.FieldEditor);
                Assert.Equal(SelectionMode.MultiExtended, list.SelectionMode);
                Assert.Equal(3, list.Items.Count);
                list.SetSelected(0, true);
                list.SetSelected(2, true);
                viewer.EndFieldEdit(commit: true);

                Assert.Equal("en\nfr", viewer.ChangedFieldValues["langs"]);
                Assert.Equal(new[] { "en", "fr" }, Saved(viewer).Single(f => f.FullyQualifiedName == "langs").SelectedValues);

                // Reopened, the list shows the choice; one item left is one value.
                Press(viewer, "langs");
                list = Assert.IsAssignableFrom<ListBox>(viewer.FieldEditor);
                Assert.Equal(new[] { 0, 2 }, list.SelectedIndices.Cast<int>().ToArray());
                list.SetSelected(0, false);
                viewer.EndFieldEdit(commit: true);
                Assert.Equal(new[] { "fr" }, Saved(viewer).Single(f => f.FullyQualifiedName == "langs").SelectedValues);
            }
        });
    }

    // ── What the document and its signatures withhold ─────────────────────────

    /// <summary>
    /// An open field editor is the text being typed into: the document shortcuts stay out of its
    /// way, as they do for the find box — Ctrl+C copied the page's selection over the field's text,
    /// Ctrl+A selected the page, PageUp turned it. Closed, the shortcuts return.
    /// </summary>
    [Fact]
    public void AnOpenFieldEditor_CountsAsTextEntry_SoTheDocumentShortcutsStayOut()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Assert.False(viewer.IsTextEntryFocused());
                Press(viewer, "name");
                Assert.NotNull(viewer.FieldEditor);

                Assert.True(viewer.IsTextEntryFocused());
                Assert.Equal(PdfViewerControl.CmdKeyAction.None,
                    PdfViewerControl.ClassifyCmdKey(Keys.Control | Keys.C, false, viewer.IsTextEntryFocused(), true));
                Assert.Equal(PdfViewerControl.CmdKeyAction.None,
                    PdfViewerControl.ClassifyCmdKey(Keys.PageDown, false, viewer.IsTextEntryFocused(), true));

                viewer.EndFieldEdit(commit: false);
                Assert.False(viewer.IsTextEntryFocused());
                Assert.Equal(PdfViewerControl.CmdKeyAction.Copy,
                    PdfViewerControl.ClassifyCmdKey(Keys.Control | Keys.C, false, viewer.IsTextEntryFocused(), true));
            }
        });
    }

    /// <summary>
    /// A load the viewer refuses — a document with no page — leaves the shown document as it was:
    /// its typed values stand, it is still modified, and a save writes them. Resetting the form state
    /// before the refusal kept the page showing the values while the save wrote the original without them.
    /// </summary>
    [Fact]
    public void ARefusedLoad_KeepsTheShownDocumentsValues_SoASaveStillWritesThem()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(AForm());
            using (form)
            {
                Assert.True(viewer.SetFieldValue("name", "Dana"));
                Assert.True(viewer.IsModified);

                Assert.ThrowsAny<Tetitulz.Pdf.Core.PdfException>(() => viewer.LoadBytes(TestPdf.ManyPages(0)));

                Assert.True(viewer.IsModified);
                Assert.Equal("Dana", viewer.ChangedFieldValues["name"]);
                Assert.Equal("Dana", Saved(viewer).Single(f => f.FullyQualifiedName == "name").Value);
            }
        });
    }

    /// <summary>
    /// A widget the file hides — <c>/F</c> Hidden (2) or NoView (32), Table 167: not displayed, no
    /// interaction — opens no editor and is not tinted, as the page shows no such widget; one that
    /// prints and shows (4) opens one. The flags are the form reader's, read as a flag word.
    /// </summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(32, false)]
    [InlineData(4, true)]
    public void AWidgetTheFileHides_IsNeitherEditedNorTinted(int flags, bool shown)
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.FormWithWidgetFlags(flags));
            using (form)
            {
                var widget = viewer.DocumentSource!.FormFields.Single().Widgets.Single();
                Assert.Equal(flags, widget.Flags);
                Assert.Equal(!shown, widget.IsHiddenOnScreen);

                Press(viewer, "h");
                Assert.Equal(shown, viewer.FieldEditor != null);
            }
        });
    }

    /// <summary>
    /// The document's permissions (<c>/P</c>) bind a user password, the empty one included: a document
    /// its owner protected without printing, copying or filling offers none of the three until the host
    /// turns <see cref="PdfViewerControl.HonourPermissions"/> off or opens it with the owner's password.
    /// A user-password document is bound the same way.
    /// </summary>
    [Theory]
    [InlineData("", null)]          // owner-only: the empty user password opens it, restricted
    [InlineData("user", "user")]    // the user password, restricted
    public void PermissionsTheUserPasswordLeaves_WithholdPrintCopyAndFilling(string user, string? supplied)
    {
        RunSta(() =>
        {
            byte[] restricted = Encrypt(AForm(), user, "owner",
                PdfPermissions.All & ~(PdfPermissions.Print | PdfPermissions.Copy | PdfPermissions.FillForms | PdfPermissions.AnnotateAndForms));
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Password = supplied, Features = PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(restricted);

            Assert.False(viewer.Permits(PdfPermissions.Print));
            Assert.False(viewer.PrintButton.Enabled);
            Assert.False(viewer.Permits(PdfPermissions.Copy));
            Assert.False(viewer.SetFieldValue("name", "x"));
            Assert.True(viewer.Permits(PdfPermissions.Assemble));   // a bit the document grants

            viewer.HonourPermissions = false;
            Assert.True(viewer.Permits(PdfPermissions.Print));
            Assert.True(viewer.PrintButton.Enabled);
            Assert.True(viewer.SetFieldValue("name", "x"));
        });
    }

    /// <summary>The owner's password opens the same document unrestricted (§7.6.4.2), as does no encryption at all.</summary>
    [Fact]
    public void TheOwnerPassword_LeavesEveryPermission()
    {
        RunSta(() =>
        {
            byte[] restricted = Encrypt(AForm(), "user", "owner", PdfPermissions.None);
            using var form = new Form { ClientSize = new Size(800, 700) };
            var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Password = "owner", Features = PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling };
            form.Controls.Add(viewer);
            form.CreateControl();
            viewer.LoadBytes(restricted);

            Assert.Null(viewer.DocumentSource!.Permissions);
            Assert.True(viewer.PrintButton.Enabled);
            Assert.True(viewer.SetFieldValue("name", "x"));
        });
    }

    private static (Form, PdfViewerControl) Shown(byte[] pdf,
        PdfViewerFeatures features = PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling)
    {
        var form = new Form { ClientSize = new Size(800, 700) };
        var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = features };
        form.Controls.Add(viewer);
        form.CreateControl();
        viewer.LoadBytes(pdf);
        return (form, viewer);
    }

    private static void Press(PdfViewerControl viewer, string field) => PressAt(viewer, viewer.FieldCentreOnSurface(field));

    private static void PressAt(PdfViewerControl viewer, Point p)
    {
        viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
        viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
    }

    /// <summary>The surface point at the centre of the radio button exporting <paramref name="state"/>.</summary>
    private static Point RadioCentre(PdfViewerControl viewer, string state)
    {
        double x = state == "S" ? 30 : 70;
        Assert.True(viewer.TryPageToClient(0, new RectangleF((float)x, 210, 1, 1), out RectangleF c));
        return viewer.PageSurface.PointToClient(viewer.PointToScreen(new Point((int)c.X, (int)c.Y)));
    }

    private static string OnState(PdfViewerControl viewer, string field)
        => Saved(viewer).Single(f => f.FullyQualifiedName == field).OnStates.First();

    /// <summary>The fields of the document the viewer would save now.</summary>
    private static IReadOnlyList<PdfFormFieldInfo> Saved(PdfViewerControl viewer)
    {
        return PdfFormReader.GetFields(new MemoryStream(SaveBytes(viewer)));
    }

    private static byte[] SaveBytes(PdfViewerControl viewer)
    {
        using var ms = new MemoryStream();
        viewer.SaveAs(ms);
        return ms.ToArray();
    }

    private static int EofCount(byte[] pdf)
    {
        string s = Encoding.ASCII.GetString(pdf);
        int n = 0, at = 0;
        while ((at = s.IndexOf("%%EOF", at, StringComparison.Ordinal)) >= 0) { n++; at += 5; }
        return n;
    }

    private static byte[] Encrypt(byte[] plain, string user, string owner, PdfPermissions permissions = PdfPermissions.All)
    {
        using var input = new MemoryStream(plain);
        using var output = new MemoryStream();
        PdfEncryptor.Encrypt(input, output, new EncryptionParameters
        {
            UserPassword = user,
            OwnerPassword = owner,
            Algorithm = EncryptionAlgorithm.AES128,
            Permissions = permissions,
        });
        return output.ToArray();
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
