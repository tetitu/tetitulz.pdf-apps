// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Drawing;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>
    /// Pages at the monitor's resolution: 100% zoom is a page at its printed size on the screen, so a
    /// 150% or 200% display gets a page rendered at 144 or 192 dpi rather than one rendered at 96 and
    /// drawn small. The zoom box still reads the zoom alone.
    /// </summary>
    public sealed partial class PdfViewerControl
    {
        /// <summary>The dpi 100% zoom is reckoned in, as a whole number.</summary>
        private static readonly int ReferenceDpi = Core.Util.SaturatingCast.ToInt(ZoomMath.BaseDpi);

        private bool _scaleToDeviceDpi = true;

        /// <summary>The monitor's dpi over the 96 that 100% zoom is reckoned in; 1 when not scaling.</summary>
        private double _deviceScale = 1.0;

        /// <summary>
        /// A dpi the tests put in place of the monitor's; 0 reads the monitor. A field, not a
        /// property, so the designer serialises nothing for it.
        /// </summary>
        internal int DeviceDpiOverride;

        /// <summary>
        /// Whether pages render at the monitor's resolution (the default): at 100% zoom a page then
        /// covers its printed size, and on a scaled display (150%, 200%) it renders at that display's
        /// dpi, sharp, rather than at 96 dpi and small. Off, a page renders at 96 dpi times the zoom
        /// on every monitor — for a host that sizes things around the page's pixel size.
        /// </summary>
        /// <remarks>
        /// The monitor's dpi is the control's <c>DeviceDpi</c> on .NET 10, followed as the window
        /// moves to a monitor of another scale; on .NET Framework it is the screen's dpi read when
        /// the handle is created, which a process not declared dpi-aware is told is 96.
        /// </remarks>
        [Category("PDF"), DefaultValue(true),
         Description("Render pages at the monitor's resolution, so 100% is the page's printed size.")]
        public bool ScaleToDeviceDpi
        {
            get => _scaleToDeviceDpi;
            set
            {
                if (_scaleToDeviceDpi == value) return;
                _scaleToDeviceDpi = value;
                RefreshDeviceScale();
            }
        }

        /// <summary>The scale pages render at over 96 dpi times the zoom (for tests).</summary>
        internal double DeviceScale => _deviceScale;

        /// <summary>The dpi pages render at now (for tests).</summary>
        internal int CurrentRenderDpi => RenderDpi;

        /// <summary>The monitor's dpi as the control sees it, or 96 when it cannot tell.</summary>
        private int MonitorDpi()
        {
            if (DeviceDpiOverride > 0) return DeviceDpiOverride;
#if NETFRAMEWORK
            if (!IsHandleCreated) return ReferenceDpi;
            using (Graphics g = CreateGraphics())
            {
                float dpi = g.DpiX;
                return dpi > 0 && !float.IsNaN(dpi) && !float.IsInfinity(dpi)
                    ? Core.Util.SaturatingCast.ToInt(Math.Round(dpi))
                    : ReferenceDpi;
            }
#else
            return DeviceDpi > 0 ? DeviceDpi : ReferenceDpi;
#endif
        }

        /// <summary>
        /// Reads the monitor's dpi again and, when the scale changed, lays the pages out at it with the
        /// reader's place kept; a fit is fitted anew, since the same view holds another zoom.
        /// </summary>
        internal void RefreshDeviceScale()
        {
            double scale = _scaleToDeviceDpi ? MonitorDpi() / ZoomMath.BaseDpi : 1.0;
            if (!(scale > 0)) scale = 1.0;
            if (Math.Abs(scale - _deviceScale) < 1e-9) return;

            PageAnchor? reading = _doc != null && _layout.Length > 0
                ? CaptureAnchor(new PointF(_surface.ClientSize.Width / 2f, 0f))
                : null;
            _deviceScale = scale;
            if (_doc == null) return;

            if (RendersInBackground) RetireRenderCacheForZoom(RenderDpi);
            else ClearRenderCache();
            RebuildLayout();
            if (reading.HasValue) RestoreAnchor(reading.Value);
            ApplyZoomMode();
            _surface.Invalidate();
            UpdateCurrentPageFromScroll();
        }

        /// <inheritdoc />
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RefreshDeviceScale();
            RefreshChromeScale();
            // Windows' app mode and accent are followed from here on; the subscription is static, so
            // Dispose takes it back.
            ListenToSystem(true);
            ApplyTheme();
        }

        /// <inheritdoc />
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            // The host's font (or the ambient one, through its parent) is the chrome's body text.
            if (_renderer != null) ApplyTypography();
        }

#if !NETFRAMEWORK
        /// <inheritdoc />
        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            RefreshDeviceScale();
            RefreshChromeScale();
        }
#endif
    }
}
