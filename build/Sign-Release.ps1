[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $CertificateThumbprint,
    [string] $Executable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\release\PSWindowsUpdateGUI.exe'),
    [string] $TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$certificate = Get-ChildItem -Path Cert:\CurrentUser\My,Cert:\LocalMachine\My |
    Where-Object Thumbprint -eq $CertificateThumbprint |
    Select-Object -First 1
if (-not $certificate) { throw 'The requested Authenticode certificate was not found.' }
if ($PSCmdlet.ShouldProcess($Executable, 'Authenticode sign executable')) {
    $signature = Set-AuthenticodeSignature -LiteralPath $Executable -Certificate $certificate -TimestampServer $TimestampServer -HashAlgorithm SHA256
    if ($signature.Status -ne 'Valid') { throw "Signing failed: $($signature.StatusMessage)" }
}
