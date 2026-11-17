// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;

namespace Tetitulz.Pdf.WinForms.PdfViewer
{
    public sealed partial class PdfViewerControl
    {
        private string? _password;

        /// <summary>
        /// Password tried first when opening an encrypted PDF. Set this before calling a
        /// <c>Load*</c> method to open a known password-protected document without prompting.
        /// </summary>
        [Category("PDF"), DefaultValue(null), Description("Password tried first for encrypted PDFs.")]
        public string? Password
        {
            get => _password;
            set => _password = value;
        }

        /// <summary>
        /// Raised when an encrypted PDF needs a password (the empty password and any preset
        /// <see cref="Password"/> failed). Answer it by setting
        /// <see cref="PdfViewerPasswordRequestedEventArgs.Password"/>, or set
        /// <see cref="PdfViewerPasswordRequestedEventArgs.Cancel"/> to abort the load.
        /// </summary>
        public event EventHandler<PdfViewerPasswordRequestedEventArgs>? PasswordRequested;

        /// <summary>
        /// Builds a per-load password provider: try the <see cref="Password"/> property once,
        /// then ask the host via <see cref="PasswordRequested"/>. Returns null (→ fail the load)
        /// when there is no preset password and no handler, or when a handler cancels.
        /// </summary>
        private PasswordProvider BuildPasswordProvider()
        {
            bool triedProperty = false;
            return (attempt, info) =>
            {
                if (!triedProperty)
                {
                    triedProperty = true;
                    if (!string.IsNullOrEmpty(_password)) return _password;
                }

                if (PasswordRequested == null) return null;   // no way to obtain a password

                var args = new PdfViewerPasswordRequestedEventArgs(
                    attempt, info.V, info.R, info.KeyLengthBits);
                PasswordRequested.Invoke(this, args);
                return args.Cancel ? null : args.Password;
            };
        }
    }
}
