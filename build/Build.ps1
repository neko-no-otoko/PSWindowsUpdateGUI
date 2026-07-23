[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')] [string] $Configuration = 'Release',
    [string] $Version = '3.0.0-beta.1',
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
Invoke-DotNet clean (Join-Path $repoRoot 'PSWindowsUpdateGUI.sln') -c $Configuration '-p:Platform=x64' --verbosity minimal
$generatedManifest = Join-Path $repoRoot "src\PSWindowsUpdateGui\obj\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\Manifests\app.manifest"
if (Test-Path -LiteralPath $generatedManifest) { Remove-Item -LiteralPath $generatedManifest -Force }
Invoke-DotNet build (Join-Path $repoRoot 'PSWindowsUpdateGUI.sln') -c $Configuration "-p:Version=$Version" '-p:Platform=x64' --no-restore --no-incremental
if (-not $SkipTests) {
    Invoke-DotNet test (Join-Path $repoRoot 'tests\PSWindowsUpdateGui.Tests\PSWindowsUpdateGui.Tests.csproj') -c $Configuration '-p:Platform=x64' --no-build --no-restore --logger 'trx;LogFileName=PSWindowsUpdateGui.Tests.trx'
}

$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$publishRoot = Join-Path $repoRoot 'artifacts\publish'
foreach ($directory in @($releaseRoot, $publishRoot)) {
    if (Test-Path -LiteralPath $directory) {
        $resolvedDirectory = [System.IO.Path]::GetFullPath($directory).TrimEnd('\')
        $expectedDirectory = [System.IO.Path]::GetFullPath($directory).TrimEnd('\')
        if ($resolvedDirectory -ne $expectedDirectory -or -not $resolvedDirectory.StartsWith([System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected artifact path: $resolvedDirectory"
        }
        Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

Invoke-DotNet publish (Join-Path $repoRoot 'src\PSWindowsUpdateGui\PSWindowsUpdateGui.csproj') -c $Configuration -r win-x64 --self-contained true --no-restore '-p:Platform=x64' "-p:Version=$Version" '-p:WindowsPackageType=None' '-p:WindowsAppSDKSelfContained=true' '-p:PublishSingleFile=true' '-p:IncludeAllContentForSelfExtract=true' '-p:EnableMsixTooling=true' '-p:PublishTrimmed=false' '-p:PublishReadyToRun=false' '-o' $publishRoot

$source = Join-Path $publishRoot 'PSWindowsUpdateGUI.exe'
if (-not (Test-Path -LiteralPath $source)) { throw "Single-file publish did not create $source." }
& (Join-Path $PSScriptRoot 'Verify-Release.ps1') -Executable $source
$destination = Join-Path $releaseRoot 'PSWindowsUpdateGUI.exe'
Copy-Item -LiteralPath $source -Destination $destination -Force
$checksum = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant() + '  PSWindowsUpdateGUI.exe'
Set-Content -LiteralPath (Join-Path $releaseRoot 'PSWindowsUpdateGUI.exe.sha256') -Value $checksum -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.txt') -Destination $releaseRoot -Force
& (Join-Path $PSScriptRoot 'New-Sbom.ps1') -Executable $destination -OutputPath (Join-Path $releaseRoot 'PSWindowsUpdateGUI.spdx.json') -Version $Version
if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'Test-GuiSmoke.ps1') -Configuration $Configuration
}
Write-Host "Release artifacts: $releaseRoot"
