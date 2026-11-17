// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations.Forms;
using Tetitulz.Pdf.Base.Scripting;
using Tetitulz.Pdf.Core.Plugins;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// <see cref="PdfViewerFeatures.FormScripts"/>: a committed value goes through the field's scripts
/// and the document's calculations (<see cref="PdfFieldScripts"/>), only when the host turned the
/// feature on.
/// </summary>
/// <remarks>
/// The engine is a stub reading a few instructions from the <c>/JS</c> text — this project
/// references no plugin, as the viewer does not. One class registers it, so the tests here run in
/// order (the host is process-wide), and none of the viewer's other tests turns the feature on.
/// </remarks>
[Collection(nameof(FormScriptTests))]
[CollectionDefinition(nameof(FormScriptTests), DisableParallelization = true)]
public sealed class FormScriptTests
{
    /// <summary>
    /// <c>upper</c>; <c>rc=false</c>; <c>msg=X</c>; <c>alert=X</c>; <c>set=Name:X</c>
    /// (getField(Name).value = X); <c>sum=A,B</c>, separated by <c>;</c>.
    /// </summary>
    private sealed class InstructionJsPlugin : IJavaScriptPlugin
    {
        internal readonly List<string> Ran = new();

        public string Name => "viewer-instruction-js-stub";
        public string Description => "runs the fixture's instructions";
        public Version Version => new(1, 0, 0);
        public void Initialize() { }
        public void Shutdown() { }

        public bool TryExecute(string script, PdfJsExecutionContext ctx)
        {
            Ran.Add(script);
            foreach (string part in script.Split(';'))
            {
                string[] kv = part.Split(new[] { '=' }, 2);
                string arg = kv.Length > 1 ? kv[1] : "";
                switch (kv[0])
                {
                    case "upper": ctx.EventValue = ctx.EventValue.ToUpperInvariant(); break;
                    case "value": ctx.EventValue = arg; break;
                    case "change": ctx.EventChange = arg; break;
                    case "rc": ctx.ValidationPassed = arg != "false"; break;
                    case "msg": ctx.ValidationMessage = arg; break;
                    case "alert": ctx.AlertMessages.Add(arg); break;
                    case "set":
                        string[] nv = arg.Split(':');
                        ctx.ModifiedFieldValues[nv[0]] = nv[1];
                        break;
                    case "state":                                   // state=Name:readonly|hidden:true|false
                        string[] ns = arg.Split(':');
                        if (!ctx.FieldStateWrites.TryGetValue(ns[0], out PdfJsFieldState? st))
                            ctx.FieldStateWrites[ns[0]] = st = new PdfJsFieldState();
                        if (ns[1] == "readonly") st.ReadOnly = ns[2] == "true";
                        else st.Hidden = ns[2] == "true";
                        break;
                    case "sum":
                        double total = 0;
                        foreach (string f in arg.Split(','))
                            if (ctx.FieldValues.TryGetValue(f, out string? v)
                                && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                                total += d;
                        ctx.EventValue = total.ToString(CultureInfo.InvariantCulture);
                        break;
                }
            }
            return true;
        }
    }

    private sealed class RegisteredStub : IDisposable
    {
        internal readonly InstructionJsPlugin Plugin = new();
        internal RegisteredStub() => PdfPluginHost.Instance.Register(Plugin);
        public void Dispose() => PdfPluginHost.Instance.Unregister(Plugin.Name);
    }

    private const PdfViewerFeatures Filling = PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling;
    private const PdfViewerFeatures Scripted = Filling | PdfViewerFeatures.FormScripts;

    [Fact]
    public void FormScripts_AreOffByDefault_AndThenNoScriptRuns()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();
            byte[] pdf = TestPdf.ScriptedForm(null, "rc=false", null);

            var (form, viewer) = Shown(pdf, Filling);
            using (form)
            {
                Assert.False(viewer.Features.HasFlag(PdfViewerFeatures.FormScripts));
                Assert.True(viewer.SetFieldValue("a", "50"));
                Assert.Empty(stub.Plugin.Ran);
            }

