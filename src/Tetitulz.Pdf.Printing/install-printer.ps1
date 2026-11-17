param(
  [string] $Name = 'Tetitulz PDF',
  [string] $Pipe = 'TetitulzPdfPrinter',
  [string] $Driver = 'Microsoft PS Class Driver',
  [string] $DriverInf,
  [string] $DriverSigner
)
# Adds the printer on a local port whose name is a named pipe, which the listener serves. The driver
# is Windows' own PostScript class driver (prnms005.inf, in the driver store), or - with -DriverInf -
# the product's signed v4 package over that class driver (one with its own printer description,
# whose pages have no hardware margins), staged and added each time, so a newer one replaces the older.
# A printer already installed on another driver is moved to this one. Needs an elevated shell. Each
# other step is skipped when what it adds is already there.
#
# The guard on the package: Windows installs a driver package only when its catalog is signed by a
# publisher the machine trusts, and this script trusts exactly one - the certificate whose thumbprint
# the product names (-DriverSigner), added to the machine's Trusted Publishers when the catalog's
# signer is that certificate. A catalog another certificate signed, however validly, is not trusted
# here: Windows then asks the user, or refuses. A valid signature says who signed, not that the
# product chose them.
$ErrorActionPreference = 'Stop'
$port = "\\.\pipe\$Pipe"

if ($DriverInf) {
  $catalogs = @(Get-ChildItem -Path (Split-Path $DriverInf) -Filter *.cat)
  if ($catalogs.Count -eq 0) { throw "The driver package beside $DriverInf has no catalog: it was not signed" }
  foreach ($catalog in $catalogs) {
    $signature = Get-AuthenticodeSignature -FilePath $catalog.FullName
    if ($signature.Status -ne 'Valid') { throw "The driver package's catalog $($catalog.Name) is not validly signed: $($signature.StatusMessage)" }
    $signer = $signature.SignerCertificate
    if ($DriverSigner -and $signer.Thumbprint -eq $DriverSigner) {
      $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPublisher', 'LocalMachine')
      $store.Open('ReadWrite')
      try {
        if (-not ($store.Certificates | Where-Object Thumbprint -eq $signer.Thumbprint)) {
          "Trusting the driver's publisher: $($signer.Subject)"
          $store.Add($signer)
        }
      }
      finally { $store.Close() }
    }
    else {
      "The catalog $($catalog.Name) is signed by $($signer.Subject) ($($signer.Thumbprint)), not the product's signer; Windows decides whether to trust it"
    }
  }
  "Adding the driver package: $DriverInf"
  & pnputil.exe /add-driver $DriverInf
  # 0: added; 259: the same package is there already; 3010: added, a restart wanted.
  if ($LASTEXITCODE -notin 0, 259, 3010) { throw "pnputil could not add the driver package ($LASTEXITCODE)" }
  Add-PrinterDriver -Name $Driver
}
elseif (-not (Get-PrinterDriver -Name $Driver -ErrorAction SilentlyContinue)) {
  "Adding the driver: $Driver"
  Add-PrinterDriver -Name $Driver
}
if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) {
  "Adding the port: $port"
  Add-PrinterPort -Name $port
}
$printer = Get-Printer -Name $Name -ErrorAction SilentlyContinue
if (-not $printer) {
  "Adding the printer: $Name"
  Add-Printer -Name $Name -DriverName $Driver -PortName $port
}
elseif ($printer.DriverName -ne $Driver) {
  "Moving the printer to its driver: $Name, $($printer.DriverName) -> $Driver"
  Set-Printer -Name $Name -DriverName $Driver
}
"Installed: $Name on $port with $Driver"
# Stated: the caller exits with $LASTEXITCODE, which still holds pnputil's 259 or 3010 — answers this
# script accepts — when nothing native ran after it.
exit 0
