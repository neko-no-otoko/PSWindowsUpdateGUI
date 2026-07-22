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
$resourceRoot = Join-Path $projectRoot 'src\PSWindowsUpdateGui\Resources'
if (Test-Path -LiteralPath $resourceRoot) {
    $unexpectedAssets = Get-ChildItem -LiteralPath $resourceRoot -Recurse -File |
        Where-Object { $_.Extension -in @('.nupkg', '.psd1', '.psm1') }
    if ($unexpectedAssets) {
        $paths = ($unexpectedAssets.FullName -join ', ')
        throw "Bundled update-engine package or PowerShell module assets are not allowed: $paths"
    }
}

Write-Host "Verified native Windows Update Agent: $wuapi"