            var (form2, scripted) = Shown(pdf, Scripted);                // the control
            using (form2)
            {
                Assert.False(scripted.SetFieldValue("a", "50"));
                Assert.Single(stub.Plugin.Ran);
            }
        });
    }

    /// <summary>
    /// A field state the scripts write is applied to the viewer's own handling: a field made
    /// read-only or hidden takes no value and no press afterwards, while before the write it did
    /// (the control), and a refused value's writes are dropped; opening another document clears them.
    /// </summary>
    [Fact]
    public void AScriptsReadOnlyOrHiddenWrite_StopsTheFieldTakingAValue()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();
            byte[] pdf = TestPdf.ScriptedForm(null, "rc=true;state=b:readonly:true;state=total:hidden:true", null);
            var (form, viewer) = Shown(pdf, Scripted);
            using (form)
            {
                Assert.True(viewer.SetFieldValue("b", "1"));            // the control: nothing written yet
                Assert.Empty(viewer.ScriptFieldStates);

                Assert.True(viewer.SetFieldValue("a", "5"));
                Assert.True(viewer.ScriptFieldStates["b"].ReadOnly);
                Assert.True(viewer.ScriptFieldStates["total"].Hidden);
                Assert.False(viewer.SetFieldValue("b", "2"));
                Assert.Equal("1", viewer.ChangedFieldValues["b"]);
                Assert.False(viewer.SetFieldValue("total", "9"));
                Assert.True(viewer.SetFieldValue("a", "6"));             // a itself was not written

                viewer.LoadBytes(pdf);
                Assert.Empty(viewer.ScriptFieldStates);
                Assert.True(viewer.SetFieldValue("b", "3"));
            }

            // A refused value carries no state write.
            var (form2, refused) = Shown(TestPdf.ScriptedForm(null, "rc=false;state=b:readonly:true", null), Scripted);
            using (form2)
            {
                Assert.False(refused.SetFieldValue("a", "5"));
                Assert.Empty(refused.ScriptFieldStates);
                Assert.True(refused.SetFieldValue("b", "1"));
            }
        });
    }

    [Fact]
    public void ARefusedValue_IsNotStored_AndTheScriptsReasonIsReported()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();
            var (form, viewer) = Shown(TestPdf.ScriptedForm(null, "rc=false;msg=Too big", "sum=a,b"), Scripted);
            using (form)
            {
                var messages = new List<PdfViewerFormScriptEventArgs>();
                var changed = new List<string>();
                viewer.FormScriptMessage += (_, e) => messages.Add(e);
                viewer.FieldValueChanged += (_, e) => changed.Add(e.FieldName);

                Assert.False(viewer.SetFieldValue("a", "50"));

                Assert.Empty(viewer.ChangedFieldValues);
                Assert.False(viewer.IsModified);
                Assert.Empty(changed);
                var m = Assert.Single(messages);
                Assert.True(m.IsRejection);
                Assert.Equal("a", m.FieldName);
                Assert.Contains("Too big", m.Message);
                Assert.Contains("Too big", viewer.StatusLabel.Text);
            }
        });
    }

    [Fact]
    public void AnAcceptedValue_RunsTheCalculations_AndSavesEveryValue()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();
            var (form, viewer) = Shown(TestPdf.ScriptedForm("upper", "alert=Thanks", "sum=a,b"), Scripted);
            using (form)
            {
                var messages = new List<PdfViewerFormScriptEventArgs>();
                var changed = new List<(string, string)>();
                viewer.FormScriptMessage += (_, e) => messages.Add(e);
                viewer.FieldValueChanged += (_, e) => changed.Add((e.FieldName, e.Value));

                Assert.True(viewer.SetFieldValue("b", "4"));
                Assert.True(viewer.SetFieldValue("a", "3"));

                Assert.Equal("7", viewer.ChangedFieldValues["total"]);
                Assert.Equal(("a", "3"), changed[changed.Count - 2]);      // the field, then what it changed
                Assert.Equal(("total", "7"), changed[changed.Count - 1]);
                var alert = Assert.Single(messages);
                Assert.False(alert.IsRejection);
                Assert.Equal("Thanks", alert.Message);

                Dictionary<string, string> saved = SavedValues(viewer);
                Assert.Equal("3", saved["a"]);
                Assert.Equal("4", saved["b"]);
                Assert.Equal("7", saved["total"]);
            }
        });
    }

    /// <summary>A script's write to a field the form has is stored; one to a name it does not have is not.</summary>
    [Fact]
    public void AScriptsWriteToNoField_StoresNothing()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();
            var (form, viewer) = Shown(TestPdf.ScriptedForm("set=ghost:1;set=b:2", null, null), Scripted);
            using (form)
            {
                var changed = new List<string>();
                viewer.FieldValueChanged += (_, e) => changed.Add(e.FieldName);

                Assert.True(viewer.SetFieldValue("a", "x"));

                Assert.Equal("2", viewer.ChangedFieldValues["b"]);         // potency: a real field's write lands
                Assert.False(viewer.ChangedFieldValues.ContainsKey("ghost"));
                Assert.DoesNotContain("ghost", changed);
            }
        });
    }

    /// <summary>
    /// A Format script's text is what the field shows — on the page and in the saved file — while
    /// its value is what it holds; a later value without a format drops the old text.
    /// </summary>
    [Fact]
    public void AFormatScriptsText_IsShownAndSaved_AndTheValueIsKept()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();
            var (form, viewer) = Shown(TestPdf.ScriptedForm(null, null, "sum=a,b", formatA: null, formatTotal: "value=$7.00"), Scripted);
            using (form)
            {
                Assert.True(viewer.SetFieldValue("b", "4"));
                Assert.True(viewer.SetFieldValue("a", "3"));

                Assert.Equal("7", viewer.ChangedFieldValues["total"]);
                Assert.Equal("$7.00", viewer.DisplayedFieldValues["total"]);
                Assert.False(viewer.DisplayedFieldValues.ContainsKey("a"));     // no format: drawn as it is

                using (var shown = viewer.DocumentSource!.OpenDocument())
                    Assert.Contains("($7.00)", AppearanceOf(shown, "total"));          // on the page as well

                byte[] saved = SaveBytes(viewer);
                Assert.Equal("7", PdfFormReader.GetFields(new MemoryStream(saved)).Single(f => f.FullyQualifiedName == "total").Value);
                Assert.Contains("($7.00)", AppearanceOf(Open(saved), "total"));
                Assert.Contains("(3)", AppearanceOf(Open(saved), "a"));

                // Scripts off: a new value is drawn as it is, the old formatted text dropped.
                viewer.Features &= ~PdfViewerFeatures.FormScripts;
                Assert.True(viewer.SetFieldValue("total", "9"));
                Assert.False(viewer.DisplayedFieldValues.ContainsKey("total"));
                Assert.Contains("(9)", AppearanceOf(Open(SaveBytes(viewer)), "total"));

                // Another document forgets them.
                viewer.Features |= PdfViewerFeatures.FormScripts;
                Assert.True(viewer.SetFieldValue("a", "6"));                    // total 10: changed, so formatted
                Assert.NotEmpty(viewer.DisplayedFieldValues);
                viewer.LoadBytes(TestPdf.ScriptedForm(null, null, null));
                Assert.Empty(viewer.DisplayedFieldValues);
            }

            // An encrypted form keeps the formatted text under its own key.
            var (form2, encrypted) = Shown(Encrypt(TestPdf.ScriptedForm(null, null, "sum=a,b", formatTotal: "value=$7.00"), "user"), Scripted, "user");
            using (form2)
            {
                Assert.True(encrypted.SetFieldValue("b", "4"));
                Assert.True(encrypted.SetFieldValue("a", "3"));
                byte[] saved = SaveBytes(encrypted);
                using var src = new MemoryStream(saved);
                using var plain = new MemoryStream();
                Tetitulz.Pdf.Encryption.PdfDecryptor.Decrypt(src, plain, "user");
                Assert.Contains("($7.00)", AppearanceOf(Open(plain.ToArray()), "total"));
            }
        });

        static Tetitulz.Pdf.Core.Document.PdfDocument Open(byte[] pdf)
            => Tetitulz.Pdf.Core.Document.PdfDocument.Open(new MemoryStream(pdf));

        static byte[] Encrypt(byte[] pdf, string user)
        {
            using var input = new MemoryStream(pdf);
            using var output = new MemoryStream();
            Tetitulz.Pdf.Encryption.PdfEncryptor.Encrypt(input, output, new Tetitulz.Pdf.Base.Encryption.EncryptionParameters
            {
                UserPassword = user,
                OwnerPassword = "owner",
                Algorithm = Tetitulz.Pdf.Base.Encryption.EncryptionAlgorithm.AES128,
            });
            return output.ToArray();
        }

        static string AppearanceOf(Tetitulz.Pdf.Core.Document.PdfDocument doc, string field)
        {
            // The field's widget (merged with it in this fixture) found by its /T; its /N appearance decoded.
            for (int i = 1; i <= doc.CrossReferenceTable.GetMaxObjectNumber(); i++)
            {
                if (!(doc.GetObject(i) is Tetitulz.Pdf.Core.Objects.PdfDictionary d)
                    || (doc.ResolveObject(d[Tetitulz.Pdf.Core.Objects.PdfName.T]) as Tetitulz.Pdf.Core.Objects.PdfString)?.GetText() != field)
                    continue;
                var ap = (Tetitulz.Pdf.Core.Objects.PdfDictionary)doc.ResolveObject(d[Tetitulz.Pdf.Core.Objects.PdfName.AP])!;
                var n = (Tetitulz.Pdf.Core.Objects.PdfStream)doc.ResolveObject(ap[Tetitulz.Pdf.Core.Objects.PdfName.N])!;
                return System.Text.Encoding.ASCII.GetString(n.GetDecodedData());
            }
            throw new InvalidOperationException("no field " + field);
        }

        static byte[] SaveBytes(PdfViewerControl viewer)
        {
            using var ms = new MemoryStream();
            viewer.SaveAs(ms);
            return ms.ToArray();
        }
    }

    /// <summary>
    /// A paste is one change offered whole to the field's Keystroke script: refused, nothing goes
    /// in; rewritten, the script's text goes in — and with form scripts off, what was pasted, a
    /// single-line field taking the first line.
    /// </summary>
    [Fact]
    public void PastingIntoAField_GoesThroughItsKeystrokeScript_AsOneChange()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();

            var (form, rewriting) = Shown(TestPdf.ScriptedForm("change=Z", null, null), Scripted);
            using (form)
            {
                OpenEditor(rewriting, "a");
                Assert.True(rewriting.PasteIntoEditor("pasted text"));
                Assert.Equal("Z", rewriting.FieldEditor!.Text);
                Assert.Single(stub.Plugin.Ran);                                  // one change, one script run
            }

            var (form2, refusing) = Shown(TestPdf.ScriptedForm("rc=false", null, null), Scripted);
            using (form2)
            {
                OpenEditor(refusing, "a");
                Assert.False(refusing.PasteIntoEditor("pasted text"));
                Assert.Equal("", refusing.FieldEditor!.Text);
            }

            stub.Plugin.Ran.Clear();
            var (form3, plain) = Shown(TestPdf.ScriptedForm("rc=false", null, null), Filling);
            using (form3)
            {
                OpenEditor(plain, "a");
                Assert.True(plain.PasteIntoEditor("pasted"));
                Assert.True(plain.PasteIntoEditor(" text\r\nsecond line"));
                Assert.Equal("pasted text", plain.FieldEditor!.Text);
                Assert.False(plain.PasteIntoEditor(""));
                Assert.Empty(stub.Plugin.Ran);
            }
        });

        static void OpenEditor(PdfViewerControl viewer, string field)
        {
            viewer.Features |= PdfViewerFeatures.FormFilling;
            Point p = viewer.FieldCentreOnSurface(field);
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            Assert.IsAssignableFrom<TextBox>(viewer.FieldEditor);
        }
    }

    /// <summary>
    /// Typing into a field offers each keystroke to its Keystroke script: refused, nothing goes in;
    /// rewritten, the script's text goes in — and with form scripts off, what was typed.
    /// </summary>
    [Fact]
    public void TypingIntoAField_GoesThroughItsKeystrokeScript()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();

            var (form, rewriting) = Shown(TestPdf.ScriptedForm("change=Z", null, null), Scripted);
            using (form)
            {
                OpenEditor(rewriting, "a");
                Assert.True(rewriting.TypeIntoEditor("q"));
                Assert.Equal("Z", rewriting.FieldEditor!.Text);
                Assert.Contains("change=Z", stub.Plugin.Ran);
            }

            var (form2, refusing) = Shown(TestPdf.ScriptedForm("rc=false", null, null), Scripted);
            using (form2)
            {
                OpenEditor(refusing, "a");
                Assert.False(refusing.TypeIntoEditor("q"));
                Assert.Equal("", refusing.FieldEditor!.Text);
            }

            // The control: scripts off, the keystroke goes in as typed and no script runs.
            stub.Plugin.Ran.Clear();
            var (form3, plain) = Shown(TestPdf.ScriptedForm("rc=false", null, null), Filling);
            using (form3)
            {
                OpenEditor(plain, "a");
                Assert.True(plain.TypeIntoEditor("q"));
                Assert.Equal("q", plain.FieldEditor!.Text);
                Assert.Empty(stub.Plugin.Ran);
            }
        });

        static void OpenEditor(PdfViewerControl viewer, string field)
        {
            Point p = viewer.FieldCentreOnSurface(field);
            viewer.Surface_MouseDown(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            viewer.Surface_MouseUp(null, new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0));
            Assert.IsAssignableFrom<TextBox>(viewer.FieldEditor);
        }
    }

    [Fact]
    public void AKeystrokeScriptsValue_IsTheOneStored()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();
            var (form, viewer) = Shown(TestPdf.ScriptedForm("upper", null, null), Scripted);
            using (form)
            {
                Assert.True(viewer.SetFieldValue("a", "abc"));
                Assert.Equal("ABC", viewer.ChangedFieldValues["a"]);
                Assert.Equal("ABC", SavedValues(viewer)["a"]);
            }
        });
    }

    [Fact]
    public void WithNoEngineRegistered_TheValueIsTakenAsTyped_AndTheStatusSaysScriptsDidNotRun()
    {
        RunSta(() =>
        {
            var (form, viewer) = Shown(TestPdf.ScriptedForm("upper", "rc=false", "sum=a,b"), Scripted);
            using (form)
            {
                Assert.True(viewer.SetFieldValue("a", "abc"));
                Assert.Equal("abc", viewer.ChangedFieldValues["a"]);
                Assert.False(viewer.ChangedFieldValues.ContainsKey("total"));
                Assert.Contains("IJavaScriptPlugin", viewer.StatusLabel.Text);
            }
        });
    }

    [Fact]
    public void TheBudget_RefusesANegativeValue_AndAZeroBudgetRefusesAGatedField()
    {
        RunSta(() =>
        {
            using var stub = new RegisteredStub();
            var (form, viewer) = Shown(TestPdf.ScriptedForm(null, "alert=ok", null), Scripted);
            using (form)
            {
                Assert.Equal(TimeSpan.FromSeconds(5), viewer.FormScriptBudget);
                Assert.Throws<ArgumentOutOfRangeException>(() => viewer.FormScriptBudget = TimeSpan.FromSeconds(-1));

                viewer.FormScriptBudget = TimeSpan.Zero;
                Assert.False(viewer.SetFieldValue("a", "1"));
                Assert.True(viewer.SetFieldValue("b", "1"));               // no gate on b

                viewer.FormScriptBudget = TimeSpan.FromSeconds(5);
                Assert.True(viewer.SetFieldValue("a", "1"));               // the control
            }
        });
    }

    // ----------------------------------------------------------------

    private static (Form, PdfViewerControl) Shown(byte[] pdf, PdfViewerFeatures features, string? password = null)
    {
        var form = new Form { ClientSize = new Size(800, 700) };
        var viewer = new PdfViewerControl { Dock = DockStyle.Fill, Features = features };
        if (password != null) viewer.Password = password;
        form.Controls.Add(viewer);
        form.CreateControl();
        viewer.LoadBytes(pdf);
        return (form, viewer);
    }

    private static Dictionary<string, string> SavedValues(PdfViewerControl viewer)
    {
        using var ms = new MemoryStream();
        viewer.SaveAs(ms);
        return PdfFormReader.GetFields(new MemoryStream(ms.ToArray()))
            .ToDictionary(f => f.FullyQualifiedName, f => f.Value);
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
