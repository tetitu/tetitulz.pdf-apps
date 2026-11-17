param(
  [string] $Name = 'Tetitulz PDF',
  [string] $Pipe = 'TetitulzPdfPrinter',
  [string] $DriverSigner
)
# Removes the printer, its pipe port, and the trust the install gave the product's driver signer
# (-DriverSigner, the certificate's thumbprint, taken out of the machine's Trusted Publishers). The
# driver stays: Windows' own class driver other printers may use, or a product's package, which a
# later install adds again in place. Needs an elevated shell.
$ErrorActionPreference = 'Stop'
$port = "\\.\pipe\$Pipe"

if (Get-Printer -Name $Name -ErrorAction SilentlyContinue) {
  "Removing the printer: $Name"
  Remove-Printer -Name $Name
}
if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) {
  "Removing the port: $port"
  Remove-PrinterPort -Name $port
}
if ($DriverSigner) {
  $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('TrustedPublisher', 'LocalMachine')
  $store.Open('ReadWrite')
  try {
    foreach ($trusted in @($store.Certificates | Where-Object Thumbprint -eq $DriverSigner)) {
      "Withdrawing the trust in the driver's publisher: $($trusted.Subject)"
      $store.Remove($trusted)
    }
  }
  finally { $store.Close() }
}
"Uninstalled: $Name"
# Stated, as the install states it: the caller exits with $LASTEXITCODE.
exit 0
