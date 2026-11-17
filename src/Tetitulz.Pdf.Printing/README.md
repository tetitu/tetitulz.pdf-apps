# Tetitulz.Pdf.Printing

A Windows virtual PDF printer's machinery: a PostScript printer on a named-pipe port, each job it
receives named from the spooler and converted to PDF by the library's PostScript plugin, and the
printer installed and removed. A program built on it appears in every Print dialog as a printer and
saves what is printed to it as PDF.

**Target:** `net10.0-windows`
**Dependencies:** `Tetitulz.Pdf.Plugins.PostScript` (and through it `Tetitulz.Pdf.Core`), `Serilog`.
No SkiaSharp, no native libraries.

---

## What's inside

- **`PrinterIdentity`** — what makes a printer one product's own: its name, its pipe, its folders,
  its driver (Windows' PostScript class driver, or the product's own signed package over it).
- **`PrinterSetup`** — the printer's port, mutex and folders from its identity; installing it (or moving
  an installed one to the product's driver), removing it, and the logon start.
- **`JobListener`** — the port: one job a connection, spooled to a file, handed on one at a time.
- **`SpoolerJobs`** — the document name the spooler gave the job printing on the port, and the
  account that submitted it (`SpooledJob`).
- **`JobConverter`** — a job to a PDF, written as `.partial` and renamed when complete; a job that
  converts to no page is kept for diagnosis.
- **`DocumentNames`** — the file name a print suggests ("Microsoft Word - Invoice 42.docx" → "Invoice 42").

## Quick start

A console program that installs its printer when it is missing and saves every job to
`Documents\Contoso PDF`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Tetitulz.Pdf.Printing;

// Logging is Serilog's static Log; the host picks the sinks (here Serilog.Sinks.Console).
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

var identity = new PrinterIdentity(
    printerName: "Contoso PDF",       // what the Print dialog lists
    pipeName: "ContosoPdfPrinter",    // the port's pipe, the mutex's and the logon start's name
    dataFolder: "ContosoPdfPrinter",  // under %LOCALAPPDATA%: spool, kept jobs
    outputFolder: "Contoso PDF",      // under Documents: where PDFs go
    driverName: PrinterIdentity.ClassDriverName);
var printer = new PrinterSetup(identity);

// One listener serves a printer at a time.
using var one = new Mutex(initiallyOwned: false, printer.ListenerMutexName);
if (!one.WaitOne(0)) { Console.WriteLine("Another listener serves this printer."); return; }

if (!printer.IsInstalled(printer.DefaultPrinterName))
{
    int code = printer.Install(printer.DefaultPrinterName);   // asks for elevation
    if (code != 0) { Console.WriteLine($"The printer was not installed ({code})."); return; }
}

