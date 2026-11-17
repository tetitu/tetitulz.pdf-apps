<#
  Builds a printer driver package's catalog and signs it: Windows installs a driver package only when
  its catalog, holding a hash of every file, is signed by a publisher it trusts.

    .\sign-driver.ps1 -Package <folder> -Thumbprint <certificate> [-TimestampUrl <url>]

  The catalog is the one the package's INF names (CatalogFile=), over every other file in the folder,
  made with the Windows SDK's makecat, and signed (Set-AuthenticodeSignature) with the certificate of
  that thumbprint in the current user's personal store — a certificate on a hardware token asks for the
  token's PIN — and, unless -TimestampUrl is empty, timestamped, so the signature outlives the certificate.
#>
param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$Thumbprint,
    [string]$TimestampUrl = 'http://timestamp.sectigo.com'
)
$ErrorActionPreference = 'Stop'

$kits = 'C:\Program Files (x86)\Windows Kits\10\bin'
$kit = Get-ChildItem $kits -Directory -Filter '10.*' | Sort-Object { [version]$_.Name } -Descending |
       ForEach-Object { Join-Path $_.FullName 'x64' } | Where-Object { Test-Path (Join-Path $_ 'makecat.exe') } | Select-Object -First 1
if (-not $kit) { throw "The Windows SDK's makecat.exe was not found under $kits" }

$inf = @(Get-ChildItem $Package -Filter *.inf)
if ($inf.Count -ne 1) { throw "$Package holds $($inf.Count) INF files; a driver package has one" }
$catalogName = (Select-String -Path $inf[0].FullName -Pattern '^\s*CatalogFile\s*=\s*(\S+)').Matches.Groups[1].Value
if (-not $catalogName) { throw "$($inf[0].Name) names no CatalogFile" }
$catalog = Join-Path $Package $catalogName
if (Test-Path $catalog) { Remove-Item $catalog }

$cdf = Join-Path ([IO.Path]::GetTempPath()) "driver-$([guid]::NewGuid().ToString('N')).cdf"
$lines = @(
    '[CatalogHeader]'
    "Name=$catalogName"
    "ResultDir=$Package"
    'PublicVersion=0x0000001'
    'EncodingType=0x00010001'
    'CATATTR1=0x10010001:OSAttr:2:10.0'
    ''
    '[CatalogFiles]'
)
foreach ($file in Get-ChildItem $Package -File | Where-Object Extension -ne '.cat') {
    $lines += "<hash>$($file.Name)=$($file.FullName)"
}
try {
    $lines | Set-Content $cdf -Encoding ascii
    & (Join-Path $kit 'makecat.exe') $cdf | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makecat failed ($LASTEXITCODE)" }
}
finally { Remove-Item $cdf -ErrorAction SilentlyContinue }

# Signed with the one certificate named, not through signtool: signtool opens the key of every
# certificate in the store while it looks for the one asked for, and a hardware token's then asks for
# its PIN even when the certificate chosen is another.
$certificate = Get-Item "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
if (-not $certificate) { throw "No certificate $Thumbprint in the current user's personal store" }
$signing = @{ FilePath = $catalog; Certificate = $certificate; HashAlgorithm = 'SHA256' }
if ($TimestampUrl) { $signing.TimestampServer = $TimestampUrl }
$signature = Set-AuthenticodeSignature @signing
if ($signature.Status -ne 'Valid') { throw "$catalogName could not be signed: $($signature.StatusMessage)" }
Write-Host "Signed $catalogName with $Thumbprint"
