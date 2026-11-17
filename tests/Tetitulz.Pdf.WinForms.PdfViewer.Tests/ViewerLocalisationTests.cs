// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer.Tests;

/// <summary>
/// <see cref="PdfViewerControl.Strings"/> — presenting the chrome in another language.
/// </summary>
/// <remarks>
/// <para><b>Every case reads the text off a real control</b>, because the failure this guards
/// against is a label that was written at its construction site and never routed through the
/// strings — which leaves a control that is half translated and looks, from the property, entirely
/// translated.</para>
///
/// <para><b>The sweep at the end is the one that scales.</b> Asserting each label individually
/// pins the labels that exist today; walking every <see cref="ToolStripItem"/> and asserting that
/// none of them still shows an English default catches the next one somebody adds. Both are here,
/// because the sweep alone would pass on a control whose labels were all blank.</para>
///
/// <para>No case needs a window or a message loop, following <c>RtlChromeTests</c>: the strings are
/// assigned in the constructor and by the property, neither of which waits for a handle. The cases
/// still run on an STA thread, like the rest of this assembly.</para>
/// </remarks>
public sealed class ViewerLocalisationTests
{
    /// <summary>The chrome is English until a host says otherwise.</summary>
    [Fact]
    public void TheDefaultStringsAreEnglish()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();

