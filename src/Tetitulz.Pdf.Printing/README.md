# Tetitulz.Pdf.Printing

A Windows virtual PDF printer's machinery: a PostScript printer on a named-pipe port, each job it
receives named from the spooler and converted to PDF by the PostScript plugin, and the printer
installed and removed.

- **`PrinterIdentity`** — what makes a printer one product's own: its name, its pipe, its folders,
  its driver (Windows' PostScript class driver, or the product's own signed package over it).
- **`PrinterSetup`** — the printer's port, mutex and folders from its identity; installing it (or moving
  an installed one to the product's driver), removing it, and the logon start. Installing runs the
  `install-printer.ps1` this package copies beside the program, elevated through the consent prompt,
  in Windows PowerShell started by its full path with the system directory as its working directory:
  a bare `powershell.exe` is looked for in the current directory first, so one left where the installer
  was started would run as administrator.
- **`JobListener`** — the port: one job a connection, spooled to a file, handed on one at a time. It
  makes the pipe's first instance or does not start (`PipeAlreadyServedException`), admits the
  spooler and the current user only, and drops a job another account submitted.
- **`SpoolerJobs`** — the document name the spooler gave the job printing on the port.
- **`JobConverter`** — a job to a PDF, written as `.partial` and renamed when complete; a job that
  converts to no page is kept for diagnosis.
- **`DocumentNames`** — the file name a print suggests ("Microsoft Word - Invoice 42.docx" → "Invoice 42").

Windows only (`net10.0-windows`): the port must be the pipe's first instance, which .NET Framework
cannot ask for without a P/Invoke.

```csharp
using Tetitulz.Pdf.Printing;

var printer = new PrinterSetup(PrinterIdentity.TetitulzPdf);
if (!printer.IsInstalled(printer.DefaultPrinterName))
    printer.Install(printer.DefaultPrinterName);

JobConverter.SizeForPrintJobs();
var converter = new JobConverter(printer.DefaultOutput, System.IO.Path.Combine(printer.AppData, "failed"), keepPostScript: false);
var listener = new JobListener(printer.PipeName, System.IO.Path.Combine(printer.AppData, "spool"),
                               new SpoolerJobs(printer.PortName),
                               job =>
                               {
                                   converter.Convert(job.PostScriptPath, job.Received, job.DocumentName);
                                   return System.Threading.Tasks.Task.CompletedTask;
                               });
listener.RunAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();
```

Logging goes through Serilog's static `Log`; the host configures it.
