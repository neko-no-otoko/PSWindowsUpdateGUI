[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')] [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }
$project = Join-Path $repoRoot 'src\PSWindowsUpdateGui\PSWindowsUpdateGui.csproj'
$smokeRoot = Join-Path $repoRoot 'artifacts\ui-smoke-build'
$darkCapture = Join-Path $repoRoot 'artifacts\ui-smoke-history-dark.png'
$lightCapture = Join-Path $repoRoot 'artifacts\ui-smoke-updates-light.png'
$executable = Join-Path $smokeRoot 'PSWindowsUpdateGUI.exe'
$errorLog = Join-Path (Split-Path -Parent $executable) 'ui-smoke-error.log'
& $dotnet build $project -c UiSmoke '-p:Platform=x64' '-p:UiSmokeBuild=true' "-p:OutputPath=$smokeRoot\" --no-restore
if ($LASTEXITCODE -ne 0) { throw "WinUI smoke build failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $executable)) { throw "WinUI smoke executable was not built: $executable" }

foreach ($file in @($errorLog, $darkCapture, $lightCapture)) {
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}

$previousDotnetRootX64 = $env:DOTNET_ROOT_X64
try {
    if ($dotnet -ne 'dotnet') { $env:DOTNET_ROOT_X64 = [System.IO.Path]::GetFullPath((Split-Path -Parent $dotnet)) }
    $scenarios = @(
        @{ Theme = 'Dark'; Page = 'history'; Capture = $darkCapture },
        @{ Theme = 'Light'; Page = 'updates'; Capture = $lightCapture }
    )
    foreach ($scenario in $scenarios) {
        $process = Start-Process -FilePath $executable -ArgumentList @('--ui-smoke', '--theme', $scenario.Theme, '--page', $scenario.Page, '--capture', "`"$($scenario.Capture)`"") -PassThru -Wait
        if ($process.ExitCode -ne 0) {
            $detail = if (Test-Path -LiteralPath $errorLog) { Get-Content -LiteralPath $errorLog -Raw } else { "Exit code $($process.ExitCode)." }
            throw "WinUI $($scenario.Theme) $($scenario.Page) smoke test failed. $detail"
        }
    }
}
finally {
    $env:DOTNET_ROOT_X64 = $previousDotnetRootX64
}

foreach ($capture in @($darkCapture, $lightCapture)) {
    if (-not (Test-Path -LiteralPath $capture) -or (Get-Item -LiteralPath $capture).Length -lt 1024) { throw "WinUI smoke test did not create a valid screenshot: $capture" }
}
Write-Host "WinUI production-assembly smoke tests passed: $darkCapture, $lightCapture"
