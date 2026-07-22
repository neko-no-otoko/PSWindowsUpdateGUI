[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$packagePath = Join-Path $repoRoot 'src\PSWindowsUpdateGui\Resources\PSWindowsUpdate.2.2.1.5.nupkg'
$manifestPath = Join-Path $repoRoot 'src\PSWindowsUpdateGui\Resources\vendor-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
if ($packageHash -ne $manifest.packageSha256) {
    throw "Package hash mismatch. Expected $($manifest.packageSha256), received $packageHash."
}

$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) ('PSWUGui-verify-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verificationRoot | Out-Null
try {
    $zipPath = Join-Path $verificationRoot 'package.zip'
    Copy-Item -LiteralPath $packagePath -Destination $zipPath
    Expand-Archive -LiteralPath $zipPath -DestinationPath (Join-Path $verificationRoot 'module')
    foreach ($file in $manifest.files) {
        $path = Join-Path (Join-Path $verificationRoot 'module') $file.path
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actual -ne $file.sha256) { throw "Hash mismatch: $($file.path)" }
    }

    foreach ($name in 'PSWindowsUpdate.dll','PSWindowsUpdate.psd1','PSWindowsUpdate.psm1','PSWindowsUpdate.Format.ps1xml') {
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path (Join-Path $verificationRoot 'module') $name)
        if ($signature.Status -ne 'Valid') { throw "Invalid Authenticode signature: $name ($($signature.Status))" }
    }
}
finally {
    Remove-Item -LiteralPath $verificationRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'PSWindowsUpdate 2.2.1.5 vendor verification passed.'
