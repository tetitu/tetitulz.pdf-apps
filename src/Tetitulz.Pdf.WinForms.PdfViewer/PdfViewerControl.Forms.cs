// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Tetitulz.Pdf.Base.Operations.Forms;
using Tetitulz.Pdf.Base.Scripting;
using Tetitulz.Pdf.Core.Forms;
using Tetitulz.Pdf.Core.Util;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Filling the document's AcroForm in place (<see cref="PdfViewerFeatures.FormFilling"/>, off by
    /// default): a click toggles a check box or picks a radio button, and opens one live editor —
    /// a text box or a list — over a text or choice field; every other field stays the page's own
    /// drawing. A value is filled into the decrypted document as opened, so the page shows what the
    /// library writes; <see cref="SaveAs(Stream)"/> appends the values to the original file as one
    /// revision, under its own encryption when it has one.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        /// <summary>Every value set since the document was opened, by fully qualified field name.</summary>
        private readonly Dictionary<string, string> _fieldValues = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The text a field is drawn with in place of its value — its Format script's — by fully qualified name.</summary>
        private readonly Dictionary<string, string> _displayValues = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>What each formatted field shows — what a test reads.</summary>
        internal IReadOnlyDictionary<string, string> DisplayedFieldValues => _displayValues;

        /// <summary>
        /// The field states the form scripts wrote (<c>readonly</c>, <c>hidden</c>, <c>display</c>,
        /// <c>required</c>) since the document was opened, by fully qualified name, the last write
        /// of each property kept: a field made read-only or hidden takes no press and no value;
        /// its appearance stays as the document draws it.
        /// </summary>
        private readonly Dictionary<string, PdfJsFieldState> _fieldStates = new Dictionary<string, PdfJsFieldState>(StringComparer.Ordinal);

        /// <summary>The states the scripts wrote — what a test reads.</summary>
        internal IReadOnlyDictionary<string, PdfJsFieldState> ScriptFieldStates => _fieldStates;

        /// <summary>Whether a value changed since the last save.</summary>
        private bool _unsavedFieldValues;

        /// <summary>The one live editor, a child of the page surface, and the field it edits.</summary>
        private Control? _fieldEditor;
        private string? _editingField;

        private bool _highlightFormFields = true;

        /// <summary>Raised before a field takes a new value; the handler may change the value or cancel.</summary>
        public event EventHandler<PdfViewerFieldChangingEventArgs>? FieldValueChanging;

        /// <summary>Raised after a field took a new value and the page was redrawn with it.</summary>
        public event EventHandler<PdfViewerFieldChangedEventArgs>? FieldValueChanged;

        /// <summary>Whether a field value changed, or a field was signed, since the document was opened or last saved.</summary>
        [Browsable(false)]
        public bool IsModified => _unsavedFieldValues || _unsavedSignature;

        /// <summary>The values set since the document was opened, by fully qualified field name.</summary>
        [Browsable(false)]
        public IReadOnlyDictionary<string, string> ChangedFieldValues => _fieldValues;

        /// <summary>
        /// Raised when a form script refused a committed value or showed an alert
        /// (<see cref="PdfViewerFeatures.FormScripts"/>); the status line shows it as well.
        /// </summary>
        public event EventHandler<PdfViewerFormScriptEventArgs>? FormScriptMessage;

        private TimeSpan _formScriptBudget = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The execution time one commit's scripts share — the field's own and every calculation —
        /// while <see cref="PdfViewerFeatures.FormScripts"/> is on. A field whose Keystroke or
        /// Validate script the budget leaves unrun is refused. Zero runs no script.
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public TimeSpan FormScriptBudget
        {
            get => _formScriptBudget;
            set
            {
                if (value < TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The budget cannot be negative.");
                _formScriptBudget = value;
            }
        }

        /// <summary>Whether fillable fields are tinted while <see cref="PdfViewerFeatures.FormFilling"/> is on.</summary>
        [Category("PDF"), DefaultValue(true), Description("Tint the fields a user can fill.")]
        public bool HighlightFormFields
        {
            get => _highlightFormFields;
            set { _highlightFormFields = value; _surface.Invalidate(); }
        }

        /// <summary>
        /// Sets a field's value as the user would — through <see cref="FieldValueChanging"/>, the page
        /// redrawn, <see cref="FieldValueChanged"/> raised. False when form filling is off, no
        /// fillable field has that name, or a handler cancelled. A check box or radio button takes a
        /// state name (<c>Off</c> clears it); a choice field its export value — a multi-select list
        /// box's several joined with <see cref="PdfFormFiller.MultiSelectSeparator"/>.
        /// </summary>
        public bool SetFieldValue(string fieldName, string value)
        {
            if (fieldName == null) throw new ArgumentNullException(nameof(fieldName));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (_doc == null || !Has(PdfViewerFeatures.FormFilling)) return false;
            PdfFormFieldInfo? field = _doc.FormFields.FirstOrDefault(f => f.FullyQualifiedName == fieldName);
            if (field == null || !IsFillable(field) || !ScriptsAllow(field)) return false;
            return ApplyFieldValue(field, value);
        }

        /// <summary>
        /// Writes the document with every changed field value: the original file followed by one
        /// appended revision, so a signature over the original still verifies, encrypted with the
        /// document's own key when it was encrypted. The original, unchanged, when nothing changed.
        /// </summary>
        /// <exception cref="InvalidOperationException">No document is loaded.</exception>
        public void SaveAs(Stream output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (_doc == null) throw new InvalidOperationException("No document is loaded.");
            EndFieldEdit(commit: true);
            FormFillResult? result = _doc.SaveFilled(output, _fieldValues, _displayValues);
            _unsavedFieldValues = false;
            _unsavedSignature = false;
            ReportFillResult(result);
            UpdateSaveButton();
        }

        /// <summary>Writes the document as <see cref="SaveAs(Stream)"/> does, to a file.</summary>
        public void SaveAs(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
                SaveAs(file);
        }

        // ── Built-in save ─────────────────────────────────────────────────────

        private void SaveWithDialog()
        {
            if (_doc == null || !CanSave) return;
            using (var dlg = new SaveFileDialog { DefaultExt = "pdf" })
            {
                dlg.Filter = DialogFilter(_strings.SaveFileFilter, PdfViewerStrings.English().SaveFileFilter);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    SaveAs(dlg.FileName);
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.SavedFormat, Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex)
                {
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.SaveFailedFormat, ex.Message);
                }
            }
        }

        private void UpdateSaveButton() => _btnSave.Enabled = _doc != null && (_unsavedFieldValues || _unsavedSignature);

        // ── Which fields a user can fill ──────────────────────────────────────

        /// <summary>
        /// A field the viewer edits: not read-only, not a push button, not a signature; one the
        /// document's permissions let a user fill (<see cref="Permits"/>); and one no signature
        /// already in the document locks — a certification at DocMDP level 1 locks every field, a
        /// FieldMDP lock the fields it names (<see cref="PdfDocumentSource.IsFieldLocked"/>) — since a
        /// save appends a revision the certification or lock forbids, and the signature over the
        /// original would no longer verify.
        /// </summary>
        private bool IsFillable(PdfFormFieldInfo f)
            => _doc != null
               && Permits(Tetitulz.Pdf.Base.Encryption.PdfPermissions.FillForms | Tetitulz.Pdf.Base.Encryption.PdfPermissions.AnnotateAndForms)
               && !f.IsReadOnly && !f.IsPushButton
               && (f.FieldType == PdfFieldType.Text || f.FieldType == PdfFieldType.Choice || f.FieldType == PdfFieldType.Button)
               && !_doc.IsFieldLocked(f.FullyQualifiedName);

        /// <summary>The fillable field and widget under a canvas point, the last drawn first.</summary>
        private (PdfFormFieldInfo Field, PdfFormWidgetInfo Widget)? HitTestField(Point v)
        {
            if (_doc == null || !Has(PdfViewerFeatures.FormFilling)) return null;
            int page = PageAtVirtual(v);
            if (page < 0 || !TryGetBounds(page, out Rectangle bounds)) return null;
            (PdfFormFieldInfo, PdfFormWidgetInfo)? hit = null;
            foreach (var (field, widget) in FillableWidgetsOn(page))
                if (WidgetToCanvas(widget, bounds).Contains(v)) hit = (field, widget);
            return hit;
        }

        /// <summary>Whether the form scripts left the field editable: not made read-only, not hidden.</summary>
        private bool ScriptsAllow(PdfFormFieldInfo f)
            => !_fieldStates.TryGetValue(f.FullyQualifiedName, out PdfJsFieldState? state)
               || (state.ReadOnly != true && state.Hidden != true);

        /// <summary>
        /// The fillable fields' widgets on <paramref name="page"/> that the page shows: one the file
        /// hides (<c>/F</c> Hidden or NoView, Table 165: not displayed, no interaction) is neither
        /// tinted, hit nor edited, as the renderer draws no such widget.
        /// </summary>
        private IEnumerable<(PdfFormFieldInfo Field, PdfFormWidgetInfo Widget)> FillableWidgetsOn(int page)
        {
            if (_doc == null) yield break;
            foreach (var field in _doc.FormFields)
            {
                if (!IsFillable(field) || !ScriptsAllow(field)) continue;
                foreach (var widget in field.Widgets)
                    if (widget.PageIndex == page && !widget.IsHiddenOnScreen) yield return (field, widget);
            }
        }

        private RectangleF WidgetToCanvas(PdfFormWidgetInfo w, Rectangle pageBounds)
            => MapPdfRectToCanvas(w.PageIndex, pageBounds, (float)w.X, (float)w.Y, (float)w.Width, (float)w.Height);

        /// <summary>
        /// The field's value as the viewer last set it, else as the document holds it — a
        /// multi-select list box's selections joined with <see cref="PdfFormFiller.MultiSelectSeparator"/>.
        /// </summary>
        private string CurrentValue(PdfFormFieldInfo field)
            => _fieldValues.TryGetValue(field.FullyQualifiedName, out string? v) ? v
               : field.IsMultiSelect ? string.Join(PdfFormFiller.MultiSelectSeparator, field.SelectedValues)
               : field.Value ?? "";

        // ── A press on a field ────────────────────────────────────────────────

        /// <summary>
        /// Acts on a press over a fillable field: a button toggles at once, a text or choice field
        /// opens its editor. True when the press was a field's, so no link or selection starts.
        /// </summary>
        internal bool BeginFieldInteraction(Point v)
        {
            var hit = HitTestField(v);
            EndFieldEdit(commit: true);
            if (hit == null) return false;
            var (field, widget) = hit.Value;

            if (field.FieldType == PdfFieldType.Button)
            {
                string on = widget.OnStates.FirstOrDefault() ?? field.OnStates.FirstOrDefault() ?? "Yes";
                bool isOn = CurrentValue(field) == on;
                // A radio button is chosen, never cleared by a second click (§12.7.4.2.4 NoToggleToOff
                // is the common case, and clearing a group by clicking it is not what users expect).
                string next = field.IsRadio ? on : (isOn ? "Off" : on);
                if (next != CurrentValue(field)) ApplyFieldValue(field, next);
                return true;
            }

            if (!TryGetBounds(widget.PageIndex, out Rectangle bounds)) return true;
            OpenFieldEditor(field, WidgetToCanvas(widget, bounds));
            return true;
        }

        private void OpenFieldEditor(PdfFormFieldInfo field, RectangleF canvas)
        {
            // The surface's client space is the canvas moved by the scroll offset.
            var at = new Rectangle(
                SaturatingCast.ToInt(Math.Round(canvas.X)) + _surface.AutoScrollPosition.X,
                SaturatingCast.ToInt(Math.Round(canvas.Y)) + _surface.AutoScrollPosition.Y,
                Math.Max(8, SaturatingCast.ToInt(Math.Round(canvas.Width))),
                Math.Max(8, SaturatingCast.ToInt(Math.Round(canvas.Height))));
            float fontPx = Math.Max(7f, Math.Min(at.Height * 0.6f, 28f));
            var font = new Font(Font.FontFamily, fontPx, GraphicsUnit.Pixel);

            Control editor;
            if (field.IsMultiSelect)
            {
                // A multi-select list box: its options as a list, several chosen with Ctrl and Shift.
                var list = new ListBox { SelectionMode = SelectionMode.MultiExtended, Font = font, DisplayMember = nameof(ChoiceItem.Display) };
                var chosen = new HashSet<string>(
                    CurrentValue(field).Split(new[] { PdfFormFiller.MultiSelectSeparator }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.Ordinal);
                foreach (var option in field.Options)
                {
                    var item = new ChoiceItem(option.DisplayText ?? option.ExportValue ?? "", option.ExportValue ?? option.DisplayText ?? "");
                    list.Items.Add(item);
                    if (chosen.Contains(item.Export)) list.SetSelected(list.Items.Count - 1, true);
                }
                list.KeyDown += FieldEditor_KeyDown;
                editor = list;
            }
            else if (field.FieldType == PdfFieldType.Choice)
            {
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Font = font,
                    DisplayMember = nameof(ChoiceItem.Display),
                };
                string current = CurrentValue(field);
                foreach (var option in field.Options)
                {
                    var item = new ChoiceItem(option.DisplayText ?? option.ExportValue ?? "",
                                              option.ExportValue ?? option.DisplayText ?? "");
                    combo.Items.Add(item);
                    if (item.Export == current) combo.SelectedItem = item;
                }
                combo.SelectionChangeCommitted += (s, e) => EndFieldEdit(commit: true);
                combo.KeyDown += FieldEditor_KeyDown;
                editor = combo;
            }
            else
            {
                var box = new FieldTextBox
                {
                    Owner = this,
                    Text = CurrentValue(field),
                    Font = font,
                    Multiline = field.IsMultiline,
                    AcceptsReturn = field.IsMultiline,
                    UseSystemPasswordChar = field.IsPassword,
                    TextAlign = field.Quadding == 1 ? HorizontalAlignment.Center
                              : field.Quadding == 2 ? HorizontalAlignment.Right
                              : HorizontalAlignment.Left,
                    BorderStyle = BorderStyle.FixedSingle,
                };
                if (field.MaxLength > 0) box.MaxLength = field.MaxLength;
                box.KeyDown += FieldEditor_KeyDown;
                box.KeyPress += FieldEditor_KeyPress;
                editor = box;
                if (Has(PdfViewerFeatures.FormScripts))
                {
                    // One document for the editing session, so a keystroke does not parse the file.
                    // Unreadable, no script runs while typing — said in the status line, as a
                    // keystroke's own failure is; the commit still runs the scripts and reports.
                    try { _keystrokeDocument = _doc!.OpenDocument(); }
                    catch (Exception ex)
                    {
                        _keystrokeDocument = null;
                        _statusLabel.Text = PdfViewerStrings.Format(_strings.FormScriptsFailedFormat, ex.Message);
                    }
                }
            }

            editor.Bounds = at;
            editor.Leave += (s, e) => EndFieldEdit(commit: true);
            _editingField = field.FullyQualifiedName;
            _fieldEditor = editor;
            _surface.Controls.Add(editor);
            editor.Focus();
        }

        private void FieldEditor_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = e.SuppressKeyPress = true;
                EndFieldEdit(commit: false);
            }
            else if (e.KeyCode == Keys.Enter && !(sender is TextBox { Multiline: true }))
            {
                e.Handled = e.SuppressKeyPress = true;
                EndFieldEdit(commit: true);
            }
        }

        /// <summary>The document a field's Keystroke script is read from while the field is typed into.</summary>
        private Tetitulz.Pdf.Core.Document.PdfDocument? _keystrokeDocument;

        /// <summary>A typed character, through the field's Keystroke script when form scripts are on.</summary>
        private void FieldEditor_KeyPress(object? sender, KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar) || _keystrokeDocument == null) return;
            e.Handled = true;                                       // inserted, or not, by TypeIntoEditor
            TypeIntoEditor(e.KeyChar.ToString());
        }

        /// <summary>
        /// Puts <paramref name="change"/> in place of the editor's selection as the field's Keystroke
        /// script allows (§12.6.3 Table 196, <c>/K</c>, <c>event.willCommit</c> false): refused, it
        /// leaves the text as it was; rewritten, the script's text goes in. The field's <c>MaxLen</c>
        /// is kept, as typing would. False when nothing was inserted. A paste arrives here as one
        /// change (<see cref="PasteIntoEditor"/>), so the script sees it as Acrobat's does.
        /// </summary>
        internal bool TypeIntoEditor(string change)
        {
            if (!(_fieldEditor is TextBox box) || _editingField == null) return false;
            string text = box.Text;
            int start = box.SelectionStart, end = start + box.SelectionLength;

            string insert = change;
            if (_keystrokeDocument != null)
            {
                var current = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var f in _doc!.FormFields) current[f.FullyQualifiedName] = CurrentValue(f);
                current[_editingField] = text;
                KeystrokeResult? result = null;
                try
                {
                    result = PdfFieldScripts.Keystroke(_keystrokeDocument, _editingField, text, start, end,
                        change, current, _formScriptBudget);
                }
                catch (Exception ex)
                {
                    // The script could not be read: the keystroke goes in, and the commit decides.
                    _statusLabel.Text = PdfViewerStrings.Format(_strings.FormScriptsFailedFormat, ex.Message);
                }
                if (result != null)
                {
                    if (!result.Accepted) { System.Media.SystemSounds.Beep.Play(); return false; }
                    insert = result.Change;
                }
            }

            if (box.MaxLength > 0 && text.Length - (end - start) + insert.Length > box.MaxLength) return false;
            box.SelectedText = insert;
            return true;
        }

        /// <summary>
        /// Puts pasted <paramref name="text"/> into the editor as typing it would
        /// (<see cref="TypeIntoEditor"/>): one change, offered whole to the field's Keystroke script;
        /// a single-line field takes the first line. False when nothing went in.
        /// </summary>
        internal bool PasteIntoEditor(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (!(_fieldEditor is TextBox box)) return false;
            if (!box.Multiline)
            {
                int lineEnd = text.IndexOfAny(new[] { '\r', '\n' });
                if (lineEnd >= 0) text = text.Substring(0, lineEnd);
            }
            return text.Length > 0 && TypeIntoEditor(text);
        }

        /// <summary>The clipboard's text into the editor through <see cref="PasteIntoEditor"/>; false when it holds no text.</summary>
        private bool PasteFromClipboard()
        {
            string? text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (System.Runtime.InteropServices.ExternalException) { return false; }   // the clipboard is another process's for the moment
            if (string.IsNullOrEmpty(text)) return false;
            PasteIntoEditor(text!);
            return true;
        }

        /// <summary>The text editor: a paste reaches the field's Keystroke script as typing does.</summary>
        private sealed class FieldTextBox : TextBox
        {
            private const int WmPaste = 0x0302;

            internal PdfViewerControl? Owner;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmPaste && Owner != null && Owner.PasteFromClipboard()) return;
                base.WndProc(ref m);
            }
        }

        /// <summary>
        /// Closes the live editor, applying its value when <paramref name="commit"/> and it changed.
        /// Re-entrant calls (the editor's own <c>Leave</c> while it is being removed) find no editor.
        /// </summary>
        internal void EndFieldEdit(bool commit)
        {
            Control? editor = _fieldEditor;
            string? name = _editingField;
            if (editor == null || name == null) return;
            _fieldEditor = null;
            _editingField = null;
            _keystrokeDocument?.Dispose();
            _keystrokeDocument = null;

            string? value = editor switch
            {
                TextBox t => t.Text,
                ComboBox c => (c.SelectedItem as ChoiceItem)?.Export,
                ListBox l => string.Join(PdfFormFiller.MultiSelectSeparator, l.SelectedItems.Cast<ChoiceItem>().Select(i => i.Export)),
                _ => null,
            };
            _surface.Controls.Remove(editor);
            editor.Font?.Dispose();
            editor.Dispose();

            if (!commit || value == null || _doc == null) return;
            PdfFormFieldInfo? field = _doc.FormFields.FirstOrDefault(f => f.FullyQualifiedName == name);
            if (field != null && value != CurrentValue(field)) ApplyFieldValue(field, value);
        }

        // ── Applying a value ──────────────────────────────────────────────────

        private bool ApplyFieldValue(PdfFormFieldInfo field, string value)
        {
            string name = field.FullyQualifiedName;
            var changing = new PdfViewerFieldChangingEventArgs(name, CurrentValue(field), value);
            FieldValueChanging?.Invoke(this, changing);
            if (changing.Cancel || changing.NewValue == null) return false;

            // The value and every other field the commit changes: the scripts' answer, or the value alone.
            var changes = new List<KeyValuePair<string, string>>();
            if (!RunFormScripts(name, changing.NewValue, changes, out IReadOnlyDictionary<string, string>? displays)) return false;

            var before = new Dictionary<string, string>(_fieldValues, StringComparer.Ordinal);
            var displayedBefore = new Dictionary<string, string>(_displayValues, StringComparer.Ordinal);
            foreach (var change in changes)
            {
                _fieldValues[change.Key] = change.Value;
                // A changed value drops the text its old value was formatted as; its Format script's, when it ran.
                if (displays != null && displays.TryGetValue(change.Key, out string? shown)) _displayValues[change.Key] = shown;
                else _displayValues.Remove(change.Key);
            }
            try
            {
                ReplaceDocument(_doc!.Filled(_fieldValues, out FormFillResult result, _displayValues));
                ReportFillResult(result);
            }
            catch (Exception ex)
            {
                // Put the value set back as it was: the page and the set must not disagree.
                _fieldValues.Clear();
                foreach (var kv in before) _fieldValues[kv.Key] = kv.Value;
                _displayValues.Clear();
                foreach (var kv in displayedBefore) _displayValues[kv.Key] = kv.Value;
                _statusLabel.Text = PdfViewerStrings.Format(_strings.FormFillFailedFormat, ex.Message);
                return false;
            }

            _unsavedFieldValues = true;
            ClearRenderCache();
            _surface.Invalidate();
            UpdateSaveButton();
            foreach (var change in changes)
                FieldValueChanged?.Invoke(this, new PdfViewerFieldChangedEventArgs(change.Key, change.Value));
            return true;
        }

        /// <summary>
        /// Puts the committed value, and every value the commit changes, in <paramref name="changes"/>
        /// — the field first. With <see cref="PdfViewerFeatures.FormScripts"/> on they are the field's
        /// scripts' and the calculations' answer; false when a script refused the value.
        /// <paramref name="displays"/> is what the changed fields' Format scripts made of them, null
        /// when no script ran.
        /// </summary>
        private bool RunFormScripts(string name, string value, List<KeyValuePair<string, string>> changes,
            out IReadOnlyDictionary<string, string>? displays)
        {
            displays = null;
            if (!Has(PdfViewerFeatures.FormScripts))
            {
                changes.Add(new KeyValuePair<string, string>(name, value));
                return true;
            }

            var current = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in _doc!.FormFields)
                current[field.FullyQualifiedName] = CurrentValue(field);

            FieldCommitResult commit;
            try
            {
                using (var document = _doc.OpenDocument())
                    commit = PdfFieldScripts.Commit(document, name, value, current, _formScriptBudget);
            }
            catch (Exception ex)
            {
                // The scripts could not even be read: nothing approved the value.
                ReportScriptMessage(name, PdfViewerStrings.Format(_strings.FormScriptsFailedFormat, ex.Message), true);
                return false;
            }

            if (!commit.Accepted)
            {
                ReportScriptMessage(name, PdfViewerStrings.Format(_strings.FieldValueRefusedFormat, name,
                    commit.RejectionMessage ?? ""), true);
                return false;
            }

            changes.Add(new KeyValuePair<string, string>(name, commit.Value));
            displays = commit.DisplayValues;
            foreach (var written in commit.FieldStates)
            {
                if (!_fieldStates.TryGetValue(written.Key, out PdfJsFieldState? state))
                    _fieldStates[written.Key] = state = new PdfJsFieldState();
                if (written.Value.ReadOnly.HasValue) state.ReadOnly = written.Value.ReadOnly;
                if (written.Value.Hidden.HasValue) state.Hidden = written.Value.Hidden;
                if (written.Value.Required.HasValue) state.Required = written.Value.Required;
            }
            var known = new HashSet<string>(current.Keys, StringComparer.Ordinal);
            foreach (var other in commit.OtherChanges)
                if (other.Key != name && known.Contains(other.Key))   // a script's write to no field writes nothing
                    changes.Add(other);

            foreach (string alert in commit.Alerts)
                ReportScriptMessage(name, alert, false);
            if (!commit.ScriptsRan && commit.Warnings.Count > 0)
                _statusLabel.Text = PdfViewerStrings.Format(_strings.FormScriptsNotRunFormat, commit.Warnings[0]);
            return true;
        }

        private void ReportScriptMessage(string field, string message, bool isRejection)
        {
            _statusLabel.Text = message;
            FormScriptMessage?.Invoke(this, new PdfViewerFormScriptEventArgs(field, message, isRejection));
        }

        /// <summary>Says in the status line which values could not be drawn — the fill still stored them.</summary>
        private void ReportFillResult(FormFillResult? result)
        {
            if (result == null || result.FieldsWithoutAppearance.Count == 0) return;
            _statusLabel.Text = PdfViewerStrings.Format(_strings.FieldsNotDrawnFormat,
                string.Join(", ", result.FieldsWithoutAppearance));
        }

        /// <summary>Forgets every value and closes the editor — a new document, or none.</summary>
        private void ResetFormState()
        {
            EndFieldEdit(commit: false);
            _fieldValues.Clear();
            _displayValues.Clear();
            _fieldStates.Clear();
            _unsavedFieldValues = false;
            _unsavedSignature = false;
            UpdateSaveButton();
        }

        // ── Painting ──────────────────────────────────────────────────────────

        private Color FormFieldColor => _palette.FormField;

        private void DrawFormFieldHighlights(Graphics g, int pageIndex, Rectangle bounds)
        {
            if (!Has(PdfViewerFeatures.FormFilling) || !_highlightFormFields || _doc == null) return;
            using (var tint = new SolidBrush(FormFieldColor))
                foreach (var (_, widget) in FillableWidgetsOn(pageIndex))
                    g.FillRectangle(tint, WidgetToCanvas(widget, bounds));
        }

        // ── Test hooks ────────────────────────────────────────────────────────

        /// <summary>The live editor, or null.</summary>
        internal Control? FieldEditor => _fieldEditor;

        /// <summary>The surface point at the centre of a field's first widget, for a test's press.</summary>
        internal Point FieldCentreOnSurface(string fieldName)
        {
            PdfFormFieldInfo field = _doc!.FormFields.First(f => f.FullyQualifiedName == fieldName);
            PdfFormWidgetInfo w = field.Widgets.First();
            if (!TryGetBounds(w.PageIndex, out Rectangle bounds)) throw new InvalidOperationException("page not laid out");
            RectangleF r = WidgetToCanvas(w, bounds);
            return new Point(SaturatingCast.ToInt(r.X + r.Width / 2) + _surface.AutoScrollPosition.X,
                             SaturatingCast.ToInt(r.Y + r.Height / 2) + _surface.AutoScrollPosition.Y);
        }

        /// <summary>A choice's display text and export value, for the combo box.</summary>
        private sealed class ChoiceItem
        {
            public ChoiceItem(string display, string export) { Display = display; Export = export; }
            public string Display { get; }
            public string Export { get; }
            public override string ToString() => Display;
        }
    }
}
