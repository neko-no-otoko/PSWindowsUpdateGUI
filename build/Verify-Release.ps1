[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Executable
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$buildToolsRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
$manifestTool = Get-ChildItem -LiteralPath $buildToolsRoot -Recurse -Filter 'mt.exe' -File |
    Where-Object { $_.FullName -match '\\x64\\mt\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $manifestTool) { throw "Could not locate the x64 Windows SDK manifest tool under $buildToolsRoot." }

$inspectionPath = Join-Path ([System.IO.Path]::GetTempPath()) ("PSWindowsUpdateGUI-manifest-{0}.xml" -f [Guid]::NewGuid().ToString('N'))
try {
    & $manifestTool.FullName "-inputresource:$resolvedExecutable;#1" "-out:$inspectionPath" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "mt.exe could not inspect the published executable (exit $LASTEXITCODE)." }
    $manifest = Get-Content -LiteralPath $inspectionPath -Raw
    if ($manifest -notmatch 'name="PSWindowsUpdateGUI\.app"') { throw 'Published executable does not contain the production application identity.' }
    if ($manifest -notmatch 'level="requireAdministrator"') { throw 'Published executable does not require administrator elevation.' }
    if ($manifest -match 'UiSmoke|level="asInvoker"') { throw 'Published executable contains the test-only smoke manifest.' }
    if ($manifest -notmatch 'loadFrom="%MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY%Microsoft\.ui\.xaml\.dll"') {
        throw 'Published executable is missing the extracted single-file WinUI activation mapping.'
    }
}
finally {
    if (Test-Path -LiteralPath $inspectionPath) { Remove-Item -LiteralPath $inspectionPath -Force }
}

Write-Host "Verified release manifest: elevated production identity in $resolvedExecutable"
