# An edge-to-edge printer driver for Tetitulz.Pdf.Printing

A template for a product's own printer driver package, so that what is printed to its virtual
printer reaches the PDF to the paper's edges.

## Why

Windows' **Microsoft PS Class Driver**, the driver `Tetitulz.Pdf.Printing` installs by default,
declares half an inch of hardware margin on every side. A program that clips what it prints to the
printable area cuts every page there, and the PDF shows a white border with the content inside it
trimmed.

This package is the same class driver with a printer description of its own (`contoso.ppd`) that
declares no hardware margins (`*HWMargins: 0 0 0 0`, every page size's `*ImageableArea` the whole
sheet). It is a v4 driver: the only files of its own are the description, a manifest and the filter
pipeline. The rendering is Windows' own (`MSxpsPS.dll`, already in every Windows's driver store).

| File | What it is |
|---|---|
| `driver/contoso.inf` | The package: names the driver, its files and its catalog |
| `driver/contoso.ppd` | The printer description: A4, Letter, Legal, A3 and A5, each printable to its edges, 600 dpi |
| `driver/contoso-manifest.ini` | The v4 manifest: the description, `MSxpsPS.dll`, the driver's ID |
| `driver/contoso-pipelineconfig.xml` | The filter pipeline: Windows' XPS-to-PostScript filter |
| `sign-driver.ps1` | Makes the package's catalog and signs it |

## Make it your product's

1. **Copy `driver/`** into your printer program's project.
2. **Rename** `Contoso` everywhere, in the file names and in the files. The model name
   (`Contoso PostScript Driver` in the INF and the PPD's `*ModelName`/`*NickName`) is the driver's
   name in Windows. Keep the INF's `CatalogFile=`, `[..._FILES]` and `[SourceDisksFiles]` lists in
   step with the file names.
3. **Give it its own ID.** Replace `PrinterDriverID` in the manifest with a GUID of your own
   (`[guid]::NewGuid()`), and the PPD's `*MSPrintSchemaPrivateNamespaceURI` with a URI of yours. Two
   drivers must never share an ID.
4. **Set the version.** `DriverVer=` in the INF is a date and a version. Raise them with each change
   to the package, so that Windows replaces the installed one.
5. **Ship it beside the program**, in a `driver` folder of the output:

   ```xml
   <ItemGroup>
     <None Update="driver\**" CopyToOutputDirectory="PreserveNewest" />
   </ItemGroup>
   ```

6. **Name it in your identity:**

   ```csharp
   var identity = new PrinterIdentity(
       printerName: "Contoso PDF", pipeName: "ContosoPdfPrinter",
       dataFolder: "ContosoPdfPrinter", outputFolder: "Contoso PDF",
       driverName: "Contoso PostScript Driver",          // the INF's model name
       driverPackage: @"driver\contoso.inf",             // relative to the program's folder
       driverSigner: "<your certificate's thumbprint>"); // the one publisher the install trusts
   ```

## Sign it

Windows installs a driver package only when its catalog, holding a hash of every file, is signed by a
publisher the machine trusts. Sign the copy you ship, after the build or publish has put it in place,
with your code-signing certificate:

```powershell
.\sign-driver.ps1 -Package <output>\driver -Thumbprint <certificate thumbprint>
```

It needs the Windows SDK (its `makecat.exe`). It makes the catalog the INF names over every other file
in the folder, signs it with that certificate from the current user's personal store (a certificate on
a hardware token asks for its PIN once), and timestamps it (`-TimestampUrl`, Sectigo's by default;
`''` for none), so the signature stays valid after the certificate expires. A change to any file in
the folder after signing makes the catalog invalid: sign last.

When `Install` runs, the install script checks that the catalog is validly signed and adds its
certificate to the machine's Trusted Publishers only when it is the one `driverSigner` names. A
catalog signed by another certificate is left to Windows, which asks the user or refuses.

## Moving installed printers to it

A printer already installed on the class driver is moved to yours by `Install`:
`printer.NeedsDriverUpdate(printer.InstalledDriver(name))` tells when one is. Calling `Install`
then, once, after an update gives existing users the edge-to-edge pages too.

## What it covers

- **x64 Windows** (`NTamd64`). The class driver exists on ARM64 too; a package for it needs an
  `NTarm64` section, which this template does not have and which has not been tried.
- **The five page sizes above**, the only ones the printer offers. For more, add a `*PageSize`,
  `*PageRegion`, `*ImageableArea` and `*PaperDimension` line for each (in points, 1/72 inch).
- The package installs the description, the manifest and the pipeline only; nothing runs in the
  spooler but Windows' own filter.
