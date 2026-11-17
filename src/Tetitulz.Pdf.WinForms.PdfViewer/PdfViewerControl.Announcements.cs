// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
#if !NETFRAMEWORK
using System.Windows.Forms.Automation;
#endif

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// The status line spoken by a screen reader as it changes: a document loaded, a page that
    /// failed, a search's result, a field refused — what a sighted reader learns from a glance at it.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        private bool _announceStatusChanges = true;

        /// <summary>
        /// Whether a change of the status line is announced to a screen reader (the default): through
        /// UI Automation's notification event on .NET 10, as the most recent important message, so a
        /// later message replaces one not yet spoken. Only a status line on screen speaks: nothing
        /// before the control is shown, nothing with the status bar hidden. .NET Framework 4.6.2 has
        /// no such event, and there the status line is read only when the reader moves to it.
        /// </summary>
        [Category("Accessibility"), DefaultValue(true),
         Description("Announce status-line messages to a screen reader as they appear.")]
        public bool AnnounceStatusChanges
        {
            get => _announceStatusChanges;
            set => _announceStatusChanges = value;
        }

        /// <summary>The last message handed to the screen reader (for tests); null before any.</summary>
        internal string? LastAnnouncement { get; private set; }

        /// <summary>How many messages were handed to the screen reader (for tests).</summary>
        internal int AnnouncementCount { get; private set; }

        /// <summary>
        /// Hands the status line's new text to the screen reader — only while the status line is on
        /// screen: a control not yet shown, or a status bar the host hid, says nothing. The same text
        /// set again raises no change, so a message standing is not spoken twice.
        /// </summary>
        private void AnnounceStatus()
        {
            string text = _statusLabel.Text ?? "";
            if (!_announceStatusChanges || text.Length == 0) return;
            if (!_statusStrip.IsHandleCreated || !_statusStrip.Visible) return;
            LastAnnouncement = text;
            AnnouncementCount++;
#if !NETFRAMEWORK
            _statusStrip.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text);
#endif
        }
    }
}
