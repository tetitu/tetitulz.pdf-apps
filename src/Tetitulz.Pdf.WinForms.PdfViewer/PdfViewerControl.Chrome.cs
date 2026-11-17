// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// What a host can switch off or take over: <see cref="Features"/> (a capability, everywhere),
    /// the <c>Show*</c> properties (one control, the capability kept for the host's own UI), and
    /// <see cref="CommandExecuting"/> (the built-in action of a chrome command replaced).
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        private PdfViewerFeatures _features = PdfViewerFeatures.Default;

        private bool _showToolbar = true;
        private bool _showSidebar = true;
        private bool _showOpenButton = true;
        private bool _showNavigationControls = true;
        private bool _showZoomControls = true;
        private bool _showLayoutButton = true;
        private bool _showSidebarButton = true;
        private bool _showFindButton = true;
        private bool _showPrintButton = true;
        private bool _showStatusBar = true;
        private bool _showSaveButton = true;
        private bool _showLinkToolTips = true;

        /// <summary>The hovered link's tooltip, on the page surface.</summary>
        private readonly ToolTip _linkTip = new ToolTip();

        /// <summary>The page surface's current tooltip text, for a test.</summary>
        internal string LinkToolTipText => _linkTip.GetToolTip(_surface) ?? "";

        /// <summary>
        /// Raised before the viewer acts on a command the user started from its chrome — a toolbar
        /// button, a shortcut, a click in the sidebar. Set <see cref="PdfViewerCommandEventArgs.Handled"/>
        /// to stop the built-in action. Calls the host makes itself (<see cref="Print"/>,
        /// <see cref="GoToPage(int)"/>, …) do not raise it, so a handler may call them.
        /// </summary>
        public event EventHandler<PdfViewerCommandEventArgs>? CommandExecuting;

        /// <summary>
        /// The capabilities the viewer offers; <see cref="PdfViewerFeatures.Default"/> unless the host
        /// narrows it. A capability switched off is off everywhere: its control is hidden, its
        /// shortcut passes to the host's form and its public method does nothing.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A bit that names no feature.</exception>
        [Category("PDF"), DefaultValue(PdfViewerFeatures.Default),
         Description("The capabilities the viewer offers.")]
        public PdfViewerFeatures Features
        {
            get => _features;
            set
            {
                if ((value & ~KnownFeatures) != 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Unknown viewer feature.");
                const PdfViewerFeatures sidebar = PdfViewerFeatures.Thumbnails | PdfViewerFeatures.Bookmarks
                    | PdfViewerFeatures.Comments | PdfViewerFeatures.Signatures;
                bool sidebarChanged = ((_features ^ value) & sidebar) != 0;
                _features = value;

                if (!Has(PdfViewerFeatures.Search) && _findBar.Visible) HideFindBar();
                if (!Has(PdfViewerFeatures.FormFilling)) EndFieldEdit(commit: false);
                if (!Has(PdfViewerFeatures.TextSelection)) ClearSelection();
                if (!Has(PdfViewerFeatures.Links))
                {
                    _hoverLink = null;
                    _pendingLink = null;
                    _linkTip.SetToolTip(_surface, null);
                }
                if (!Has(PdfViewerFeatures.Comments))
                {
                    _hoverComment = null;
                    _commentTip.SetToolTip(_surface, null);
                }
                if (!Has(PdfViewerFeatures.Signatures))
                {
                    _hoverSignature = null;
                    _signatureTip.SetToolTip(_surface, null);
                }
                ApplyChromeVisibility();
                if (sidebarChanged && _doc != null) PopulateSidebar();
                _surface.Invalidate();
            }
        }

        private bool Has(PdfViewerFeatures feature) => (_features & feature) == feature;

        /// <summary>Every feature this version knows; a bit outside it is refused.</summary>
        private const PdfViewerFeatures KnownFeatures =
            PdfViewerFeatures.Default | PdfViewerFeatures.FormFilling | PdfViewerFeatures.FormScripts
            | PdfViewerFeatures.Comments | PdfViewerFeatures.Signatures | PdfViewerFeatures.Signing
            | PdfViewerFeatures.DocumentView;

        /// <summary>The Save button, offered while <see cref="PdfViewerFeatures.FormFilling"/> or <see cref="PdfViewerFeatures.Signing"/> is on.</summary>
        [Category("PDF"), DefaultValue(true), Description("Show or hide the Save button (form filling or signing only).")]
        public bool ShowSaveButton
        {
            get => _showSaveButton;
            set { _showSaveButton = value; ApplyChromeVisibility(); }
        }

        [Category("PDF"), DefaultValue(true), Description("Show or hide the built-in toolbar.")]
        public bool ShowToolbar
        {
            get => _showToolbar;
            set { _showToolbar = value; ApplyChromeVisibility(); }
        }

        /// <summary>
        /// Whether the thumbnails / bookmarks sidebar is shown. It stays hidden while
        /// <see cref="Features"/> leaves it no tab.
        /// </summary>
        [Category("PDF"), DefaultValue(true), Description("Show or hide the thumbnails / bookmarks sidebar.")]
        public bool ShowSidebar
        {
            get => _showSidebar;
            set { _showSidebar = value; ApplyChromeVisibility(); }
        }

        /// <summary>
        /// Whether the built-in toolbar offers its own Open button. A host that opens documents itself
        /// hides it; to stop the user opening a file at all (Ctrl+O too), clear
        /// <see cref="PdfViewerFeatures.OpenFile"/> from <see cref="Features"/>.
        /// </summary>
        [Category("PDF"), DefaultValue(true), Description("Show or hide the built-in Open button.")]
        public bool ShowOpenButton
        {
            get => _showOpenButton;
            set { _showOpenButton = value; ApplyChromeVisibility(); }
        }

        /// <summary>The previous / next buttons, the page box and the page count, and Back and Forward with them.</summary>
        [Category("PDF"), DefaultValue(true), Description("Show or hide the page navigation controls.")]
        public bool ShowNavigationControls
        {
            get => _showNavigationControls;
            set { _showNavigationControls = value; ApplyChromeVisibility(); }
        }

        /// <summary>The zoom buttons, the zoom box and the two fit buttons.</summary>
        [Category("PDF"), DefaultValue(true), Description("Show or hide the zoom controls.")]
        public bool ShowZoomControls
        {
            get => _showZoomControls;
            set { _showZoomControls = value; ApplyChromeVisibility(); }
        }

        [Category("PDF"), DefaultValue(true), Description("Show or hide the continuous / single-page button.")]
        public bool ShowLayoutButton
        {
            get => _showLayoutButton;
            set { _showLayoutButton = value; ApplyChromeVisibility(); }
        }

        [Category("PDF"), DefaultValue(true), Description("Show or hide the button that shows and hides the sidebar.")]
        public bool ShowSidebarButton
        {
            get => _showSidebarButton;
            set { _showSidebarButton = value; ApplyChromeVisibility(); }
        }

        [Category("PDF"), DefaultValue(true), Description("Show or hide the Find button.")]
        public bool ShowFindButton
        {
            get => _showFindButton;
            set { _showFindButton = value; ApplyChromeVisibility(); }
        }

        [Category("PDF"), DefaultValue(true), Description("Show or hide the Print button.")]
        public bool ShowPrintButton
        {
            get => _showPrintButton;
            set { _showPrintButton = value; ApplyChromeVisibility(); }
        }

        /// <summary>
        /// Whether the document's permissions (<c>/P</c>, ISO 32000-2 Table 22) are honoured when its
        /// user password opened it — the empty one, or one the host supplied — withholding print, copy,
        /// form filling and signing as the bits say. A document its owner password opened, or one not
        /// encrypted, has no restriction; under AES-256 the bits are honoured only when the authenticated
        /// <c>/Perms</c> vouched for them (<c>PdfDecryptor.LastPermsValidation</c>). Off, a host that
        /// has the owner's authority by other means offers everything.
        /// </summary>
        [Category("PDF"), DefaultValue(true), Description("Honour the document's permissions when its user password opened it.")]
        public bool HonourPermissions
        {
            get => _honourPermissions;
            set { _honourPermissions = value; if (_doc != null) UpdateCommandStates(); }
        }

        private bool _honourPermissions = true;

        /// <summary>
        /// Whether the open document permits <paramref name="permission"/> — any of its bits: it does
        /// when it is not encrypted, its owner password opened it, or <see cref="HonourPermissions"/> is off.
        /// </summary>
        internal bool Permits(Tetitulz.Pdf.Base.Encryption.PdfPermissions permission)
            => !_honourPermissions || _doc?.Permissions == null || (_doc.Permissions.Value & permission) != 0;

        /// <summary>Whether hovering a link shows a tooltip with its address, page, action or file.</summary>
        [Category("PDF"), DefaultValue(true), Description("Show a tooltip naming a link's target on hover.")]
        public bool ShowLinkToolTips
        {
            get => _showLinkToolTips;
            set { _showLinkToolTips = value; if (!value) _linkTip.SetToolTip(_surface, null); }
        }

        [Category("PDF"), DefaultValue(true), Description("Show or hide the status bar.")]
        public bool ShowStatusBar
        {
            get => _showStatusBar;
            set { _showStatusBar = value; ApplyChromeVisibility(); }
        }

        /// <summary>
        /// Writes every control's visibility from the <c>Show*</c> switches and <see cref="Features"/> —
        /// the one place either is turned into a control's state, so the two cannot disagree.
        /// </summary>
        /// <remarks>
        /// A toolbar item is set through <see cref="ToolStripItem.Available"/>, not <c>Visible</c>: an
        /// item's <c>Visible</c> reads false until its window is shown.
        /// </remarks>
        private void ApplyChromeVisibility()
        {
            _toolbar.Visible = _showToolbar;
            _statusStrip.Visible = _showStatusBar;

            _btnOpen.Available = _showOpenButton && Has(PdfViewerFeatures.OpenFile);
            _btnSave.Available = _showSaveButton && CanSave;
            _btnPrev.Available = _pageBox.Available = _pageCountLabel.Available = _btnNext.Available
                = _showNavigationControls;
            // Back and Forward sit in the navigation group: hiding the group hides them as well.
            _btnBack.Available = _btnForward.Available = _showNavigationControls && _showHistoryButtons;
            _btnZoomOut.Available = _zoomCombo.Available = _btnZoomIn.Available
                = _btnFitWidth.Available = _btnFitPage.Available = _showZoomControls;
            _btnLayout.Available = _showLayoutButton;
            _btnFind.Available = _showFindButton && Has(PdfViewerFeatures.Search);
            _btnPrint.Available = _showPrintButton && Has(PdfViewerFeatures.Print);

            ApplySidebarTabs();
            bool anyTab = _sidebarTabs.TabPages.Count > 0;
            _split.Panel1Collapsed = !(_showSidebar && anyTab);
            _btnSidebar.Checked = _showSidebar;
            _btnSidebar.Available = _showSidebarButton && anyTab;

            ApplyToolbarSeparators();
        }

        /// <summary>The tabs <see cref="Features"/> allows, in their fixed order; untouched when already so.</summary>
        private void ApplySidebarTabs()
        {
            var wanted = new List<TabPage>(4);
            if (Has(PdfViewerFeatures.Thumbnails)) wanted.Add(_tabThumbnails);
            if (Has(PdfViewerFeatures.Bookmarks)) wanted.Add(_tabBookmarks);
            if (Has(PdfViewerFeatures.Comments)) wanted.Add(_tabComments);
            if (Has(PdfViewerFeatures.Signatures)) wanted.Add(_tabSignatures);
            var pages = _sidebarTabs.TabPages;
            bool already = pages.Count == wanted.Count;
            for (int i = 0; already && i < wanted.Count; i++) already = pages[i] == wanted[i];
            if (already) return;
            pages.Clear();
            foreach (var tab in wanted) pages.Add(tab);
            LayoutSidebarHeader();
        }

        /// <summary>
        /// Shows a separator only between two groups that each still show an item, so hiding a group
        /// leaves neither a doubled separator nor one at an end.
        /// </summary>
        private void ApplyToolbarSeparators()
        {
            ToolStripItem? pending = null;
            bool seenItem = false;
            foreach (ToolStripItem item in _toolbar.Items)
            {
                if (item is ToolStripSeparator)
                {
                    item.Available = false;
                    if (seenItem && pending == null) pending = item;
                }
                else if (item.Available)
                {
                    if (pending != null) { pending.Available = true; pending = null; }
                    seenItem = true;
                }
            }
        }

        /// <summary>
        /// Raises <see cref="CommandExecuting"/> and, unless the host handled it, runs the built-in
        /// action. Every chrome command goes through here; the public methods do not.
        /// </summary>
        private void RunCommand(PdfViewerCommand command, Action builtIn, int? pageIndex = null, double? zoom = null,
            PdfPageSpread? spread = null)
        {
            var args = new PdfViewerCommandEventArgs(command, pageIndex, zoom) { PageSpread = spread };
            CommandExecuting?.Invoke(this, args);
            if (!args.Handled) builtIn();
        }
    }
}
