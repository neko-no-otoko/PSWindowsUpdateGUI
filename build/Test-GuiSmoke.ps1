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
$singleFileRoot = Join-Path $repoRoot 'artifacts\ui-smoke-single-file'
$singleFileCapture = Join-Path $repoRoot 'artifacts\ui-smoke-single-file-history-dark.png'
$executable = Join-Path $smokeRoot 'PSWindowsUpdateGUI.exe'
$errorLog = Join-Path (Split-Path -Parent $executable) 'ui-smoke-error.log'
& $dotnet build $project -c UiSmoke -r win-x64 --self-contained true '-p:Platform=x64' '-p:SelfContained=true' '-p:UiSmokeBuild=true' "-p:OutputPath=$smokeRoot\" --no-restore
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

if (Test-Path -LiteralPath $singleFileRoot) {
    $resolvedSingleFileRoot = [System.IO.Path]::GetFullPath($singleFileRoot).TrimEnd('\')
    $artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')).TrimEnd('\')
    if (-not $resolvedSingleFileRoot.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected single-file smoke path: $resolvedSingleFileRoot"
    }
    Remove-Item -LiteralPath $resolvedSingleFileRoot -Recurse -Force
}
if (Test-Path -LiteralPath $singleFileCapture) { Remove-Item -LiteralPath $singleFileCapture -Force }

$singleFileManifest = Join-Path $repoRoot 'src\PSWindowsUpdateGui\obj\x64\UiSmoke\net10.0-windows10.0.26100.0\win-x64\Manifests\app.manifest'
if (Test-Path -LiteralPath $singleFileManifest) { Remove-Item -LiteralPath $singleFileManifest -Force }
& $dotnet publish $project -c UiSmoke -r win-x64 --self-contained true --no-restore '-p:Platform=x64' '-p:UiSmokeBuild=true' '-p:WindowsPackageType=None' '-p:WindowsAppSDKSelfContained=true' '-p:PublishSingleFile=true' '-p:IncludeAllContentForSelfExtract=true' '-p:EnableMsixTooling=true' '-p:PublishTrimmed=false' '-p:PublishReadyToRun=false' -o $singleFileRoot
if ($LASTEXITCODE -ne 0) { throw "WinUI single-file smoke publish failed with exit code $LASTEXITCODE." }
$singleFileExecutable = Join-Path $singleFileRoot 'PSWindowsUpdateGUI.exe'
if (-not (Test-Path -LiteralPath $singleFileExecutable)) { throw "WinUI single-file smoke executable was not published: $singleFileExecutable" }
$singleFileErrorLog = Join-Path $singleFileRoot 'ui-smoke-error.log'
$process = Start-Process -FilePath $singleFileExecutable -ArgumentList @('--ui-smoke', '--theme', 'Dark', '--page', 'history', '--capture', "`"$singleFileCapture`"") -PassThru -Wait
if ($process.ExitCode -ne 0) {
    $detail = if (Test-Path -LiteralPath $singleFileErrorLog) { Get-Content -LiteralPath $singleFileErrorLog -Raw } else { "Exit code $($process.ExitCode)." }
    throw "WinUI extracted single-file smoke test failed. $detail"
}
if (-not (Test-Path -LiteralPath $singleFileCapture) -or (Get-Item -LiteralPath $singleFileCapture).Length -lt 1024) {
    throw "WinUI single-file smoke test did not create a valid screenshot: $singleFileCapture"
}

Write-Host "WinUI production-assembly and extracted single-file smoke tests passed: $darkCapture, $lightCapture, $singleFileCapture"
