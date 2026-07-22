[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')] [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $repoRoot "tests\PSWindowsUpdateGui.UiSmoke\bin\x64\$Configuration\net48\PSWindowsUpdateGui.UiSmoke.exe"
$errorLog = Join-Path (Split-Path -Parent $executable) 'ui-smoke-error.log'
if (-not (Test-Path -LiteralPath $executable)) { throw "GUI smoke harness was not built: $executable" }
if (Test-Path -LiteralPath $errorLog) { Remove-Item -LiteralPath $errorLog -Force }

$process = Start-Process -FilePath $executable -PassThru -WindowStyle Hidden
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) {
            $detail = if (Test-Path -LiteralPath $errorLog) { Get-Content -LiteralPath $errorLog -Raw } else { "Exit code $($process.ExitCode)." }
            throw "GUI smoke harness exited before creating a window. $detail"
        }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'GUI smoke harness did not create a WPF window within 20 seconds.' }
    Write-Host "GUI smoke window created successfully (PID $($process.Id))."
}
finally {
    if (-not $process.HasExited) {
        [void] $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
    }
}
