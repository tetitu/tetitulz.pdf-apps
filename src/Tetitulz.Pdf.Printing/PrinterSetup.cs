// Copyright (c) 2026 Tamir Mordo
// Licensed under the Apache License, Version 2.0. See LICENSE and NOTICE at the repository root.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;
using Serilog;

namespace Tetitulz.Pdf.Printing
{
    /// <summary>
    /// What a product's listeners share about its printer: its names (from its
    /// <see cref="PrinterIdentity"/>), the folders they keep, installing and removing it (Windows
    /// PowerShell's printing cmdlets, elevated through the consent prompt, by the scripts beside the
    /// program), and starting a listener at logon. One listener serves a printer at a time —
    /// <see cref="ListenerMutexName"/> — so two programs never both read its pipe.
    /// </summary>
    public sealed class PrinterSetup
    {
        private const string AutostartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <param name="identity">The product's printer.</param>
        public PrinterSetup(PrinterIdentity identity)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            AppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), identity.DataFolder);
            DefaultOutput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), identity.OutputFolder);
        }

        /// <summary>The product's printer.</summary>
        public PrinterIdentity Identity { get; }

        /// <summary>The printer's name in the Print dialog, unless the caller installs it under another.</summary>
        public string DefaultPrinterName => Identity.PrinterName;

        /// <summary>The named pipe the printer's port is.</summary>
        public string PipeName => Identity.PipeName;

        /// <summary>The printer's port: the pipe's path.</summary>
        public string PortName => @"\\.\pipe\" + Identity.PipeName;

        /// <summary>The machine-wide mutex the one listener of this printer holds.</summary>
        public string ListenerMutexName => @"Global\" + Identity.PipeName + ".Listener";

        /// <summary>The driver the printer is installed with.</summary>
        public string DriverName => Identity.DriverName;

        /// <summary>Where the listeners keep their spool, logs and kept jobs.</summary>
        public string AppData { get; }

        /// <summary>Where PDFs go when nothing else is chosen.</summary>
        public string DefaultOutput { get; }

        /// <summary>
        /// Adds the printer — or moves an installed one to <see cref="DriverName"/> — with the
        /// product's signed driver package, when it has one, installed first, its signer trusted on
        /// the machine only when it is the one <see cref="PrinterIdentity.DriverSigner"/> names; the
        /// script's exit code, 0 when it is installed.
        /// </summary>
        public int Install(string printerName)
            => RunElevatedScript("install-printer.ps1", printerName, DriverName, BesideTheProgram(Identity.DriverPackage), Identity.DriverSigner);

        /// <summary><paramref name="relative"/> in the program's folder; null for null.</summary>
        private static string? BesideTheProgram(string? relative)
            => relative == null ? null : Path.Combine(AppContext.BaseDirectory, relative);

        /// <summary>Removes the printer and its port, the trust the install gave the driver's signer, and the logon start.</summary>
        public int Uninstall(string printerName)
        {
            SetAutostart(null);
            return RunElevatedScript("uninstall-printer.ps1", printerName, driverSigner: Identity.DriverSigner);
        }

        /// <summary>Whether the printer is installed on this machine on this product's port.</summary>
        public bool IsInstalled(string printerName) => InstalledDriver(printerName) != null;

        /// <summary>
        /// The driver the printer is installed with — "" when Windows does not say — or null when the
        /// printer is not installed on this product's port.
        /// </summary>
        public string? InstalledDriver(string printerName)
        {
            try
            {
                using var server = new System.Printing.LocalPrintServer();
                using var queue = new System.Printing.PrintQueue(server, printerName);
                if (!string.Equals(queue.QueuePort?.Name, PortName, StringComparison.OrdinalIgnoreCase)) return null;
                return queue.QueueDriver?.Name ?? "";
            }
            catch (Exception ex) when (ex is System.Printing.PrintQueueException or System.Printing.PrintSystemException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether an installed printer runs on another driver than <see cref="DriverName"/> — one
        /// installed before the product had its own — and so wants <see cref="Install"/> again.
        /// </summary>
        public bool NeedsDriverUpdate(string? installedDriver)
            => installedDriver is { Length: > 0 } && !string.Equals(installedDriver, DriverName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The command started at every logon — one listener of this printer — or, for null, none:
        /// the user's own Run key, no elevation.
        /// </summary>
        public void SetAutostart(string? command)
        {
            using var run = Registry.CurrentUser.CreateSubKey(AutostartKey);
            if (command == null) run.DeleteValue(Identity.PipeName, throwOnMissingValue: false);
            else run.SetValue(Identity.PipeName, command);
        }

        /// <summary>The command started at logon, or null.</summary>
        public string? Autostart()
        {
            using var run = Registry.CurrentUser.OpenSubKey(AutostartKey);
            return run?.GetValue(Identity.PipeName) as string;
        }

        /// <summary>
        /// The PowerShell command that runs <paramref name="scriptPath"/> for <paramref name="printerName"/>
        /// on <paramref name="pipeName"/> and writes everything it prints to <paramref name="transcript"/>.
        /// Every value is a single-quoted literal (<see cref="Literal"/>), so a path or a printer name is
        /// data wherever it came from: the command runs elevated, and a name that ended its own string
        /// would run as an administrator whatever followed it. The install script is also told the
        /// driver and, when the product has one, the driver package; both scripts the package's signer.
        /// </summary>
        internal static string ScriptCommand(string scriptPath, string printerName, string pipeName, string transcript,
                                             string? driver = null, string? driverPackage = null, string? driverSigner = null)
            => $"& {Literal(scriptPath)} -Name {Literal(printerName)} -Pipe {Literal(pipeName)}" +
               (driver != null ? $" -Driver {Literal(driver)}" : "") +
               (driverPackage != null ? $" -DriverInf {Literal(driverPackage)}" : "") +
               (driverSigner != null ? $" -DriverSigner {Literal(driverSigner)}" : "") +
               $" *> {Literal(transcript)}; exit $LASTEXITCODE";

        /// <summary>
        /// <paramref name="value"/> as a PowerShell single-quoted string. PowerShell ends such a string
        /// at any of five characters — the apostrophe and U+2018, U+2019, U+201A, U+201B — and reads a
        /// doubled one as the character itself, so each is doubled.
        /// </summary>
        internal static string Literal(string value)
        {
            var quoted = new System.Text.StringBuilder(value.Length + 2).Append('\'');
            foreach (char c in value)
            {
                if (c is '\'' or (>= (char)0x2018 and <= (char)0x201B)) quoted.Append(c);
                quoted.Append(c);
            }
            return quoted.Append('\'').ToString();
        }

        /// <summary>
        /// <paramref name="command"/> as <c>-EncodedCommand</c> takes it: UTF-16LE in Base64. The
        /// command line then carries no quote or space of the command's own for the two parsers between
        /// this process and PowerShell to split.
        /// </summary>
        internal static string EncodedCommand(string command)
            => Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));

        /// <summary>
        /// How the script's PowerShell is started: Windows PowerShell by its full path, in the system
        /// directory. A bare name is sought in the current directory before the PATH — by
        /// <c>CreateProcess</c> when this process is elevated and by the consent prompt's
        /// <c>ShellExecuteEx</c> when it is not — so a <c>powershell.exe</c> left where the installer
        /// was started would run as administrator.
        /// </summary>
        internal static ProcessStartInfo ElevatedStart(string arguments, bool elevated)
            => new ProcessStartInfo(
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), arguments)
            {
                UseShellExecute = !elevated,
                Verb = elevated ? "" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = elevated,
                WorkingDirectory = Environment.SystemDirectory,
            };

        /// <summary>
        /// Runs one of the two scripts beside the executable in Windows PowerShell — elevated, through the
        /// consent prompt, when this process is not — and logs what it printed.
        /// </summary>
        private int RunElevatedScript(string script, string printerName, string? driver = null, string? driverPackage = null,
                                      string? driverSigner = null)
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, script);
            string transcript = Path.Combine(Path.GetTempPath(), $"tetitulz-printer-{Guid.NewGuid():N}.txt");
            string arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " +
                               EncodedCommand(ScriptCommand(scriptPath, printerName, PipeName, transcript, driver, driverPackage, driverSigner));

            bool elevated;
            using (var identity = WindowsIdentity.GetCurrent())
                elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

            ProcessStartInfo start = ElevatedStart(arguments, elevated);
            try
            {
                using Process process = Process.Start(start)!;
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.Error("Elevation was refused: {Reason}", ex.Message);
                return 1223;
            }
            finally
            {
                if (File.Exists(transcript))
                {
                    foreach (string line in File.ReadAllLines(transcript))
                        if (line.Length > 0) Log.Information("  {Line}", line);
                    File.Delete(transcript);
                }
            }
        }
    }
}