JobConverter.SizeForPrintJobs();
var converter = new JobConverter(printer.DefaultOutput, Path.Combine(printer.AppData, "failed"), keepPostScript: false);
var listener = new JobListener(printer.PipeName, Path.Combine(printer.AppData, "spool"),
    new SpoolerJobs(printer.PortName),
    job =>
    {
        string? name = job.DocumentName is null ? null : DocumentNames.Suggest(job.DocumentName);
        string? pdf = converter.Convert(job.PostScriptPath, job.Received, name);
        Console.WriteLine(pdf ?? "The job converted to no page.");
        return Task.CompletedTask;
    });

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
await listener.RunAsync(stop.Token);
```

Print anything to "Contoso PDF" while it runs, and the PDF appears in `Documents\Contoso PDF`.

## Your printer's identity

Give your product its own `PrinterIdentity`. Two identities make two printers on two pipes, which
serve side by side; two programs sharing one identity share one printer, and only one of them can
listen. `PrinterIdentity.TetitulzPdf` is Tetitulz PDF's own printer and is meant for its apps.

| `PrinterSetup` member | Is | From the identity above |
|---|---|---|
| `DefaultPrinterName` | The printer's name in the Print dialog | `Contoso PDF` |
| `PortName` | The port: the pipe's path | `\\.\pipe\ContosoPdfPrinter` |
| `ListenerMutexName` | The machine-wide mutex the one listener holds | `Global\ContosoPdfPrinter.Listener` |
| `AppData` | Spool and kept jobs (and the host's logs, by convention) | `%LOCALAPPDATA%\ContosoPdfPrinter` |
| `DefaultOutput` | Where PDFs go when nothing else is chosen | `Documents\Contoso PDF` |

The driver is Windows' own **Microsoft PS Class Driver** (`PrinterIdentity.ClassDriverName`), in every
Windows's driver store, unless the product ships its own: `driverPackage` is its signed INF relative to
the program's folder, and `driverSigner` the thumbprint of the certificate its catalog is signed with.

### Pages to the edges

The class driver declares half an inch of hardware margin on every side, so a program that clips to
the printable area cuts every page there and the PDF has a white border. For pages printed to their
edges, ship your own driver package: the same class driver with a printer description that declares
no margins, signed with your code-signing certificate. The repository's
[`samples/edge-to-edge-driver`](https://github.com/tetitu/tetitulz.pdf-apps/tree/main/samples/edge-to-edge-driver)
is a template for one, with the script that signs it. A printer already installed on the class driver
moves to yours at the next `Install` (`NeedsDriverUpdate` tells when).

## Installing and removing the printer

`Install(name)` and `Uninstall(name)` run `install-printer.ps1` and `uninstall-printer.ps1` from the
program's folder, which this package copies there. Both need administrator rights:

- From a process that is not elevated they ask through the Windows consent prompt; one that is
  elevated runs them directly.
- Windows PowerShell is started by its full path, with the system directory as its working
  directory: a bare `powershell.exe` is looked for in the current directory first, so one left where
  the installer was started would run as administrator.
- Every value given to a script (printer name, paths) is passed as a quoted literal, never as code.
- The return value is the script's exit code: 0 when it succeeded, 1223 when the user refused the
  prompt, another code when the script failed. What the script printed goes to the log.

The install adds the port, the driver and the printer, skipping what is there already, and moves a
printer installed on another driver to the identity's (`NeedsDriverUpdate` tells when one is). With a
driver package it first checks the package's catalog is validly signed and adds that certificate to
the machine's Trusted Publishers only when it is the one `driverSigner` names; a catalog another
certificate signed is left to Windows to decide. The uninstall removes the printer, its port, that
trust, and the logon start; the driver stays.

`SetAutostart(command)` starts a listener at every logon through the user's own `Run` registry key,
with no elevation; `SetAutostart(null)` removes it, and `Autostart()` reads it back.

## Receiving jobs

The spooler opens the pipe once a job, writes the driver's PostScript into it, and closes it.
`JobListener.RunAsync` serves it until its token is cancelled:

- **Only this listener receives jobs.** It must make the pipe's first instance, or it throws
  `PipeAlreadyServedException` and serves nothing; it keeps an instance from then on, so the name is
  never free for another process to take. Its connections admit the spooler (LocalSystem) and the user
  running the listener only.
- **Only this user's jobs are kept.** The printer is the machine's, so any account can print to it. A
  job the spooler says another account submitted is dropped, neither saved nor named in the log.
- **One job at a time.** Jobs are read as they arrive, each into its own file in the spool folder,
  and handed to your handler one after another, so a handler can show a save dialog and the memory
  held by conversions stays bounded. A handler that throws fails that job alone.
- **Limits.** A job over 1 GB is read to its end and dropped; an empty connection is ignored; spooled
  files older than a day, left by a stopped process, are removed when a listener starts.

The handler owns the spooled file. `JobConverter.Convert` removes it, so a handler that does not
convert should delete it.

## Converting jobs

`JobConverter.Convert(path, received, documentName)` writes one PDF and returns its path:

- **Its name** is the document name given, cleaned for a file name and cut to 100 characters, then
  the time: `Invoice 42 2026-11-17 103005.pdf`. Without a document name it is the job's `%%Title`
  comment, else `Print` — but the class driver titles every job `MSxpsPS`, so pass the spooler's name
  (`ReceivedJob.DocumentName`, through `DocumentNames.Suggest` to drop "Microsoft Word - " and the
  extension). A name that is taken is numbered (`… (2).pdf`).
- **It appears whole.** It is written as `.pdf.partial` and renamed when complete, so a program
  watching the folder never opens half a file.
- **A job that converts to no page** returns null and keeps its PostScript in the `failed` folder
  for 14 days, for diagnosis; older ones are removed when a converter is made.
- **`keepPostScript: true`** keeps each job's PostScript beside its PDF, for debugging a driver.

Call `JobConverter.SizeForPrintJobs()` once before converting. It raises the PostScript plugin's
limits from those for PostScript inside an untrusted PDF to those for a print job (a few-page Word
document can take tens of millions of interpreter steps). The limits are process-wide.

## Deploying

- **Ship the two scripts.** `install-printer.ps1` and `uninstall-printer.ps1` are copied to the
  build output and must sit beside the program's executable: `Install` and `Uninstall` look for them
  in `AppContext.BaseDirectory`. A product's own driver package goes where its `driverPackage` says.
- **Windows 10 or later, .NET 10.** The package references the Windows desktop framework (WPF), for
  `System.Printing`, which reads the spooler's queues. There is no .NET Framework build: the port must
  be the pipe's first instance, which .NET Framework cannot request without a P/Invoke.
- **Run the listener as the signed-in user**, not as a service: jobs from other accounts are dropped,
  and PDFs go to that user's Documents.

## Logging

Everything is logged through Serilog's static `Log`: jobs received and dropped, PDFs written with
their page counts, the PostScript plugin's diagnostics for a job that did not convert cleanly, and
what the install scripts printed. Configure `Log.Logger` with the sinks you want; with none, nothing
is written.

## Licence and provenance

Apache License 2.0. Copyright (c) 2026 Tamir Mordo. Credit it as "Uses Tetitulz.Pdf by Tamir Mordo"
(the package's NOTICE file).

It depends on `Tetitulz.Pdf.Plugins.PostScript`, which is under the **Tetitulz.Pdf Attribution
License** (`LICENSE-ATTRIBUTION.txt` in the library), not Apache 2.0: the MIT License with added
conditions, among them crediting the author, reporting the use on the library's GitHub repository,
and reporting security problems found. A program that ships this package ships that plugin, so read
those terms before you distribute it.
