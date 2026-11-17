// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    public sealed partial class PdfViewerControl
    {
        private bool _inLayout;
        private bool _writingZoomCombo;

        // ── Chrome direction ──────────────────────────────────────────────────
        // Leading-edge / trailing-edge glyphs for the page-navigation buttons. ToolStrip
        // reverses item ORDER under RightToLeft but never rewrites an item's text, so a
        // hard-coded "◀" ends up sitting where "next page" now is, pointing the wrong way.
        private const string GlyphLeft = "◀";
        private const string GlyphRight = "▶";
        // Back points the way the reader came: left in left-to-right, right when mirrored.
        private const string GlyphBack = "←";
        private const string GlyphForward = "→";

        // Find-card metrics, at 96 dpi. The card is a plain Panel holding absolutely-positioned
        // children, and a plain Panel mirrors nothing (measured: a child at Location.X = 6 stays at 6
        // when the panel is RightToLeft.Yes), so these are offsets from the card's LEADING edge and
        // LayoutFindBar turns them into an X. In order: the search box (an icon, then the text), the
        // match count, previous, next, a divider, close.
        private const int FindCardHeight = 48, FindCardMargin = 12, FindPad = 8;
        private const int FindBoxWidth = 240, FindBoxHeight = 32, FindBoxIconWidth = 32;
        private const int FindGap = 8, FindNavSize = 32, FindNavGap = 2, FindDividerGap = 6;

        private void BuildUi()
        {
            SuspendLayout();
            _renderer = new ViewerToolStripRenderer(IconOf);

            // ── Toolbar ───────────────────────────────────────────────────────
            // A WinUI CommandBar: the renderer draws it, the buttons are icons (ApplyToolbarStyle).
            _toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Renderer = _renderer,
                CanOverflow = true, Stretch = true,
            };

            _btnOpen = new ToolStripButton();
            _btnOpen.Click += (s, e) => RunCommand(PdfViewerCommand.Open, OpenWithDialog);

            _btnSave = new ToolStripButton();
            _btnSave.Click += (s, e) => RunCommand(PdfViewerCommand.Save, SaveWithDialog);

            _btnBack = new ToolStripButton(GlyphBack) { Enabled = false };
            _btnBack.Click += (s, e) => RunCommand(PdfViewerCommand.Back, () => GoBack());

            _btnForward = new ToolStripButton(GlyphForward) { Enabled = false };
            _btnForward.Click += (s, e) => RunCommand(PdfViewerCommand.Forward, () => GoForward());

            _btnPrev = new ToolStripButton(GlyphLeft);
            _btnPrev.Click += (s, e) => RunCommand(PdfViewerCommand.PreviousPage, PreviousPage);

            // A WinUI NumberBox: borderless, framed by the renderer, the accent line while focused.
            _pageBox = new ToolStripTextBox { Text = "", AutoSize = false, Width = 40, BorderStyle = BorderStyle.None };
            _pageBox.TextBox.TextAlign = HorizontalAlignment.Center;
            _pageBox.KeyDown += PageBox_KeyDown;
            _pageBox.GotFocus += (s, e) => _toolbar.Invalidate();
            _pageBox.LostFocus += (s, e) => _toolbar.Invalidate();

            _pageCountLabel = new ToolStripLabel();

            _btnNext = new ToolStripButton(GlyphRight);
            _btnNext.Click += (s, e) => RunCommand(PdfViewerCommand.NextPage, NextPage);

            _btnZoomOut = new ToolStripButton("−");
            _btnZoomOut.Click += (s, e) => RunCommand(PdfViewerCommand.ZoomOut, ZoomOut);

            _zoomCombo = new ToolStripComboBox { AutoSize = false, Width = 84, FlatStyle = FlatStyle.Flat };
            _zoomComboPainter = new FlatComboPainter(this, _zoomCombo.ComboBox);
            _zoomCombo.GotFocus += (s, e) => { _toolbar.Invalidate(); _zoomCombo.ComboBox.Invalidate(); };
            _zoomCombo.LostFocus += (s, e) => { _toolbar.Invalidate(); _zoomCombo.ComboBox.Invalidate(); };
            _zoomCombo.Text = "100%";
            _zoomCombo.KeyDown += ZoomCombo_KeyDown;
            _zoomCombo.SelectedIndexChanged += (s, e) => { if (!_writingZoomCombo) ApplyZoomComboChoice(); };

            _btnZoomIn = new ToolStripButton("+");
            _btnZoomIn.Click += (s, e) => RunCommand(PdfViewerCommand.ZoomIn, ZoomIn);

            _btnFitWidth = new ToolStripButton();
            _btnFitWidth.Click += (s, e) => RunCommand(PdfViewerCommand.FitWidth, FitWidth);

            _btnFitPage = new ToolStripButton();
            _btnFitPage.Click += (s, e) => RunCommand(PdfViewerCommand.FitPage, FitPage);

            _btnLayout = new ToolStripButton { CheckOnClick = false };
            _btnLayout.Click += (s, e) => RunCommand(PdfViewerCommand.ToggleLayout, () =>
                LayoutMode = _layoutMode == PdfViewLayoutMode.Continuous
                    ? PdfViewLayoutMode.SinglePage
                    : PdfViewLayoutMode.Continuous);

            _btnSidebar = new ToolStripButton { Checked = true };
            _btnSidebar.Click += (s, e) => RunCommand(PdfViewerCommand.ToggleSidebar, () => ShowSidebar = !ShowSidebar);

            _btnFind = new ToolStripButton();
            _btnFind.Click += (s, e) => RunCommand(PdfViewerCommand.Find, () => Find());

            _btnPrint = new ToolStripButton();
            _btnPrint.Click += (s, e) => RunCommand(PdfViewerCommand.Print, PrintPreview);

            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                _btnOpen, _btnSave, new ToolStripSeparator(),
                _btnBack, _btnForward,
                _btnPrev, _pageBox, _pageCountLabel, _btnNext, new ToolStripSeparator(),
                _btnZoomOut, _zoomCombo, _btnZoomIn, _btnFitWidth, _btnFitPage, new ToolStripSeparator(),
                _btnLayout, _btnSidebar, new ToolStripSeparator(),
                _btnFind, _btnPrint
            });

            // ── Sidebar (thumbnails + bookmarks) + surface in a split ──────────
            // The page canvas is CONTENT, not chrome. Its scroll offsets, hit-testing and
            // PDF->pixel mapping (PdfViewerControl.Paint/.Input, PdfPixelMapper) are written
            // against an unmirrored client origin, so it is pinned left-to-right explicitly
            // rather than left to inherit. An explicit value wins over the ambient one for
            // the life of the control, so a host flipping RightToLeft later cannot move it.
            _surface = new Surface(this) { Dock = DockStyle.Fill, RightToLeft = RightToLeft.No, AccessibleRole = AccessibleRole.Document };
            _surface.Scroll += (s, e) => OnSurfaceScrolled();
            _surface.Resize += Surface_Resize;
            _surface.MouseDown += Surface_MouseDown;
            _surface.MouseMove += Surface_MouseMove;
            _surface.MouseUp += Surface_MouseUp;
            WireSurfaceDragDrop();
            BuildPageMenu();

            _thumbFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
            };
            _thumbFlow.Resize += (s, e) => CentreThumbnails();

            _bookmarkTree = new PaneTree { Dock = DockStyle.Fill, HideSelection = false };
            StyleTree(_bookmarkTree);
            _bookmarkTree.NodeMouseClick += BookmarkTree_NodeMouseClick;
            // Opening or closing a parent changes which node can stand for the current section.
            _bookmarkTree.AfterExpand += (s, e) => UpdateCurrentBookmark();
            _bookmarkTree.AfterCollapse += (s, e) => UpdateCurrentBookmark();

            // The pages behind the pane's header row; the tab control's own headers never show.
            _sidebarTabs = new PaneTabs { Dock = DockStyle.Fill };
            _sidebarTabs.SelectedIndexChanged += (s, e) => { foreach (var b in _paneButtons) b.Invalidate(); };
            _sidebarHeader = new Panel { Dock = DockStyle.Top, Height = 44 };
            _sidebarHeader.Resize += (s, e) => LayoutSidebarHeader();
            _tabThumbnails = new TabPage();
            _tabThumbnails.Controls.Add(_thumbFlow);
            _tabBookmarks = new TabPage();
            _tabBookmarks.Controls.Add(_bookmarkTree);
            _sidebarTabs.TabPages.Add(_tabThumbnails);
            _sidebarTabs.TabPages.Add(_tabBookmarks);

            // Comments (PdfViewerFeatures.Comments, off by default): added by ApplySidebarTabs.
            _commentTree = new PaneTree { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true };
            StyleTree(_commentTree);
            _commentTree.NodeMouseClick += CommentTree_NodeMouseClick;
            _tabComments = new TabPage();
            _tabComments.Controls.Add(_commentTree);

            // Signatures (PdfViewerFeatures.Signatures, off by default): added by ApplySidebarTabs.
            _signatureTree = new PaneTree { Dock = DockStyle.Fill, HideSelection = false };
            StyleTree(_signatureTree);
            _signatureTree.NodeMouseClick += SignatureTree_NodeMouseClick;
            _tabSignatures = new TabPage();
            _tabSignatures.Controls.Add(_signatureTree);
            _signatureBanner = new InfoBar(this) { Dock = DockStyle.Top, AutoSize = false, Height = 40, Visible = false };

            _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            _split.SizeChanged += (_, _) => SizeSidebarOnce();
            _split.Paint += Split_Paint;
            _split.Panel1.Controls.Add(_sidebarTabs);
            _split.Panel1.Controls.Add(_sidebarHeader);
            _split.Panel2.Controls.Add(_surface);
            // The signatures' banner above the pages: docked after the surface, which then fills the rest.
            _split.Panel2.Controls.Add(_signatureBanner);
            _surface.BringToFront();
            _split.Panel2.Resize += (s, e) => PlaceFindCard();
            _surface.Resize += (s, e) => PlaceFindCard();

            // ── Find card ─────────────────────────────────────────────────────
            // A WinUI flyout over the pages' trailing top corner: a search box, the match count, the
            // two steps and close. It floats above the surface, so the pages keep their full height.
            _findBar = new FindCard(this) { Visible = false };
            _findBox = new TextBox { BorderStyle = BorderStyle.None };
            _findBox.TextChanged += (s, e) => RunSearch();
            _findBox.KeyDown += FindBox_KeyDown;
            _findBox.GotFocus += (s, e) => _findBar.Invalidate();
            _findBox.LostFocus += (s, e) => _findBar.Invalidate();
            // The find-step arrows are VERTICAL: unlike the page arrows they mean the same
            // thing in either direction, so they are not mirrored.
            _btnFindPrev = FindCardButton();
            _btnFindNext = FindCardButton();
            _findResult = new Label { AutoSize = true, Text = "" };
            // No Anchor: LayoutFindBar owns every position in this card, in both directions.
            _btnFindClose = FindCardButton();
            _surface.MouseMove += (s, e) => NudgeFloatingControls();
            _surface.Resize += (s, e) => { if (_floating != null && _floating.Visible) LayoutFloatingControls(); };
            _btnFindPrev.Click += (s, e) => FindStep(-1);
            _btnFindNext.Click += (s, e) => FindStep(+1);
            _btnFindClose.Click += (s, e) => HideFindBar();
            _findBar.Controls.AddRange(new Control[] { _findBox, _btnFindPrev, _btnFindNext, _findResult, _btnFindClose });
            // The result label is AutoSize, so its width changes when RunSearch writes a count, and
            // the buttons after it, the card's width and (when mirrored) every X follow from it.
            _findResult.SizeChanged += (s, e) => LayoutFindBar();
            _split.Panel2.Controls.Add(_findBar);
            _findBar.BringToFront();

            // ── Status bar ─────────────────────────────────────────────────────
            _statusStrip = new StatusStrip { Renderer = _renderer, SizingGrip = false };
            // TextAlign is set by ApplyChromeDirection below: a StatusStrip mirrors where an
            // item is PLACED under RightToLeft, but the text inside a Spring item is aligned
            // by this property and by nothing else.
            _statusLabel = new ToolStripStatusLabel { Spring = true };
            _statusLabel.TextChanged += (s, e) => AnnounceStatus();
            _statusStrip.Items.Add(_statusLabel);

            // Dock order: Fill first (lowest z-order), then edges.
            Controls.Add(_split);
            Controls.Add(_statusStrip);
            Controls.Add(_toolbar);
            _pageMenu.Renderer = _renderer;

            // Control.RightToLeft is the opt-in (see ApplyChromeDirection). The event is raised
            // both when a host assigns it and when the control is added to an already-mirrored
            // parent, which is why this is a subscription rather than a one-off call.
            // Every label and tooltip comes from Strings, applied here rather than at each
            // construction site so that assigning Strings later goes through the same code. Two
            // paths that write the same label are two chances for one of them to be forgotten.
            RefreshChromeScale();
            ApplyStrings();

            RightToLeftChanged += (s, e) => ApplyChromeDirection();
            ApplyChromeDirection();
            ApplyChromeVisibility();
            ApplyTheme();

            ResumeLayout(false);
        }

        /// <summary>
        /// The strings the chrome is labelled with. Never null; assigning null restores English.
        /// </summary>
        /// <remarks>
        /// <b>Assigning this re-labels a control that is already built</b>, which is what makes it
        /// usable from a host that learns its UI language after construction — a settings dialog, a
        /// per-user preference, a document that arrived in another language. It does not touch
        /// <see cref="Control.RightToLeft"/>: translating and mirroring are independent, and a host
        /// that wants both sets both.
        /// </remarks>
        // Hidden from designer serialization: these are set from code by a host that knows its
        // UI language, and a designer-emitted copy of thirty-odd default strings in InitializeComponent
        // would be a second source of truth that stops tracking this type as it gains members.
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public PdfViewerStrings Strings
        {
            get => _strings;
            set
            {
                _strings = value ?? PdfViewerStrings.English();
                ApplyStrings();
            }
        }

        /// <summary>
        /// Writes every fixed label and tooltip from <see cref="Strings"/>.
        /// </summary>
        /// <remarks>
        /// The status line and the find result are <b>not</b> re-written here: both say something
        /// about the current document or the current search, and re-deriving them would need the
        /// state that produced them. They pick the new strings up the next time they change, and the
        /// page counter is refreshed because it is a pure function of the page count this already
        /// has.
        /// </remarks>
        private void ApplyStrings()
        {
            _btnOpen.Text = _strings.Open;
            _btnOpen.ToolTipText = _strings.OpenTip;
            _btnSave.Text = _strings.Save;
            _btnSave.ToolTipText = _strings.SaveTip;
            _btnBack.ToolTipText = _strings.BackTip;
            _btnForward.ToolTipText = _strings.ForwardTip;
            _btnPrev.ToolTipText = _strings.PreviousPageTip;
            _btnNext.ToolTipText = _strings.NextPageTip;
            _btnZoomOut.ToolTipText = _strings.ZoomOutTip;
            _btnZoomIn.ToolTipText = _strings.ZoomInTip;
            _btnFitWidth.Text = _strings.FitWidth;
            _btnFitWidth.ToolTipText = _strings.FitWidthTip;
            _btnFitPage.Text = _strings.FitPage;
            _btnFitPage.ToolTipText = _strings.FitPageTip;
            UpdateLayoutButton();
            _btnLayout.ToolTipText = _strings.LayoutTip;
            _btnSidebar.Text = _strings.Sidebar;
            _btnSidebar.ToolTipText = _strings.SidebarTip;
            _btnFind.Text = _strings.Find;
            _btnFind.ToolTipText = _strings.FindTip;
            _btnPrint.Text = _strings.Print;
            _btnPrint.ToolTipText = _strings.PrintTip;

            // The fields, not TabPages[i]: Features can take either tab out of the control.
            _tabThumbnails.Text = _strings.PagesTab;
            _tabBookmarks.Text = _strings.BookmarksTab;
            _tabComments.Text = _strings.CommentsTab;
            _tabSignatures.Text = _strings.SignaturesTab;
            LayoutSidebarHeader();                              // the tab buttons are named for the tabs
            UpdateSignatureBanner();

            if (string.IsNullOrEmpty(_statusLabel.Text)) _statusLabel.Text = _strings.Ready;
            UpdatePageCountLabel();
            ApplyAccessibleNames();
            FillZoomCombo();
            ApplyPageMenuStrings();
        }

        /// <summary>
        /// What a screen reader announces for each control: the glyph-only buttons would otherwise be
        /// read by their glyphs' names ("black left-pointing triangle"), and the boxes not at all.
        /// </summary>
        private void ApplyAccessibleNames()
        {
            void Name(ToolStripItem item, string name) { item.AccessibleName = name; item.AccessibleDescription = item.ToolTipText; }
            // An icon-only button is read by its name, which the icon no longer shows.
            Name(_btnOpen, _strings.Open);
            Name(_btnSave, _strings.Save);
            Name(_btnFitWidth, _strings.FitWidth);
            Name(_btnFitPage, _strings.FitPage);
            Name(_btnLayout, _btnLayout.Text ?? "");
            Name(_btnSidebar, _strings.Sidebar);
            Name(_btnFind, _strings.Find);
            Name(_btnPrint, _strings.Print);
            Name(_btnBack, _strings.BackTip);
            Name(_btnForward, _strings.ForwardTip);
            Name(_btnPrev, _strings.PreviousPageTip);
            Name(_btnNext, _strings.NextPageTip);
            Name(_btnZoomOut, _strings.ZoomOutTip);
            Name(_btnZoomIn, _strings.ZoomInTip);
            _pageBox.AccessibleName = _strings.PageBoxName;
            _zoomCombo.AccessibleName = _strings.ZoomBoxName;
            _findBox.AccessibleName = _strings.FindBoxName;
            _btnFindPrev.AccessibleName = _strings.FindPreviousName;
            _btnFindNext.AccessibleName = _strings.FindNextName;
            _btnFindClose.AccessibleName = _strings.FindCloseName;
            _findTip.SetToolTip(_btnFindPrev, _strings.FindPreviousName);
            _findTip.SetToolTip(_btnFindNext, _strings.FindNextName);
            _findTip.SetToolTip(_btnFindClose, _strings.FindCloseName);
            UpdateSurfaceAccessibleName();
        }

        /// <summary>The find bar's buttons' tooltips.</summary>
        private readonly ToolTip _findTip = new ToolTip();

        /// <summary>The page area names the page being read, as the page box shows it.</summary>
        private void UpdateSurfaceAccessibleName()
            => _surface.AccessibleName = _pageCount > 0
                ? PdfViewerStrings.Format(_strings.PageSurfaceFormat, PageBoxText(_currentPage), _pageCount)
                : _strings.Ready;

        /// <summary>The zoom presets, then the two fits by name — a choice of either fits and keeps fitting.</summary>
        private void FillZoomCombo()
        {
            _writingZoomCombo = true;
            try
            {
                string text = _zoomCombo.Text;
                _zoomCombo.Items.Clear();
                _zoomCombo.Items.AddRange(new object[] { "50%", "75%", "100%", "125%", "150%", "200%", "400%", _strings.FitWidth, _strings.FitPage });
                _zoomCombo.Text = text;
            }
            finally { _writingZoomCombo = false; }
        }

        /// <summary>
        /// Applies the chrome direction -- the parts of a right-to-left mirror that WinForms does
        /// <b>not</b> do for us -- from <see cref="Control.RightToLeft"/>.
        /// </summary>
        /// <remarks>
        /// <para><b>The opt-in is the framework's own <see cref="Control.RightToLeft"/>, not a
        /// viewer-specific flag.</b> It already inherits down the control tree, so a host whose
        /// form is right-to-left mirrors the viewer with no viewer-specific API -- and, measured
        /// on this framework, <see cref="SplitContainer"/>, <see cref="ToolStrip"/>,
        /// <see cref="StatusStrip"/> and <see cref="FlowLayoutPanel"/> all mirror <i>themselves</i>
        /// from it. A separate <c>RtlChrome</c> flag would be a second source of truth, and it
        /// would disagree with those the moment a host set the standard one.</para>
        ///
        /// <para><b>The sidebar stays in <c>Panel1</c>, and that is deliberate.</b> A
        /// <see cref="SplitContainer"/> <i>does</i> mirror itself: with <c>RightToLeft.Yes</c> and
        /// a splitter distance of 120 in a 400px control, <c>Panel1</c> measures at
        /// <c>Left = 280</c> and <c>Panel2</c> at <c>Left = 0</c>. Moving the sidebar into
        /// <c>Panel2</c> to "fix" the pinning would mirror it twice -- back to the left -- and
        /// would also break <see cref="ShowSidebar"/>, which collapses <c>Panel1</c> and would
        /// then be hiding the page canvas instead of the sidebar.</para>
        ///
        /// <para>What is left for this method is everything the framework does not carry:
        /// <see cref="TabControl.RightToLeftLayout"/> and <see cref="TreeView.RightToLeftLayout"/>
        /// (separate properties that <c>RightToLeft</c> does not imply, without which Hebrew
        /// bookmark titles keep left-to-right indentation and expander glyphs); the page-navigation
        /// glyphs, which a <see cref="ToolStrip"/> re-orders but never rewrites; the status label's
        /// own <c>TextAlign</c>; and the find bar, whose children are absolutely positioned inside
        /// a plain <see cref="Panel"/> -- which mirrors nothing at all.</para>
        ///
        /// <para><b>Not done, and why:</b> defaulting the direction from the document's
        /// <c>/ViewerPreferences /Direction /R2L</c> (readable through
        /// <c>PdfOperations.GetInitialView</c>) needs the opened file's bytes, and
        /// <c>PdfDocumentSource</c> holds them in a private field with no accessor. It wants one
        /// accessor there plus the rule that the document wins only while the host has left
        /// <see cref="Control.RightToLeft"/> alone.</para>
        /// </remarks>
        private void ApplyChromeDirection()
        {
            bool rtl = RightToLeft == RightToLeft.Yes;

            _sidebarTabs.RightToLeftLayout = rtl;
            _bookmarkTree.RightToLeftLayout = rtl;
            _commentTree.RightToLeftLayout = rtl;
            _signatureTree.RightToLeftLayout = rtl;
            _signatureBanner.TextAlign = rtl ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;

            _btnPrev.Text = rtl ? GlyphRight : GlyphLeft;
            _btnNext.Text = rtl ? GlyphLeft : GlyphRight;
            _btnBack.Text = rtl ? GlyphForward : GlyphBack;
            _btnForward.Text = rtl ? GlyphBack : GlyphForward;

            _statusLabel.TextAlign = rtl ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
            _pageMenu.RightToLeft = RightToLeft;

            // The icons that point (back, the page chevrons, the pane) are chosen by direction when drawn.
            _toolbar.Invalidate();
            LayoutSidebarHeader();
            LayoutFindBar();
            UpdateFloatingControls();
        }

        // -- Test hooks (internal; internals are excluded from the public API baseline) --------
        // The chrome is private and WinForms layout is observable only through the controls
        // themselves, so these exist so a headless test can assert measured positions and real
        // property values rather than that construction did not throw.

        /// <summary>The sidebar / canvas splitter. The sidebar is in <c>Panel1</c> in both directions.</summary>
        internal SplitContainer SidebarSplit => _split;

        /// <summary>The thumbnails / bookmarks tab host.</summary>
        internal TabControl SidebarTabs => _sidebarTabs;

        /// <summary>The bookmark tree.</summary>
        internal TreeView BookmarkTree => _bookmarkTree;

        /// <summary>The scrolling page canvas -- pinned left-to-right whatever the chrome does.</summary>
        internal Control PageSurface => _surface;

        /// <summary>The toolbar itself, for a sweep over every item it holds.</summary>
        internal ToolStrip Toolbar => _toolbar;

        /// <summary>The open button - a labelled one, unlike the glyph-only arrows.</summary>
        internal ToolStripButton OpenButton => _btnOpen;

        /// <summary>The page counter beside the page box.</summary>
        internal ToolStripLabel PageCountLabel => _pageCountLabel;

        /// <summary>The find button.</summary>
        internal ToolStripButton FindButton => _btnFind;

        /// <summary>Previous-page toolbar button.</summary>
        internal ToolStripButton PreviousPageButton => _btnPrev;

        /// <summary>Next-page toolbar button.</summary>
        internal ToolStripButton NextPageButton => _btnNext;

        /// <summary>The zoom-in toolbar button.</summary>
        internal ToolStripButton ZoomInButton => _btnZoomIn;

        /// <summary>The zoom box.</summary>
        internal ToolStripComboBox ZoomCombo => _zoomCombo;

        /// <summary>The save toolbar button.</summary>
        internal ToolStripButton SaveButton => _btnSave;

        /// <summary>The print toolbar button.</summary>
        internal ToolStripButton PrintButton => _btnPrint;

        /// <summary>The sidebar toolbar button.</summary>
        internal ToolStripButton SidebarButton => _btnSidebar;

        /// <summary>The status bar.</summary>
        internal StatusStrip StatusBar => _statusStrip;

        /// <summary>The status line.</summary>
        internal ToolStripStatusLabel StatusLabel => _statusLabel;

        /// <summary>The find bar panel.</summary>
        internal Panel FindBarPanel => _findBar;

        /// <summary>The find query box.</summary>
        internal Control FindTextBox => _findBox;

        /// <summary>The find bar's previous, next and close buttons, in that order.</summary>
        internal Control[] FindBarButtons => new Control[] { _btnFindPrev, _btnFindNext, _btnFindClose };

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            SizeSidebarOnce();
            RebuildLayout();
            _surface.Invalidate();
        }

        // The sidebar's first width is set the first time the split is wide enough for it, not only
        // at load: a host that sizes the control after loading it (a WPF WindowsFormsHost does) left
        // the SplitContainer's 50-pixel default, the tab headers clipped. After that the user's
        // splitter position is kept.
        private bool _sidebarSized;

        private void SizeSidebarOnce()
        {
            if (_sidebarSized || _split.Width <= Px(260)) return;
            try { _split.SplitterDistance = Px(240); _sidebarSized = true; } catch { /* tiny host */ }
        }

        private void Surface_Resize(object? sender, EventArgs e)
        {
            if (_inLayout) return;
            _inLayout = true;
            // The refit runs under the same guard: its rebuild can add or drop a scroll bar, which
            // resizes the surface again, and a nested refit there would chase its own scroll bar.
            try
            {
                if (_zoomMode != PdfZoomMode.Custom) ApplyZoomMode();
                RebuildLayout();
            }
            finally { _inLayout = false; }
            _surface.Invalidate();
        }

        // ── Toolbar helpers ───────────────────────────────────────────────────

        private void OpenWithDialog()
        {
            if (!Has(PdfViewerFeatures.OpenFile)) return;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = DialogFilter(_strings.OpenFileFilter, PdfViewerStrings.English().OpenFileFilter);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try { LoadFile(dlg.FileName); }
                    catch (Exception ex) { _statusLabel.Text = PdfViewerStrings.Format(_strings.OpenFailedFormat, ex.Message); }
                }
            }
        }

        /// <summary>
        /// A translated file-dialog filter, or the English one when it is not in the dialog's
        /// <c>description|pattern</c> form — a dialog throws on a malformed filter, and a mistyped
        /// translation must not stop the user opening or saving a file.
        /// </summary>
        internal static string DialogFilter(string? translated, string english)
        {
            if (string.IsNullOrEmpty(translated)) return english;
            string[] parts = translated!.Split('|');
            if (parts.Length % 2 != 0) return english;
            foreach (string part in parts) if (part.Trim().Length == 0) return english;
            return translated;
        }

        /// <summary>The layout button names the layout shown, in the current strings.</summary>
        private void UpdateLayoutButton()
        {
            _btnLayout.Text = _layoutMode == PdfViewLayoutMode.Continuous ? _strings.Layout : _strings.LayoutSinglePage;
            _btnLayout.AccessibleName = _btnLayout.Text;
            _toolbar.Invalidate();                              // its icon names the layout too
        }

        private void PageBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            int page = PageFromEntry(_pageBox.Text);
            if (page >= 0)
                RunCommand(PdfViewerCommand.GoToPage, () => JumpTo(page, null), pageIndex: page);
            else if (int.TryParse(_pageBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                RunCommand(PdfViewerCommand.GoToPage, () => JumpTo(n - 1, null), pageIndex: n - 1);   // clamped to the document
            else
                UpdatePageBox();
        }

        private void ZoomCombo_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            ApplyZoomComboText();
        }

        /// <summary>A choice in the zoom box: a fit by its name, else a factor.</summary>
        internal void ApplyZoomComboChoice()
        {
            string chosen = _zoomCombo.SelectedItem as string ?? "";
            if (chosen == _strings.FitWidth) RunCommand(PdfViewerCommand.FitWidth, FitWidth);
            else if (chosen == _strings.FitPage) RunCommand(PdfViewerCommand.FitPage, FitPage);
            else { ApplyZoomComboText(); return; }
            UpdateZoomCombo();                               // the box shows the fitted factor, not the name
        }

        private void ApplyZoomComboText()
        {
            string t = (_zoomCombo.Text ?? "").Replace("%", "").Trim();
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) && pct > 0)
                RunCommand(PdfViewerCommand.SetZoom, () => SetZoom(pct / 100.0, ViewportCentre()), zoom: pct / 100.0);
            else
                UpdateZoomCombo();
        }

        private void UpdateZoomCombo()
        {
            string text = Math.Round(_zoom * 100).ToString(CultureInfo.InvariantCulture) + "%";
            // Writing a preset's text selects that preset, and the selection event is the user's
            // zoom command: without the flag every zoom to 100% raised a SetZoom the user never gave.
            _writingZoomCombo = true;
            try { if (_zoomCombo.Text != text) _zoomCombo.Text = text; }
            finally { _writingZoomCombo = false; }
            _btnFitWidth.Checked = _zoomMode == PdfZoomMode.FitWidth;
            _btnFitPage.Checked  = _zoomMode == PdfZoomMode.FitPage;
            UpdateFloatingControls();
        }
    }
}
