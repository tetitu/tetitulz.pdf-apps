// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    /// <summary>The colours the viewer's chrome is drawn in, as WinUI 3 draws an app's.</summary>
    /// <remarks>
    /// Windows' high-contrast setting overrides every member: the chrome is then drawn in the system's
    /// colours, whatever the theme. The pages themselves are drawn as the document says in every theme.
    /// </remarks>
    public enum PdfViewerTheme
    {
        /// <summary>
        /// Light or dark as Windows' "app mode" says, followed when the user changes it; on .NET 10, as
        /// the host's <c>Application.SetColorMode</c> says when the host chose one.
        /// </summary>
        System = 0,
        /// <summary>WinUI's light theme.</summary>
        Light = 1,
        /// <summary>WinUI's dark theme.</summary>
        Dark = 2,
    }

    /// <summary>What the toolbar's buttons show.</summary>
    public enum PdfViewerToolbarStyle
    {
        /// <summary>An icon alone, the button's name in its tooltip (the default), as Edge's PDF bar.</summary>
        Icons = 0,
        /// <summary>An icon with the button's name beside it, as a WinUI <c>CommandBar</c> with its labels on the right.</summary>
        IconsAndText = 1,
    }
}
