[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$wuapi = Join-Path $env:SystemRoot 'System32\wuapi.dll'
if (-not (Test-Path -LiteralPath $wuapi)) {
    throw "Windows Update Agent type library was not found: $wuapi"
}

$signature = Get-AuthenticodeSignature -LiteralPath $wuapi
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft') {
    throw "Windows Update Agent failed Microsoft Authenticode verification: $($signature.Status)"
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$forbidden = @(
    (Join-Path $projectRoot 'src\PSWindowsUpdateGui\Resources\PSWindowsUpdate.2.2.1.5.nupkg'),
    (Join-Path $projectRoot 'src\PSWindowsUpdateGui\Resources\vendor-manifest.json')
)
foreach ($path in $forbidden) {
    if (Test-Path -LiteralPath $path) { throw "Removed PSWindowsUpdate runtime asset is still present: $path" }
}

Write-Host "Verified native Windows Update Agent: $wuapi"
