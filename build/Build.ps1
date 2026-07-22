[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')] [string] $Configuration = 'Release',
    [string] $Version = '2.0.0-beta.1',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $Arguments)
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

& (Join-Path $PSScriptRoot 'Verify-Native.ps1')
Invoke-DotNet restore (Join-Path $repoRoot 'PSWindowsUpdateGUI.sln') --use-lock-file '-p:Platform=x64'
Invoke-DotNet build (Join-Path $repoRoot 'PSWindowsUpdateGUI.sln') -c $Configuration "-p:Version=$Version" '-p:Platform=x64' --no-restore
if (-not $SkipTests) {
    Invoke-DotNet test (Join-Path $repoRoot 'tests\PSWindowsUpdateGui.Tests\PSWindowsUpdateGui.Tests.csproj') -c $Configuration '-p:Platform=x64' --logger 'trx;LogFileName=PSWindowsUpdateGui.Tests.trx'
    & (Join-Path $PSScriptRoot 'Test-GuiSmoke.ps1') -Configuration $Configuration
}

$releaseRoot = Join-Path $repoRoot 'artifacts\release'
if (Test-Path -LiteralPath $releaseRoot) {
    $resolvedRelease = [System.IO.Path]::GetFullPath($releaseRoot).TrimEnd('\')
    $expectedRelease = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\release')).TrimEnd('\')
    if ($resolvedRelease -ne $expectedRelease) { throw "Refusing to clean unexpected release path: $resolvedRelease" }
    Remove-Item -LiteralPath $resolvedRelease -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$source = Join-Path $repoRoot "src\PSWindowsUpdateGui\bin\x64\$Configuration\net48\PSWindowsUpdateGUI.exe"
$destination = Join-Path $releaseRoot 'PSWindowsUpdateGUI.exe'
Copy-Item -LiteralPath $source -Destination $destination -Force
$checksum = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() + '  PSWindowsUpdateGUI.exe'
Set-Content -LiteralPath (Join-Path $releaseRoot 'PSWindowsUpdateGUI.exe.sha256') -Value $checksum -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.txt') -Destination $releaseRoot -Force
& (Join-Path $PSScriptRoot 'New-Sbom.ps1') -Executable $destination -OutputPath (Join-Path $releaseRoot 'PSWindowsUpdateGUI.spdx.json') -Version $Version
Write-Host "Release artifacts: $releaseRoot"