            Assert.Equal("Open", ctrl.OpenButton.Text);
            Assert.Equal("Find", ctrl.FindButton.Text);
            Assert.Equal("Pages", ctrl.SidebarTabs.TabPages[0].Text);
        });
    }

    /// <summary>
    /// Assigning strings after construction re-labels the control that is already built.
    /// </summary>
    /// <remarks>
    /// This is what makes the property usable from a host that learns its UI language later — a
    /// settings dialog, a per-user preference. A seam that only worked in the constructor would
    /// force a host to rebuild the viewer to change language, and rebuilding it loses the open
    /// document, the scroll position and the zoom.
    /// </remarks>
    [Fact]
    public void AssigningStringsRelabelsAnAlreadyBuiltControl()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            Assert.Equal("Open", ctrl.OpenButton.Text);

            ctrl.Strings = PdfViewerStrings.Hebrew();

            Assert.Equal("פתח", ctrl.OpenButton.Text);
            Assert.Equal("חיפוש", ctrl.FindButton.Text);
            Assert.Equal("עמודים", ctrl.SidebarTabs.TabPages[0].Text);
            Assert.Equal("סימניות", ctrl.SidebarTabs.TabPages[1].Text);
        });
    }

    /// <summary>Tooltips are translated too, not only the visible labels.</summary>
    /// <remarks>
    /// A tooltip is the half that gets forgotten, because nothing about the control's appearance
    /// shows it is still English.
    /// </remarks>
    [Fact]
    public void TooltipsAreTranslatedAsWellAsLabels()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            string englishTip = ctrl.OpenButton.ToolTipText;

            ctrl.Strings = PdfViewerStrings.Hebrew();

            Assert.NotEqual(englishTip, ctrl.OpenButton.ToolTipText);
            Assert.Contains("PDF", ctrl.OpenButton.ToolTipText);   // the acronym is not translated
            Assert.Contains("פתיחת", ctrl.OpenButton.ToolTipText);
        });
    }

    /// <summary>Assigning null restores English rather than clearing the chrome.</summary>
    /// <remarks>
    /// A null here is a host resetting to the default, and blanking every label would be a viewer
    /// with no visible controls — a failure that looks like a rendering bug.
    /// </remarks>
    [Fact]
    public void AssigningNullRestoresTheDefaults()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            ctrl.Strings = PdfViewerStrings.Hebrew();

            ctrl.Strings = null;

            Assert.NotNull(ctrl.Strings);
            Assert.Equal("Open", ctrl.OpenButton.Text);
        });
    }

    /// <summary>
    /// No toolbar item is left showing an English default once Hebrew strings are assigned.
    /// </summary>
    /// <remarks>
    /// <para><b>The sweep, and it is what stops the next label being forgotten.</b> A label written
    /// at its construction site rather than through the strings survives every individual
    /// assertion above, because those name the labels that already work.</para>
    ///
    /// <para>Glyph-only buttons carry no translatable text, so they are compared against the
    /// English defaults rather than required to change — and the count of items that DID change is
    /// asserted, or a control whose labels were all blanked would pass this too.</para>
    /// </remarks>
    [Fact]
    public void NoToolbarItemKeepsAnEnglishDefault()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();

            var english = ctrl.Toolbar.Items.Cast<ToolStripItem>()
                .Select(i => (i.Text, i.ToolTipText)).ToList();

            ctrl.Strings = PdfViewerStrings.Hebrew();

            var hebrew = ctrl.Toolbar.Items.Cast<ToolStripItem>()
                .Select(i => (i.Text, i.ToolTipText)).ToList();

            int changed = 0;
            for (int i = 0; i < english.Count; i++)
            {
                if (english[i] != hebrew[i]) changed++;

                foreach (string text in new[] { hebrew[i].Text, hebrew[i].ToolTipText })
                    Assert.False(HasLatinWord(text),
                        $"toolbar item {i} still reads '{text}' after the strings were translated");
            }

            Assert.True(changed >= 8, $"only {changed} toolbar items changed");
        });
    }

    /// <summary>
    /// Every settable string on <see cref="PdfViewerStrings"/> is translated by
    /// <see cref="PdfViewerStrings.Hebrew"/>.
    /// </summary>
    /// <remarks>
    /// A property added later and not given a Hebrew value leaves the control mixing languages, and
    /// nothing about the type shows it. Numeric-only templates are exempt because there is nothing
    /// in them to translate — <c>"/ {0}"</c> reads the same in either language.
    /// </remarks>
    [Fact]
    public void EveryStringHasAHebrewValueOrNothingToTranslate()
    {
        var english = PdfViewerStrings.English();
        var hebrew = PdfViewerStrings.Hebrew();

        foreach (PropertyInfo property in typeof(PdfViewerStrings).GetProperties())
        {
            if (!property.CanWrite || property.PropertyType != typeof(string)) continue;

            string from = (string)property.GetValue(english);
            string to = (string)property.GetValue(hebrew);

            if (!HasLatinWord(from)) continue;   // "/ {0}" and friends

            Assert.True(from != to,
                $"{property.Name} is still the English '{from}' in PdfViewerStrings.Hebrew()");
        }
    }

    /// <summary>
    /// A malformed template is shown literally rather than throwing.
    /// </summary>
    /// <remarks>
    /// A translated string is caller-supplied data on its way to <c>string.Format</c>. A stray
    /// <c>{</c> — easy to type, easy for a translation tool to introduce — would otherwise throw
    /// from wherever the status line is written, which for the open path means the viewer crashes
    /// when a document finishes loading. A label that reads oddly is the better failure.
    /// </remarks>
    [Fact]
    public void AMalformedTemplateIsShownRatherThanThrown()
    {
        // Asserted on the formatter itself. The first version of this test assigned a malformed
        // PageCountFormat to a control and checked it did not throw - which it could not have,
        // because with no document open the page counter takes the "unknown" branch and never
        // formats anything. A mutation removing the try/catch left it green.
        Assert.Equal("{0} of {oops", PdfViewerStrings.Format("{0} of {oops", 3));

        // The control: a well-formed template IS formatted, or the line above would also pass for
        // a formatter that never substituted anything.
        Assert.Equal("3 pages", PdfViewerStrings.Format("{0} pages", 3));
    }

    /// <summary>
    /// The page counter picks up new strings, even with no document open.
    /// </summary>
    /// <remarks>
    /// It is the one label whose text is a function of state rather than a constant, so it is
    /// re-derived when the strings change rather than left until the state next moves. Asserted
    /// through the "no document" form because both shipped languages spell the counter itself the
    /// same way — a mutation dropping the refresh survived a test that compared English to Hebrew.
    /// </remarks>
    [Fact]
    public void ThePageCounterPicksUpNewStrings()
    {
        RunSta(() =>
        {
            using var ctrl = new PdfViewerControl();
            string before = ctrl.PageCountLabel.Text;

            var strings = PdfViewerStrings.English();
            strings.PageCountUnknown = "no pages yet";
            ctrl.Strings = strings;

            Assert.NotEqual(before, ctrl.PageCountLabel.Text);
            Assert.Equal("no pages yet", ctrl.PageCountLabel.Text);
        });
    }

    // ----------------------------------------------------------------

    /// <summary>
    /// Whether the text contains a run of Latin letters — the mark of an untranslated label.
    /// </summary>
    /// <remarks>
    /// A single letter is not enough: <c>Ctrl+C</c>, <c>PDF</c> and <c>PageUp</c> stay in Latin in
    /// a Hebrew UI because they name keys and formats rather than describing anything, so the test
    /// looks for a Latin run that is not one of those.
    /// </remarks>
    private static bool HasLatinWord(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        foreach (string word in text.Split(
            new[] { ' ', '(', ')', '+', '/', '—', '-', ':', '.', ',' },
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length < 2) continue;
            if (word == "PDF" || word == "Ctrl" || word == "Alt" || word == "PageUp" || word == "PageDown") continue;
            if (word.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return true;
        }

        return false;
    }

    private static void RunSta(Action action) => StaThread.Run(action);
}
