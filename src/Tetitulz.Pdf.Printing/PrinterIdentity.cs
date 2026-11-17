// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Tetitulz.Pdf.Printing
{
    /// <summary>
    /// The names that make a virtual printer one product's own: what Windows lists it as, the pipe
    /// its port is, the folders it keeps, and its driver. <see cref="PrinterSetup"/> derives the
    /// rest — the port, the single-listener mutex, the logon start — from these, so two products
    /// with two identities get two printers that serve side by side and never share a port.
    /// </summary>
    public sealed class PrinterIdentity
    {
        /// <summary>Windows' own PostScript class driver, in every Windows's driver store.</summary>
        public const string ClassDriverName = "Microsoft PS Class Driver";

        /// <summary>Tetitulz PDF's printer: on Windows' class driver.</summary>
        public static PrinterIdentity TetitulzPdf { get; } =
            new PrinterIdentity("Tetitulz PDF", "TetitulzPdfPrinter", "TetitulzPdfPrinter", "Tetitulz PDF", ClassDriverName);

        /// <param name="printerName">The printer's name in every program's Print dialog.</param>
        /// <param name="pipeName">The named pipe the port is; also the mutex's and the logon start's name.</param>
        /// <param name="dataFolder">The folder under the user's local application data for the spool, logs and kept jobs.</param>
        /// <param name="outputFolder">The folder under Documents where PDFs go when nothing else is chosen.</param>
        /// <param name="driverName">The printer driver the printer is installed with.</param>
        /// <param name="driverPackage">
        /// The product's signed driver package (its INF) relative to the program's folder, installed
        /// with the printer; null when the driver is Windows' own.
        /// </param>
        /// <param name="driverSigner">
        /// The thumbprint of the certificate the product's driver package is signed with — the one
        /// publisher the install trusts on the machine for it (<see cref="PrinterSetup.Install"/>);
        /// null trusts none, and Windows decides about the package's signer.
        /// </param>
        public PrinterIdentity(string printerName, string pipeName, string dataFolder, string outputFolder,
                               string driverName, string? driverPackage = null, string? driverSigner = null)
        {
            PrinterName = Required(printerName, nameof(printerName));
            PipeName = Required(pipeName, nameof(pipeName));
            DataFolder = Required(dataFolder, nameof(dataFolder));
            OutputFolder = Required(outputFolder, nameof(outputFolder));
            DriverName = Required(driverName, nameof(driverName));
            DriverPackage = driverPackage;
            DriverSigner = driverSigner;
        }

        /// <summary>The printer's name in every program's Print dialog.</summary>
        public string PrinterName { get; }

        /// <summary>The named pipe the printer's port is; also the mutex's and the logon start's name.</summary>
        public string PipeName { get; }

        /// <summary>The folder under the user's local application data for the spool, logs and kept jobs.</summary>
        public string DataFolder { get; }

        /// <summary>The folder under Documents where PDFs go when nothing else is chosen.</summary>
        public string OutputFolder { get; }

        /// <summary>The printer driver the printer is installed with.</summary>
        public string DriverName { get; }

        /// <summary>The product's signed driver package relative to the program's folder; null when the driver is Windows' own.</summary>
        public string? DriverPackage { get; }

        /// <summary>The thumbprint of the certificate the driver package is signed with, the one publisher the install trusts for it; null for none.</summary>
        public string? DriverSigner { get; }

        private static string Required(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A printer identity's names cannot be empty.", name) : value;
    }
}
